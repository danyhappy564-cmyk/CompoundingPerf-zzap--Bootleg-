using System.Diagnostics;
using CompoundingPerf.Diagnostics;
using CompoundingPerf.Telemetry;
using SPTarkov.Common.Models.Logging;

namespace CompoundingPerf.Features;

/// <summary>
/// S16 (new in 2.1) — gives memory back to Windows between raids, at a moment nobody is
/// waiting on.
///
/// <para>Vanilla's raid-start collect is <c>GCCollectionMode.Aggressive</c>, which besides
/// compacting also decommits the free heap back to the OS. S15 turns that into a
/// background, non-compacting collect so the loading screen does not wait on it — which
/// also means the server stopped returning memory between raids. This puts the aggressive
/// collect back where it costs nothing: after <c>EndLocalRaidAsync</c>, after a delay, once
/// no player request has arrived for a few seconds (the post-raid screens), and only when
/// the GC has committed enough memory to be worth returning.</para>
///
/// <para>Never worse than vanilla: vanilla paid the same blocking collect every raid with
/// the player on a loading screen. If the server never goes quiet the collect still runs
/// after <c>MaxWaitSeconds</c>; if a new raid starts first it is dropped (that raid's own
/// start collect, S15, has the job then).</para>
/// </summary>
internal static class PostRaidCleanup
{
    public static volatile bool IsEnabled;

    private static int _delaySeconds = 30;
    private static int _quietSeconds = 3;
    private static int _maxWaitSeconds = 120;
    private static long _minCommittedBytes = 512L * 1024 * 1024;
    private static int _pending;

    /// <summary>One-liner about the last cleanup for the client's F12 status (Korean / English).</summary>
    public static volatile string? LastResult;

    /// <inheritdoc cref="LastResult"/>
    public static volatile string? LastResultEn;
    private static ISptLogger<CompoundingPerfMod>? _logger;

    public static void Configure(PostRaidCleanupOptions options, ISptLogger<CompoundingPerfMod>? logger)
    {
        if (logger is not null)
        {
            _logger = logger;
        }

        _delaySeconds = Math.Clamp(options.DelaySeconds, 0, 600);
        _quietSeconds = Math.Clamp(options.QuietSeconds, 0, 60);
        _maxWaitSeconds = Math.Clamp(options.MaxWaitSeconds, 0, 1800);
        _minCommittedBytes = Math.Max(0, options.MinCommittedMb) * 1024L * 1024;
        IsEnabled = options.Enabled;

        if (options.Enabled)
        {
            logger?.Success($"[CompoundingPerf/S16] post-raid cleanup ACTIVE — after each raid, once the server is quiet, memory is compacted and returned to Windows (when over {options.MinCommittedMb} MB)");
        }
        else
        {
            logger?.Info("[CompoundingPerf/S16] post-raid cleanup disabled in config");
        }
    }

    /// <summary>Called by <see cref="RaidWatcher"/> once the raid-end request has finished.</summary>
    public static void OnRaidEnded(int raidSequence)
    {
        if (!IsEnabled)
        {
            return;
        }

        // FIKA can end several players' raids back to back; one cleanup covers them all.
        if (Interlocked.Exchange(ref _pending, 1) == 1)
        {
            DebugLog.Write("S16", "raid ended while a cleanup is already scheduled — not scheduling a second one");
            return;
        }

        DebugLog.Write("S16", $"scheduled: wait {_delaySeconds}s, then for {_quietSeconds}s without player requests (at most {_maxWaitSeconds}s more)");
        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(raidSequence).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugLog.Write("S16", $"cleanup failed: {ex}");
            }
            finally
            {
                Volatile.Write(ref _pending, 0);
            }
        });
    }

    private static async Task RunAsync(int raidSequence)
    {
        await Task.Delay(TimeSpan.FromSeconds(_delaySeconds)).ConfigureAwait(false);

        var waitStart = Environment.TickCount64;
        var deadline = waitStart + _maxWaitSeconds * 1000L;
        var forced = false;
        while (true)
        {
            if (RaidWatcher.RaidSequence != raidSequence)
            {
                DebugLog.Write("S16", "skipped — a new raid started before the server went quiet");
                LastResult = $"{DateTime.Now:HH:mm} 건너뜀 (정리 전에 새 레이드 시작)";
                LastResultEn = $"{DateTime.Now:HH:mm} skipped (a new raid started before the cleanup)";
                return;
            }

            var quietMs = Environment.TickCount64 - RequestTracker.LastActivityTick;
            if (quietMs >= _quietSeconds * 1000L)
            {
                break;
            }

            if (Environment.TickCount64 >= deadline)
            {
                forced = true;
                break;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        var before = ServerStats.Take();
        if (before.CommittedBytes < _minCommittedBytes)
        {
            LastResult = $"{DateTime.Now:HH:mm} 건너뜀 (서버 메모리 {ServerStats.Mb(before.CommittedBytes)} MB < 기준 {ServerStats.Mb(_minCommittedBytes)} MB)";
            LastResultEn = $"{DateTime.Now:HH:mm} skipped (server memory {ServerStats.Mb(before.CommittedBytes)} MB < threshold {ServerStats.Mb(_minCommittedBytes)} MB)";
            DebugLog.Write("S16", $"skipped — GC committed only {ServerStats.Mb(before.CommittedBytes)} MB (threshold {ServerStats.Mb(_minCommittedBytes)} MB). {before.Memory()}");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        stopwatch.Stop();
        TelemetryHub.Increment("s16.postraid.cleanups");

        var after = ServerStats.Take();
        var waited = (Environment.TickCount64 - waitStart) / 1000.0;
        var summary =
            $"heap {ServerStats.Mb(before.HeapBytes)} → {ServerStats.Mb(after.HeapBytes)} MB, " +
            $"GC committed {ServerStats.Mb(before.CommittedBytes)} → {ServerStats.Mb(after.CommittedBytes)} MB, " +
            $"process RAM {ServerStats.Mb(before.WorkingSetBytes)} → {ServerStats.Mb(after.WorkingSetBytes)} MB, " +
            $"pause {stopwatch.ElapsedMilliseconds} ms";

        _logger?.Info($"[CompoundingPerf/S16] post-raid cleanup: {summary}");
        LastResult = $"{DateTime.Now:HH:mm} 정리함: 서버 메모리 {ServerStats.Mb(before.WorkingSetBytes)} → {ServerStats.Mb(after.WorkingSetBytes)} MB, 멈춤 {stopwatch.ElapsedMilliseconds} ms";
        LastResultEn = $"{DateTime.Now:HH:mm} cleaned: server memory {ServerStats.Mb(before.WorkingSetBytes)} → {ServerStats.Mb(after.WorkingSetBytes)} MB, paused {stopwatch.ElapsedMilliseconds} ms";
        DebugLog.Write("S16", $"done{(forced ? " (server never went quiet — ran after the max wait)" : string.Empty)}, waited {waited:0.0}s for quiet: {summary}");
    }
}
