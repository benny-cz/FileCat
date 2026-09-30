using FileCat.Core.Diagnostics;

namespace FileCat.Platform.Windows.Shell;

/// <summary>
/// Talks to FileCat.ShellHost.exe, where Windows Shell handlers run instead of in FileCat (plan §8.2, TV-16). One
/// request at a time, each with a deadline: a helper that hangs or crashes is ended and replaced, the item that caused
/// it is not asked for again this session, and repeated failures turn Shell pictures off until FileCat restarts.
/// </summary>
public sealed class ShellHostClient : IDisposable
{
    public const string ExecutableName = "FileCat.ShellHost.exe";
    private const int FailuresBeforeDisabling = 3;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(2);
    /// <summary>How long a new helper may take to say it is ready (a new process, the runtime, COM, on a busy computer).</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

    private readonly string _executable;
    private readonly bool _lowIntegrity, _testFaults;
    private readonly long _memoryLimit;
    private readonly object _lock = new();
    private readonly HashSet<string> _poisoned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<DateTime> _failures = new();
    private RestrictedProcess? _process;
    private BinaryWriter? _writer;
    private BinaryReader? _reader;
    private bool _disposed;

    /// <param name="testFaults">Lets tests make the helper hang, crash, or probe its limits.</param>
    public ShellHostClient(string executable, bool lowIntegrity = true, long memoryLimitBytes = 1L << 30, bool testFaults = false)
    {
        _executable = executable;
        _lowIntegrity = lowIntegrity;
        _memoryLimit = memoryLimitBytes;
        _testFaults = testFaults;
    }

    /// <summary>The helper beside FileCat's executable, or null when this build has none.</summary>
    public static string? FindExecutable(string? directory = null)
    {
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, ExecutableName);
        return OperatingSystem.IsWindows() && File.Exists(path) ? path : null;
    }

    /// <summary>Set once the helper failed repeatedly; nothing more is asked this session.</summary>
    public string? DisabledReason { get; private set; }

    /// <summary>How many helpers were started (a replacement after a hang or crash counts).</summary>
    public int Starts { get; private set; }

    /// <summary>Whether the running helper is at low integrity; null while none runs.</summary>
    public bool? RunsAtLowIntegrity
    {
        get
        {
            lock (_lock) return _process?.LowIntegrity;
        }
    }

    public ShellImage? Get(ShellImageKind kind, string path, int size, TimeSpan timeout) =>
        Ask((byte)kind, path, size, timeout) is { Status: ShellHostProtocol.Status.Image } r ? r.Image : null;

    /// <summary>Test requests (hang, crash, spawn, integrity, write probe); only a helper started with test faults honors them.</summary>
    public string? AskTest(byte request, string argument, TimeSpan timeout) => Ask(request, argument, 1, timeout)?.Message;

    private (ShellHostProtocol.Status Status, ShellImage? Image, string? Message)? Ask(byte kind, string path, int size, TimeSpan timeout)
    {
        lock (_lock)
        {
            string key = kind + "|" + path;
            if (_disposed || DisabledReason is not null || _poisoned.Contains(key)) return null;
            if (!EnsureStarted()) return null;
            try
            {
                ShellHostProtocol.WriteRequest(_writer!, kind, Math.Clamp(size, 1, ShellHostProtocol.MaxPixels), path);
            }
            catch (IOException)
            {
                Failed(key, "The Shell helper stopped unexpectedly.");
                return null;
            }
            var reader = _reader!;
            var answer = Task.Run(() => ShellHostProtocol.ReadResponse(reader));
            bool finished;
            try { finished = answer.Wait(timeout); }
            catch (AggregateException) { finished = true; }
            if (!finished)
            {
                Failed(key, $"A Shell handler did not answer within {timeout.TotalSeconds:0.#} seconds for {Path.GetFileName(path)}; the helper was ended.");
                try { answer.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
                return null;
            }
            if (answer.IsFaulted)
            {
                Failed(key, $"The Shell helper ended while handling {Path.GetFileName(path)} (a handler crashed).");
                return null;
            }
            var result = answer.Result;
            if (result.Status == ShellHostProtocol.Status.Failed) AppLog.Info("Shell picture unavailable: " + result.Message);
            return result;
        }
    }

    private bool EnsureStarted()
    {
        if (_process is { HasExited: false }) return true;
        Reset();
        try
        {
            var args = ShellHostProtocol.ServeArgument + (_testFaults ? " " + ShellHostProtocol.TestFaultsArgument : string.Empty);
            _process = RestrictedProcess.Start(_executable, args, _lowIntegrity, _memoryLimit);
            _writer = new BinaryWriter(_process.Input);
            _reader = new BinaryReader(_process.Output);
            Starts++;
            if (!_process.LowIntegrity && _lowIntegrity) AppLog.Warn("The Shell helper runs at medium integrity: Windows refused a low-integrity start.");
            // A request's timeout measures its Shell handler: the start is waited for first, with room for a slow computer.
            var reader = _reader;
            var hello = Task.Run(() => ShellHostProtocol.ReadResponse(reader));
            bool ready;
            try { ready = hello.Wait(StartupTimeout) && hello.Result is { Status: ShellHostProtocol.Status.Text, Message: ShellHostProtocol.ReadyMessage }; }
            catch (AggregateException) { ready = false; }
            if (ready) return true;
            Failure($"The Shell helper did not say it was ready within {StartupTimeout.TotalSeconds:0} seconds; it was ended.");
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            DisabledReason = "The Shell helper could not be started: " + ex.Message;
            AppLog.Error(DisabledReason, ex);
            return false;
        }
    }

    /// <summary>A request that hung or crashed the helper: that item is not asked for again.</summary>
    private void Failed(string key, string message)
    {
        _poisoned.Add(key);
        Failure(message);
    }

    private void Failure(string message)
    {
        AppLog.Warn(message);
        Reset();
        var now = DateTime.UtcNow;
        _failures.Enqueue(now);
        while (_failures.Count > 0 && now - _failures.Peek() > FailureWindow) _failures.Dequeue();
        if (_failures.Count >= FailuresBeforeDisabling)
            DisabledReason = $"Shell pictures are off until FileCat restarts: the helper failed {FailuresBeforeDisabling} times within {FailureWindow.TotalMinutes:0} minutes. Last: {message}";
    }

    private void Reset()
    {
        _process?.Dispose();
        _process = null;
        _writer = null;
        _reader = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            Reset();
        }
    }
}
