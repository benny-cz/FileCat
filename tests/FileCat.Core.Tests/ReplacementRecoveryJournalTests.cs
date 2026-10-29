using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ReplacementRecoveryJournalTests
{
    [Fact]
    public void Interrupted_replace_retains_its_durable_recovery_name_for_manual_review()
    {
        using var root = new TempDir();
        string source = root.File("source.bin", "incoming");
        string target = root.File("dest/source.bin", "old destination");
        string retained = Path.Combine(Path.GetDirectoryName(target)!, ".filecat-replaced-owned.bak");
        var request = new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(Path.GetDirectoryName(target)!),
        };
        var job = new Job(request, "Owned replacement", "device", [source], [target]);
        string journals = root.Dir("journals");
        using (var writer = JobJournal.Create(journals, job))
        {
            int step = writer.Intent("move-replace", source, target);
            writer.ReplacementRecovery(step, retained);
            File.Move(target, retained); // owned fixture models interruption immediately after retirement.
        }
        var interrupted = Assert.Single(JournalRecovery.Scan(journals));
        var pending = Assert.Single(interrupted.OpenIntents);
        Assert.Equal(retained, pending.RecoveryPath);
        Assert.Equal("old destination", File.ReadAllText(retained));
        Assert.False(File.Exists(target));
    }
}
