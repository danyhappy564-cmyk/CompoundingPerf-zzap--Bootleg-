using System.Collections.Concurrent;
using System.Diagnostics;
using HarmonyLib;
using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers.Http;

namespace CompoundingPerf.Diagnostics;

/// <summary>
/// Watches <c>SptHttpListener.HandleAsync</c> — the whole life of one SPT request: read and
/// inflate the body, route, build the response, compress, send. Two jobs:
///
/// <list type="bullet">
///   <item><b>Activity stamp</b> (always on, one <c>Volatile.Write</c>): when the player last
///     did something, so the post-raid cleanup (S16) can wait for a quiet moment. Pings,
///     keepalives and notifier polls are background chatter and do not count.</item>
///   <item><b>Timing</b> (debug log only): per-path count / total / worst, slow requests
///     logged as they happen, and a per-period table dumped at raid start and raid end.</item>
/// </list>
///
/// <para><c>HandleAsync</c> is async, so the postfix runs when the Task is handed back, not
/// when the request is done. The postfix therefore swaps the Task for one that awaits the
/// original and then records — the pattern this repo's CLAUDE.md calls for.</para>
/// </summary>
internal static class RequestTracker
{
    private const int MaxPaths = 300;
    private const int MaxSlowLinesPerPeriod = 100;

    private static readonly string[] BackgroundPrefixes =
    [
        "/client/notifier",
        "/notifierServer",
        "/client/game/keepalive",
        "/launcher/ping",
        "/fika/update/ping",
        "/client/putMetrics",
    ];

    private sealed class PathStat
    {
        public long Count;
        public long TotalTicks;
        public long MaxTicks;
    }

    private static ConcurrentDictionary<string, PathStat> _period = new(StringComparer.Ordinal);
    private static long _lastActivityTick = Environment.TickCount64;
    private static int _slowLines;
    private static int _slowSuppressed;
    private static long _requests;
    private static long _slowRequests;

    public static volatile bool TimingEnabled;
    public static int SlowRequestMs = 250;

    public static string Status { get; private set; } = "not installed";

    /// <summary><see cref="Environment.TickCount64"/> of the last non-background request.</summary>
    public static long LastActivityTick => Volatile.Read(ref _lastActivityTick);

    public static long Requests => Interlocked.Read(ref _requests);

    public static long SlowRequests => Interlocked.Read(ref _slowRequests);

    public static void Apply(Harmony harmony, bool timing, ISptLogger<CompoundingPerfMod> logger)
    {
        var target = AccessTools.Method(typeof(SptHttpListener), nameof(SptHttpListener.HandleAsync));
        if (target is null)
        {
            Status = "inactive (SptHttpListener.HandleAsync not found)";
            logger.Warning("[CompoundingPerf] SptHttpListener.HandleAsync not found — request activity cannot be observed; post-raid cleanup will not wait for a quiet moment.");
            return;
        }

        TimingEnabled = timing;
        harmony.Patch(
            target,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(RequestTracker), nameof(Prefix))),
            postfix: timing ? new HarmonyMethod(AccessTools.Method(typeof(RequestTracker), nameof(Postfix))) : null);
        Status = timing ? "ok (activity + timing)" : "ok (activity only)";
    }

    private static void Prefix(HttpContext context, out long __state)
    {
        __state = 0;
        try
        {
            var path = context?.Request.Path.Value;
            if (path is not null && !IsBackground(path))
            {
                Volatile.Write(ref _lastActivityTick, Environment.TickCount64);
            }

            if (TimingEnabled)
            {
                __state = Stopwatch.GetTimestamp();
            }
        }
        catch
        {
            // Observation only - never break request handling.
        }
    }

    private static void Postfix(HttpContext context, long __state, ref Task __result)
    {
        if (__state == 0 || !TimingEnabled)
        {
            return;
        }

        string path;
        string method;
        try
        {
            path = context?.Request.Path.Value ?? "?";
            method = context?.Request.Method ?? "?";
        }
        catch
        {
            return;
        }

        if (__result.IsCompleted)
        {
            Record(method, path, __state);
            return;
        }

        __result = AwaitThenRecord(__result, method, path, __state);
    }

    private static async Task AwaitThenRecord(Task inner, string method, string path, long start)
    {
        try
        {
            await inner.ConfigureAwait(false);
        }
        finally
        {
            Record(method, path, start);
        }
    }

    private static void Record(string method, string path, long start)
    {
        try
        {
            var ticks = Stopwatch.GetTimestamp() - start;
            Interlocked.Increment(ref _requests);

            var key = Normalize(path);
            var period = _period;
            if (!period.TryGetValue(key, out var stat))
            {
                stat = period.Count < MaxPaths ? period.GetOrAdd(key, _ => new PathStat()) : period.GetOrAdd("(other)", _ => new PathStat());
            }

            Interlocked.Increment(ref stat.Count);
            Interlocked.Add(ref stat.TotalTicks, ticks);
            long seen;
            while (ticks > (seen = Interlocked.Read(ref stat.MaxTicks)) && Interlocked.CompareExchange(ref stat.MaxTicks, ticks, seen) != seen)
            {
            }

            var ms = ToMs(ticks);
            if (ms >= SlowRequestMs && !IsBackground(path))
            {
                Interlocked.Increment(ref _slowRequests);
                if (Interlocked.Increment(ref _slowLines) <= MaxSlowLinesPerPeriod)
                {
                    DebugLog.Write("slow", $"{ms:0} ms  {method} {path}");
                }
                else
                {
                    Interlocked.Increment(ref _slowSuppressed);
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    /// <summary>Writes the table for the period that just ended (top paths by total time)
    /// and starts a new one.</summary>
    public static void DumpPeriod(string label, int top = 12)
    {
        var period = Interlocked.Exchange(ref _period, new ConcurrentDictionary<string, PathStat>(StringComparer.Ordinal));
        var suppressed = Interlocked.Exchange(ref _slowSuppressed, 0);
        Interlocked.Exchange(ref _slowLines, 0);
        if (!DebugLog.Enabled || period.IsEmpty)
        {
            return;
        }

        var count = period.Values.Sum(s => s.Count);
        var totalMs = period.Values.Sum(s => ToMs(s.TotalTicks));
        DebugLog.Write("requests", $"{label}: {count} requests, {totalMs / 1000.0:0.0} s total server time across {period.Count} paths" +
                                   (suppressed > 0 ? $" ({suppressed} more slow requests not listed individually)" : string.Empty));

        foreach (var (path, stat) in period.OrderByDescending(kv => kv.Value.TotalTicks).Take(top))
        {
            DebugLog.Write("requests", $"  {ToMs(stat.TotalTicks),8:0} ms total  {stat.Count,5}x  avg {ToMs(stat.TotalTicks) / Math.Max(1, stat.Count),6:0.0}  max {ToMs(stat.MaxTicks),6:0}  {path}");
        }
    }

    internal static bool IsBackground(string path)
    {
        foreach (var prefix in BackgroundPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Folds per-item paths together so the table groups by endpoint: 24-char
    /// Mongo ids become <c>{id}</c>, and anything under <c>/files/</c> is one bucket.</summary>
    internal static string Normalize(string path)
    {
        if (path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase))
        {
            return "/files/*";
        }

        if (path.IndexOf('?') is var q and >= 0)
        {
            path = path[..q];
        }

        var segments = path.Split('/');
        var changed = false;
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length >= 20 && segments[i].All(Uri.IsHexDigit))
            {
                segments[i] = "{id}";
                changed = true;
            }
        }

        return changed ? string.Join('/', segments) : path;
    }

    internal static double ToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
