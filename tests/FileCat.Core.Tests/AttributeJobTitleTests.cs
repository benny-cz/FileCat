using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class AttributeJobTitleTests
{
    [Fact]
    public void Job_history_says_which_metadata_the_request_changes()
    {
        using var root = new TempDir();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root.Path, "journal"));
        var source = ItemRef.ForFileSystemPath(Path.Combine(root.Path, "note.txt"), EntryKind.File);
        var time = new DateTime(2024, 1, 2, 2, 4, 0, DateTimeKind.Utc);

        string Title(AttributeChangeSet change) => jobs.Describe(new JobRequest
        {
            Kind = JobKind.Attributes,
            Sources = [source],
            Attributes = change,
        }).Title;

        Assert.Equal("Change modified time of \"note.txt\"", Title(new(0, 0, time, null, false)));
        Assert.Equal("Change creation time of \"note.txt\"", Title(new(0, 0, null, time, false)));
        Assert.Equal("Change file times of \"note.txt\"", Title(new(0, 0, time, time, false)));
        Assert.Equal("Change attributes of \"note.txt\"", Title(new(FileAttributes.ReadOnly, 0, null, null, false)));
        Assert.Equal("Change attributes and times of \"note.txt\"", Title(new(FileAttributes.ReadOnly, 0, time, null, false)));
    }
}
