using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // G-MAC owner changes: macOS lets only root change an owner, and a privileged FileCat refuses reviewed writes, so the
    // ordinary routes offer no owner change and refuse to prepare one, while the same inspection still offers mode
    // changes. An owner change on macOS is the authorized route's (D3).
    [Fact]
    public void Darwin_ordinary_routes_offer_no_owner_change()
    {
        RequireDarwinWriter();
        if (Environment.IsPrivilegedProcess) Assert.Skip("Requires ordinary macOS credentials.");
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!;
        var before = UnixFiles.Stat(path, followLinks: false);
        Assert.NotNull(before);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyDescriptorWrites: true, qualifyProtectionWrites: true,
            qualifyAclWrites: true, qualifyGroupWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        Assert.Null(DarwinPermissionService.ModeRefusal(inspection.Snapshot));
        var owner = Assert.Single(inspection.Snapshot.Capabilities,
            capability => capability is { Section: PermissionSections.Owner, Action: PermissionAction.Change });
        Assert.NotEqual(PermissionAvailability.Available, owner.Availability);
        Assert.False(string.IsNullOrWhiteSpace(owner.Reason));
        var root = new Principal(PrincipalKind.UnixUser, 0.ToString(CultureInfo.InvariantCulture), subject.Authority.Id);
        var refused = Assert.Throws<NotSupportedException>(() => DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new AssignPermissionPrincipal(true, root)]), false, ct));
        Assert.Contains("group, mode, times, file flags or extended ACL", refused.Message);
        Assert.Equal(before, UnixFiles.Stat(path, followLinks: false));
    }
}
