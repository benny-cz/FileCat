using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// No data loss through aliases: a destination reached through a junction can be the source itself, and a move that
/// copied a file "onto itself" and then deleted its source would lose it. The junction's folder acts as another volume
/// here (like a mounted drive), so the copy-then-delete path of a move runs.
/// </summary>
public sealed class AliasSafetyTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-alias", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        // Junctions first, so the recursive delete never walks through one.
        foreach (var dir in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Reverse())
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) Directory.Delete(dir);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Native operations where <paramref name="volume"/> (a junction) is a volume of its own.</summary>
    private sealed class SplitVolumes(string volume) : WindowsFileOperations
    {
        public override string GetVolumeRoot(string path) =>
            PathUtil.IsSameOrUnder(path, volume) ? volume : base.GetVolumeRoot(path);
    }

    private sealed class NoLinks(string volume) : WindowsFileOperations
    {
        public override bool TryCopyLink(string source, string destination, bool isDirectory, out string? error)
        {
            error = "Links cannot be made here (a test).";
            return false;
        }

        public override string GetVolumeRoot(string path) =>
            PathUtil.IsSameOrUnder(path, volume) ? volume : base.GetVolumeRoot(path);
    }

    private async Task<(Job Job, List<DecisionRequest> Asked)> RunAsync(IFileSystemOperations ops, JobKind kind, string source, EntryKind entryKind, string destination,
        ConflictPolicy conflicts, DecisionAction answer)
    {
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var manager = new JobManager(ops, providers, Path.Combine(_root, "journal-" + Guid.NewGuid().ToString("N")[..6]));
        var asked = new List<DecisionRequest>();
        manager.DecisionRequested += d =>
        {
            lock (asked) asked.Add(d.Request);
            d.Resolve(new Decision(answer));
        };
        var job = manager.Submit(new JobRequest
        {
            Kind = kind,
            Sources = [ItemRef.ForFileSystemPath(source, entryKind)],
            Destination = Location.FileSystem(destination),
            Options = new TransferOptions { Conflicts = conflicts },
        });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return (job, asked);
    }

    [Fact]
    public async Task A_move_onto_itself_through_a_junction_keeps_the_file()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Junctions are Windows'.");
        var data = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        string file = Path.Combine(data, "precious.txt");
        File.WriteAllText(file, "precious");
        string alias = Path.Combine(_root, "alias");
        Junction.Create(alias, data);
        var ops = new SplitVolumes(alias);
        Assert.Equal(ops.GetFileIdentity(file), ops.GetFileIdentity(Path.Combine(alias, "precious.txt")));

        // "Replace" preset for every conflict: the destination is the file itself, so nothing may be replaced or deleted.
        var (moved, _) = await RunAsync(ops, JobKind.Move, file, EntryKind.File, alias, ConflictPolicy.Replace, DecisionAction.Replace);
        Assert.Equal("precious", File.ReadAllText(file));
        Assert.Contains(moved.Issues, i => i.Message.Contains("same item", StringComparison.Ordinal));

        // A copy onto itself is a question whose Replace is not offered; a remembered Replace keeps both instead.
        var (copied, asked) = await RunAsync(ops, JobKind.Copy, file, EntryKind.File, alias, ConflictPolicy.Replace, DecisionAction.Replace);
        var conflict = Assert.IsType<ConflictRequest>(Assert.Single(asked));
        Assert.True(conflict.SameItem);
        Assert.False(conflict.CanReplace);
        Assert.Equal("precious", File.ReadAllText(file));
        Assert.Equal(2, Directory.GetFiles(data).Length); // the original and a copy under a new name
        Assert.True(copied.State.IsFinished());
    }

    [Fact]
    public async Task A_folder_is_not_moved_into_itself_through_a_junction()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Junctions are Windows'.");
        var photos = Directory.CreateDirectory(Path.Combine(_root, "photos")).FullName;
        var sub = Directory.CreateDirectory(Path.Combine(photos, "sub")).FullName;
        File.WriteAllText(Path.Combine(photos, "a.jpg"), "a");
        File.WriteAllText(Path.Combine(sub, "b.jpg"), "b");
        string alias = Path.Combine(_root, "into-sub");
        Junction.Create(alias, sub);

        var (job, _) = await RunAsync(new SplitVolumes(alias), JobKind.Move, photos, EntryKind.Directory, alias, ConflictPolicy.Ask, DecisionAction.Skip);
        Assert.Contains(job.Issues, i => i.Message.Contains("into itself", StringComparison.Ordinal));
        Assert.Equal("a", File.ReadAllText(Path.Combine(photos, "a.jpg")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(sub, "b.jpg")));
        Assert.Equal(["b.jpg"], Directory.GetFileSystemEntries(sub).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Replacing_with_a_link_that_cannot_be_made_keeps_the_existing_file()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Symbolic links need Windows here.");
        var src = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        var dst = Directory.CreateDirectory(Path.Combine(_root, "dst")).FullName;
        File.WriteAllText(Path.Combine(src, "target.txt"), "t");
        string link = Path.Combine(src, "note.txt");
        try { File.CreateSymbolicLink(link, "target.txt"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Assert.Skip("Creating symbolic links needs Developer Mode or administrator rights."); }
        File.WriteAllText(Path.Combine(dst, "note.txt"), "keep me");

        // Replace preset; the link cannot be recreated there, and the answer to "Link cannot be copied" is Skip.
        var (_, asked) = await RunAsync(new NoLinks(Path.Combine(_root, "none")), JobKind.Copy, link, EntryKind.File, dst, ConflictPolicy.Replace, DecisionAction.Skip);
        Assert.Contains(asked, r => r is ConfirmRequest c && c.Title == "Link cannot be copied as a link");
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(dst, "note.txt")));
        Assert.Single(Directory.GetFileSystemEntries(dst)); // no staged link left behind
    }
}
