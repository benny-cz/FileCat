using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

/// <summary>Plan §11.7: plain-language refusals, and "not saved" only where the job had not reached publication.</summary>
public sealed class ArchiveWriteMessagesTests
{
    private static ArchiveWriteException Error(ArchiveWriteReason reason) => new(reason, "technical detail " + reason);

    [Theory]
    [InlineData(ArchiveWriteReason.SourceChanged, "The archive changed while this update was being prepared. Your changes were not saved. Refresh and review the update again.")]
    [InlineData(ArchiveWriteReason.InvalidArchive, "The archive is damaged or inconsistent. FileCat did not save an update.")]
    [InlineData(ArchiveWriteReason.BudgetExceeded, "This archive needs more memory or expanded data than FileCat currently allows for writing. It remains read-only.")]
    [InlineData(ArchiveWriteReason.WorkerUnavailable, "FileCat stopped processing this archive safely. No replacement was published.")]
    [InlineData(ArchiveWriteReason.ProtocolViolation, "FileCat stopped processing this archive safely. No replacement was published.")]
    [InlineData(ArchiveWriteReason.VerificationFailed, "FileCat stopped processing this archive safely. No replacement was published.")]
    [InlineData(ArchiveWriteReason.NeedsReview, "FileCat could not confirm which version is now at the archive path. Both available versions were kept. Review these files before continuing.")]
    public void Refusals_before_publication_use_the_plan_text(ArchiveWriteReason reason, string expected)
    {
        foreach (var phase in new ArchiveJobPhase?[] { null, ArchiveJobPhase.Preparing, ArchiveJobPhase.ReadingArchive, ArchiveJobPhase.Rebuilding, ArchiveJobPhase.Checking })
            Assert.Equal(expected, ArchiveWriteMessages.ForUser(Error(reason), ArchiveWriteFormat.Tar, phase));
    }

    [Fact]
    public void Unsupported_metadata_names_the_format_and_offers_extraction()
    {
        Assert.Equal("This TAR contains metadata FileCat cannot preserve when saving. It is read-only here. You can still extract supported files with F5.",
            ArchiveWriteMessages.ForUser(Error(ArchiveWriteReason.UnsupportedFeature), ArchiveWriteFormat.TarZstd, ArchiveJobPhase.ReadingArchive));
        Assert.StartsWith("This 7z archive contains metadata", ArchiveWriteMessages.ForUser(Error(ArchiveWriteReason.UnsupportedFeature), ArchiveWriteFormat.SevenZip, null));
        // AW7: a gzip file with a stored name or timestamp (the gzip tool's default) is a compressed file, not an archive.
        foreach (var format in new[] { ArchiveWriteFormat.Gzip, ArchiveWriteFormat.Bzip2, ArchiveWriteFormat.Zstd })
            Assert.StartsWith("This compressed file contains metadata FileCat cannot preserve when saving.", ArchiveWriteMessages.ForUser(Error(ArchiveWriteReason.UnsupportedFeature), format, ArchiveJobPhase.ReadingArchive));
    }

    [Fact]
    public void Once_saving_began_no_message_claims_that_nothing_was_saved()
    {
        foreach (var reason in Enum.GetValues<ArchiveWriteReason>())
        {
            string saving = ArchiveWriteMessages.ForUser(Error(reason), ArchiveWriteFormat.Rar5, ArchiveJobPhase.Saving);
            string finishing = ArchiveWriteMessages.ForUser(Error(reason), ArchiveWriteFormat.Rar5, ArchiveJobPhase.FinishingSafely);
            foreach (string text in new[] { saving, finishing })
            {
                Assert.DoesNotContain("not saved", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("No replacement was published", text, StringComparison.Ordinal);
                Assert.DoesNotContain("did not save", text, StringComparison.Ordinal);
            }
            // A definite publication refusal keeps its own exact reason; anything else is reported as unconfirmed.
            Assert.Equal(reason == ArchiveWriteReason.PublicationUnavailable ? "technical detail PublicationUnavailable"
                : "FileCat could not confirm which version is now at the archive path. Both available versions were kept. Review these files before continuing.", saving);
            Assert.StartsWith("The archive was saved, but a follow-up step did not finish.", finishing);
        }
    }

    [Fact]
    public void Insufficient_space_states_both_amounts_and_the_next_step()
    {
        var review = new ArchiveSpaceReview("parent", new ArchiveSpaceEstimate(0, 2_400L << 20, 1L << 20, 0),
            new FileSystemCapacitySnapshot("volume", 600L << 20));
        var error = Assert.Throws<ArchiveWriteException>(review.RequireAvailable);
        Assert.Matches(@"^Saving this archive needs up to 2[.,]\d GiB of additional space\. About 600 MiB is available\. Choose another destination or free space first\.$", error.Message);
        // The space text is kept as the user message: never the memory-policy text of other budget refusals.
        Assert.Equal(error.Message, error.Data["FileCat.ArchiveUserMessage"]);
        Assert.Equal("1 byte", ArchiveSpaceReview.Amount(1)); Assert.Equal("1.5 KiB", ArchiveSpaceReview.Amount(1536).Replace(',', '.'));
    }

    [Fact]
    public void A_full_drive_says_so_before_saving_and_never_after()
    {
        int code = OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28;
        var full = new IOException("No space left on device : '/private/stage'", code);
        // The real route: the stage write fails inside the worker conversation and becomes a typed worker refusal.
        var wrapped = new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, "The archive worker conversation failed.", full);
        foreach (var phase in new ArchiveJobPhase?[] { null, ArchiveJobPhase.Preparing, ArchiveJobPhase.Rebuilding, ArchiveJobPhase.Checking })
        {
            Assert.Equal(ArchiveWriteMessages.OutOfSpace, ArchiveWriteMessages.ForUser(wrapped, ArchiveWriteFormat.Tar, phase));
            Assert.Equal(ArchiveWriteMessages.OutOfSpace, ArchiveWriteMessages.ForUser(full, phase));
        }
        Assert.DoesNotContain("/private/stage", ArchiveWriteMessages.OutOfSpace, StringComparison.Ordinal);
        Assert.NotEqual(ArchiveWriteMessages.OutOfSpace, ArchiveWriteMessages.ForUser(wrapped, ArchiveWriteFormat.Tar, ArchiveJobPhase.Saving));
        Assert.Null(ArchiveWriteMessages.ForUser(full, ArchiveJobPhase.FinishingSafely));
        // Quota counts as full; other device errors keep their own text.
        int quota = OperatingSystem.IsWindows() ? unchecked((int)0x8007050F) : OperatingSystem.IsMacOS() ? 69 : 122;
        Assert.True(ArchiveWriteMessages.IsOutOfSpace(new AggregateException(new IOException("quota", quota))));
        // A Unix I/O error (EIO) is the drive-error text; Windows' device codes keep their own text.
        Assert.Equal(OperatingSystem.IsWindows() ? null : ArchiveWriteMessages.DriveError,
            ArchiveWriteMessages.ForUser(new IOException("The device is not ready.", OperatingSystem.IsWindows() ? unchecked((int)0x80070015) : 5), ArchiveJobPhase.Rebuilding));
        Assert.Equal("FileCat stopped processing this archive safely. No replacement was published.",
            ArchiveWriteMessages.ForUser(new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, "x", new IOException("broken pipe", 32)), ArchiveWriteFormat.Tar, ArchiveJobPhase.Rebuilding));
    }

    /// <summary>
    /// AT08: an I/O error from the drive (a stage write, or the fsync that seals it) says so, with "no archive was changed"
    /// only before saving; once saving may have begun the text asks for a review. A full drive or quota that only the
    /// fsync reports (delayed allocation) is the full-drive text. Unix only: the code is errno.
    /// </summary>
    [Fact]
    public void A_drive_error_says_so_and_a_full_drive_reported_by_fsync_is_a_full_drive()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix errno codes."); return; }
        var eio = FileCat.Core.Archives.Unix.UnixArchiveNative.SyncFailed(5);
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, eio.Reason);
        foreach (var phase in new ArchiveJobPhase?[] { null, ArchiveJobPhase.Preparing, ArchiveJobPhase.Rebuilding, ArchiveJobPhase.Checking })
            Assert.Equal(ArchiveWriteMessages.DriveError, ArchiveWriteMessages.ForUser(eio, ArchiveWriteFormat.Tar, phase));
        Assert.Equal(ArchiveWriteMessages.DriveErrorDuringSave, ArchiveWriteMessages.ForUser(eio, ArchiveWriteFormat.Tar, ArchiveJobPhase.Saving));
        // A worker's stage write that met EIO, and a file system that turned read-only after one.
        var worker = new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, "x", new IOException("Input/output error", 5));
        Assert.Equal(ArchiveWriteMessages.DriveError, ArchiveWriteMessages.ForUser(worker, ArchiveWriteFormat.Tar, ArchiveJobPhase.Rebuilding));
        // A read-only file system is not a drive error (a medium mounted read-only gives it too); the destination refusal
        // words it without advising a permission change.
        Assert.False(ArchiveWriteMessages.IsDriveError(new AggregateException(new IOException("Read-only file system", 30))));
        Assert.Equal("“media” is on a drive that is read-only now. Nothing was changed. Choose another folder, or check the drive and make it writable first.",
            ArchiveWriteMessages.DestinationDenied("/mnt/media", readOnly: true).Data[ArchiveWriteMessages.UserMessageKey]);
        int quota = OperatingSystem.IsMacOS() ? 69 : 122;
        foreach (int code in new[] { 28, quota })
            Assert.Equal(ArchiveWriteMessages.OutOfSpace,
                ArchiveWriteMessages.ForUser(FileCat.Core.Archives.Unix.UnixArchiveNative.SyncFailed(code), ArchiveWriteFormat.Tar, ArchiveJobPhase.Checking));
        Assert.False(ArchiveWriteMessages.IsDriveError(new IOException("broken pipe", 32)));
        Assert.DoesNotContain("errno", ArchiveWriteMessages.ForUser(eio, ArchiveWriteFormat.Tar, ArchiveJobPhase.Saving), StringComparison.Ordinal);
        // A raw wrapper around it (the real failing volume: a stage folder whose creation could not be made durable).
        var wrapped = new IOException("The created private archive artifact requires review: /x", eio);
        Assert.Equal(ArchiveWriteMessages.DriveError, ArchiveWriteMessages.ForUser(wrapped, ArchiveJobPhase.Preparing));
        Assert.Null(ArchiveWriteMessages.ForUser(wrapped, ArchiveJobPhase.Saving));
        // The stage folder's own mkdirat that met EIO (measured on the failing ext4 volume): its errno survives the review
        // wrapper. A refusal without a system error keeps the wrapper's own text.
        var mkdir = new FileCat.Core.Archives.Unix.UnixArchiveArtifactCreationException("/x/.filecat-archive-stage-0",
            FileCat.Core.Archives.Unix.UnixArchiveNative.Unavailable(5));
        Assert.Equal(ArchiveWriteMessages.DriveError, ArchiveWriteMessages.ForUser(mkdir, ArchiveJobPhase.Preparing));
        Assert.Null(ArchiveWriteMessages.ForUser(new FileCat.Core.Archives.Unix.UnixArchiveArtifactCreationException("/x",
            FileCat.Core.Archives.Unix.UnixArchiveNative.Unavailable()), ArchiveJobPhase.Preparing));
        Assert.Equal(ArchiveWriteMessages.OutOfSpace, ArchiveWriteMessages.ForUser(new FileCat.Core.Archives.Unix.UnixArchiveArtifactCreationException("/x",
            FileCat.Core.Archives.Unix.UnixArchiveNative.Unavailable(28)), ArchiveJobPhase.Preparing));
    }

    /// <summary>
    /// A refusal at the publication boundary that the journal proves never published anything (the space review just
    /// before the content snapshot, a refused exchange) is worded as one before saving; without that proof the saving text
    /// stands. Found when a drive filled between the rebuild and the snapshot on a small NTFS volume.
    /// </summary>
    [Fact]
    public void A_refusal_proven_unpublished_during_saving_keeps_the_text_before_saving()
    {
        var review = new ArchiveSpaceReview("parent", new ArchiveSpaceEstimate(0, 112L << 20, 1L << 20, 0),
            new FileSystemCapacitySnapshot("volume", 1L << 20));
        var space = Assert.Throws<ArchiveWriteException>(review.RequireAvailable);
        var changed = Error(ArchiveWriteReason.SourceChanged);
        foreach (var error in new[] { space, changed })
        {
            Assert.Equal(ArchiveJobPhase.Saving, ArchiveWriteMessages.WordingPhase(ArchiveJobPhase.Saving, error));
            error.Data[ArchiveWriteMessages.NothingPublishedKey] = true;
            Assert.Null(ArchiveWriteMessages.WordingPhase(ArchiveJobPhase.Saving, error));
        }
        // The space review keeps its own text (the job leaves a refusal's own text alone before saving); a changed source
        // says nothing was saved instead of asking to review two versions.
        Assert.StartsWith("Saving this archive needs up to", (string)space.Data[ArchiveWriteMessages.UserMessageKey]!, StringComparison.Ordinal);
        Assert.Equal("The archive changed while this update was being prepared. Your changes were not saved. Refresh and review the update again.",
            ArchiveWriteMessages.ForUser(changed, ArchiveWriteFormat.Tar, ArchiveWriteMessages.WordingPhase(ArchiveJobPhase.Saving, changed)));
        var full = new IOException("No space left on device", OperatingSystem.IsWindows() ? unchecked((int)0x80070070) : 28);
        Assert.Null(ArchiveWriteMessages.ForUser(full, ArchiveWriteMessages.WordingPhase(ArchiveJobPhase.Saving, full)));
        full.Data[ArchiveWriteMessages.NothingPublishedKey] = true;
        Assert.Equal(ArchiveWriteMessages.OutOfSpace, ArchiveWriteMessages.ForUser(full, ArchiveWriteMessages.WordingPhase(ArchiveJobPhase.Saving, full)));
        // Once finishing began the archive was saved; the marker is never set there, and without it nothing changes.
        Assert.Equal(ArchiveJobPhase.FinishingSafely, ArchiveWriteMessages.WordingPhase(ArchiveJobPhase.FinishingSafely, Error(ArchiveWriteReason.NeedsReview)));
    }

    [Fact]
    public void Other_reasons_keep_their_specific_message()
    {
        foreach (var reason in new[] { ArchiveWriteReason.InvalidRequest, ArchiveWriteReason.PublicationUnavailable, ArchiveWriteReason.PasswordRequired })
            Assert.Equal("technical detail " + reason, ArchiveWriteMessages.ForUser(Error(reason), ArchiveWriteFormat.Tar, ArchiveJobPhase.Preparing));
    }
}
