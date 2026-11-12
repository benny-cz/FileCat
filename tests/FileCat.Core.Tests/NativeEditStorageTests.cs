using System.Runtime.InteropServices;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

// Remaining-work ledger rows 4 and 8: ordinary startup composes the native edit store only for a qualified runtime and
// filesystem, and keeps the legacy store everywhere else.
public sealed class NativeEditStorageTests
{
    private static readonly NativeEditStorage.Route[] Example =
    [
        new("win-x64", "NTFS", "evidence-a"),
        new("linux-x64", "ext4", "evidence-b"),
    ];

    private static EditDirectoryLease Refuse(string path, bool requirePrivate, CancellationToken ct) =>
        throw new InvalidOperationException("An unqualified composition must not open native storage.");

    [Fact]
    public void This_build_lists_native_edit_storage_on_the_owner_s_routes_only()
    {
        // The owner's route decision of 12 November 2026, listed after the final independent review closed. Adding or
        // removing a runtime is a reviewed release decision: this list changes with it, never as a side effect.
        Assert.Equal(new NativeEditStorage.Route[]
        {
            new("win-x64", "NTFS", "AWQ-win-x64-NTFS"), new("linux-x64", "ext4", "AWQ-linux-x64-ext4"), new("osx-arm64", "apfs", "AWQ-osx-arm64-apfs"),
        }, NativeEditStorage.Qualified);
        using var temp = new TempDir();
        // An unlisted runtime probes nothing: no filesystem query and no lease.
        var store = NativeEditStorage.Compose(NativeEditStorage.Qualified, "win-arm64",
            Path.Combine(temp.Path, "edit-sessions"), Path.Combine(temp.Path, "edit-sessions-native"), new PortableFileOperations(),
            _ => throw new InvalidOperationException("An unqualified startup must not query the filesystem."), Refuse, null);
        Assert.Equal(store.Root, store.NativeRoot);
        Assert.False(store.SupportsRetainedCaptureRemoval);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "edit-sessions-native")));
    }

    [Theory]
    [InlineData("win-x64", "NTFS", true)]
    [InlineData("win-x64", "ntfs", true)]
    [InlineData("win-x64", "FAT32", false)]
    [InlineData("win-x64", null, false)]
    [InlineData("win-arm64", "NTFS", false)]
    [InlineData("linux-x64", "ext4", true)]
    [InlineData("osx-arm64", "apfs", false)]
    public void A_runtime_and_its_filesystem_qualify_together(string runtime, string? fileSystem, bool expected) =>
        Assert.Equal(expected, NativeEditStorage.IsQualified(Example, runtime, fileSystem));

    [Fact]
    public void An_unqualified_filesystem_an_unknown_one_or_missing_lease_support_keeps_the_legacy_store()
    {
        using var temp = new TempDir();
        string legacy = Path.Combine(temp.Path, "edit-sessions"), native = Path.Combine(temp.Path, "edit-sessions-native");
        var fs = new PortableFileOperations();
        foreach (var store in new[]
        {
            NativeEditStorage.Compose(Example, "win-x64", legacy, native, fs, _ => "FAT32", Refuse, null),
            NativeEditStorage.Compose(Example, "win-x64", legacy, native, fs, _ => null, Refuse, null),
            NativeEditStorage.Compose(Example, "win-x64", legacy, native, fs, _ => "NTFS", null, null),
            NativeEditStorage.Compose(Example, "linux-arm64", legacy, native, fs, _ => "ext4", Refuse, null),
        })
        {
            Assert.Equal(store.Root, store.NativeRoot);
            Assert.False(store.SupportsRetainedCaptureRemoval);
        }
        // The native root is never the legacy folder itself: adoption and retirement need the two apart.
        Assert.Throws<ArgumentException>(() => NativeEditStorage.Compose(Example, "win-x64", legacy, legacy + Path.DirectorySeparatorChar,
            fs, _ => "NTFS", Refuse, null));
    }

    // A state folder reached through a link (an AppData moved with a junction, a symbolic link to a home folder, macOS's
    // /tmp), or one this process may not hold: the native store could create no edit there, so the legacy store stays.
    [Fact]
    public void A_native_root_whose_folder_cannot_be_held_without_following_links_keeps_the_legacy_store()
    {
        using var temp = new TempDir();
        string legacy = Path.Combine(temp.Path, "edit-sessions"), native = Path.Combine(temp.Path, "edit-sessions-native");
        var opened = new List<(string Path, bool Private)>();
        EditDirectoryLease Linked(string path, bool requirePrivate, CancellationToken ct)
        {
            opened.Add((path, requirePrivate));
            throw new IOException("The edit root cannot be pinned without following links.");
        }
        var store = NativeEditStorage.Compose(Example, "win-x64", legacy, native, new PortableFileOperations(), _ => "NTFS", Linked, null);
        Assert.Equal(store.Root, store.NativeRoot);
        Assert.False(store.SupportsRetainedCaptureRemoval);
        // Only the folder that would hold the native root was opened, without asking it to be private, and nothing created.
        Assert.Equal([(Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp.Path)), false)], opened);
        Assert.False(Directory.Exists(native));
        var refused = NativeEditStorage.Compose(Example, "win-x64", legacy, native, new PortableFileOperations(), _ => "NTFS",
            (_, _, _) => throw new UnauthorizedAccessException("Edit storage requires an unelevated FileCat process."), null);
        Assert.Equal(refused.Root, refused.NativeRoot);
    }

    [Fact]
    public void On_Unix_a_state_folder_reached_through_a_symbolic_link_keeps_the_legacy_store_and_its_real_path_does_not()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Unix symbolic links; Windows refuses links through its own pins.");
        if (Environment.IsPrivilegedProcess) Assert.Skip("Native edit storage refuses root; run as an ordinary user.");
        using var temp = new TempDir();
        // macOS: the temporary folder is itself reached through /var, a link.
        string real = OperatingSystem.IsMacOS() && temp.Path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + temp.Path : temp.Path;
        string state = Directory.CreateDirectory(Path.Combine(real, "state")).FullName;
        string link = Path.Combine(real, "linked-state");
        Directory.CreateSymbolicLink(link, state);
        var routes = new NativeEditStorage.Route[] { new(RuntimeInformation.RuntimeIdentifier, "testfs", "test") };
        var fs = new PortableFileOperations();
        EditDirectoryLease Open(string path, bool requirePrivate, CancellationToken ct) =>
            OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? UnixEditDirectoryLease.OpenRoot(path, requirePrivate, ct)
                : throw new PlatformNotSupportedException();
        var linked = NativeEditStorage.Compose(routes, RuntimeInformation.RuntimeIdentifier, Path.Combine(link, "edit-sessions"),
            Path.Combine(link, "edit-sessions-native"), fs, _ => "testfs", Open, null);
        Assert.Equal(linked.Root, linked.NativeRoot);
        var direct = NativeEditStorage.Compose(routes, RuntimeInformation.RuntimeIdentifier, Path.Combine(state, "edit-sessions"),
            Path.Combine(state, "edit-sessions-native"), fs, _ => "testfs", Open, null);
        Assert.Equal(Path.Combine(state, "edit-sessions-native"), direct.NativeRoot);
        Assert.False(Directory.Exists(Path.Combine(state, "edit-sessions-native"))); // Created on first use only.
    }

    [Fact]
    public void The_filesystem_is_judged_where_the_native_root_will_be_created()
    {
        using var temp = new TempDir();
        string native = Path.Combine(temp.Path, "edit-sessions-native");
        string? asked = null;
        NativeEditStorage.Compose(Example, "win-x64", Path.Combine(temp.Path, "edit-sessions"), native, new PortableFileOperations(),
            folder => { asked = folder; return "FAT32"; }, Refuse, null);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp.Path)), asked);
    }
}
