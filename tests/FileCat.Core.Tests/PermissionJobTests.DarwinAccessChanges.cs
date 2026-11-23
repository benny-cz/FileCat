using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // G-RESULT durability on macOS: both writer routes commit the change with F_FULLFSYNC before reporting it verified.
    // A flush that fails leaves the change uncertain, with the reason, although the file system already reads it back.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Darwin_job_reports_a_change_it_could_not_flush_as_uncertain(bool descriptor)
    {
        RequireDarwinWriter();
        if (Environment.IsPrivilegedProcess) Assert.Skip("Requires ordinary macOS credentials.");
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        using var second = DarwinJournalFixture(); // one job per fixture
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!;
        File.SetUnixFileMode(path, (UnixFileMode)0x1c0); // owner execute keeps the regular file eligible for O_EXEC.
        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor);
        async Task<PermissionResult> Run(Fixture fixture, ushort mode)
        {
            using var inspection = service.Inspect(subject, DarwinSections, ct);
            Assert.Equal(descriptor ? DarwinBindingRoute.Descriptor : DarwinBindingRoute.ParentRelative, inspection.Snapshot.Binding?.DarwinRoute);
            using var review = DarwinPermissionService.Prepare(inspection, item,
                new(PermissionScope.Objects, [new ChangePermissionMode(mode, 0xfff)]), false, ct).Review;
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            return Assert.Single(job.PermissionResults);
        }
        var verified = await Run(fixture, 0x1e0);
        Assert.True(verified.Verdict == PermissionVerdict.Verified, $"{verified.Verdict}: {verified.Detail}");
        DarwinPermissionService.FlushFaultForTests = locator => locator == path ? 5 /* EIO */ : 0;
        PermissionResult result;
        try { result = await Run(second, 0x1c0); }
        finally { DarwinPermissionService.FlushFaultForTests = null; }
        Assert.Equal(PermissionVerdict.Uncertain, result.Verdict);
        var component = Assert.Single(result.Components);
        Assert.Equal(PermissionVerification.Unavailable, component.Verification);
        Assert.Contains("could not write its pending changes to the device (error 5)", component.Detail);
        Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(path));
    }

    [Theory]
    [InlineData(false, "protected")]
    [InlineData(true, "protected")]
    [InlineData(false, "mixed-group")]
    [InlineData(true, "mixed-group")]
    [InlineData(false, "restriction")]
    [InlineData(true, "restriction")]
    [InlineData(false, "grant")]
    [InlineData(true, "grant")]
    [InlineData(false, "drift")]
    [InlineData(true, "drift")]
    public async Task Darwin_compound_ACL_reviews_intermediate_mode_group_and_protection(bool descriptor, string scenario)
    {
        RequireDarwinWriter();
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!;
        File.SetUnixFileMode(path, (UnixFileMode)0x1e0); // 0740 keeps the regular file eligible for O_EXEC.
        RunAclTool("chmod", "+a#", "0", "everyone deny write", path);
        RunAclTool("chmod", "+a#", "1", "everyone allow readattr", path);
        bool protect = scenario == "protected", assign = scenario is "mixed-group" or "restriction" or "grant";
        RunAclTool("chflags", protect ? "nodump,uchg" : "nodump", path);
        var baseline = ReadDarwinGroupMode(path);
        DateTime modified = File.GetLastWriteTimeUtc(path);
        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
            qualifyProtectionWrites: true, qualifyAclWrites: true, qualifyGroupWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        var snapshot = inspection.Snapshot;
        Assert.True(snapshot.Binding?.DarwinRoute == (descriptor ? DarwinBindingRoute.Descriptor : DarwinBindingRoute.ParentRelative),
            DarwinPermissionService.ModeRefusal(snapshot));
        var originalAcl = Assert.IsType<DarwinAcl>(snapshot.Access.Value?.Acl);
        var first = originalAcl.Entries[0];
        var denyRead = first with { Rights = 1u << 1 };
        DarwinAclDelta edit = scenario switch
        {
            "grant" => new ChangeDarwinAce(0, first, null),
            "restriction" => new AddDarwinAce(0, denyRead),
            _ => new ChangeDarwinAce(0, first, denyRead)
        };
        var draft = new PermissionDraft(PermissionScope.Objects, [edit]);
        DarwinGroupAuthority? authority = null;
        uint group = baseline.Group;
        if (assign)
        {
            authority = DarwinPermissionService.ReadGroupAuthority(ct);
            if (!authority.Groups.Any(id => id != group)) Assert.Skip("Requires a supplementary group in the ordinary fixture credential.");
            group = authority.Groups.First(id => id != group);
            var principal = new Principal(PrincipalKind.UnixGroup, group.ToString(CultureInfo.InvariantCulture), subject.Authority.Id);
            draft = draft with { Changes = draft.Changes.Add(new AssignPermissionPrincipal(false, principal)) };
        }
        if (protect || scenario == "drift")
            draft = draft with { Changes = draft.Changes.Add(new ChangePermissionMode(protect ? (ushort)0x528 : (ushort)0x128, 0xfff)) };
        try
        {
            if (protect)
            {
                var refusal = Assert.Throws<NotSupportedException>(() => DarwinPermissionService.PreviewAccessChanges(snapshot, draft));
                Assert.Contains("clearing the active file protection", refusal.Message);
                draft = draft with { Changes = draft.Changes.Add(new ChangePermissionFlags(DarwinPermissionService.UserAppendOnly,
                    DarwinPermissionService.UserProtection)) };
                Assert.True(DarwinPermissionService.PreviewAccessChanges(snapshot, draft).NeedsSetIdConsent);
                Assert.Throws<InvalidOperationException>(() => DarwinPermissionService.Prepare(inspection, item, draft, false, ct));
                Assert.Contains("uchg", RunAclTool("stat", "-f", "%Sf", path));
                Assert.Null(fixture.JournalPath);
            }

            var observed = new List<(uint User, uint Group, ushort Mode, string Acl, string Flags)>();
            fixture.Verify = () =>
            {
                if (fixture.JournalPath is not { } journal || JobJournal.ReadAll(journal).Count(record => record.Type == "permission-outcome") <= observed.Count) return;
                var current = ReadDarwinGroupMode(path);
                observed.Add((current.User, current.Group, current.Mode, RunAclTool("ls", "-led", path), RunAclTool("stat", "-f", "%Sf", path)));
                if (scenario == "drift" && observed.Count == 1) RunAclTool("chmod", "+a#", "0", "everyone deny execute", path);
            };
            var plan = DarwinPermissionService.PreviewAccessChanges(snapshot, draft, authority);
            using var review = DarwinPermissionService.Prepare(inspection, item, draft, true, ct, authority).Review;
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
            if (scenario == "drift")
            {
                Assert.Equal(PermissionVerdict.Partial, result.Verdict);
                Assert.Equal(PermissionVerdict.Verified, result.Components[0].Verdict);
                Assert.All(result.Components.Skip(1), component => Assert.Null(component.NativeCall));
                Assert.Equal((ushort)0x120, ReadDarwinGroupMode(path).Mode);
                Assert.Contains("everyone deny execute", RunAclTool("ls", "-led", path));
                return;
            }
            Assert.True(result.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift,
                string.Join(" | ", result.Components.Select(step => $"{step.Verdict} {step.NativeCall} {step.NativeError} {step.Detail}")));
            Assert.Equal(plan.Stages.Length, observed.Count);
            Assert.Equal(plan.Stages.Select(stage => stage.Expected.Mode.Value!.Value), observed.Select(state => state.Mode));
            Assert.Equal(plan.Stages.Select(stage => stage.Expected.Group.Value!.Value.Id),
                observed.Select(state => state.Group.ToString(CultureInfo.InvariantCulture)));
            Assert.All(observed, state => { Assert.Equal(baseline.User, state.User); Assert.Contains("nodump", state.Flags); });
            using var final = new DarwinPermissionService().Inspect(subject, DarwinSections, ct);
            Assert.Equal(PermissionRevision.Read(plan.Expected, DarwinPermissionService.AccessChangeSections),
                PermissionRevision.Read(final.Snapshot, DarwinPermissionService.AccessChangeSections));
            PermissionSections[] sequence = scenario switch
            {
                "protected" => [PermissionSections.Flags, PermissionSections.Mode, PermissionSections.Access,
                    PermissionSections.Mode, PermissionSections.Mode, PermissionSections.Flags],
                "mixed-group" => [PermissionSections.Mode, PermissionSections.Group, PermissionSections.Access, PermissionSections.Mode],
                "restriction" => [PermissionSections.Access, PermissionSections.Group],
                _ => [PermissionSections.Group, PermissionSections.Access]
            };
            Assert.Equal(sequence, result.Components.Select(step => step.Section));
            if (protect)
            {
                Assert.DoesNotContain("uchg", observed[0].Flags);
                Assert.DoesNotContain("uappnd", observed[^2].Flags);
                Assert.Contains("uappnd", observed[^1].Flags);
                Assert.Contains("everyone deny write", observed[1].Acl);
                Assert.Contains("everyone deny read", observed[2].Acl);
            }
            if (scenario == "mixed-group")
            {
                Assert.Equal((ushort)0x1c0, observed[0].Mode);
                Assert.Equal((ushort)0x1c0, observed[1].Mode);
                Assert.Equal((ushort)0x1c0, observed[2].Mode);
                Assert.Contains("everyone deny write", observed[1].Acl);
                Assert.Contains("everyone deny read", observed[2].Acl);
                Assert.Equal(baseline.Mode, observed[3].Mode);
            }
        }
        finally { if (protect) RunAclTool("chflags", "nouchg,nouappnd", path); }
    }

    [Fact]
    public async Task Darwin_compound_ACL_puts_a_checked_directory_namespace_grant_last()
    {
        RequireDarwinWriter();
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        string path = Directory.CreateDirectory(Path.Join(fixture.Root, "private-folder"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute).FullName;
        var (item, subject) = TreeSubject(path, EntryKind.Directory);
        RunAclTool("chmod", "+a#", "0", "everyone allow list", path);
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyAclWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        var baseline = Assert.IsType<DarwinAcl>(inspection.Snapshot.Access.Value?.Acl);
        var add = new AddDarwinAce(1, baseline.Entries[0] with { Rights = 1u << 2 });
        var bothOutside = new PermissionDraft(PermissionScope.Objects, [add, new ChangePermissionMode(0x1f8, 0xfff)]);
        var refusal = Assert.Throws<NotSupportedException>(() => DarwinPermissionService.Prepare(inspection, item, bothOutside, false, ct));
        Assert.Contains("last write", refusal.Message);
        Assert.Null(fixture.JournalPath);
        var draft = new PermissionDraft(PermissionScope.Objects, [add, new ChangePermissionMode(0x1c8, 0xfff)]);
        using var review = DarwinPermissionService.Prepare(inspection, item, draft, false, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        Assert.True(result.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift,
            string.Join(" | ", result.Components.Select(step => $"{step.Verdict} {step.NativeCall} {step.NativeError} {step.Detail}")));
        Assert.Equal(new[] { PermissionSections.Mode, PermissionSections.Access }, result.Components.Select(step => step.Section));
        Assert.Equal((ushort)0x1c8, ReadDarwinGroupMode(path).Mode);
        Assert.Contains("everyone allow add_file", RunAclTool("ls", "-led", path));
    }
}
