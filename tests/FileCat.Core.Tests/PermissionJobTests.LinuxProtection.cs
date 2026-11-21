using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    private const PermissionSections LinuxProtectionRead = PermissionSections.Ordinary | PermissionSections.Flags;

    [Fact]
    public void Ext4_inode_protection_observation_is_independent_of_mode_and_acl()
    {
        RequireExt4Writer();
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_IMMUTABLE_CAP") == "1")
            Assert.Skip("Requires the ordinary credential without CAP_LINUX_IMMUTABLE.");
        using var fixture = new Fixture();
        var (item, subject) = Ext4Subject(fixture);
        string path = item.FileSystemPath!;
        RunAclTool("chattr", "+d", path);
        var ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService(qualifyProtectionWrites: true);
        using var inspection = service.Inspect(subject, LinuxProtectionRead, ct);
        var flags = Assert.IsType<PermissionFlagsValue>(inspection.Snapshot.Flags.Value);
        Assert.Equal(uint.MaxValue, flags.KnownBits);
        Assert.Contains('d', InodeFlagText(path));
        Assert.Equal(0UL, flags.WritableBits);
        Assert.Contains("CAP_LINUX_IMMUTABLE", inspection.Snapshot.Flags.Reason);
        var draft = new PermissionDraft(PermissionScope.Objects, [new ChangePermissionFlags(
            LinuxPermissionService.ImmutableInode, LinuxPermissionService.ImmutableInode)]);
        Assert.Throws<UnauthorizedAccessException>(() => LinuxPermissionService.Prepare(inspection, item, draft, false, ct));

        File.SetUnixFileMode(path, 0);
        using var pinned = service.Inspect(subject, LinuxProtectionRead, ct);
        Assert.Equal((ushort)0, pinned.Snapshot.Mode.Value);
        Assert.Equal(PermissionReadStatus.Absent, pinned.Snapshot.Access.Status);
        Assert.Equal(new PermissionFlagsValue(0, LinuxPermissionService.InodeProtection), pinned.Snapshot.Flags.Value);
        Assert.Equal(13, pinned.Snapshot.Flags.NativeError); // EACCES opening the readable descriptor.
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("mode-zero")]
    [InlineData("drift")]
    [InlineData("access")]
    [InlineData("default")]
    public async Task Ext4_inode_protection_job_verifies_touched_flags_and_compound_order(string scenario)
    {
        RequireExt4ProtectionCapability();
        // The installed client must use capability-free journal storage. Exercise the native setter with the test journal.
        using var fixture = new Fixture();
        bool defaults = scenario == "default";
        var (item, subject) = Ext4Subject(fixture);
        if (defaults)
        {
            item = new ItemRef(Location.FileSystem(fixture.Root), "protected-dir", EntryKind.Directory);
            Directory.CreateDirectory(item.FileSystemPath!);
            subject = subject with { RawLocator = item.FileSystemPath!, DisplayPath = item.FileSystemPath!, Kind = PermissionObjectKind.Directory };
        }
        string path = item.FileSystemPath!;
        var ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService(qualifyProtectionWrites: true, qualifyDefaultWrites: true);
        PermissionDelta change = scenario is "access" or "default" ? new ChangePosixAcl(defaults,
            [new(PosixAclTag.Owner, 6, null), new(PosixAclTag.NamedUser, 4, new(PrincipalKind.UnixUser, "12345", subject.Authority.Id)),
                new(PosixAclTag.OwningGroup, 0, null), new(PosixAclTag.Mask, 4, null), new(PosixAclTag.Other, 0, null)])
            : new ChangePermissionMode((ushort)(scenario == "mode-zero" ? 0 : 0x100), 0xfff);
        bool append = scenario != "mode-zero";
        uint finalFlags = append ? LinuxPermissionService.AppendOnlyInode : 0;
        var draft = new PermissionDraft(defaults ? PermissionScope.DefaultsForNewChildren : PermissionScope.Objects,
            [new ChangePermissionFlags(finalFlags, LinuxPermissionService.ImmutableInode | finalFlags), change]);
        string opening = InodeFlagText(path);
        var atDurableOutcomes = new List<string>();
        try
        {
            RunAclTool("chattr", "+di", path);
            using var inspection = service.Inspect(subject, LinuxProtectionRead, ct);
            Assert.True(LinuxPermissionService.CanEditProtection(inspection.Snapshot));
            Assert.Throws<UnauthorizedAccessException>(() => LinuxPermissionService.Prepare(inspection, item, new(draft.Scope, [change]), false, ct));
            var plan = LinuxPermissionService.PreviewProtection(inspection.Snapshot, draft, null);
            Assert.Equal(new[] { PermissionSections.Flags, change.Section }.Concat(append ? [PermissionSections.Flags] : []),
                plan.Stages.Select(stage => stage.Change.Section));
            using var review = LinuxPermissionService.Prepare(inspection, item, draft, false, ct).Review;
            fixture.Verify = () =>
            {
                if (scenario == "mode-zero" || fixture.JournalPath is not { } journal ||
                    JobJournal.ReadAll(journal).Count(row => row.Type == "permission-outcome") <= atDurableOutcomes.Count) return;
                atDurableOutcomes.Add(InodeFlagText(path));
                if (scenario == "drift" && atDurableOutcomes.Count == 1) RunAclTool("chattr", "-d", path);
            };
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            var flagIntents = JobJournal.ReadAll(fixture.JournalPath!)
                .Where(row => row.Type == "permission-intent" && row.Node["flagTouched"] is not null).ToArray();
            Assert.Equal(append && scenario != "drift" ? 2 : 1, flagIntents.Length);
            Assert.Equal(0UL, flagIntents[0].Node["flagBits"]!.GetValue<ulong>());
            Assert.Equal((ulong)LinuxPermissionService.ImmutableInode, flagIntents[0].Node["flagTouched"]!.GetValue<ulong>());
            if (scenario == "drift")
            {
                Assert.True(result.Verdict == PermissionVerdict.Partial, JobIssue.Summarize(job.Issues));
                Assert.Equal(PermissionVerdict.Verified, result.Components[0].Verdict);
                Assert.Equal(PermissionVerdict.Conflict, result.Components[1].Verdict);
                Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
                Assert.Equal(opening, InodeFlagText(path));
            }
            else
            {
                Assert.True(result.Verdict == PermissionVerdict.Verified, JobIssue.Summarize(job.Issues));
                Assert.All(result.Components, component => Assert.Equal(PermissionVerdict.Verified, component.Verdict));
                if (scenario == "mode-zero")
                    Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(path));
                else
                {
                    Assert.Equal((ulong)LinuxPermissionService.AppendOnlyInode, flagIntents[1].Node["flagBits"]!.GetValue<ulong>());
                    Assert.Equal((ulong)LinuxPermissionService.AppendOnlyInode, flagIntents[1].Node["flagTouched"]!.GetValue<ulong>());
                    Assert.DoesNotContain('i', atDurableOutcomes[0]);
                    Assert.DoesNotContain('a', atDurableOutcomes[1]);
                    Assert.Contains('a', InodeFlagText(path));
                    Assert.Contains('d', InodeFlagText(path));
                }
                if (scenario == "access") Assert.Contains("user:12345:r--", RunAclTool("getfacl", "-ncp", path));
                if (defaults) Assert.Contains("default:user:12345:r--", RunAclTool("getfacl", "-ncp", path));
            }
        }
        finally
        {
            if (File.GetUnixFileMode(path) == 0) File.SetUnixFileMode(path, (UnixFileMode)0x180);
            RunAclTool("chattr", "-iad", path);
        }
    }

    [Fact]
    public async Task Ext4_inode_protection_job_reviews_group_side_effects_before_append()
    {
        RequireExt4ProtectionCapability();
        string? path = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_FLAG_GROUP_FILE");
        if (string.IsNullOrWhiteSpace(path)) Assert.Skip("Requires a private file-capability fixture for the protection job.");
        using var fixture = new Fixture();
        var item = new ItemRef(Location.FileSystem(Path.GetDirectoryName(path)!), Path.GetFileName(path), EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var ct = TestContext.Current.CancellationToken;
        var credential = LinuxPermissionService.ReadGroupAuthority(ct);
        var service = new LinuxPermissionService(qualifyGroupWrites: true, qualifyProtectionWrites: true);
        try
        {
            RunAclTool("chattr", "+di", path);
            using var inspection = service.Inspect(subject, LinuxProtectionRead, ct);
            Assert.Equal(PermissionReadStatus.Present, inspection.Snapshot.FileCapabilities?.Status);
            var group = PickAnotherGroup(credential, subject.Authority, inspection.Snapshot.Group.Value);
            var draft = new PermissionDraft(PermissionScope.Objects, [
                new ChangePermissionFlags(LinuxPermissionService.AppendOnlyInode, LinuxPermissionService.InodeProtection),
                new AssignPermissionPrincipal(false, group)]);
            var plan = LinuxPermissionService.PreviewProtection(inspection.Snapshot, draft, credential);
            Assert.True(plan.NeedsSetIdConsent);
            Assert.Equal(new[] { PermissionSections.Flags, PermissionSections.Group, PermissionSections.Flags },
                plan.Stages.Select(stage => stage.Change.Section));
            Assert.Equal(PermissionReadStatus.Absent, plan.Stages[1].Expected.FileCapabilities?.Status);
            Assert.Throws<InvalidOperationException>(() => LinuxPermissionService.Prepare(inspection, item, draft, false, ct, credential));
            using var review = LinuxPermissionService.Prepare(inspection, item, draft, true, ct, credential).Review;
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            Assert.True(Assert.Single(job.PermissionResults).Verdict == PermissionVerdict.Verified, JobIssue.Summarize(job.Issues));
            Assert.Equal(group.Id, RunAclTool("stat", "-c", "%g", path).Trim());
            Assert.Equal((UnixFileMode)0x1e8, File.GetUnixFileMode(path));
            Assert.Empty(RunAclTool("getcap", path).Trim());
            Assert.Contains('a', InodeFlagText(path));
            Assert.Contains('d', InodeFlagText(path));
        }
        finally { RunAclTool("chattr", "-iad", path); }
    }

    private static string InodeFlagText(string path) => RunAclTool("lsattr", "-d", path).Split(' ', 2)[0];

    private static void RequireExt4ProtectionCapability()
    {
        RequireExt4Writer();
        if (Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_IMMUTABLE_CAP") != "1")
            Assert.Skip("Requires non-root CAP_LINUX_IMMUTABLE in the private fixture.");
        var credentials = LinuxPermissionService.ReadGroupAuthority(TestContext.Current.CancellationToken);
        Assert.Equal(1UL << 9, credentials.Capabilities);
    }
}
