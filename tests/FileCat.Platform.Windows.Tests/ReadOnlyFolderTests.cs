using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A folder's read-only attribute on Windows marks a customized folder (one with a desktop.ini), and OneDrive sets it on
/// every folder it keeps in sync; it protects nothing, and Explorer deletes such folders. FileCat's permanent delete
/// stopped at each of them with "Access is denied" (E-CLOUD-1: a test folder in OneDrive could not be removed).
/// </summary>
public sealed class ReadOnlyFolderTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-ro-folders", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var d in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories)) new DirectoryInfo(d).Attributes = FileAttributes.Directory;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string ReadOnlyFolder(string name)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, name));
        dir.Attributes |= FileAttributes.ReadOnly;
        return dir.FullName;
    }

    [Fact]
    public void A_read_only_folder_is_removed_as_Explorer_removes_it()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("The attribute means this only on Windows."); return; }
        string folder = ReadOnlyFolder("customized");
        new WindowsFileOperations().DeleteDirectory(folder);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void One_refused_for_another_reason_keeps_its_mark()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("The attribute means this only on Windows."); return; }
        var fs = new WindowsFileOperations();
        // Not empty: refused for that, and the mark is not touched.
        string full = ReadOnlyFolder("full");
        File.WriteAllText(Path.Combine(full, "x.txt"), "x");
        Assert.ThrowsAny<IOException>(() => fs.DeleteDirectory(full));
        Assert.True(new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public async Task Deleting_a_tree_of_them_asks_nothing_and_removes_it_all()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("The attribute means this only on Windows."); return; }
        string tree = ReadOnlyFolder("tree");
        string inner = Directory.CreateDirectory(Path.Combine(tree, "inner")).FullName;
        File.WriteAllText(Path.Combine(inner, "desktop.ini"), "[.ShellClassInfo]\r\nIconResource=shell32.dll,4\r\n");
        new DirectoryInfo(inner).Attributes |= FileAttributes.ReadOnly;
        File.WriteAllText(Path.Combine(tree, "a.txt"), "a");
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_root, "journal"));
        var job = jobs.Submit(new JobRequest { Kind = JobKind.Delete, Sources = [ItemRef.ForFileSystemPath(tree, EntryKind.Directory)] });
        string? asked = null;
        while (!job.State.IsFinished())
        {
            if (job.Decision is { } d)
            {
                asked = d.Request.Message;
                d.Resolve(new Decision(DecisionAction.CancelJob));
            }
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Null(asked);
        Assert.Equal(JobState.Completed, job.State);
        Assert.False(Directory.Exists(tree));
    }
}
