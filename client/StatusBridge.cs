namespace CompoundingPerf.Client;

/// <summary>
/// Read-only window for other plugins (RAM Cleaner's web page shows this mod's settings and status).
/// They find it by name through reflection, so neither mod references the other. The settings
/// themselves are ordinary F12 entries in this plugin's config file; changing one there sends it
/// to the server exactly like an F12 edit. Bump <see cref="Version"/> if a member changes meaning.
/// </summary>
public static class StatusBridge
{
    public const int Version = 1;

    /// <summary>Connected to the server mod and settings loaded.</summary>
    public static bool Connected => ServerSettingsMenu.Instance?.Connected ?? false;

    /// <summary>The F12 status lines (connection, server version and memory, last cleanup), current language.</summary>
    public static string Status => ServerSettingsMenu.Instance?.StatusText ?? "";

    /// <summary>What the last change did on the server ("" before the first change).</summary>
    public static string LastResult => ServerSettingsMenu.Instance?.LastResult ?? "";

    /// <summary>Read the settings from the server again on the next frame.</summary>
    public static void Refresh() => ServerSettingsMenu.Instance?.RequestRefresh();
}
