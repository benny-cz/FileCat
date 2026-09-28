namespace FileCat.Platform.Windows.Shell;

/// <summary>
/// A short-lived worker for hostile input (plan §16.1: a picture to decode) in the Shell helper's sandbox: low integrity
/// where Windows allows it, and a job object that caps its memory, allows it no child processes, and ends it together
/// with FileCat. It talks over its standard input and output only.
/// </summary>
public sealed class SandboxedWorker : IDisposable
{
    private readonly RestrictedProcess _process;

    private SandboxedWorker(RestrictedProcess process) => _process = process;

    /// <summary>Starts <paramref name="executable"/> with <paramref name="arguments"/> (already quoted as needed).</summary>
    public static SandboxedWorker Start(string executable, string arguments, long memoryLimitBytes) =>
        new(RestrictedProcess.Start(executable, arguments, lowIntegrity: true, memoryLimitBytes));

    public Stream Input => _process.Input;
    public Stream Output => _process.Output;

    /// <summary>True when the worker runs at low integrity (it cannot write where the user's programs can).</summary>
    public bool LowIntegrity => _process.LowIntegrity;

    public bool HasExited => _process.HasExited;

    public void Kill() => _process.Kill();

    public void Dispose() => _process.Dispose();
}
