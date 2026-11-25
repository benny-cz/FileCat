using FileCat.App.Services;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// An elevated or root FileCat neither shows nor keeps reviewed recovery reports, which live in the ordinary account's
/// profile (I655): it reads none, as it lists no journal, and refuses to save one, since saving prunes that folder.
/// </summary>
public sealed class RecoveryReviewStoreAuthorityTests
{
    [Fact]
    public void An_elevated_or_root_FileCat_neither_loads_nor_saves_reviewed_recovery_reports()
    {
        string journals = Path.Combine(Path.GetTempPath(), "filecat-review-authority", Guid.NewGuid().ToString("N"));
        try
        {
            var record = new RecoveryReviewRecord(Path.Combine(journals, "job-1.json"), "Copy notes", "Copy", DateTime.UtcNow,
                "Stopped before changes", "report", ClosureConfirmed: false);
            using (ProcessAuthority.Assume(false))
            {
                RecoveryReviewStore.Save(journals, record);
                Assert.Single(RecoveryReviewStore.Load(journals));
            }
            using (ProcessAuthority.Assume(true))
            {
                Assert.Empty(RecoveryReviewStore.Load(journals));
                Assert.Throws<IOException>(() => RecoveryReviewStore.Save(journals,
                    record with { JournalPath = Path.Combine(journals, "job-2.json"), Title = "Move notes" }));
            }
            using (ProcessAuthority.Assume(false))
                Assert.Equal("Copy notes", Assert.Single(RecoveryReviewStore.Load(journals)).Title);
        }
        finally
        {
            try { Directory.Delete(journals, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
