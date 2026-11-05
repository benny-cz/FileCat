using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Shell;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Tests;

// Generated-fixture feasibility controls only. These do not qualify or expose an operation adapter.
public sealed partial class AnalysisWindowsTests
{
    [Fact]
    public void MetadataOnlyGuardRemovesTheSelectedHardLinkWithoutReadingContent()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture();
        string original = Path.Combine(fixture.Path, "original.bin"), selected = Path.Combine(fixture.Path, "selected.bin");
        byte[] bytes = [1, 3, 5, 7]; File.WriteAllBytes(original, bytes); Assert.True(HardLink(selected, original, 0));
        var info = new FileInfo(original); byte[] security = info.GetAccessControl().GetSecurityDescriptorBinaryForm();
        try
        {
            using var user = WindowsIdentity.GetCurrent(); var denyRead = info.GetAccessControl();
            denyRead.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.ReadData, AccessControlType.Deny));
            info.SetAccessControl(denyRead);
            Assert.Throws<UnauthorizedAccessException>(() => { using var reader = File.OpenRead(selected); });
            using (var parent = LocalIconFileLease.TryOpenDirectory(fixture.Path, LeaseLinks.Refuse, removable: true))
            {
                Assert.NotNull(parent);
                using var guard = OpenOrThrow(parent.LastHandle, "selected.bin", ReadAttributes | Delete | Synchronize,
                    ShareRead, Open, NonDirectoryFile | OpenReparsePoint, "Open metadata-only feasibility guard");
                Assert.Equal(bytes.Length, RandomAccess.GetLength(guard));
                Assert.False(IsLink(guard)); Assert.False(IsDirectory(GetBasic(guard).FileAttributes));
                Assert.Throws<IOException>(() => { using var writer = File.OpenHandle(selected, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
                Assert.Throws<IOException>(() => File.Move(selected, Path.Combine(fixture.Path, "renamed.bin")));
                Assert.Throws<IOException>(() => Directory.Move(fixture.Path, fixture.Path + "-moved"));
                DeleteOpen(guard);
            }
            Assert.False(File.Exists(selected)); Assert.True(File.Exists(original));
        }
        finally
        {
            var restored = new FileSecurity(); restored.SetSecurityDescriptorBinaryForm(security, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }
        Assert.Equal(bytes, File.ReadAllBytes(original));
    }

    [Fact]
    public void ExistingWriterPreventsMetadataOnlyGuardAdmission()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture(); string path = Path.Combine(fixture.Path, "chosen.bin"); File.WriteAllBytes(path, [1, 2]);
        using var parent = LocalIconFileLease.TryOpenDirectory(fixture.Path, LeaseLinks.Refuse, removable: true); Assert.NotNull(parent);
        using var writer = File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        int status = TryOpen(parent.LastHandle, "chosen.bin", ReadAttributes | Delete | Synchronize,
            ShareRead, Open, NonDirectoryFile | OpenReparsePoint, out var refused);
        using (refused) { Assert.True(status < 0); Assert.True(refused.IsInvalid); }
        // The fixture survives refusal; an adapter must not release guards and silently retry by path.
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DenyingWriteSharingDoesNotExcludeAttributeOnlyMutation()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture(); string path = Path.Combine(fixture.Path, "chosen.bin"); File.WriteAllBytes(path, [1, 2]);
        using var parent = LocalIconFileLease.TryOpenDirectory(fixture.Path, LeaseLinks.Refuse, removable: true); Assert.NotNull(parent);
        using var guard = OpenOrThrow(parent.LastHandle, "chosen.bin", ReadAttributes | Delete | Synchronize,
            ShareRead, Open, NonDirectoryFile | OpenReparsePoint, "Open metadata-only feasibility guard");
        var before = GetBasic(guard);
        // Attribute-only access is outside Windows' ordinary read/write/delete sharing check. A bare
        // share-denying handle therefore cannot qualify coherent version evidence through commit.
        using var mutator = OpenOrThrow(parent.LastHandle, "chosen.bin", WriteAttributes | Synchronize,
            ShareAll, Open, NonDirectoryFile | OpenReparsePoint, "Open attribute-only feasibility mutator");
        long changed = before.LastWriteTime - TimeSpan.TicksPerSecond * 10;
        SetBasic(mutator, new BasicInfo { LastWriteTime = changed });
        Assert.Equal(changed, GetBasic(guard).LastWriteTime);
        Assert.True(File.Exists(path));
    }
}
