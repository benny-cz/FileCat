namespace FileCat.App.Services;

/// <summary>Early stream-only dispatch. An ordinary command-line invocation cannot start hostile parsing unconfined.</summary>
internal static class ArchiveWorker
{
    public const string Argument = "--archive-worker";
    public const string VerificationArgument = "--archive-verify-worker";
    public const string TransformArgument = "--archive-transform-worker";

    public static int Run(string[] args)
    {
        if (args.Length != 5 || args[0] is not (Argument or VerificationArgument or TransformArgument) || !Guid.TryParseExact(args[1], "D", out Guid nonce) || nonce == Guid.Empty || args[2].Length != 16 ||
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
            VerificationArgument => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, nonce, ct),
            _ => FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, nonce, ct)
        }); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        { Console.Error.WriteLine("The archive worker cancellation channel is unavailable."); return 65; }
    }
}
