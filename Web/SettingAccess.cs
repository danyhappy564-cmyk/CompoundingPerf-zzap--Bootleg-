using System.Globalization;

namespace CompoundingPerf.Web;

/// <summary>
/// Reads and writes one <see cref="SettingsCatalog"/> entry ("section|key") on the live config records,
/// as the strings the web page sends ("true", "30", "Fastest").
/// </summary>
internal static class SettingAccess
{
    public static string Get(ServerToggles s, DebugOptions d, string id) => id switch
    {
        "S16 PostRaidCleanup|Enabled" => B(s.PostRaidCleanup.Enabled),
        "S16 PostRaidCleanup|DelaySeconds" => I(s.PostRaidCleanup.DelaySeconds),
        "S16 PostRaidCleanup|QuietSeconds" => I(s.PostRaidCleanup.QuietSeconds),
        "S16 PostRaidCleanup|MaxWaitSeconds" => I(s.PostRaidCleanup.MaxWaitSeconds),
        "S16 PostRaidCleanup|MinCommittedMb" => I(s.PostRaidCleanup.MinCommittedMb),
        "S15 RaidStartGc|Enabled" => B(s.RaidStartGc.Enabled),
        "S15 RaidStartGc|Mode" => s.RaidStartGc.Mode,
        "S8 RagfairCalmUpdates|Enabled" => B(s.RagfairCalmUpdates.Enabled),
        "S9 FastCompression|Enabled" => B(s.FastCompression.Enabled),
        "S9 FastCompression|Level" => s.FastCompression.Level,
        "S12 IsolatedBotRandomisation|Enabled" => B(s.IsolatedBotRandomisation.Enabled),
        "S13 CalmNotifier|Enabled" => B(s.CalmNotifier.Enabled),
        "S11 SaveDirtyTracking|Enabled" => B(s.SaveDirtyTracking.Enabled),
        "S11 SaveDirtyTracking|ForceSaveIntervalSeconds" => I(s.SaveDirtyTracking.ForceSaveIntervalSeconds),
        "Debug|Enabled" => B(d.Enabled),
        "Debug|SummaryIntervalMinutes" => I(d.SummaryIntervalMinutes),
        "Debug|SlowRequestMs" => I(d.SlowRequestMs),
        "Debug|KeepFiles" => I(d.KeepFiles),
        _ => throw new KeyNotFoundException(id),
    };

    /// <summary>Copies of the records with one value changed (the caller hands them to LiveConfig.Update, which clamps).</summary>
    public static (ServerToggles, DebugOptions) Set(ServerToggles s, DebugOptions d, string id, string value)
    {
        bool b() => bool.TryParse(value, out var v) ? v : throw new FormatException("expected true/false");
        int i() => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw new FormatException("expected a whole number");
        string list() => SettingsCatalog.Get(id.Split('|')[0], id.Split('|')[1]).Options.FirstOrDefault(o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase))
                         ?? throw new FormatException("not one of the choices");

        return id switch
        {
            "S16 PostRaidCleanup|Enabled" => (s with { PostRaidCleanup = s.PostRaidCleanup with { Enabled = b() } }, d),
            "S16 PostRaidCleanup|DelaySeconds" => (s with { PostRaidCleanup = s.PostRaidCleanup with { DelaySeconds = i() } }, d),
            "S16 PostRaidCleanup|QuietSeconds" => (s with { PostRaidCleanup = s.PostRaidCleanup with { QuietSeconds = i() } }, d),
            "S16 PostRaidCleanup|MaxWaitSeconds" => (s with { PostRaidCleanup = s.PostRaidCleanup with { MaxWaitSeconds = i() } }, d),
            "S16 PostRaidCleanup|MinCommittedMb" => (s with { PostRaidCleanup = s.PostRaidCleanup with { MinCommittedMb = i() } }, d),
            "S15 RaidStartGc|Enabled" => (s with { RaidStartGc = s.RaidStartGc with { Enabled = b() } }, d),
            "S15 RaidStartGc|Mode" => (s with { RaidStartGc = s.RaidStartGc with { Mode = list() } }, d),
            "S8 RagfairCalmUpdates|Enabled" => (s with { RagfairCalmUpdates = s.RagfairCalmUpdates with { Enabled = b() } }, d),
            "S9 FastCompression|Enabled" => (s with { FastCompression = s.FastCompression with { Enabled = b() } }, d),
            "S9 FastCompression|Level" => (s with { FastCompression = s.FastCompression with { Level = list() } }, d),
            "S12 IsolatedBotRandomisation|Enabled" => (s with { IsolatedBotRandomisation = s.IsolatedBotRandomisation with { Enabled = b() } }, d),
            "S13 CalmNotifier|Enabled" => (s with { CalmNotifier = s.CalmNotifier with { Enabled = b() } }, d),
            "S11 SaveDirtyTracking|Enabled" => (s with { SaveDirtyTracking = s.SaveDirtyTracking with { Enabled = b() } }, d),
            "S11 SaveDirtyTracking|ForceSaveIntervalSeconds" => (s with { SaveDirtyTracking = s.SaveDirtyTracking with { ForceSaveIntervalSeconds = i() } }, d),
            "Debug|Enabled" => (s, d with { Enabled = b() }),
            "Debug|SummaryIntervalMinutes" => (s, d with { SummaryIntervalMinutes = i() }),
            "Debug|SlowRequestMs" => (s, d with { SlowRequestMs = i() }),
            "Debug|KeepFiles" => (s, d with { KeepFiles = i() }),
            _ => throw new KeyNotFoundException(id),
        };
    }

    private static string B(bool value) => value ? "true" : "false";

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
}
