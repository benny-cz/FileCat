namespace FileCat.App.Services;

/// <summary>Early stream-only dispatch. An ordinary command-line invocation cannot start hostile parsing unconfined.</summary>
internal static class ArchiveWorker
{
    public const string Argument = "--archive-worker";

    public static int Run(string[] args)
    {
        if (args.Length != 3 || !Guid.TryParseExact(args[1], "D", out Guid nonce) || nonce == Guid.Empty || args[2].Length != 16 ||
            !ulong.TryParse(args[2], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out ulong notify) ||
            args[2] != notify.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)) return 64;
        // Linux/macOS launch/resource/package gates remain unqualified. Never replace this refusal with an
        // ordinary Process.Start route. Windows also verifies the actual token and immediate job before input.
        if (!OperatingSystem.IsWindows()) return 65;
        if (!FileCat.Platform.Windows.Shell.ArchiveAppContainer.TryVerifyCurrentProcess(out string refusal, unchecked((long)notify)))
        { Console.Error.WriteLine(refusal); return 65; }
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        return FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, nonce);
    }
}
