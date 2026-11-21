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

    private static void Check(DarwinAccessReport report, string right, PermissionReadStatus status)
    {
        var check = Assert.Single(report.Checks, check => check.Right == right);
        Assert.True(check.Status == status, $"{right}: expected {status}, got {check.Status} (errno {check.NativeError}).");
    }
}
