namespace FileCat.App.Services;

/// <summary>Early stream-only dispatch. An ordinary command-line invocation cannot start hostile parsing unconfined.</summary>
internal static class ArchiveWorker
{
    public const string Argument = "--archive-worker";
    public const string VerificationArgument = "--archive-verify-worker";
    public const string TransformArgument = "--archive-transform-worker";
    public const string SecretArgument = "--archive-worker-v2";
    public const string SecretVerificationArgument = "--archive-verify-worker-v2";
    public const string SecretTransformArgument = "--archive-transform-worker-v2";
    public static bool IsArgument(string argument) => argument is Argument or VerificationArgument or TransformArgument or
        SecretArgument or SecretVerificationArgument or SecretTransformArgument;

    /// <summary>
    /// Linux confined lane: the trusted launcher attaches the delegated cgroup, CPU limit, no-new-privileges and descriptor
    /// policy, then executes this binary again in worker mode, which installs seccomp and Landlock before the fixed role reads
    /// one byte of archive input. Only the six fixed roles with this task's nonce are accepted.
    /// </summary>
    public static bool IsLinuxArgument(string argument) => OperatingSystem.IsLinux() &&
        argument is FileCat.Core.Archives.Linux.LinuxArchiveIsolation.LauncherArgument or FileCat.Core.Archives.Linux.LinuxArchiveIsolation.WorkerArgument;

    public static int RunLinux(string[] args)
    {
        if (!OperatingSystem.IsLinux()) return 64;
        if (args[0] == FileCat.Core.Archives.Linux.LinuxArchiveIsolation.LauncherArgument)
            return FileCat.Core.Archives.Linux.LinuxArchiveIsolation.RunLauncher(args);
        try
        {
            return FileCat.Core.Archives.Linux.LinuxArchiveIsolation.RunWorker(args, startup =>
            {
                string[] request = args[4..^2];
                if (request.Length != 2 || request[1] != startup.Nonce.ToString("D")) return 64;
                Func<Stream, Stream, CancellationToken, int>? role = request[0] switch
                {
                    "archive-assess" => (input, output, ct) => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, startup.Nonce, ct),
                    "archive-assess-v2" => (input, output, ct) => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, startup.Nonce, ct, 2),
                    "archive-transform" => (input, output, ct) => FileCat.Archives.Writing.ArchiveTransformWorker.RunAfterIsolation(input, output, startup.Nonce, ct),
                    "archive-transform-v2" => (input, output, ct) => FileCat.Archives.Writing.ArchiveTransformWorker.RunAfterIsolation(input, output, startup.Nonce, ct, 2),
                    "archive-verify" => (input, output, ct) => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, startup.Nonce, ct),
                    "archive-verify-v2" => (input, output, ct) => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, startup.Nonce, ct, 2),
                    _ => null,
                };
                return role is null ? 64 : FileCat.Core.Archives.ArchiveWorkerCancellation.Run(startup.CancellationDescriptor, role);
            });
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
        { Console.Error.WriteLine("The confined Linux archive worker could not start."); return 65; }
    }

    /// <summary>
    /// macOS sandboxed lane: the parent starts the App Sandbox launcher beside this executable, which applies the limits the
    /// sandbox leaves open, gives up its bootstrap port and executes this binary in worker mode. The worker checks every one
    /// of those facts and the sandbox's denials before the fixed role reads one byte of archive input (MacArchiveIsolation).
    /// </summary>
    public static bool IsMacArgument(string argument) => OperatingSystem.IsMacOS() &&
        argument == FileCat.Core.Archives.MacOS.MacArchiveIsolation.WorkerArgument;

    public static int RunMac(string[] args)
    {
        if (!OperatingSystem.IsMacOS()) return 64;
        try
        {
            return FileCat.Core.Archives.MacOS.MacArchiveIsolation.RunWorker(args, startup =>
            {
                if (startup.Request.Length != 2 || startup.Request[1] != startup.Nonce.ToString("D")) return 64;
                Func<Stream, Stream, CancellationToken, int>? role = startup.Request[0] switch
                {
                    "archive-assess-v2" => (input, output, ct) => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, startup.Nonce, ct, 2),
                    "archive-transform-v2" => (input, output, ct) => FileCat.Archives.Writing.ArchiveTransformWorker.RunAfterIsolation(input, output, startup.Nonce, ct, 2),
                    "archive-verify-v2" => (input, output, ct) => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, startup.Nonce, ct, 2),
                    _ => null,
                };
                return role is null ? 64 : FileCat.Core.Archives.ArchiveWorkerCancellation.Run(startup.CancellationDescriptor, role);
            });
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
        { Console.Error.WriteLine("The sandboxed macOS archive worker could not start: " + ex.Message); return 65; }
    }

    public static int Run(string[] args)
    {
        if (args.Length != 5 || !IsArgument(args[0]) || !Guid.TryParseExact(args[1], "D", out Guid nonce) || nonce == Guid.Empty || args[2].Length != 16 ||
            !ulong.TryParse(args[2], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out ulong notify) ||
            args[2] != notify.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)) return 64;
        long cancellation;
        try { cancellation = FileCat.Core.Archives.ArchiveWorkerCancellation.Parse(args[3], args[4]); }
        catch (ArgumentException) { return 64; }
        // Linux/macOS launch/resource/package gates remain unqualified. Never replace this refusal with an
        // ordinary Process.Start route. Windows also verifies the actual token and immediate job before input.
        if (!OperatingSystem.IsWindows()) return 65;
        if (!FileCat.Platform.Windows.Shell.ArchiveAppContainer.TryVerifyCurrentProcess(out string refusal, unchecked((long)notify)))
        { Console.Error.WriteLine(refusal); return 65; }
        try { return FileCat.Core.Archives.ArchiveWorkerCancellation.Run(cancellation, (input, output, ct) => args[0] switch
        {
            TransformArgument => FileCat.Archives.Writing.ArchiveTransformWorker.RunAfterIsolation(input, output, nonce, ct),
            SecretTransformArgument => FileCat.Archives.Writing.ArchiveTransformWorker.RunAfterIsolation(input, output, nonce, ct, 2),
            VerificationArgument => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, nonce, ct),
            SecretVerificationArgument => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, nonce, ct, 2),
            SecretArgument => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, nonce, ct, 2),
            _ => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, nonce, ct)
        }); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        { Console.Error.WriteLine("The archive worker cancellation channel is unavailable."); return 65; }
    }
}
