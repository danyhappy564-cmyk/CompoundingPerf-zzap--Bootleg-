using System.Diagnostics;
using System.Runtime;
using CompoundingPerf.Telemetry;

namespace CompoundingPerf.Diagnostics;

/// <summary>
/// Point-in-time server state for the debug log: memory (managed heap, what the GC has
/// committed from the OS, process working set), GC counts and pause time, allocation, the
/// thread pool, and every feature counter. Two snapshots give a "what happened in
/// between" line — used for the periodic summary and for the per-raid summary.
/// </summary>
internal sealed class ServerStats
{
    /// <summary>Counter keys the features increment, with the short label used in the log.</summary>
    private static readonly (string Key, string Label)[] FeatureCounters =
    [
        ("s8.ragfair.collects_skipped", "S8 ragfair GC skipped"),
        ("s9.compression.responses", "S9 fast-compressed"),
        ("s11.saves.skipped", "S11 saves skipped"),
        ("s12.randomisation.clones", "S12 clones"),
        ("s13.notifier.polls", "S13 polls"),
        ("s15.raidstart.collects_backgrounded", "S15 backgrounded"),
        ("s15.raidstart.collects_skipped", "S15 skipped"),
        ("s16.postraid.cleanups", "S16 cleanups"),
    ];

    private DateTime _at;
    private long _heap;
    private long _committed;
    private long _workingSet;
    private int _gen0;
    private int _gen1;
    private int _gen2;
    private double _pauseMs;
    private long _allocated;
    private long _requests;
    private long _slow;
    private long[] _counters = [];

    public static ServerStats Take()
    {
        var info = GC.GetGCMemoryInfo();
        long workingSet = 0;
        try
        {
            using var process = Process.GetCurrentProcess();
            workingSet = process.WorkingSet64;
        }
        catch
        {
            // ignored - shown as 0
        }

        return new ServerStats
        {
            _at = DateTime.UtcNow,
            _heap = GC.GetTotalMemory(false),
            _committed = info.TotalCommittedBytes,
            _workingSet = workingSet,
            _gen0 = GC.CollectionCount(0),
            _gen1 = GC.CollectionCount(1),
            _gen2 = GC.CollectionCount(2),
            _pauseMs = GC.GetTotalPauseDuration().TotalMilliseconds,
            _allocated = GC.GetTotalAllocatedBytes(false),
            _requests = RequestTracker.Requests,
            _slow = RequestTracker.SlowRequests,
            _counters = FeatureCounters.Select(c => TelemetryHub.Get(c.Key)).ToArray(),
        };
    }

    public long CommittedBytes => _committed;

    public long WorkingSetBytes => _workingSet;

    public long HeapBytes => _heap;

    /// <summary>"Now" part: absolute memory figures.</summary>
    public string Memory() =>
        $"heap {Mb(_heap)} MB, GC committed {Mb(_committed)} MB, process RAM {Mb(_workingSet)} MB";

    /// <summary>"What changed since <paramref name="before"/>" part.</summary>
    public string Since(ServerStats before)
    {
        var seconds = Math.Max(0.001, (_at - before._at).TotalSeconds);
        var pause = _pauseMs - before._pauseMs;
        var parts = new List<string>
        {
            $"over {Duration(seconds)}",
            $"GC gen0 {_gen0 - before._gen0} / gen1 {_gen1 - before._gen1} / gen2 {_gen2 - before._gen2}",
            seconds >= 10 ? $"GC pause {pause:0} ms ({pause / 10.0 / seconds:0.00}% of the time)" : $"GC pause {pause:0} ms",
            $"allocated {Mb(_allocated - before._allocated)} MB",
            $"RAM {Signed(Mb(_workingSet) - Mb(before._workingSet))} MB",
            $"threads {ThreadPool.ThreadCount} (queued {ThreadPool.PendingWorkItemCount})",
        };

        if (RequestTracker.TimingEnabled)
        {
            parts.Add($"requests {_requests - before._requests} (slow {_slow - before._slow})");
        }

        var features = new List<string>();
        for (var i = 0; i < FeatureCounters.Length && i < _counters.Length && i < before._counters.Length; i++)
        {
            var delta = _counters[i] - before._counters[i];
            if (delta != 0)
            {
                features.Add($"{FeatureCounters[i].Label} {delta}");
            }
        }

        parts.Add(features.Count > 0 ? "features: " + string.Join(", ", features) : "features: no activity");
        return string.Join(" | ", parts);
    }

    public static string GcConfiguration() =>
        $"ServerGC={GCSettings.IsServerGC}, concurrent={(AppContext.TryGetSwitch("System.GC.Concurrent", out var c) ? c.ToString() : "default(on)")}, " +
        $"latency={GCSettings.LatencyMode}, CPUs={Environment.ProcessorCount}, " +
        $"memory limit seen by GC {Mb(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes)} MB";

    public static long Mb(long bytes) => bytes / (1024 * 1024);

    private static string Signed(long value) => value >= 0 ? "+" + value : value.ToString();

    private static string Duration(double seconds) =>
        seconds >= 3600 ? $"{(int)(seconds / 3600)}h {(int)(seconds % 3600 / 60)}m"
        : seconds >= 60 ? $"{(int)(seconds / 60)}m {(int)(seconds % 60)}s"
        : $"{seconds:0}s";
}
