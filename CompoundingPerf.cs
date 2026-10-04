using System.Reflection;
using CompoundingPerf.Diagnostics;
using CompoundingPerf.Features;
using CompoundingPerf.Telemetry;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CompoundingPerf;

/// <summary>SPT mod metadata. No package.json — this record replaces it. 4.1 swapped the
/// abstract <c>AbstractModMetadata</c> base for the <c>IModMetadata</c> interface and
/// added <c>HasPrepatcher</c>.</summary>
public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = CompoundingPerfMod.ModGuid;
    public string Name { get; init; } = "CompoundingPerf";
    public string Author { get; init; } = "EchoStarz";
    public SemanticVersioning.Version Version { get; init; } = new("2.1.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.5");
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;

    public List<string>? Contributors { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
}

/// <summary>
/// Server-side entry for CompoundingPerf. Reads <c>config.json</c> and installs the
/// individual features.
///
/// <para><b>Why everything is Harmony now.</b> Through 4.0 every feature here was a DI
/// subclass registered with <c>Injectable.TypeOverride</c> — normal virtual dispatch, no
/// IL surgery, and other mods' Harmony patches on those classes kept working because our
/// subclass <i>was</i> the object they patched. SPT 4.1 removed that option: the
/// <c>TypeOverride</c> property is gone from the attribute, <c>SaveServer</c>,
/// <c>RagfairServer</c>, <c>RandomUtil</c> and <c>SptWebSocketConnectionHandler</c> are
/// sealed, and not one of the methods this mod used to override is virtual any more.
/// Deriving and overriding is simply not a thing that can be done on 4.1.</para>
///
/// <para>So the surviving features are Harmony patches, kept as small as the job allows —
/// two of them rewrite a single call or constant rather than replace a method body, which
/// makes them behaviour-neutral by construction instead of by careful re-implementation.
/// Six features were retired rather than translated, because 4.1 does the job itself; see
/// <c>ServerToggles</c> for the per-feature evidence.</para>
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 50)]
public class CompoundingPerfMod(
    ISptLogger<CompoundingPerfMod> logger,
    ModHelper modHelper,
    ICloner cloner,
    NotifierHelper notifierHelper,
    NotificationService notificationService) : IOnLoad
{
    public const string ModGuid = "com.echostarz.compoundingperf";

    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var config = modHelper.GetJsonDataFromFile<CompoundingPerfConfig>(modPath, "config.json");

            TelemetryHub.TimingEnabled = config.Telemetry.TimingEnabled;
            OpenDebugLog(config.Debug, modPath);

#if BENCH
            // Dev benchmark builds only: server-side GC/counter sampler. Runs on BOTH
            // sides of an A/B (it tags each line with masterEnabled), so start it
            // before the master-switch bail below.
            BenchRecorder.Start(logger, config.MasterEnabled);
#endif

            // Master A/B switch. Unlike 4.0 this now also decides whether the patches are
            // installed at all: a Harmony patch cannot be uninstalled per-feature the way
            // a DI override could be left inert, and every feature keeps its own runtime
            // kill-switch anyway.
            if (!config.MasterEnabled)
            {
                logger.Warning("[CompoundingPerf] MASTER SWITCH OFF — no patches installed (benchmark baseline mode). Flip MasterEnabled to true to re-enable.");
                DebugLog.Write("startup", "MasterEnabled=false — no patches installed, nothing else will be logged");
                return Task.CompletedTask;
            }

            var harmony = new Harmony(ModGuid);

            CalmRagfair.Apply(harmony, logger);                                            // S8
            FastCompression.Apply(harmony, logger);                                        // S9
            SaveDirtyTracking.Apply(harmony, logger);                                      // S11
            IsolatedBotRandomisation.Apply(harmony, cloner, logger);                       // S12
            CalmNotifier.Apply(harmony, notifierHelper, notificationService, logger);      // S13
            CalmRaidStart.Apply(harmony, logger);                                          // S15
            RequestTracker.Apply(harmony, DebugLog.Enabled, logger);                       // S16 quiet detection + debug timing
            RaidWatcher.Apply(harmony, logger);                                            // S16 trigger + debug raid summaries
            if (DebugLog.Enabled)
            {
                BotGenerationTracker.Apply(harmony);
            }

            CalmRagfair.Configure(config.Server.RagfairCalmUpdates, logger);
            FastCompression.Configure(config.Server.FastCompression, logger);
            SaveDirtyTracking.Configure(config.Server.SaveDirtyTracking, logger);
            IsolatedBotRandomisation.Configure(config.Server.IsolatedBotRandomisation, logger);
            CalmNotifier.Configure(config.Server.CalmNotifier, logger);
            CalmRaidStart.Configure(config.Server.RaidStartGc, logger);
            PostRaidCleanup.Configure(config.Server.PostRaidCleanup, logger);

            WriteSelfCheck(config);
            StartPeriodicSummary(config.Debug);

            logger.Success("[CompoundingPerf] server-side features loaded");
        }
        catch (Exception ex)
        {
            logger.Error($"[CompoundingPerf] failed to load: {ex}");
            DebugLog.Write("startup", $"LOAD FAILED: {ex}");
        }

        return Task.CompletedTask;
    }

    private void OpenDebugLog(DebugOptions options, string modPath)
    {
        if (!options.Enabled)
        {
            return;
        }

        // The server runs with SPT_Runtime as its working directory; fall back to the mod
        // folder if that ever stops being true.
        var userLogs = Path.Combine(Directory.GetCurrentDirectory(), "user", "logs");
        var directory = Directory.Exists(userLogs) ? Path.Combine(userLogs, "CompoundingPerf") : Path.Combine(modPath, "logs");
        if (!DebugLog.Open(directory, options.KeepFiles))
        {
            logger.Warning($"[CompoundingPerf] debug log is on but {directory} could not be written — debug log disabled");
            return;
        }

        RequestTracker.SlowRequestMs = Math.Max(1, options.SlowRequestMs);
        logger.Info($"[CompoundingPerf] debug log ON → {DebugLog.FilePath}");
        DebugLog.Write("startup", $"CompoundingPerf {new ModMetadata().Version} debug log | {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        DebugLog.Write("startup", $"GC: {ServerStats.GcConfiguration()}");
        DebugLog.Write("startup", $"memory: {ServerStats.Take().Memory()}");

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            DebugLog.Write("shutdown", $"server stopping | {ServerStats.Take().Memory()}");
            RequestTracker.DumpPeriod("since the last raid boundary");
            DebugLog.Close();
        };
    }

    private static void WriteSelfCheck(CompoundingPerfConfig config)
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
        DebugLog.Write("selfcheck", $"debug   bot generation timing {BotGenerationTracker.Status}, slow request ≥ {RequestTracker.SlowRequestMs} ms, summary every {config.Debug.SummaryIntervalMinutes} min");
    }

    private static string On(bool value) => value ? "ON" : "off";

    private static Timer? _summaryTimer;

    private static void StartPeriodicSummary(DebugOptions options)
    {
        if (!DebugLog.Enabled || options.SummaryIntervalMinutes <= 0)
        {
            return;
        }

        var previous = ServerStats.Take();
        var interval = TimeSpan.FromMinutes(options.SummaryIntervalMinutes);
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
