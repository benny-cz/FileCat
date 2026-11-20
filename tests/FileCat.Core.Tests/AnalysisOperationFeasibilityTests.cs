using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

/// <summary>Adversarial generated fixtures: metadata descriptors alone are not namespace-entry operation guards.</summary>
public sealed partial class AnalysisOperationFeasibilityTests
{
    [Fact]
    public void UnixHeldChildDoesNotPreventReplacementBeforeUnlinkAt()
    {
        OrdinaryUnix(); using var fixture = new Fixture(); string selected = Path.Combine(fixture.Path, "selected.bin");
        string moved = Path.Combine(fixture.Path, "old-object.bin"); File.WriteAllBytes(selected, new byte[17]);
        using var parent = DirectoryDescriptor(fixture.Path); using var oldObject = MetadataDescriptor(parent, "selected.bin");
        File.Move(selected, moved); File.WriteAllBytes(selected, new byte[23]);
        var heldIdentity = Facts(oldObject).Identity;
        Assert.Equal(17, Facts(oldObject).Size); Assert.Equal(23, new FileInfo(selected).Length);
        Assert.Equal(0, UnlinkAt(parent, "selected.bin", 0));
        // Even with both descriptors retained, unlinkat removed the replacement, not the approved old object.
        Assert.False(File.Exists(selected)); Assert.True(File.Exists(moved)); Assert.Equal(17, Facts(oldObject).Size);
        Assert.Equal(heldIdentity, Facts(oldObject).Identity);
        Assert.Equal(heldIdentity, Assert.IsType<UnixStat>(UnixFiles.Stat(moved)).Identity);
    }

    [Fact]
    public void UnixHeldParentDoesNotPinItsSelectedAncestorBinding()
    {
        OrdinaryUnix(); using var fixture = new Fixture(); string chosen = Path.Combine(fixture.Path, "chosen");
        string moved = Path.Combine(fixture.Path, "renamed-parent"); Directory.CreateDirectory(chosen);
        File.WriteAllBytes(Path.Combine(chosen, "selected.bin"), new byte[17]);
        using var parent = DirectoryDescriptor(chosen); using var oldObject = MetadataDescriptor(parent, "selected.bin");
        Directory.Move(chosen, moved); Directory.CreateDirectory(chosen);
        string newEntry = Path.Combine(chosen, "selected.bin"); File.WriteAllBytes(newEntry, new byte[23]);
        Assert.Equal(0, UnlinkAt(parent, "selected.bin", 0));
        // The parent descriptor still resolves its old directory after the captured ancestor binding moved.
        Assert.False(File.Exists(Path.Combine(moved, "selected.bin"))); Assert.Equal(23, new FileInfo(newEntry).Length);
        Assert.Equal(17, Facts(oldObject).Size);
    }

    private static void OrdinaryUnix() => Assert.SkipWhen(OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null,
        "Needs ordinary macOS/Linux native fixtures.");
    private static UnixStat Facts(SafeFileHandle handle) => Assert.IsType<UnixStat>(UnixFiles.Stat(handle));
    private static SafeFileHandle DirectoryDescriptor(string path)
    {
        int flags = OperatingSystem.IsMacOS() ? 0x1000000 | 0x100000 | 0x100 : 0x80000 | 0x10000 | 0x20000;
        int fd = Open(path, flags); Assert.True(fd >= 0, "Open fixture directory errno=" + Marshal.GetLastPInvokeError());
        return new SafeFileHandle(fd, ownsHandle: true);
    }
    private static SafeFileHandle MetadataDescriptor(SafeFileHandle parent, string name)
    {
        // Same no-content-open flag families as the production metadata scanner: O_PATH or O_SYMLINK/O_EVTONLY.
        int flags = OperatingSystem.IsMacOS() ? 0x1000000 | 0x200000 | 0x8000 | 0x4 : 0x200000 | 0x80000 | 0x20000;
        int fd = OpenAt(parent, name, flags); Assert.True(fd >= 0, "Open fixture metadata errno=" + Marshal.GetLastPInvokeError());
        return new SafeFileHandle(fd, ownsHandle: true);
    }
    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-operation-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)] private static partial int Open(string path, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)] private static partial int OpenAt(SafeFileHandle parent, string name, int flags);
    [LibraryImport("libc", EntryPoint = "unlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)] private static partial int UnlinkAt(SafeFileHandle parent, string name, int flags);
}
