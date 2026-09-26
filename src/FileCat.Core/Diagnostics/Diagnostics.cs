using System.Diagnostics.Tracing;
using System.Security.Cryptography;
using System.Text;

namespace FileCat.Core.Diagnostics;

/// <summary>
/// Performance and lifecycle tracing compatible with standard .NET/ETW tooling (plan §19.2):
/// UI stalls, queue depth, I/O latency, and job transitions. No network use.
/// </summary>
[EventSource(Name = "FileCat")]
public sealed class FileCatEventSource : EventSource
{
    public static readonly FileCatEventSource Log = new();

    [Event(1, Level = EventLevel.Warning)]
    public void UiStall(double milliseconds) => WriteEvent(1, milliseconds);

    [Event(2, Level = EventLevel.Informational)]
    public void JobStateChanged(string jobId, string state) => WriteEvent(2, jobId, state);

    [Event(3, Level = EventLevel.Verbose)]
    public void ListingCompleted(int entries, double milliseconds) => WriteEvent(3, entries, milliseconds);

    [Event(4, Level = EventLevel.Warning)]
    public void DeviceHealth(string device, string health) => WriteEvent(4, device, health);

    [Event(5, Level = EventLevel.Verbose)]
    public void QueueDepth(string device, int depth) => WriteEvent(5, device, depth);
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Bounded local log with privacy defaults: paths are replaced by short stable hashes unless diagnostic
/// mode is enabled; credentials, Registry data, and file contents are never logged (plan §19.2).
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Lock = new();
    private static string? _file;

    public static bool DiagnosticMode { get; set; }

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "filecat.log");
        try
        {
            var fi = new FileInfo(_file);
            if (fi.Exists && fi.Length > MaxBytes) File.Move(_file, _file + ".1", overwrite: true);
        }
        catch (IOException) { }
    }

    public static string CurrentLogFile => _file ?? string.Empty;

    /// <summary>Redacts a path unless diagnostic mode is on.</summary>
    public static string P(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "<none>";
        if (DiagnosticMode) return path;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return "path#" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    public static void Write(LogLevel level, string message, Exception? ex = null)
    {
        var line = $"{DateTime.UtcNow:O} {level,-7} {message}{(ex is null ? "" : " | " + ex.GetType().Name + ": " + ex.Message)}";
        System.Diagnostics.Debug.WriteLine(line);
        if (_file is null) return;
        lock (Lock)
        {
            try { File.AppendAllText(_file, line + Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warning, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);
}
