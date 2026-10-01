using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// Release plan DPI P11: attributes and times are changed on the items chosen, never through a link on what it points
/// to (which may be anywhere): a link chosen itself keeps its target as it was.
/// </summary>
public sealed class AttributeLinkTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir.Path, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        _dir.Dispose();
    }

    [Fact]
    public async Task Changing_a_links_time_or_read_only_never_changes_what_it_points_to()
    {
        var elsewhere = _dir.Dir("elsewhere");
        var target = Path.Combine(elsewhere, "target.txt");
        File.WriteAllText(target, "the target");
        var targetTime = new DateTime(2020, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, targetTime);
        var link = Path.Combine(_dir.Dir("chosen"), "link.txt");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Assert.Skip("Symbolic links cannot be made here: " + ex.Message); }

        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Attributes,
            Sources = [ItemRef.ForFileSystemPath(link, EntryKind.File)],
            Attributes = new AttributeChangeSet(FileAttributes.ReadOnly, 0, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, Recursive: false),
        });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal(targetTime, File.GetLastWriteTimeUtc(target));
        Assert.Equal(0, (int)(File.GetAttributes(target) & FileAttributes.ReadOnly));
        Assert.Equal("the target", File.ReadAllText(target));
        TestContext.Current.TestOutputHelper?.WriteLine($"{job.State}: {string.Join(" | ", job.Issues.Select(i => i.Message))}");
    }
}
