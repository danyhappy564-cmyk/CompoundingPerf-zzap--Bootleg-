using System.Text;

namespace CompoundingPerf.Diagnostics;

/// <summary>
/// The verification log: one file per server start under
/// <c>user/logs/CompoundingPerf/CompoundingPerf-debug-yyyyMMdd-HHmmss.log</c>, kept apart
/// from the server console so it can be handed over whole without the rest of the server
/// noise. Every write is a no-op while <see cref="Enabled"/> is false, so callers do not
/// need to guard (but should, when building the message itself costs something).
/// </summary>
internal static class DebugLog
{
    public static volatile bool Enabled;

    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static string? FilePath { get; private set; }

    /// <summary>Opens a new file and prunes old ones. Returns false (and stays disabled)
    /// when the folder cannot be written.</summary>
    public static bool Open(string directory, int keepFiles)
    {
        try
        {
            Directory.CreateDirectory(directory);
            Prune(directory, Math.Max(1, keepFiles) - 1);

            FilePath = Path.Combine(directory, $"CompoundingPerf-debug-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            Enabled = true;
            return true;
        }
        catch
        {
            Enabled = false;
            return false;
        }
    }

    public static void Write(string tag, string message)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] {message}");
            }
            catch
            {
                // Diagnostics must never take the server down.
            }
        }
    }

    public static void Close()
    {
        lock (Gate)
        {
            try
            {
                _writer?.Dispose();
            }
            catch
            {
                // ignored
            }

            _writer = null;
            Enabled = false;
        }
    }

    private static void Prune(string directory, int keep)
    {
        var old = new DirectoryInfo(directory)
            .GetFiles("CompoundingPerf-debug-*.log")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Skip(keep);

        foreach (var file in old)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // In use or read-only - leave it.
            }
        }
    }
}
