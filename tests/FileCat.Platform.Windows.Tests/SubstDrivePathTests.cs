using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// A SUBST drive's root resolves to the folder behind it, without the trailing separator a volume root has. Handle-bound
/// deletion and replacement walk the rest of the path from there, so S:\proj\x must resolve to work\proj\x, never to the
/// sibling workproj\x (review finding F-H4 of f134ba37).
/// </summary>
public sealed partial class SubstDrivePathTests : IDisposable
{
    private const uint RemoveDefinition = 0x2, ExactMatchOnRemove = 0x4;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-subst-" + Guid.NewGuid().ToString("N"));
    private string? _drive;
    private string? _target;

    [LibraryImport("kernel32.dll", EntryPoint = "DefineDosDeviceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DefineDosDevice(uint flags, string deviceName, string? targetPath);

    // Maps a free drive letter to the folder, as subst does, for this logon session only.
    private string Subst(string folder)
    {
        var used = Environment.GetLogicalDrives().Select(d => char.ToUpperInvariant(d[0])).ToHashSet();
        char letter = "ZYXWVUTSRQPONMLKJIHG".FirstOrDefault(c => !used.Contains(c));
        Assert.SkipWhen(letter == default, "No free drive letter for a SUBST drive.");
        Assert.True(DefineDosDevice(0, letter + ":", folder), "DefineDosDevice: " + Marshal.GetLastPInvokeError());
        _drive = letter + ":";
        _target = folder;
        return _drive;
    }

    public void Dispose()
    {
        if (_drive is not null) DefineDosDevice(RemoveDefinition | ExactMatchOnRemove, _drive, _target);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_path_on_a_subst_drive_resolves_inside_the_folder_behind_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "SUBST drives are Windows'.");
        string work = Path.Combine(_root, "work");
        Directory.CreateDirectory(Path.Combine(work, "proj", "x"));
        Directory.CreateDirectory(Path.Combine(_root, "workproj", "x"));
        string drive = Subst(work);

        string resolved = ElevationPaths.ToVolumePathWithoutFollowing(drive + @"\proj\x");

        Assert.Equal(ElevationPaths.ToVolumePath(Path.Combine(work, "proj", "x")), resolved, ignoreCase: true);
        Assert.Equal(ElevationPaths.ToVolumePath(work), ElevationPaths.ToVolumePathWithoutFollowing(drive + @"\"), ignoreCase: true);
    }

    [Fact]
    public void Deleting_a_folder_on_a_subst_drive_deletes_that_folder_and_not_its_namesake_beside_the_drive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "SUBST drives are Windows'.");
        string work = Path.Combine(_root, "work");
        Directory.CreateDirectory(Path.Combine(work, "proj", "x"));
        File.WriteAllText(Path.Combine(work, "proj", "x", "chosen.txt"), "chosen");
        Directory.CreateDirectory(Path.Combine(_root, "workproj", "x"));
        File.WriteAllText(Path.Combine(_root, "workproj", "x", "unrelated.txt"), "unrelated");
        string drive = Subst(work);

        var deletion = new WindowsFileOperations().DeleteTreeBound(drive + @"\proj\x", () => { });

        Assert.NotNull(deletion);
        Assert.Equal(0, deletion.Failed);
        Assert.False(Directory.Exists(Path.Combine(work, "proj", "x")));
        Assert.Equal("unrelated", File.ReadAllText(Path.Combine(_root, "workproj", "x", "unrelated.txt")));
    }
}
