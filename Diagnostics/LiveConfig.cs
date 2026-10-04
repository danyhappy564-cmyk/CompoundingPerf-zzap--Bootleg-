using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CompoundingPerf.Features;
using SPTarkov.Common.Models.Logging;

namespace CompoundingPerf.Diagnostics;

/// <summary>
/// The live settings: what config.json said at startup, plus every change made since from
/// the client's F12 menu (<see cref="SettingsRouter"/>). A change is applied immediately —
/// each feature's switch is a field read on every call — and written back to config.json
/// so the next server start keeps it.
///
/// <para>Only the <c>Server</c> and <c>Debug</c> sections are live. <c>MasterEnabled</c>
/// decides whether the patches are installed at all, so it stays a config.json-plus-restart
/// setting.</para>
/// </summary>
internal static class LiveConfig
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Keep the notes' em dashes and arrows readable instead of —.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static CompoundingPerfConfig _current = new();
    private static string? _path;
    private static ISptLogger<CompoundingPerfMod>? _logger;

    public static string RouteStatus { get; set; } = "not registered";

    public static CompoundingPerfConfig Current
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
    }

    public static void Init(CompoundingPerfConfig config, string configPath, ISptLogger<CompoundingPerfMod> logger)
    {
        _current = config;
        _path = configPath;
        _logger = logger;
    }

    /// <summary>Startup: apply everything, with the usual one line per feature.</summary>
    public static void ApplyAtStartup(CompoundingPerfConfig config)
    {
        Push(config.Server, config.Debug, config, _logger);
    }

    /// <summary>A change from the F12 menu. Returns a short description of what changed
    /// ("" when nothing did) and whether config.json was written.</summary>
    public static (string changes, bool saved) Update(ServerToggles? server, DebugOptions? debug)
    {
        lock (Gate)
        {
            var next = _current with
            {
                Server = Sanitize(server ?? _current.Server),
                Debug = Sanitize(debug ?? _current.Debug),
            };

            var changes = Diff(_current, next);
            if (changes.Length == 0)
            {
                return ("", false);
            }

            _current = next;

            // Quiet: one summary line below instead of every feature's banner.
            if (next.MasterEnabled)
            {
                Push(next.Server, next.Debug, next, null);
            }

            var saved = Save(next);
            _logger?.Info($"[CompoundingPerf] F12 settings applied{(saved ? " and saved" : " (NOT saved to config.json)")}: {changes}");
            DebugLog.Write("F12", $"applied{(saved ? ", saved to config.json" : ", config.json NOT written")}: {changes}");
            return (changes, saved);
        }
    }

    private static void Push(ServerToggles s, DebugOptions debug, CompoundingPerfConfig whole, ISptLogger<CompoundingPerfMod>? logger)
    {
        CalmRagfair.Configure(s.RagfairCalmUpdates, logger);
        FastCompression.Configure(s.FastCompression, logger);
        SaveDirtyTracking.Configure(s.SaveDirtyTracking, logger);
        IsolatedBotRandomisation.Configure(s.IsolatedBotRandomisation, logger);
        CalmNotifier.Configure(s.CalmNotifier, logger);
        CalmRaidStart.Configure(s.RaidStartGc, logger);
        PostRaidCleanup.Configure(s.PostRaidCleanup, logger);
        DebugControl.Apply(debug, whole);
    }

    /// <summary>Clamp to the same ranges the F12 sliders use (keep the two in step, or a
    /// value the menu cannot show gets clamped and sent back on the next edit). Applied to
    /// config.json at startup too.</summary>
    internal static ServerToggles Sanitize(ServerToggles s) => s with
    {
        RagfairCalmUpdates = s.RagfairCalmUpdates ?? new(),
        FastCompression = (s.FastCompression ?? new()) with { Level = FastCompression.ParseLevel(s.FastCompression?.Level).ToString() },
        SaveDirtyTracking = (s.SaveDirtyTracking ?? new()) with
        {
            // Must stay above SPT's 60 s save tick or the skip never fires.
            ForceSaveIntervalSeconds = Math.Clamp(s.SaveDirtyTracking?.ForceSaveIntervalSeconds ?? 300, 90, 1800),
        },
        IsolatedBotRandomisation = s.IsolatedBotRandomisation ?? new(),
        CalmNotifier = s.CalmNotifier ?? new(),
        RaidStartGc = (s.RaidStartGc ?? new()) with { Mode = CalmRaidStart.ParseMode(s.RaidStartGc?.Mode).ToString() },
        PostRaidCleanup = (s.PostRaidCleanup ?? new()) with
        {
            DelaySeconds = Math.Clamp(s.PostRaidCleanup?.DelaySeconds ?? 30, 0, 600),
            QuietSeconds = Math.Clamp(s.PostRaidCleanup?.QuietSeconds ?? 3, 0, 60),
            MaxWaitSeconds = Math.Clamp(s.PostRaidCleanup?.MaxWaitSeconds ?? 120, 0, 1800),
            MinCommittedMb = Math.Clamp(s.PostRaidCleanup?.MinCommittedMb ?? 512, 0, 16384),
        },
    };

    internal static DebugOptions Sanitize(DebugOptions d) => d with
    {
        SummaryIntervalMinutes = Math.Clamp(d.SummaryIntervalMinutes, 0, 60),
        SlowRequestMs = Math.Clamp(d.SlowRequestMs, 50, 5000),
        KeepFiles = Math.Clamp(d.KeepFiles, 1, 50),
    };

    /// <summary>"Server.FastCompression.Level Fastest → Optimal, Debug.Enabled false → true".</summary>
    internal static string Diff(CompoundingPerfConfig before, CompoundingPerfConfig after)
    {
        var a = Flatten(JsonSerializer.SerializeToNode(new { before.Server, before.Debug }));
        var b = Flatten(JsonSerializer.SerializeToNode(new { after.Server, after.Debug }));
        return string.Join(", ", b
            .Where(kv => !a.TryGetValue(kv.Key, out var old) || old != kv.Value)
            .Select(kv => $"{kv.Key} {(a.TryGetValue(kv.Key, out var old) ? old : "?")} → {kv.Value}"));
    }

    private static Dictionary<string, string> Flatten(JsonNode? node, string prefix = "", Dictionary<string, string>? into = null)
    {
        into ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is JsonObject obj)
        {
            foreach (var (key, child) in obj)
            {
                Flatten(child, prefix.Length == 0 ? key : $"{prefix}.{key}", into);
            }
        }
        else
        {
            into[prefix] = node?.ToJsonString() ?? "null";
        }

        return into;
    }

    /// <summary>Write the live Server and Debug sections over config.json's values, keeping
    /// everything else in the file — the <c>_..._note</c> explanations included.</summary>
    private static bool Save(CompoundingPerfConfig config)
    {
        if (_path is null)
        {
            return false;
        }

        try
        {
            var file = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject ?? new JsonObject();
            Overlay(file, "Server", JsonSerializer.SerializeToNode(config.Server));
            Overlay(file, "Debug", JsonSerializer.SerializeToNode(config.Debug));

            var temp = _path + ".tmp";
            File.WriteAllText(temp, file.ToJsonString(WriteOptions));
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Warning($"[CompoundingPerf] could not write config.json ({ex.Message}) — the F12 change is live but will be lost on restart");
            return false;
        }
    }

    internal static void Overlay(JsonObject target, string key, JsonNode? source)
    {
        if (source is JsonObject sourceObject && target[key] is JsonObject targetObject)
        {
            foreach (var (childKey, child) in sourceObject.ToList())
            {
                Overlay(targetObject, childKey, child);
            }

            return;
        }

        target[key] = source?.DeepClone();
    }
}
