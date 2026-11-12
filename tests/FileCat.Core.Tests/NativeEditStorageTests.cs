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
    public void This_build_qualifies_native_edit_storage_nowhere()
    {
        // Adding a runtime is a reviewed release decision taken after its connected three-platform qualification closed on
        // the release candidate; this assertion changes with that decision, never as a side effect.
        Assert.Empty(NativeEditStorage.Qualified);
        using var temp = new TempDir();
        // Ordinary startup probes nothing: no filesystem query and no lease.
        var store = NativeEditStorage.Compose(NativeEditStorage.Qualified, RuntimeInformation.RuntimeIdentifier,
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
