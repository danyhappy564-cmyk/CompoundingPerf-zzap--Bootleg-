using CompoundingPerf.Features;

namespace CompoundingPerf.Diagnostics;

/// <summary>
/// Turns the debug log on and off — at startup from config.json, or at runtime from the
/// client's F12 menu. Every hook the log reads from is installed unconditionally at
/// startup and checks the flags here, so switching needs no restart: turning it on opens
/// a new file (header + self-check), turning it off closes it.
/// </summary>
internal static class DebugControl
{
    private static readonly object Gate = new();
    private static Timer? _summaryTimer;
    private static string _directory = "";
    private static Action<string>? _announce;

    /// <summary>Where the file goes; called once at startup.</summary>
    public static void Init(string directory, Action<string> announce)
    {
        _directory = directory;
        _announce = announce;
    }

    public static void Apply(DebugOptions options, CompoundingPerfConfig config)
    {
        lock (Gate)
        {
            RequestTracker.SlowRequestMs = Math.Max(1, options.SlowRequestMs);

            if (options.Enabled && !DebugLog.Enabled)
            {
                if (!DebugLog.Open(_directory, options.KeepFiles))
                {
                    _announce?.Invoke($"[CompoundingPerf] debug log is on but {_directory} could not be written — debug log disabled");
                    return;
                }

                _announce?.Invoke($"[CompoundingPerf] debug log ON → {DebugLog.FilePath}");
                DebugLog.Write("startup", $"CompoundingPerf {new ModMetadata().Version} debug log | {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
                DebugLog.Write("startup", $"GC: {ServerStats.GcConfiguration()}");
                DebugLog.Write("startup", $"memory: {ServerStats.Take().Memory()}");
                WriteSelfCheck(config);
                RaidWatcher.ResetMenuBaseline();
            }
            else if (!options.Enabled && DebugLog.Enabled)
            {
                DebugLog.Write("shutdown", "debug log switched off");
                DebugLog.Close();
                _announce?.Invoke("[CompoundingPerf] debug log OFF");
            }

            RequestTracker.TimingEnabled = DebugLog.Enabled;
            RestartSummary(DebugLog.Enabled ? options.SummaryIntervalMinutes : 0);
        }
    }

    /// <summary>Last words on server shutdown.</summary>
    public static void OnShutdown()
    {
        if (!DebugLog.Enabled)
        {
            return;
        }

        DebugLog.Write("shutdown", $"server stopping | {ServerStats.Take().Memory()}");
        RequestTracker.DumpPeriod("since the last raid boundary");
        DebugLog.Close();
    }

    public static void WriteSelfCheck(CompoundingPerfConfig config)
    {
        if (!DebugLog.Enabled)
        {
            return;
        }

        var s = config.Server;
        DebugLog.Write("selfcheck", "patch = did the code hook land in SPT 4.1.5 | config = is the feature switched on");
        DebugLog.Write("selfcheck", $"S8  calm ragfair GC     patch {CalmRagfair.Status} | config {On(s.RagfairCalmUpdates.Enabled)}");
        DebugLog.Write("selfcheck", $"S9  fast compression    patch {FastCompression.Status} | config {On(s.FastCompression.Enabled)} level {FastCompression.Level}");
        DebugLog.Write("selfcheck", $"S11 save dirty-tracking patch {SaveDirtyTracking.Status} | config {On(s.SaveDirtyTracking.Enabled)}");
        DebugLog.Write("selfcheck", $"S12 isolated bot random patch {IsolatedBotRandomisation.Status} | config {On(s.IsolatedBotRandomisation.Enabled)}");
        DebugLog.Write("selfcheck", $"S13 calm notifier       patch {CalmNotifier.Status} | config {On(s.CalmNotifier.Enabled)}");
        DebugLog.Write("selfcheck", $"S15 raid-start GC       patch {CalmRaidStart.Status} | config {On(s.RaidStartGc.Enabled)} mode {CalmRaidStart.Mode}");
        DebugLog.Write("selfcheck", $"S16 post-raid cleanup   raid hooks {RaidWatcher.Status}, request hook {RequestTracker.Status} | config {On(s.PostRaidCleanup.Enabled)}" +
                                    $" (delay {s.PostRaidCleanup.DelaySeconds}s, quiet {s.PostRaidCleanup.QuietSeconds}s, max wait {s.PostRaidCleanup.MaxWaitSeconds}s, min {s.PostRaidCleanup.MinCommittedMb} MB)");
        DebugLog.Write("selfcheck", $"F12     settings route {LiveConfig.RouteStatus}");
        DebugLog.Write("selfcheck", BotModsLine());
        DebugLog.Write("selfcheck", $"debug   bot generation timing {BotGenerationTracker.Status}, slow request ≥ {RequestTracker.SlowRequestMs} ms, summary every {config.Debug.SummaryIntervalMinutes} min");
    }

    private static string On(bool value) => value ? "ON" : "off";

    /// <summary>APBS generates PMC gear and ABPS places spawns; both change what the bot
    /// numbers in this log mean, so say whether they are loaded.</summary>
    private static string BotModsLine()
    {
        var names = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? "").ToList();
        var apbs = names.Any(n => n.StartsWith("acidphantasm-progressivebotsystem", StringComparison.OrdinalIgnoreCase));
        var abps = names.Any(n => n.StartsWith("acidphantasm-botplacementsystem", StringComparison.OrdinalIgnoreCase));
        return "bot mods APBS " + (apbs ? "loaded (its gear generation is inside the [bots] times; S12 leaves its read-only calls alone)" : "not loaded") +
               " | ABPS " + (abps ? "loaded (its raid start/end hooks are on the same requests: in the [requests] table, not in 'raid-start response built')" : "not loaded");
    }

    private static void RestartSummary(int minutes)
    {
        _summaryTimer?.Dispose();
        _summaryTimer = null;
        if (minutes <= 0)
        {
            return;
        }

        var previous = ServerStats.Take();
        var interval = TimeSpan.FromMinutes(minutes);
        _summaryTimer = new Timer(_ =>
        {
            try
            {
                var now = ServerStats.Take();
                DebugLog.Write("summary", $"{now.Memory()} | {now.Since(previous)}");
                previous = now;
            }
            catch (Exception ex)
            {
                DebugLog.Write("summary", $"failed: {ex.Message}");
            }
        }, null, interval, interval);
    }
}
