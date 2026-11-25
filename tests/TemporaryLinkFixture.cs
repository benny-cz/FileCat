using System.Diagnostics;

namespace FileCat.Tests;

/// <summary>
/// A temporary tree with links in it (I645): junctions on Windows, which any user may make, as a program at medium
/// integrity would plant them; symbolic links on Linux and macOS. It removes each link itself before the rest of the tree.
/// </summary>
internal sealed class TemporaryLinkFixture : IDisposable
{
    private readonly List<string> _links = [];

    public TemporaryLinkFixture() => Directory.CreateDirectory(Root);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "filecat-tests", "links-" + Guid.NewGuid().ToString("N")[..10]);

    /// <summary>A folder below the root, or below an absolute first part.</summary>
    public string Dir(params string[] parts)
    {
        string path = Path.Combine([Root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A file holding its own name.</summary>
    public string File(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        System.IO.File.WriteAllText(path, name);
        return path;
    }

    /// <summary>
    /// A folder outside FileCat's temporary folder, shaped like one: a read-only file (hidden and system too on Windows),
    /// two-day-old leftovers named as the sweeps name theirs, and two-day-old folders where the drag and clipboard sweeps
    /// look. A folder only administrators may change would look the same to the sweeps.
    /// </summary>
    public string Outside(string name)
    {
        string folder = Dir(name);
        string keep = File(folder, "keep.txt");
        System.IO.File.SetAttributes(keep, OperatingSystem.IsWindows()
            ? FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System : FileAttributes.ReadOnly);
        foreach (string leftover in new[] { "member-0123456789abcdef.tmp", "nested-0123456789abcdef.zip" })
            System.IO.File.SetLastWriteTimeUtc(File(folder, leftover), DateTime.UtcNow.AddDays(-2));
        Stale(Dir(folder, "20260101-000000-0123abcd"), "staged.txt");
        Stale(Dir(folder, "drag", "20260101-000000-4567cdef"), "a.txt");
        Stale(Dir(folder, "clipboard", "0123456789abcdef0123456789abcdef"), "b.txt");
        return folder;
    }

    /// <summary>
    /// A folder holding a read-only file and a writable one, made two days ago (backdated after it was filled). A deletion
    /// that stops at the read-only file still takes the other.
    /// </summary>
    public void Stale(string folder, string file)
    {
        System.IO.File.SetAttributes(File(folder, file), FileAttributes.ReadOnly);
        File(folder, "writable-" + file);
        Directory.SetCreationTimeUtc(folder, DateTime.UtcNow.AddDays(-2));
    }

    /// <summary>A link to a folder: a junction on Windows, a symbolic link elsewhere.</summary>
    public string FolderLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cmd.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(process.ExitCode == 0, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
        }
        else Directory.CreateSymbolicLink(link, target);
        _links.Add(link);
        Assert.True(IsLink(link), link);
        return link;
    }

    /// <summary>Linux and macOS: a symbolic link to a file (on Windows one needs a privilege or developer mode).</summary>
    public string FileLink(string link, string target)
    {
        System.IO.File.CreateSymbolicLink(link, target);
        _links.Add(link);
        Assert.True(IsLink(link), link);
        return link;
    }

    /// <summary>Whether something is at the path itself: a link counts, whatever it leads to.</summary>
    public static bool Exists(string path)
    {
        try
        {
            System.IO.File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    private static bool IsLink(string path) => (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>
    /// The folder and everything in it: names and attributes, on Linux and macOS the modes, and the files' sizes and last
    /// write times. A deletion, an attribute or mode change, or a write anywhere in it changes this.
    /// </summary>
    public static IReadOnlyList<string> Snapshot(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        return Directory.EnumerateFileSystemEntries(folder, "*", options).Prepend(folder)
            .Select(path => Describe(Path.GetRelativePath(folder, path), path))
            .Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// What the item's own record says, not its entry in the folder's listing: NTFS brings a folder's times in its parent's
    /// listing up to date lazily. A folder's own times change with what is added to it or removed, which the names show.
    /// </summary>
    private static string Describe(string relative, string path)
    {
        var attributes = System.IO.File.GetAttributes(path);
        string mode = OperatingSystem.IsWindows() ? "" : " " + Convert.ToString((int)System.IO.File.GetUnixFileMode(path), 8);
        string content = (attributes & FileAttributes.Directory) != 0 ? ""
            : $" {new FileInfo(path).Length} {System.IO.File.GetLastWriteTimeUtc(path).Ticks}";
        return $"{relative} {attributes}{mode}{content}";
    }

    public void Dispose()
    {
        // Each link itself first, never what it leads to; then the plain tree, read-only files included.
        foreach (string link in Enumerable.Reverse(_links))
        {
            try
            {
                if (!Exists(link)) continue;
                if (OperatingSystem.IsWindows()) Directory.Delete(link);
                else System.IO.File.Delete(link);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        try
        {
            var plain = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (string file in Directory.EnumerateFiles(Root, "*", plain)) System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
