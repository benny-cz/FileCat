using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release issue I22: a copy that replaces a file another program has open, sharing it for reading, writing and
/// deleting (FileCat's own viewer and comparison open files that way, plan §9.5).
/// </summary>
public sealed class OpenTargetReplaceTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-open-target", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<(Job Job, List<DecisionRequest> Asked)> CopyAsync(string source, string destination, DecisionAction answer)
    {
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var manager = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_root, "journal"));
        var asked = new List<DecisionRequest>();
        manager.DecisionRequested += d =>
        {
            lock (asked) asked.Add(d.Request);
            d.Resolve(new Decision(answer));
        };
        var job = manager.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(destination),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
        });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return (job, asked);
    }

    private static string Questions(List<DecisionRequest> asked) => string.Join(" | ", asked.Select(a => a switch
    {
        ErrorRequest e => $"{e.Title}: {e.Message} ({e.ErrorClass})",
        _ => a.ToString(),
    }));

    private (string Source, string Target, string Folder) Files()
    {
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var dst = Directory.CreateDirectory(Path.Combine(_root, "dst")).FullName;
        File.WriteAllText(Path.Combine(src, "log.txt"), "newer");
        File.WriteAllText(Path.Combine(dst, "log.txt"), "old");
        return (Path.Combine(src, "log.txt"), Path.Combine(dst, "log.txt"), dst);
    }

    [Fact]
    public async Task A_file_open_in_a_viewer_is_replaced_and_the_viewer_keeps_what_it_read()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Replacing a file another handle holds open is refused only on Windows.");
        var (source, target, folder) = Files();

        using var viewer = new FileContentSource(target);
        var (job, asked) = await CopyAsync(source, folder, DecisionAction.Skip);
        Assert.True(File.ReadAllText(target) == "newer", $"{job.State}; asked: {Questions(asked)}");
        Assert.Empty(asked);
        Assert.Equal(JobState.Completed, job.State);
        // As on Linux and macOS: the viewer's handle still reads the file it opened, and no staged copy is left over.
        var bytes = new byte[8];
        Assert.Equal("old", System.Text.Encoding.UTF8.GetString(bytes, 0, viewer.Read(0, bytes)));
        Assert.Equal(["log.txt"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    /// <summary>
    /// FAT32 and exFAT have no POSIX renames, so I22's fallback cannot apply there (release plan V03): the file a viewer
    /// holds open stays as it was, the question says it is in use, nothing staged is left, and once the viewer has
    /// closed it, Retry replaces it. Runs where FILECAT_FAT_FOLDER names a folder on such a drive.
    /// </summary>
    [Fact]
    public async Task On_a_FAT_drive_an_open_file_is_reported_in_use_and_replaced_once_closed()
    {
        string? fat = Environment.GetEnvironmentVariable("FILECAT_FAT_FOLDER");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(fat)) Assert.Skip("Set FILECAT_FAT_FOLDER to a folder on a FAT32 or exFAT drive.");
        string format = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(fat))!).DriveFormat;
        Assert.True(format is "FAT32" or "exFAT" or "FAT", $"FILECAT_FAT_FOLDER is on {format}.");
        string folder = Directory.CreateDirectory(Path.Combine(fat, "filecat-open-target-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
            string source = Path.Combine(src, "log.txt"), target = Path.Combine(folder, "log.txt");
            File.WriteAllText(source, "newer");
            File.WriteAllText(target, "old");
            using (var viewer = new FileContentSource(target))
            {
                var (job, asked) = await CopyAsync(source, folder, DecisionAction.Skip);
                var question = Assert.IsType<ErrorRequest>(Assert.Single(asked));
                Assert.True(question.ErrorClass == "sharing" && question.Message.Contains("in use", StringComparison.Ordinal), $"{format}: {Questions(asked)}");
                Assert.NotEqual(JobState.Completed, job.State);
            }
            Assert.Equal("old", File.ReadAllText(target));
            Assert.Equal(["log.txt"], Directory.GetFiles(folder).Select(Path.GetFileName));
            var (again, _) = await CopyAsync(source, folder, DecisionAction.Retry);
            Assert.Equal(JobState.Completed, again.State);
            Assert.Equal("newer", File.ReadAllText(target));
            Assert.Equal(["log.txt"], Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task A_file_held_without_sharing_deletion_is_reported_in_use_and_kept()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Replacing a file another handle holds open is refused only on Windows.");
        var (source, target, folder) = Files();

        // An editor that shares reading and writing but not deletion: the replace cannot happen, and the question must
        // say the file is in use, not that access is denied.
        using (var editor = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var (job, asked) = await CopyAsync(source, folder, DecisionAction.Skip);
            var question = Assert.IsType<ErrorRequest>(Assert.Single(asked));
            Assert.Equal("sharing", question.ErrorClass);
            Assert.Contains("in use", question.Message, StringComparison.Ordinal);
            Assert.NotEqual(JobState.Completed, job.State);
        }
        Assert.Equal("old", File.ReadAllText(target));
        Assert.Equal(["log.txt"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }
}
