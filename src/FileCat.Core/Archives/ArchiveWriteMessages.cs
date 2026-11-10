using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>
/// The plan's user-facing text for a controlled archive refusal (§11.7). "Not saved" and "no replacement was published"
/// are only said where the job had not yet reached publication; once saving may have begun, the text says what is known.
/// The technical message is kept as issue details.
/// </summary>
public static class ArchiveWriteMessages
{
    internal const string UserMessageKey = "FileCat.ArchiveUserMessage";

    public static string ForUser(ArchiveWriteException error, ArchiveWriteFormat? format, ArchiveJobPhase? phase)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (phase == ArchiveJobPhase.FinishingSafely)
            return "The archive was saved, but a follow-up step did not finish. Review the operation details before continuing.";
        if (phase == ArchiveJobPhase.Saving)
            return error.Reason == ArchiveWriteReason.PublicationUnavailable ? error.Message : Ambiguous;
        // A full drive surfaces through whichever step was writing (the stage, a sidecar or the worker transport).
        if (IsOutOfSpace(error)) return OutOfSpace;
        return error.Reason switch
        {
            ArchiveWriteReason.SourceChanged =>
                "The archive changed while this update was being prepared. Your changes were not saved. Refresh and review the update again.",
            ArchiveWriteReason.InvalidArchive => "The archive is damaged or inconsistent. FileCat did not save an update.",
            ArchiveWriteReason.UnsupportedFeature =>
                $"This {Name(format)} contains metadata FileCat cannot preserve when saving. It is read-only here. You can still extract supported files with F5.",
            ArchiveWriteReason.BudgetExceeded =>
                "This archive needs more memory or expanded data than FileCat currently allows for writing. It remains read-only.",
            ArchiveWriteReason.WorkerUnavailable or ArchiveWriteReason.ProtocolViolation or ArchiveWriteReason.VerificationFailed =>
                "FileCat stopped processing this archive safely. No replacement was published.",
            ArchiveWriteReason.NeedsReview => Ambiguous,
            _ => error.Message,
        };
    }

    /// <summary>A file with another hard link is not archived or updated: its other names would silently diverge.</summary>
    internal static ArchiveWriteException HardLinked(string path)
    {
        var error = new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "A retained archive binding requires a single-link regular object: " + path);
        error.Data[UserMessageKey] = $"\u201c{Path.GetFileName(path)}\u201d has another hard link (a second name for the same file), which FileCat does not " +
            "archive or update. Nothing was changed.";
        return error;
    }

    internal const string OutOfSpace =
        "The drive ran out of space before the archive was saved, so no archive was changed. Free up space or choose another destination, then try again.";

    /// <summary>
    /// For a raw IO failure that escaped as itself rather than as a typed refusal. Only a full drive or quota gets the
    /// plan's text, and only before saving began; anything else keeps its own message.
    /// </summary>
    internal static string? ForUser(IOException error, ArchiveJobPhase? phase) =>
        phase is ArchiveJobPhase.Saving or ArchiveJobPhase.FinishingSafely || !IsOutOfSpace(error) ? null : OutOfSpace;

    /// <summary>
    /// True when the exception or one of its causes carries the operating system's out-of-space or quota error:
    /// ENOSPC/EDQUOT (raw errno in <see cref="Exception.HResult"/> on Unix) or ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL and
    /// ERROR_DISK_QUOTA_EXCEEDED on Windows.
    /// </summary>
    internal static bool IsOutOfSpace(Exception? error)
    {
        for (int depth = 0; error is not null && depth < 8; depth++)
        {
            if (error is IOException io && IsOutOfSpaceCode(io.HResult)) return true;
            if (error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsOutOfSpace)) return true;
            error = error.InnerException;
        }
        return false;
    }

    private static bool IsOutOfSpaceCode(int code) => OperatingSystem.IsWindows()
        ? code is unchecked((int)0x80070070) or unchecked((int)0x80070027) or unchecked((int)0x8007050F)
        : code == 28 /* ENOSPC */ || code == (OperatingSystem.IsMacOS() ? 69 : 122) /* EDQUOT */;

    private const string Ambiguous =
        "FileCat could not confirm which version is now at the archive path. Both available versions were kept. Review these files before continuing.";

    private static string Name(ArchiveWriteFormat? format) => format switch
    {
        ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd => "TAR",
        ArchiveWriteFormat.SevenZip => "7z archive",
        ArchiveWriteFormat.Rar5 => "RAR archive",
        ArchiveWriteFormat.Zip => "ZIP",
        _ => "archive",
    };
}
