using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using FileCat.App;
using FileCat.App.Services;
using FileCat.Core.Archives;

namespace FileCat.ArchiveQualificationHost;

/// <summary>
/// Never shipped. Usage: FileCat.ArchiveQualificationHost --data &lt;throwaway folder&gt; --worker &lt;published FileCat executable&gt;
/// [FileCat arguments]. The interface is the application's own; only its archive backends are composed explicitly. On macOS,
/// which has no production worker launcher, the host starts itself as the worker (no worker isolation there: these runs
/// qualify the interface, not containment).
/// </summary>
internal static class QualificationHost
{
    private const string WorkerArgument = "--qualification-worker";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == WorkerArgument) return RunWorker(args[1..]);
        string? data = null, worker = null; var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data" && i + 1 < args.Length) data = args[++i];
            else if (args[i] == "--worker" && i + 1 < args.Length) worker = args[++i];
            else rest.Add(args[i]);
        }
        string self = Environment.ProcessPath ?? throw new InvalidOperationException("The host executable path is unknown.");
        if (data is null || !Path.IsPathFullyQualified(data) || !OperatingSystem.IsMacOS() && (worker is null || !File.Exists(worker)))
        {
            Console.Error.WriteLine("Usage: FileCat.ArchiveQualificationHost --data <throwaway folder> --worker <published FileCat executable> [FileCat arguments]");
            return 2;
        }
        Directory.CreateDirectory(data);
        // On Windows the worker runs in FileCat's archive AppContainer, which may read only a provisioned bundle; an
        // installer grants that once, and this test-only host does it for the published worker it is given.
        if (OperatingSystem.IsWindows()) Platform.Windows.Shell.ArchiveAppContainer.ProvisionRuntimeReadAccess(Path.GetDirectoryName(worker!)!);
        App.App.StartupOptions = StartupOptions.Parse([.. rest, "--data", data]);
        App.App.QualificationServices = startup => AppServices.InitializeForQualification(
            Core.State.AppPaths.Resolve(startup.Profile, dataRoot: startup.DataRoot),
            AppServices.QualificationBackends(worker ?? self, OperatingSystem.IsMacOS() ? (nonce, role) => SelfWorker.Start(self, nonce, role) : null));
        return App.Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime([.. rest]);
    }

    // The fixed worker roles, as the production worker and the test probe dispatch them.
    private static int RunWorker(string[] args)
    {
        if (args.Length != 4 || args[2] != ArchiveWorkerCancellation.Argument || !Guid.TryParse(args[1], out Guid nonce) || nonce == Guid.Empty) return 64;
        long handle;
        try { handle = ArchiveWorkerCancellation.Parse(args[2], args[3]); }
        catch (ArgumentException) { return 64; }
        return args[0] switch
        {
            "archive-assess-v2" or "archive-transform-v2" or "archive-verify-v2" => ArchiveWorkerCancellation.Run(handle, (input, output, ct) => args[0] switch
            {
                "archive-transform-v2" => FileCat.Archives.Writing.ArchiveTransformWorker.RunAfterIsolation(input, output, nonce, ct, 2),
                "archive-verify-v2" => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, nonce, ct, 2),
                _ => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, nonce, ct, 2),
            }),
            _ => 64,
        };
    }

    /// <summary>The host relaunched as one worker role, with the protocol on its standard streams and a cancellation pipe.</summary>
    private sealed class SelfWorker(Process process, System.IO.Pipes.AnonymousPipeServerStream cancellation) : IArchiveWorkerProcess
    {
        private readonly Task<bool> stderr = Drain(process);
        private uint exit; private TimeSpan cpu; private bool disposed;
        public Stream Input => process.StandardInput.BaseStream;
        public Stream Output => process.StandardOutput.BaseStream;
        public bool HasExited => disposed || process.HasExited;
        public uint ExitCode => disposed ? exit : (uint)process.ExitCode;
        public TimeSpan TotalProcessorTime
        {
            get
            {
                if (!disposed)
                {
                    try { cpu = process.TotalProcessorTime; }
                    catch (Exception ex) when ((ex is System.ComponentModel.Win32Exception or InvalidOperationException) && process.HasExited) { }
                }
                return cpu;
            }
        }
        public bool ErrorLimitExceeded { get; private set; }
        public void RequestCancellation() => cancellation.Dispose();
        public void Kill() { if (!disposed && !process.HasExited) process.Kill(entireProcessTree: true); }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return; Kill(); await process.WaitForExitAsync(); exit = (uint)process.ExitCode;
            ErrorLimitExceeded = await stderr; cancellation.Dispose(); process.Dispose(); disposed = true;
        }
        private static async Task<bool> Drain(Process child)
        {
            char[] buffer = new char[1024]; int total = 0;
            while (await child.StandardError.ReadAsync(buffer) is int count && count != 0)
            {
                total += count;
                if (total >= 64 * 1024) { if (!child.HasExited) child.Kill(entireProcessTree: true); return true; }
            }
            return total != 0;
        }
        internal static SelfWorker Start(string self, Guid nonce, string role)
        {
            var start = new ProcessStartInfo(self)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in new[] { WorkerArgument, role, nonce.ToString("D") }) start.ArgumentList.Add(value);
            var cancellation = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out, HandleInheritability.Inheritable);
            start.ArgumentList.Add(ArchiveWorkerCancellation.Argument); start.ArgumentList.Add(cancellation.GetClientHandleAsString());
            try { var process = Process.Start(start)!; cancellation.DisposeLocalCopyOfClientHandle(); return new(process, cancellation); }
            catch { cancellation.Dispose(); throw; }
        }
    }
}
