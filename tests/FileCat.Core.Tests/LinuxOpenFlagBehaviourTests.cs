using System.Reflection;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Content;

namespace FileCat.Core.Tests;

/// <summary>
/// I656 on the architecture the tests run on: each Linux open that takes O_DIRECTORY or O_NOFOLLOW opens what it should and
/// refuses a link. Linux ARM64 reads x64's values as O_DIRECT, which a folder refuses, and O_LARGEFILE, which openat2
/// refuses beside O_PATH and which follows a link; there every test here failed before the fix. x64 runs them unchanged.
/// <see cref="LinuxOpenFlagPolicyTests"/> checks the flags themselves for both architectures.
/// </summary>
[Collection("Analysis native source device")]
public sealed class LinuxOpenFlagBehaviourTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static void Linux() => Assert.SkipUnless(OperatingSystem.IsLinux(), "Linux open flags.");

    private static void LinuxAnalysis()
    {
        Linux();
        string? refusal = AnalysisAdmission.Refusal();
        Assert.SkipWhen(refusal is not null, "Needs an ordinary process: " + refusal);
    }

    // Private storage qualifies its file system; the build folder is on the checkout's disk, not on a tmpfs.
    private static TempDir NativeFolder() => new(Path.Combine(Environment.CurrentDirectory, ".tmp"));

    [Fact]
    public void Analysis_opens_its_root_and_the_folders_and_files_below_it()
    {
        LinuxAnalysis();
        using var temp = NativeFolder();
        string root = temp.Dir("root");
        temp.File(Path.Combine("root", "folder", "inner"), "inner");
        temp.File(Path.Combine("root", "file"), "owned fixture");
        // The root is pinned (O_PATH | O_NOFOLLOW) and probed as a folder through openat2 (O_DIRECTORY).
        using var backend = new UnixAnalysisBackend(root);
        // Each child is pinned through openat2 (O_PATH | O_NOFOLLOW); the folder is then read (O_DIRECTORY).
        var children = backend.Children(backend.RootEntry, Cancellation).OrderBy(child => child.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(["file", "folder"], children.Select(child => child.Name));
        Assert.Equal(AnalysisKind.File, children[0].Kind);
        Assert.Equal(AnalysisKind.Directory, children[1].Kind);
        Assert.Equal("inner", Assert.Single(backend.Children(children[1], Cancellation)).Name);
    }

    [Fact]
    public void Private_storage_opens_its_folders_creates_unnamed_files_and_refuses_a_linked_entry()
    {
        LinuxAnalysis();
        using var temp = NativeFolder();
        // Every ancestor from / down is opened as a folder (O_DIRECTORY | O_NOFOLLOW).
        using var directory = new UnixAnalysisPrivateDirectory(temp.Path, "saved");
        // Staging and scratch files are unnamed (O_TMPFILE, which includes O_DIRECTORY); staging is then published.
        using (var staging = directory.CreateStaging())
        {
            staging.Stream.Write("inventory"u8);
            staging.Flush();
            staging.PublishNew("one.fcdsa");
        }
        using (var scratch = directory.CreateScratch())
        {
            scratch.WriteAt("scratch"u8, 0);
            Assert.Equal(7, scratch.Length);
        }
        using (var read = directory.OpenRead("one.fcdsa"))
        using (var reader = new StreamReader(read.Stream, Encoding.UTF8))
            Assert.Equal("inventory", reader.ReadToEnd());
        // A file is opened with O_NOFOLLOW: a link in the private folder is refused, not followed.
        string saved = Path.Combine(temp.Path, "saved");
        File.CreateSymbolicLink(Path.Combine(saved, "link.fcdsa"), Path.Combine(saved, "one.fcdsa"));
        Assert.ThrowsAny<IOException>(() => directory.OpenRead("link.fcdsa"));
    }

    [Fact]
    public void An_export_publishes_an_unnamed_file_and_a_chosen_inventory_opens_only_without_a_link()
    {
        LinuxAnalysis();
        using var temp = NativeFolder();
        string folder = temp.Dir("output");
        // The output folder's ancestors are opened as folders and the export is staged as an unnamed file (O_TMPFILE).
        using (var export = UnixAnalysisPrivateDirectory.OpenExportDestination(folder))
        {
            export.Stream.Write("exported"u8);
            export.FlushDurable();
            export.PublishNew("out.fcdsa");
        }
        string exported = Path.Combine(folder, "out.fcdsa");
        Assert.Equal("exported", File.ReadAllText(exported));
        // A chosen inventory is opened with O_NOFOLLOW, its folders with O_DIRECTORY | O_NOFOLLOW.
        using (var chosen = UnixAnalysisPrivateDirectory.OpenChosenInventory(exported))
        using (var reader = new StreamReader(chosen.Stream, Encoding.UTF8))
            Assert.Equal("exported", reader.ReadToEnd());
        string link = Path.Combine(folder, "linked.fcdsa");
        File.CreateSymbolicLink(link, exported);
        Assert.ThrowsAny<IOException>(() => UnixAnalysisPrivateDirectory.OpenChosenInventory(link).Dispose());
    }

    [Fact]
    public void A_page_resource_open_refuses_a_link_put_in_place_after_its_links_were_resolved()
    {
        Linux();
        using var temp = new TempDir();
        string target = temp.File("style.css", "body {}");
        string link = Path.Combine(temp.Path, "linked.css");
        File.CreateSymbolicLink(link, target);
        // HtmlPage resolves links before it opens; this open is the one after that, and must not follow a final link.
        Assert.Null(OpenResource(link));
        using var stream = Assert.IsType<FileStream>(OpenResource(target));
        Assert.Equal(7, stream.Length);
    }

    private static object? OpenResource(string path) =>
        typeof(HtmlPage).GetMethod("OpenResource", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [path]);
}
