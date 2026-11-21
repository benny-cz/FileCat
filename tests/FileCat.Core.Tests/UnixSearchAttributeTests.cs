using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>
/// F-FIND on Linux and macOS: an attribute search over a folder that vanishes after its parent was listed, or whose
/// listing is denied, logs that folder as inaccessible, keeps the other matches and leaves the result incomplete. A denied
/// folder is also offered for an explicit retry. The read-only mode is the native attribute the search tests. The Windows
/// versions are in NativeSearchAttributeTests.
/// </summary>
public sealed class UnixSearchAttributeTests
{
    private const UnixFileMode ReadOnly = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private sealed class FolderHook(Action<string> onFile) : IArchiveMembers
    {
        public bool IsArchive(string fileName)
        {
            onFile(fileName);
            return false;
        }

        public IEnumerable<ItemRef> List(string archivePath, CancellationToken ct) => throw new InvalidOperationException("No archive is listed.");
    }

    private static (SearchSession Session, ResultSet Set) Find(SearchQuery query)
    {
        var set = new ResultSet(Guid.NewGuid().ToString("N"), "find", "native fixture");
        var session = new SearchSession(query, set);
        session.Run(TestContext.Current.CancellationToken);
        return (session, set);
    }

    private static string[] Names(ResultSet set) => [.. set.Snapshot().Select(r => r.Item.Name).Order(StringComparer.Ordinal)];

    private static void Write(string path)
    {
        File.WriteAllText(path, "x");
        File.SetUnixFileMode(path, ReadOnly);
    }

    private static void RequireUnixUser()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Unix native attribute fixture.");
        // Root ignores mode bits, so neither the read-only attribute nor a denied listing would hold.
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "Mode bits do not bind a privileged process.");
    }

    [Fact]
    public void A_folder_that_vanishes_after_its_parent_was_listed_leaves_the_attribute_search_incomplete()
    {
        RequireUnixUser();
        using var temp = new TempDir();
        string gone = Directory.CreateDirectory(Path.Join(temp.Path, "gone")).FullName;
        string kept = Directory.CreateDirectory(Path.Join(temp.Path, "kept")).FullName;
        foreach (string path in new[] { Path.Join(temp.Path, "trigger.bin"), Path.Join(gone, "lost.bin"), Path.Join(kept, "keep.bin") })
            Write(path);
        // Files are examined before subfolders, so the archive probe of a file in the parent is a deterministic point.
        var probed = new List<string>();
        var hook = new FolderHook(name =>
        {
            probed.Add(name);
            if (name == "trigger.bin") Directory.Delete(gone, recursive: true);
        });
        var (session, results) = Find(new SearchQuery
            { Roots = [temp.Path], AttributesSet = FileAttributes.ReadOnly, AttributesClear = FileAttributes.Directory, Archives = hook });
        Assert.Equal(["keep.bin", "trigger.bin"], Names(results));
        Assert.Equal(["trigger.bin", "keep.bin"], probed);
        Assert.False(results.IsComplete);
        var entry = Assert.Single(session.Log);
        Assert.Equal(SearchLogKind.Inaccessible, entry.Kind);
        Assert.Equal("gone", Path.GetFileName(entry.Path));
        Assert.Empty(session.DeniedFolders);
        Assert.Contains(results.Issues, issue => issue.StartsWith("Not searched (inaccessible): ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_folder_that_refuses_listing_is_logged_offered_for_retry_and_leaves_the_search_incomplete()
    {
        RequireUnixUser();
        using var temp = new TempDir();
        string closed = Directory.CreateDirectory(Path.Join(temp.Path, "closed")).FullName;
        string open = Directory.CreateDirectory(Path.Join(temp.Path, "open")).FullName;
        Write(Path.Join(closed, "inside.bin"));
        Write(Path.Join(open, "visible.bin"));
        var mode = File.GetUnixFileMode(closed);
        File.SetUnixFileMode(closed, UnixFileMode.None);
        try
        {
            var control = Record.Exception(() => Directory.EnumerateFileSystemEntries(closed).ToList());
            Assert.SkipWhen(control is null, "This process lists the folder despite its mode.");
            Assert.IsType<UnauthorizedAccessException>(control);

            var (session, results) = Find(new SearchQuery
                { Roots = [temp.Path], AttributesSet = FileAttributes.ReadOnly, AttributesClear = FileAttributes.Directory });
            Assert.Equal(["visible.bin"], Names(results));
            Assert.False(results.IsComplete);
            // The engine may report the root in its canonical spelling (macOS /var is /private/var).
            var retry = Assert.Single(session.DeniedFolders);
            Assert.Equal(("closed", 1), (Path.GetFileName(retry.Folder), retry.Depth));
            Assert.Equal(Path.GetFileName(temp.Path), Path.GetFileName(retry.Root));
            Assert.Equal("closed", Path.GetFileName(Assert.Single(session.Inaccessible)));
        }
        finally { File.SetUnixFileMode(closed, mode); }
    }
}
