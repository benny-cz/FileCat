using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Mtp;

namespace FileCat.App.Tests;

/// <summary>
/// Review finding R-M11: a Move to a phone held its local source under a private name before checking and deleting it,
/// with nothing journaled, so a crash in between left the user's file hidden under that name with no record of it.
/// </summary>
public sealed class MtpMoveJournalTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-mtp-journal-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private JobJournal NewJournal(string folder) =>
        JobJournal.Create(Directory.CreateDirectory(Path.Combine(_dir, folder)).FullName,
            new Job(new JobRequest { Kind = JobKind.Move }, "Move to a phone", "test", [], []));

    [Fact]
    public void A_move_killed_while_its_source_is_held_gets_the_source_back()
    {
        const string original = "the only local copy";
        string source = Path.Combine(_dir, "photo.jpg");
        File.WriteAllText(source, original);
        string atKill = Directory.CreateDirectory(Path.Combine(_dir, "journal-at-kill")).FullName;
        string? held = null;
        using (var journal = NewJournal("journal"))
        {
            Assert.True(MtpMoveSafety.RetireJournaled(journal, source, hold =>
            {
                held = hold;
                File.Move(source, hold);
                // The journal as a kill right now would leave it.
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(journal.Path)!, "job-*.fcj"))
                {
                    using var from = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var copy = File.Create(Path.Combine(atKill, Path.GetFileName(file)));
                    from.CopyTo(copy);
                }
                return true;
            }));
        }
        Assert.NotNull(held);

        // The disk as the kill left it: the original only under its hold name.
        var interrupted = Assert.Single(JournalRecovery.Scan(atKill));
        Assert.DoesNotContain(held!, JournalRecovery.FindStagedLeftovers(interrupted), StringComparer.OrdinalIgnoreCase);
        var hold = Assert.Single(JournalRecovery.FindRenameLeftovers(interrupted));
        Assert.Equal(held, hold.Via);
        Assert.Empty(JournalRecovery.FinishRenames([hold], out int finished));
        Assert.Equal(1, finished);
        Assert.Equal(original, File.ReadAllText(source));
        Assert.False(File.Exists(held));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_source_not_retired_leaves_the_step_open_only_while_it_is_still_held(bool stillHeld)
    {
        string source = Path.Combine(_dir, "clip.mp4");
        File.WriteAllText(source, "kept");
        using var journal = NewJournal("journal");
        Assert.False(MtpMoveSafety.RetireJournaled(journal, source, hold =>
        {
            File.Move(source, hold);
            if (!stillHeld) File.Move(hold, source); // put back, as a failed check does
            return false;
        }));
        Assert.Equal(stillHeld, journal.NeedsRecovery);
    }
}
