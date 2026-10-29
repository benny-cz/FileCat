using System.Diagnostics;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// What counts as a link on the way to an item (review finding F-H3 of f134ba37). Only symbolic links, junctions and
/// mount points are links, and an elevated FileCat refuses them so that a standard user's link cannot redirect it; without
/// elevation a junction above the chosen folder is followed, as by any program. Inside a deleted tree a link is still
/// removed as a link, and what it points to is kept.
/// </summary>
public sealed class LinkReparseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-links-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];

    private static bool Elevated => Core.State.WindowsTokenAuthority.IsHighIntegrity;

    public LinkReparseTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // The junctions first, as links, so that deleting the folder never goes through one.
        foreach (string junction in _junctions)
            if (Directory.Exists(junction)) Directory.Delete(junction);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // A junction, made as any user can (mklink /J needs no privilege).
    private void Junction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cmd.exe")) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "mklink timed out.");
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(link);
    }

    [Fact]
    public void A_folder_reached_through_a_junction_is_held_and_deleted_without_elevation_and_left_alone_with_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Junctions are Windows'.");
        string real = Directory.CreateDirectory(Path.Combine(_root, "real", "work")).FullName;
        File.WriteAllText(Path.Combine(real, "a.txt"), "a");
        string junction = Path.Combine(_root, "junction");
        Junction(junction, Path.Combine(_root, "real"));
        string throughJunction = Path.Combine(junction, "work");
        var ops = new WindowsFileOperations();

        if (Elevated)
        {
            Assert.Throws<IOException>(() => ops.HoldDirectory(throughJunction));
            Assert.Null(LocalIconFileLease.TryOpen(Path.Combine(throughJunction, "a.txt")));
            try { ops.DeleteTreeBound(throughJunction, () => { }); }
            catch (IOException) { }
            Assert.Equal("a", File.ReadAllText(Path.Combine(real, "a.txt")));
            return;
        }

        using (ops.HoldDirectory(throughJunction)) { }
        using (var icon = LocalIconFileLease.TryOpen(Path.Combine(throughJunction, "a.txt"))) Assert.NotNull(icon);
        var deletion = ops.DeleteTreeBound(throughJunction, () => { });
        Assert.NotNull(deletion);
        Assert.Equal(0, deletion.Failed);
        Assert.False(Directory.Exists(real));
        Assert.True(Directory.Exists(junction));
    }

    [Fact]
    public void A_junction_below_the_chosen_folder_is_refused_with_or_without_elevation()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Junctions are Windows'.");
        string chosen = Directory.CreateDirectory(Path.Combine(_root, "chosen")).FullName;
        string outside = Directory.CreateDirectory(Path.Combine(_root, "outside", "deep")).FullName;
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
        File.WriteAllText(Path.Combine(chosen, "own.txt"), "own");
        Junction(Path.Combine(chosen, "linked"), Path.Combine(_root, "outside"));
        string below = Path.Combine(chosen, "linked", "deep");
        var ops = new WindowsFileOperations();

        // Below the folder the user chose, a link is never followed: as a destination, nor for a source added from it.
        Assert.Throws<IOException>(() => ops.HoldDirectory(Path.Combine(chosen, "linked"), chosen));
        Assert.Throws<IOException>(() => ops.HoldDirectory(below, chosen));
        Assert.Throws<IOException>(() => ops.HoldRegularFile(Path.Combine(below, "secret.txt"), chosen));
        using (ops.HoldDirectory(chosen, chosen)) { }
        using (ops.HoldRegularFile(Path.Combine(chosen, "own.txt"), chosen)) { }
        if (Elevated) return;

        // Without elevation, the chosen folder may itself be reached through one, as by any program.
        using (ops.HoldDirectory(below)) { }
        string via = Path.Combine(_root, "via");
        Junction(via, chosen);
        using (ops.HoldDirectory(via, via)) { }
        using (ops.HoldRegularFile(Path.Combine(via, "own.txt"), via)) { }
        Assert.Throws<IOException>(() => ops.HoldRegularFile(Path.Combine(via, "linked", "deep", "secret.txt"), via));
    }

    [Fact]
    public void A_junction_inside_a_deleted_folder_is_removed_as_a_link_and_its_target_kept()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Junctions are Windows'.");
        string tree = Directory.CreateDirectory(Path.Combine(_root, "tree")).FullName;
        File.WriteAllText(Path.Combine(tree, "own.txt"), "own");
        string target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        Junction(Path.Combine(tree, "link"), target);

        var deletion = new WindowsFileOperations().DeleteTreeBound(tree, () => { });

        Assert.NotNull(deletion);
        Assert.Equal(0, deletion.Failed);
        Assert.False(Directory.Exists(tree));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void An_icon_behind_a_junction_is_read_but_never_one_behind_a_symbolic_link()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Links are Windows' here.");
        Assert.SkipWhen(Elevated, "An elevated FileCat follows no link for icons.");
        string folder = Directory.CreateDirectory(Path.Combine(_root, "icons")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "app.ico"), [0, 0, 1, 0]);
        Junction(Path.Combine(_root, "icons-junction"), folder);
        using (var lease = LocalIconFileLease.TryOpen(Path.Combine(_root, "icons-junction", "app.ico"))) Assert.NotNull(lease);
        string symlink = Path.Combine(_root, "icons-symlink");
        try { Directory.CreateSymbolicLink(symlink, folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Creating symbolic links is unavailable: " + ex.GetType().Name);
        }
        _junctions.Add(symlink);
        Assert.Null(LocalIconFileLease.TryOpen(Path.Combine(symlink, "app.ico")));
    }
}
