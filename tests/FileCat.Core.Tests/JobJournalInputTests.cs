using System.Text;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class JobJournalInputTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Oversized_recovery_record_is_refused_without_materialization_or_journal_deletion()
    {
        string path = Path.Combine(_dir.Path, "job-oversized.fcj");
        byte[] input = Encoding.ASCII.GetBytes("00000000 " + new string('x', 16 * 1024 * 1024) + "\n");
        File.WriteAllBytes(path, input);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Exception? error = Record.Exception(() => JobJournal.ReadAll(path));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Current.TestOutputHelper?.WriteLine($"Journal read allocated {allocated:N0} bytes; error={error?.GetType().Name ?? "none"}.");
        Assert.IsType<IOException>(error);
        Assert.True(allocated < 16 * 1024 * 1024, $"Journal read allocated {allocated:N0} bytes.");
        Assert.Empty(JournalRecovery.Scan(_dir.Path));
        Assert.Equal(input, File.ReadAllBytes(path));
    }

    [Fact]
    public void Oversized_job_journal_is_retained_for_review()
    {
        string path = Path.Combine(_dir.Path, "job-retain.fcj");
        byte[] input = Encoding.ASCII.GetBytes("00000000 " + new string('x', 5 * 1024 * 1024) + "\n");
        File.WriteAllBytes(path, input);
        Assert.Empty(JournalRecovery.Scan(_dir.Path));
        Assert.Equal(input, File.ReadAllBytes(path));
    }

    [Fact]
    public void Oversized_source_manifest_line_is_refused_without_materialization()
    {
        string manifest = Path.Combine(_dir.Path, "job-sources.fcj.sources");
        File.WriteAllText(manifest, new string('x', 16 * 1024 * 1024) + "\n");
        var job = new InterruptedJob(Path.Combine(_dir.Path, "job-sources.fcj"), "Copy", "test",
            DateTime.UtcNow, [], null, [], [], 0, 65) { ManifestPath = manifest };
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Null(JournalRecovery.LoadSources(job));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Current.TestOutputHelper?.WriteLine($"Manifest read allocated {allocated:N0} bytes.");
        Assert.True(allocated < 16 * 1024 * 1024, $"Manifest read allocated {allocated:N0} bytes.");
        Assert.True(File.Exists(manifest));
    }
}
