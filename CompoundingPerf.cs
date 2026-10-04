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
    public SemanticVersioning.Version Version { get; init; } = new("2.2.0");
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
            config = config with
            {
                Server = LiveConfig.Sanitize(config.Server ?? new ServerToggles()),
                Debug = LiveConfig.Sanitize(config.Debug ?? new DebugOptions()),
            };
            LiveConfig.Init(config, Path.Combine(modPath, "config.json"), logger);
            LiveConfig.RouteStatus = "ok (/compoundingperf/config/get|set)";

            // The server runs with SPT_Runtime as its working directory; fall back to the mod
            // folder if that ever stops being true.
            var userLogs = Path.Combine(Directory.GetCurrentDirectory(), "user", "logs");
            DebugControl.Init(
                Directory.Exists(userLogs) ? Path.Combine(userLogs, "CompoundingPerf") : Path.Combine(modPath, "logs"),
                message => logger.Info(message));
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DebugControl.OnShutdown();

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
                DebugControl.Apply(config.Debug, config);
                DebugLog.Write("startup", "MasterEnabled=false — no patches installed, F12 switches have no effect until it is true again");
                return Task.CompletedTask;
            }

            var harmony = new Harmony(ModGuid);

            CalmRagfair.Apply(harmony, logger);                                            // S8
            FastCompression.Apply(harmony, logger);                                        // S9
            SaveDirtyTracking.Apply(harmony, logger);                                      // S11
            IsolatedBotRandomisation.Apply(harmony, cloner, logger);                       // S12
            CalmNotifier.Apply(harmony, notifierHelper, notificationService, logger);      // S13
            CalmRaidStart.Apply(harmony, logger);                                          // S15
            RequestTracker.Apply(harmony, logger);                                         // S16 quiet detection + debug timing
            RaidWatcher.Apply(harmony, logger);                                            // S16 trigger + debug raid summaries
            BotGenerationTracker.Apply(harmony);                                           // debug only (gated at runtime)

            // Every switch is read at call time, so the F12 menu can flip any of them later.
            LiveConfig.ApplyAtStartup(config);

            logger.Success("[CompoundingPerf] server-side features loaded");
        }
        catch (Exception ex)
        {
            logger.Error($"[CompoundingPerf] failed to load: {ex}");
            DebugLog.Write("startup", $"LOAD FAILED: {ex}");
        }

        return Task.CompletedTask;
    }
}
