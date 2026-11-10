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
        Assert.Null(ArchiveWriteMessages.ForUser(new IOException("The device is not ready.", OperatingSystem.IsWindows() ? unchecked((int)0x80070015) : 5), ArchiveJobPhase.Rebuilding));
        Assert.Equal("FileCat stopped processing this archive safely. No replacement was published.",
            ArchiveWriteMessages.ForUser(new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, "x", new IOException("broken pipe", 32)), ArchiveWriteFormat.Tar, ArchiveJobPhase.Rebuilding));
    }

    [Fact]
    public void Other_reasons_keep_their_specific_message()
    {
        foreach (var reason in new[] { ArchiveWriteReason.InvalidRequest, ArchiveWriteReason.PublicationUnavailable, ArchiveWriteReason.PasswordRequired })
            Assert.Equal("technical detail " + reason, ArchiveWriteMessages.ForUser(Error(reason), ArchiveWriteFormat.Tar, ArchiveJobPhase.Preparing));
    }
}
