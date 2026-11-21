using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    [Theory]
    [InlineData(false, "group-only")]
    [InlineData(true, "group-only")]
    [InlineData(false, "protected-compound")]
    [InlineData(true, "protected-compound")]
    [InlineData(false, "drift-after-intent")]
    [InlineData(true, "drift-after-intent")]
    [InlineData(false, "directory-grant")]
    [InlineData(true, "directory-grant")]
    public async Task Darwin_group_job_restricts_before_chgrp_and_verifies_set_id_effects(bool descriptor, string scenario)
    {
        RequireDarwinWriter();
        CancellationToken ct = TestContext.Current.CancellationToken;
        var authority = DarwinPermissionService.ReadGroupAuthority(ct);
        using var fixture = DarwinJournalFixture();
        var (item, subject) = DarwinSubject(fixture);
        bool directory = scenario == "directory-grant", compound = scenario == "protected-compound";
        if (directory)
        {
            string folder = Directory.CreateDirectory(Path.Combine(item.Parent.Path, "group-folder"), (UnixFileMode)0x1c0).FullName;
            (item, subject) = TreeSubject(folder, EntryKind.Directory);
        }
        string path = item.FileSystemPath!;
        var nativeBefore = ReadDarwinGroupMode(path);
        var allowed = authority.Groups.Where(group => group != nativeBefore.Group).ToArray();
        if (allowed.Length == 0 || (scenario == "drift-after-intent" && allowed.Length < 2))
            Assert.Skip("Requires additional groups in the ordinary fixture credential.");
        uint targetGroup = allowed[0];
        ushort baselineMode = directory ? (ushort)0x1c0 : compound ? (ushort)0xde0 : descriptor ? (ushort)0xdc0 : (ushort)0xc00;
        File.SetUnixFileMode(path, (UnixFileMode)baselineMode);
        Assert.Equal(baselineMode, ReadDarwinGroupMode(path).Mode);
        if (!directory && scenario != "group-only") RunAclTool("chmod", "+a#", "0", "everyone allow readattr", path);
        RunAclTool("chflags", compound ? "nodump,uchg" : "nodump", path);
        var modified = File.GetLastWriteTimeUtc(path);
        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
            qualifyProtectionWrites: true, qualifyGroupWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        var snapshot = inspection.Snapshot;
        Assert.Equal(descriptor ? DarwinBindingRoute.Descriptor : DarwinBindingRoute.ParentRelative, snapshot.Binding?.DarwinRoute);
        Assert.True(DarwinPermissionService.CanEditGroup(snapshot));
        var assignment = new AssignPermissionPrincipal(false, Group(targetGroup));
        var draft = new PermissionDraft(PermissionScope.Objects, [assignment]);
        if (compound)
            draft = draft with { Changes = draft.Changes.Add(new ChangePermissionMode(0x588, 0xfff))
                .Add(new ChangePermissionFlags(DarwinPermissionService.UserAppendOnly, DarwinPermissionService.UserProtection)) };
        if (directory) draft = draft with { Changes = draft.Changes.Add(new ChangePermissionMode(0x1d0, 0xfff)) };
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => DarwinPermissionService.PreviewAccessChanges(snapshot,
                new(PermissionScope.Objects, [new AssignPermissionPrincipal(false, Group(uint.MaxValue - 1))]), authority));
            Assert.Throws<PermissionBindingConflictException>(() => DarwinPermissionService.Prepare(inspection, item, draft, true, ct,
                authority with { PrimaryGroup = uint.MaxValue - 1 }));
            var plan = DarwinPermissionService.PreviewAccessChanges(snapshot, draft, authority);
            if (!directory)
            {
                Assert.True(plan.NeedsSetIdConsent);
                var refused = Assert.Throws<InvalidOperationException>(() => DarwinPermissionService.Prepare(inspection, item, draft, false, ct, authority));
                Assert.Contains("set-ID effects", refused.Message);
                Assert.Equal((nativeBefore.User, nativeBefore.Group, baselineMode), ReadDarwinGroupMode(path));
                Assert.Null(fixture.JournalPath);
            }
            var observedStages = new List<(uint User, uint Group, ushort Mode, string Flags)>();
            bool drifted = false;
            fixture.Verify = () =>
            {
                if (fixture.JournalPath is not { } journal) return;
                var records = JobJournal.ReadAll(journal);
                if (scenario == "drift-after-intent" && !drifted && records.Any(record => record.Type == "permission-intent"))
                {
                    drifted = true;
                    RunAclTool("chgrp", allowed[1].ToString(CultureInfo.InvariantCulture), path);
                }
                if (records.Count(record => record.Type == "permission-outcome") <= observedStages.Count) return;
                var actual = ReadDarwinGroupMode(path);
                observedStages.Add((actual.User, actual.Group, actual.Mode, RunAclTool("stat", "-f", "%Sf", path)));
            };
            using var review = DarwinPermissionService.Prepare(inspection, item, draft, true, ct, authority).Review;
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
            if (scenario == "drift-after-intent")
            {
                Assert.True(drifted);
                Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
                Assert.Null(Assert.Single(result.Components).NativeCall);
                Assert.Equal((nativeBefore.User, allowed[1], (ushort)(baselineMode & ~0xc00)), ReadDarwinGroupMode(path));
                return;
            }
            Assert.True(result.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift,
                string.Join(" | ", result.Components.Select(step => $"{step.Verdict} {step.NativeCall} {step.NativeError} {step.Detail}")));
            Assert.All(result.Components, component => Assert.Equal(PermissionVerification.SameObject, component.Verification));
            ushort wanted = compound ? (ushort)0x588 : directory ? (ushort)0x1d0 : (ushort)(baselineMode & ~0xc00);
            Assert.Equal((nativeBefore.User, targetGroup, wanted), ReadDarwinGroupMode(path));
            Assert.All(observedStages, stage => Assert.Contains("nodump", stage.Flags));
            if (compound)
            {
                Assert.Equal(new[] { PermissionSections.Flags, PermissionSections.Mode, PermissionSections.Group,
                    PermissionSections.Mode, PermissionSections.Mode, PermissionSections.Flags }, result.Components.Select(step => step.Section));
                Assert.Equal(new[] { baselineMode, (ushort)0x180, (ushort)0x180, (ushort)0x188, wanted, wanted },
                    observedStages.Select(stage => stage.Mode));
                Assert.Equal(new[] { nativeBefore.Group, nativeBefore.Group, targetGroup, targetGroup, targetGroup, targetGroup },
                    observedStages.Select(stage => stage.Group));
                Assert.DoesNotContain("uchg", observedStages[0].Flags);
                Assert.All(observedStages.Take(5), stage => Assert.DoesNotContain("uappnd", stage.Flags));
                Assert.Contains("uappnd", observedStages[5].Flags);
            }
            if (snapshot.Access.Value?.Acl is DarwinAcl original)
            {
                using var after = new DarwinPermissionService().Inspect(subject, DarwinSections, ct);
                Assert.Equal(original.FileSecurity.ToArray(), Assert.IsType<DarwinAcl>(after.Snapshot.Access.Value?.Acl).FileSecurity.ToArray());
            }
        }
        finally { if (compound) RunAclTool("chflags", "nouchg,nouappnd", path); }

        Principal Group(uint id) => new(PrincipalKind.UnixGroup, id.ToString(CultureInfo.InvariantCulture), subject.Authority.Id);
    }

    private static (uint User, uint Group, ushort Mode) ReadDarwinGroupMode(string path)
    {
        var fields = RunAclTool("stat", "-f", "%u:%g:%Op", path).Trim().Split(':');
        return (uint.Parse(fields[0], CultureInfo.InvariantCulture), uint.Parse(fields[1], CultureInfo.InvariantCulture),
            (ushort)(Convert.ToUInt16(fields[2], 8) & 0xfff));
    }
}
