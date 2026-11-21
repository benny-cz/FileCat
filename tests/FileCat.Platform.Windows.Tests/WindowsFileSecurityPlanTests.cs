using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsFileSecurityPlanTests
{
    private const string Root = @"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\";
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_linked_file_owned_by_another_account_requires_a_strictly_restrictive_delta()
    {
        var everyone = new Principal(PrincipalKind.WindowsSid, "S-1-1-0", "local");
        var allow = new WindowsAce(0, 0, WindowsAceKind.Allow, 3, everyone, []);
        var deny = new WindowsAce(1, 0, WindowsAceKind.Deny, 1, everyone, []);
        var acl = new WindowsDacl(WindowsDaclForm.Entries, 2, [deny, allow], []);

        Assert.True(Restricts(new AddWindowsAce(deny)));
        Assert.True(Restricts(new ChangeWindowsAce(1, allow, null)));
        Assert.True(Restricts(new ChangeWindowsAce(1, allow, allow with { Mask = 1 })));
        Assert.True(Restricts(new ChangeWindowsAce(0, deny, deny with { Mask = 3 })));
        Assert.False(Restricts(new AddWindowsAce(allow with { Mask = 1 })));
        Assert.False(Restricts(new ChangeWindowsAce(0, deny, null)));
        Assert.False(Restricts(new ChangeWindowsAce(0, deny, deny with { Mask = 2 })));
        Assert.False(Restricts(new ChangeWindowsAce(1, allow, allow with { Mask = 7 })));
        Assert.False(Restricts(new AssignPermissionPrincipal(true, everyone)));
        var ownerRights = allow with { Principal = new(PrincipalKind.WindowsSid, "S-1-3-4", "local") };
        Assert.False(WindowsPermissionService.StrictlyRestrictsFileAccess(acl with { Entries = [ownerRights] },
            new ChangeWindowsAce(0, ownerRights, null)));
        Assert.False(Restricts(new AddWindowsAce(ownerRights with { Type = 1, Kind = WindowsAceKind.Deny })));
        Assert.False(WindowsPermissionService.StrictlyRestrictsFileAccess(acl with
            { Entries = [allow with { Kind = WindowsAceKind.Opaque }] }, new AddWindowsAce(deny)));

        bool Restricts(PermissionDelta delta) => WindowsPermissionService.StrictlyRestrictsFileAccess(acl, delta);
    }

    [Fact]
    public void The_fixed_request_round_trips_an_exact_item_or_volume_root()
    {
        var item = Plan();
        var bytes = ElevationPlanCodec.Serialize(item);
        var parsed = ElevationPlanCodec.Parse(bytes);
        Assert.Empty(ElevationPlanCodec.Validate(parsed, Now));
        Assert.Equal(bytes, ElevationPlanCodec.Serialize(parsed));

        var step = Assert.Single(item.Steps);
        var root = step with
        {
            Path = Root,
            FileSecurity = step.FileSecurity! with { Kind = PermissionObjectKind.VolumeRoot, FileIds = [Id(1)] }
        };
        Assert.Empty(ElevationPlanCodec.Validate(item with { Steps = [root] }, Now));
        Assert.NotEmpty(ElevationPlanCodec.Validate(item with { Steps = [root with { Verb = ElevatedVerb.DeleteTree }] }, Now));
    }

    [Theory]
    [InlineData(@"Z:\test.txt")]
    [InlineData(@"\\server\share\test.txt")]
    [InlineData(Root + @"folder\..\file.txt")]
    [InlineData(Root + "file.txt:stream")]
    [InlineData(Root + "file\0.txt")]
    [InlineData(Root + "file.txt\\")]
    public void The_target_rejects_path_aliases_and_unsafe_names(string path)
    {
        var plan = Plan();
        var step = Assert.Single(plan.Steps) with { Path = path };
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { Steps = [step] }, Now));
    }

    [Fact]
    public void Requester_and_target_evidence_cannot_be_omitted_or_aliased()
    {
        var plan = Plan();
        var step = Assert.Single(plan.Steps);
        var scope = step.FileSecurity!;
        foreach (var invalid in new[]
        {
            scope with { FileIds = [Id(1)] },
            scope with { FileIds = [Id(1), Id(1)] },
            scope with { Kind = PermissionObjectKind.Link },
            scope with { LinkCount = 0 },
            scope with { RequesterCreatedFileTime = 0 },
            scope with { RequesterLogonSid = "S-1-5-5-01-20" },
            scope with { RequesterImageId = Id(1) },
            scope with { Sections = PermissionSections.Mode },
        })
            Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { Steps = [step with { FileSecurity = invalid }] }, Now));
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { UserSid = "S-1-5-21-001" }, Now));
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan, Now.AddSeconds(90)));
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { CreatedUtc = Now.AddSeconds(1) }, Now));
    }

    [Fact]
    public void Owner_changes_and_acl_changes_have_disjoint_typed_fields()
    {
        var plan = Plan(change: true);
        var step = Assert.Single(plan.Steps);
        var scope = step.FileSecurity!;
        var owner = new ElevatedFileSecurityChange(ElevatedFileSecurityOperation.AssignOwner, Id(3, 64), Id(4, 64), "S-1-5-21-100");
        var ownerScope = scope with { Sections = PermissionSections.Owner, Change = owner };
        Assert.Empty(ElevationPlanCodec.Validate(plan with { Steps = [step with { FileSecurity = ownerScope }] }, Now));
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { Steps = [step with
            { FileSecurity = ownerScope with { Change = owner with { Index = 0 } } }] }, Now));

        var audit = new ElevatedFileSecurityRule(ElevatedFileSecurityRuleType.Audit, "S-1-1-0", 1, 0x40);
        var add = new ElevatedFileSecurityChange(ElevatedFileSecurityOperation.AddRule, Id(3, 64), Id(4, 64), NewRule: audit);
        var auditScope = scope with { Sections = PermissionSections.Audit, Change = add };
        Assert.Empty(ElevationPlanCodec.Validate(plan with { Steps = [step with { FileSecurity = auditScope }] }, Now));
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { Steps = [step with
            { FileSecurity = auditScope with { Sections = PermissionSections.Access } }] }, Now));
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { Steps = [step with
            { FileSecurity = auditScope with { Change = add with { NewRule = audit with { Flags = 0 } } } }] }, Now));
        Assert.NotEmpty(ElevationPlanCodec.Validate(plan with { Steps = [step with
            { FileSecurity = auditScope with { Change = add with { NewRule = audit with { Rights = uint.MaxValue } } } }] }, Now));
    }

    [Fact]
    public void Security_requests_cannot_reach_the_legacy_runner_or_mix_with_other_steps()
    {
        var plan = Plan();
        var step = Assert.Single(plan.Steps);
        var invalid = new[]
        {
            plan with { Version = ElevationPlan.CurrentVersion },
            plan with { Steps = [step, new(ElevatedVerb.DeleteTree) { Path = Root + "test.txt" }] },
            plan with { Steps = [step with { Destination = Root }] },
            plan with { Steps = [step with { ReplaceExisting = true }] },
            plan with { Steps = [step with { Verb = ElevatedVerb.SetAttributes, SetAttributes = FileAttributes.Hidden }] },
        };
        foreach (var item in invalid)
        {
            Assert.NotEmpty(ElevationPlanCodec.Validate(item, Now));
            Assert.Throws<InvalidDataException>(() => ElevationPlanRunner.Run(item, () => false, _ => { }));
        }
        Assert.Throws<InvalidDataException>(() => ElevationPlanRunner.Run(plan, () => false, _ => { }));
    }

    [Fact]
    public void Wire_shape_rejects_extra_duplicate_and_oversize_fields()
    {
        string normal = Encoding.UTF8.GetString(ElevationPlanCodec.Serialize(Plan()));
        Assert.Throws<InvalidDataException>(() => ElevationPlanCodec.Parse(Encoding.UTF8.GetBytes("{\"Surplus\":0," + normal[1..])));
        Assert.Throws<InvalidDataException>(() => ElevationPlanCodec.Parse(Encoding.UTF8.GetBytes("{\"Version\":2," + normal[1..])));
        Assert.Throws<InvalidDataException>(() => ElevationPlanCodec.Parse(Encoding.UTF8.GetBytes(
            normal.PadRight(ElevationPlanCodec.MaxFileSecurityPlanBytes + 1))));
    }

    private static ElevationPlan Plan(bool change = false) => new()
    {
        Version = ElevationPlan.FileSecurityVersion,
        Nonce = Id(7, 64),
        CreatedUtc = Now,
        UserSid = "S-1-5-21-1-2-3-1000",
        RequesterProcessId = 123,
        Title = change ? ElevationPlanCodec.FileSecurityChangeTitle : ElevationPlanCodec.FileSecurityInspectTitle,
        Steps = [new ElevatedStep(change ? ElevatedVerb.ChangeFileSecurity : ElevatedVerb.InspectFileSecurity)
        {
            Path = Root + "test.txt",
            FileSecurity = new(Id(5, 16), [Id(1), Id(2)], PermissionObjectKind.File, 1, 12345, 1,
                "S-1-5-5-10-20", Id(6, 48), PermissionSections.Owner | PermissionSections.Access, PermissionScope.Objects),
        }],
    };

    private static string Id(int id, int width = 32) => id.ToString($"X{width}", System.Globalization.CultureInfo.InvariantCulture);
}
