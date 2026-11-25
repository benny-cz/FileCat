using System.Diagnostics.Tracing;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.State;

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
/// mode is enabled; credentials, Registry data, and file contents are never logged (plan §19.2). The paths FileCat writes
/// go through <see cref="P"/>; those in text it does not compose (an error's message, a crash report) are found and hashed
/// the same way (I643, <see cref="LogRedaction"/>), so one path has one hash wherever it appears.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Lock = new();
    private static string? _file;

    public static bool DiagnosticMode { get; set; }

    /// <summary>
    /// Starts the log in <paramref name="directory"/>; an administrator window, or root (I655), keeps none: the folder is
    /// the ordinary account's, which could read it or turn its file into a link to elsewhere.
    /// </summary>
    public static void Initialize(string directory)
    {
        if (ProcessAuthority.IsElevatedOrRoot)
        {
            _file = null;
            return;
        }
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
        return DiagnosticMode ? path : Hash(path);
    }

    /// <summary>The short stable hash the log writes in place of a path.</summary>
    internal static string Hash(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return "path#" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>
    /// Text FileCat does not compose (a crash report, a helper's output) with its paths hashed as <see cref="P"/> hashes
    /// them, line by line, unless diagnostic mode is on (I643; <see cref="LogRedaction"/> says how, and what it keeps).
    /// </summary>
    internal static string Redact(string text) => DiagnosticMode ? text : LogRedaction.HashPaths(text);

    public static void Write(LogLevel level, string message, Exception? ex = null)
    {
        var line = Record(DateTime.UtcNow, level, message, ex, DiagnosticMode);
        System.Diagnostics.Debug.WriteLine(line);
        if (_file is null) return;
        lock (Lock)
        {
            try { File.AppendAllText(_file, line + Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// One record, on one line: its time, level and message, and an error's type and own message. Unless diagnostic mode
    /// is on, the paths in the message and in the error's text are hashed (I643): .NET's file errors quote whole paths
    /// ("Could not find file '…'"), and messages carry a server's or a helper's text as it came.
    /// </summary>
    internal static string Record(DateTime time, LogLevel level, string message, Exception? ex, bool diagnosticMode)
    {
        string text = ex is null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message;
        return OneLine($"{time:O} {level,-7} {(diagnosticMode ? text : LogRedaction.HashRecord(text))}");
    }

    /// <summary>
    /// Persist a crash only when the log path is not lower-token writable state (never in an administrator window or as
    /// root, I655). Unless diagnostic mode is on, its paths are hashed as the log's are, except the source locations of its
    /// stack frames (I643).
    /// </summary>
    public static void AppendCrash(string directory, string text)
    {
        if (ProcessAuthority.IsElevatedOrRoot) return;
        File.AppendAllText(Path.Combine(directory, "crash.log"), Redact(text));
    }

    /// <summary>
    /// One record per line: line breaks and other control characters in a message (a server's error text, a file's name)
    /// are written escaped, so no such text can pose as records of its own in a log sent for support (V23 B13).
    /// </summary>
    internal static string OneLine(string line)
    {
        static bool Control(char c) => c is < ' ' and not (char)9 or (char)0x7F or (char)0x85 or (char)0x2028 or (char)0x2029;
        if (!line.Any(Control)) return line;
        var escaped = new StringBuilder(line.Length + 16);
        foreach (char c in line)
        {
            if (!Control(c)) escaped.Append(c);
            else escaped.Append(c switch { '\r' => @"\r", '\n' => @"\n", _ => $@"\u{(int)c:x4}" });
        }
        return escaped.ToString();
    }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message, Exception? ex = null) => Write(LogLevel.Warning, message, ex);
    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);
}
