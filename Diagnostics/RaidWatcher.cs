using System.Diagnostics;
using CompoundingPerf.Features;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;

namespace CompoundingPerf.Diagnostics;

/// <summary>
/// Raid boundaries as the server sees them: <c>LocationLifecycleService.StartLocalRaidAsync</c>
/// and <c>EndLocalRaidAsync</c>. Always installed — the post-raid cleanup (S16) is driven
/// from the end hook and cancelled by the start hook. With the debug log on it also writes
/// how long the raid-start response took (the number S15 exists to shrink) and a per-raid
/// summary: memory before/after, GC work, the slowest endpoints and bot generation.
///
/// <para>Both methods are async; the postfixes only observe the returned Task with a
/// continuation and never replace it.</para>
/// </summary>
internal static class RaidWatcher
{
    private static int _raidSequence;
    private static ServerStats? _raidStartStats;
    private static ServerStats? _menuStartStats;
    private static long _raidStartTick;
    private static string _raidLabel = "?";

    public static string Status { get; private set; } = "not installed";

    /// <summary>Incremented at every raid-start request.</summary>
    public static int RaidSequence => Volatile.Read(ref _raidSequence);

    public static void Apply(Harmony harmony, ISptLogger<CompoundingPerfMod> logger)
    {
        var start = AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.StartLocalRaidAsync));
        var end = AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.EndLocalRaidAsync));
        if (start is null || end is null)
        {
            Status = "inactive (LocationLifecycleService start/end not found)";
            logger.Warning("[CompoundingPerf] LocationLifecycleService.StartLocalRaidAsync/EndLocalRaidAsync not found — post-raid cleanup and raid summaries inactive.");
            return;
        }

        harmony.Patch(start,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(RaidWatcher), nameof(StartPrefix))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(RaidWatcher), nameof(StartPostfix))));
        harmony.Patch(end,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(RaidWatcher), nameof(EndPrefix))),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(RaidWatcher), nameof(EndPostfix))));
        Status = "ok";
    }

    /// <summary>Called when the debug log opens, so the first "between raids" line has a start.</summary>
    public static void ResetMenuBaseline() => _menuStartStats = ServerStats.Take();

    private static void StartPrefix(StartLocalRaidRequestData request, out long __state)
    {
        __state = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _raidSequence);
        if (!DebugLog.Enabled)
        {
            return;
        }

        try
        {
            var now = ServerStats.Take();
            if (_menuStartStats is not null)
            {
                DebugLog.Write("menu", $"between raids: {now.Since(_menuStartStats)}");
            }

            RequestTracker.DumpPeriod("between raids (menu)");
            BotGenerationTracker.DumpPeriod("between raids (menu)");

            _raidLabel = $"{request?.Location ?? "?"} as {request?.PlayerSide ?? "?"}";
            _raidStartStats = now;
            _raidStartTick = Environment.TickCount64;
            DebugLog.Write("raid", $"===== raid start requested: {_raidLabel} | {now.Memory()}");
        }
        catch (Exception ex)
        {
            DebugLog.Write("raid", $"start bookkeeping failed: {ex.Message}");
        }
    }

    private static void StartPostfix(long __state, Task<StartLocalRaidResponseData> __result)
    {
        if (!DebugLog.Enabled || __result is null)
        {
            return;
        }

        __result.ContinueWith(task =>
        {
            var ms = RequestTracker.ToMs(Stopwatch.GetTimestamp() - __state);
            DebugLog.Write("raid", $"raid-start response built in {ms:0} ms (loot generation + S15 collect mode {CalmRaidStart.Mode})" +
                                   (task.IsFaulted ? $" — FAILED: {task.Exception?.GetBaseException().Message}" : string.Empty));
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private static void EndPrefix(EndLocalRaidRequestData request, out long __state)
    {
        __state = Stopwatch.GetTimestamp();
        if (!DebugLog.Enabled)
        {
            return;
        }

        try
        {
            DebugLog.Write("raid", $"raid end requested: {request?.ServerId ?? "?"}, result {request?.Results?.Result?.ToString() ?? "?"}");
        }
        catch
        {
            // ignored
        }
    }

    private static void EndPostfix(long __state, Task __result)
    {
        var sequence = RaidSequence;
        if (__result is null)
        {
            PostRaidCleanup.OnRaidEnded(sequence);
            return;
        }

        __result.ContinueWith(task =>
        {
            try
            {
                if (DebugLog.Enabled)
                {
                    WriteRaidSummary(task, RequestTracker.ToMs(Stopwatch.GetTimestamp() - __state));
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write("raid", $"raid summary failed: {ex.Message}");
            }

            PostRaidCleanup.OnRaidEnded(sequence);
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private static void WriteRaidSummary(Task endTask, double endMs)
    {
        var now = ServerStats.Take();
        DebugLog.Write("raid", $"raid-end processing took {endMs:0} ms" + (endTask.IsFaulted ? $" — FAILED: {endTask.Exception?.GetBaseException().Message}" : string.Empty));

        if (_raidStartStats is not null)
        {
            var minutes = (Environment.TickCount64 - _raidStartTick) / 60000.0;
            DebugLog.Write("raid", $"===== raid summary ({_raidLabel}, {minutes:0.0} min)");
            DebugLog.Write("raid", $"memory at start: {_raidStartStats.Memory()}");
            DebugLog.Write("raid", $"memory at end:   {now.Memory()}");
            DebugLog.Write("raid", $"during the raid: {now.Since(_raidStartStats)}");
        }
        else
        {
            DebugLog.Write("raid", $"===== raid summary (raid start was not seen) | {now.Memory()}");
        }

        RequestTracker.DumpPeriod("this raid");
        BotGenerationTracker.DumpPeriod("this raid");
        _raidStartStats = null;
        _menuStartStats = now;
    }
}
