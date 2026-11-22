using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    [Fact]
    public void Darwin_access_checks_effective_file_rights_and_parent_delete_without_mutating()
    {
        RequireDarwinWriter();
        if (Environment.IsPrivilegedProcess) Assert.Skip("Requires ordinary macOS credentials.");
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!;
        string directory = item.Parent.Path;
        File.SetUnixFileMode(path, (UnixFileMode)0x100);
        var service = new DarwinPermissionService(qualifyAccessChecks: true);
        using var inspection = service.Inspect(subject, PermissionSections.Flags, ct);
        Assert.True(service.CanCheckAccess(inspection.Snapshot));
        Assert.False(new DarwinPermissionService().CanCheckAccess(inspection.Snapshot));
        var expected = inspection.Snapshot.Binding!.Identity;
        subject = subject with { ExpectedIdentity = expected };

        var report = service.CheckEffectiveAccess(subject, ct);
        Assert.Equal(expected, report.Identity);
        Assert.Equal(report.RealUid, report.EffectiveUid);
        Assert.Equal(report.RealGid, report.EffectiveGid);
        Assert.NotEqual(0u, report.EffectiveUid);
        Assert.True(report.HonorsOwnership);
        Check(report, "Read content", PermissionReadStatus.Present);
        Check(report, "Write content", PermissionReadStatus.Denied);
        Check(report, "Execute permission", PermissionReadStatus.Denied);
        Check(report, "Delete this entry from its parent", PermissionReadStatus.Present);

        File.SetUnixFileMode(directory, (UnixFileMode)0x140);
        try { Check(service.CheckEffectiveAccess(subject, ct), "Delete this entry from its parent", PermissionReadStatus.Denied); }
        finally { File.SetUnixFileMode(directory, (UnixFileMode)0x1c0); }

        RunAclTool("chmod", "+a#", "0", "everyone deny read,delete", path);
        try
        {
            var access = service.CheckEffectiveAccess(subject, ct);
            Check(access, "Read content", PermissionReadStatus.Denied);
            Check(access, "Delete this entry from its parent", PermissionReadStatus.Denied);
            Assert.Equal((UnixFileMode)0x100, File.GetUnixFileMode(path));
            Assert.Contains("everyone deny read,delete", RunAclTool("ls", "-led", path));
        }
        finally { RunAclTool("chmod", "-N", path); }

        string original = Path.Join(directory, "original");
        File.Move(path, original);
        File.WriteAllText(path, "replacement");
        Assert.Throws<PermissionBindingConflictException>(() => service.CheckEffectiveAccess(subject, ct));
        File.Delete(path);
        File.CreateSymbolicLink(path, original);
        using var link = service.Inspect(subject with { Kind = PermissionObjectKind.Link, ExpectedIdentity = null }, PermissionSections.Flags, ct);
        Assert.False(service.CanCheckAccess(link.Snapshot));
        Assert.Null(fixture.JournalPath);
    }

    [Fact]
    public void Darwin_access_checks_directory_search_separately_from_listing_and_add_file()
    {
        RequireDarwinWriter();
        if (Environment.IsPrivilegedProcess) Assert.Skip("Requires ordinary macOS credentials.");
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        string directory = Directory.CreateDirectory(Path.Join(fixture.Root, "searchable"), (UnixFileMode)0x40).FullName;
        var (_, subject) = TreeSubject(directory, EntryKind.Directory);
        try
        {
            var report = new DarwinPermissionService(qualifyAccessChecks: true).CheckEffectiveAccess(subject, ct);
            Check(report, "List names", PermissionReadStatus.Denied);
            Check(report, "Search known names", PermissionReadStatus.Present);
            Check(report, "Add file permission", PermissionReadStatus.Denied);
            Check(report, "Add file and search together", PermissionReadStatus.Denied);
            Assert.Equal((UnixFileMode)0x40, File.GetUnixFileMode(directory));
        }
        finally { File.SetUnixFileMode(directory, (UnixFileMode)0x1c0); }
    }

    // G-ACCESS mount restrictions on macOS: an executable on an image mounted noexec and nosuid is reported with both
    // restrictions, and the kernel refuses execute although the mode grants it.
    [Fact]
    public void Darwin_access_check_reports_noexec_and_nosuid_mounts()
    {
        string program = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACCESS_NOEXEC") ?? string.Empty;
        if (!OperatingSystem.IsMacOS() || program.Length == 0 || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires an ordinary macOS process and an image mounted noexec and nosuid.");
        Assert.Equal(UnixFileMode.UserExecute, File.GetUnixFileMode(program) & UnixFileMode.UserExecute);
        var report = CheckDarwinAccess(program);
        Assert.True(report.NoExecute);
        Assert.True(report.NoSetId);
        Assert.False(report.ReadOnly);
        Check(report, "Read content", PermissionReadStatus.Present);
        Check(report, "Execute permission", PermissionReadStatus.Denied);
        Assert.Equal(13, Assert.Single(report.Checks, check => check.Right == "Execute permission").NativeError);
    }

    // G-ACCESS real and effective IDs on macOS: with real UID 0 and an ordinary effective UID, the check answers for the
    // effective identity, although access(2) answers for the real root identity.
    [Fact]
    public void Darwin_access_check_uses_the_effective_not_the_real_identity()
    {
        string secret = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACCESS_REAL_ROOT") ?? string.Empty;
        if (!OperatingSystem.IsMacOS() || secret.Length == 0)
            Assert.Skip("Requires a macOS process whose real UID is root and effective UID is not.");
        Assert.False(Environment.IsPrivilegedProcess);
        var report = CheckDarwinAccess(secret);
        Assert.Equal(0u, report.RealUid);
        Assert.NotEqual(0u, report.EffectiveUid);
        Check(report, "Read content", PermissionReadStatus.Denied);
        Assert.Equal(0, DarwinRealAccess(secret, 4));
    }

    // G-FLAG system flags on macOS: an owned file under system immutable or system append-only protection (set by root) is
    // read only to ordinary FileCat on both writer routes. Inspection names the protection, neither a mode change nor a
    // flag clear is prepared, and the flags stay as they were.
    [Theory]
    [InlineData("schg", 0x20000u)]
    [InlineData("sappnd", 0x40000u)]
    public void Darwin_system_protection_is_read_only_to_ordinary_reviews(string flag, uint bit)
    {
        string directory = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_SYSTEM_FLAGS") ?? string.Empty;
        if (!OperatingSystem.IsMacOS() || directory.Length == 0 || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires owned macOS files that root marked with system flags.");
        string path = Path.Join(directory, flag);
        var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var ct = TestContext.Current.CancellationToken;
        string before = RunAclTool("stat", "-f", "%Sf %Lp", path);
        Assert.Contains(flag, before);
        foreach (bool descriptor in new[] { false, true })
        {
            var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
                qualifyProtectionWrites: true);
            using (var inspection = service.Inspect(subject, DarwinSections, ct))
            {
                Assert.Equal(bit, (uint)inspection.Snapshot.Flags.Value!.Value.Bits & bit);
                Assert.Contains("System file protection is read only", DarwinPermissionService.ModeRefusal(inspection.Snapshot));
                Assert.ThrowsAny<Exception>(() => DarwinPermissionService.Prepare(inspection, item,
                    new(PermissionScope.Objects, [new ChangePermissionMode(0x180, 0xfff)]), false, ct));
            }
            using (var inspection = service.Inspect(subject, DarwinSections, ct))
                Assert.ThrowsAny<Exception>(() => DarwinPermissionService.Prepare(inspection, item,
                    new(PermissionScope.Objects, [new ChangePermissionFlags(0, bit)]), false, ct));
        }
        Assert.Equal(before, RunAclTool("stat", "-f", "%Sf %Lp", path));
    }

    private static DarwinAccessReport CheckDarwinAccess(string path)
    {
        var item = ItemRef.ForFileSystemPath(path, EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        return new DarwinPermissionService(qualifyAccessChecks: true).CheckEffectiveAccess(subject, TestContext.Current.CancellationToken);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "access", SetLastError = true)]
    private static extern int DarwinRealAccess([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string path, int mode);

    private static void Check(DarwinAccessReport report, string right, PermissionReadStatus status)
    {
        var check = Assert.Single(report.Checks, check => check.Right == right);
        Assert.True(check.Status == status, $"{right}: expected {status}, got {check.Status} (errno {check.NativeError}).");
    }
}
