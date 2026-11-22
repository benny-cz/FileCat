using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    private const PermissionSections DarwinSections = PermissionSections.Ordinary | PermissionSections.Times | PermissionSections.Flags;

    [Theory]
    [InlineData(false, "clear-then-protect")]
    [InlineData(true, "clear-then-protect")]
    [InlineData(false, "clear-append-then-lock")]
    [InlineData(true, "clear-append-then-lock")]
    [InlineData(false, "preserve-lock")]
    [InlineData(true, "preserve-lock")]
    [InlineData(false, "flag-drift")]
    [InlineData(true, "flag-drift")]
    [InlineData(false, "partial-after-unlock")]
    [InlineData(true, "partial-after-unlock")]
    public async Task Darwin_protection_jobs_touch_reviewed_bits_in_the_reviewed_order(bool descriptor, string scenario)
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!;
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor, qualifyProtectionWrites: true);
        ushort originalMode = descriptor ? (ushort)0x1c0 : (ushort)0x180;
        bool reverse = scenario == "clear-append-then-lock";
        string originalProtection = reverse ? "uappnd" : "uchg", finalProtection = reverse ? "uchg" : "uappnd";
        File.SetUnixFileMode(path, (UnixFileMode)originalMode);
        RunAclTool("chmod", "+a#", "0", "everyone allow read", path);
        RunAclTool("chflags", scenario == "flag-drift" ? "nodump" : "nodump," + originalProtection, path);
        try
        {
            using var inspection = service.Inspect(subject, DarwinSections, ct);
            var snapshot = inspection.Snapshot;
            Assert.Equal(descriptor ? DarwinBindingRoute.Descriptor : DarwinBindingRoute.ParentRelative, snapshot.Binding?.DarwinRoute);
            Assert.Equal((ulong)DarwinPermissionService.UserProtection, snapshot.Flags.Value!.Value.WritableBits & DarwinPermissionService.UserProtection);
            Assert.Throws<NotSupportedException>(() => DarwinPermissionService.PreviewFlags(snapshot,
                new ChangePermissionFlags(0, DarwinPermissionService.SystemImmutable)));
            var access = Assert.IsType<DarwinAcl>(snapshot.Access.Value?.Acl);
            var mode = new ChangePermissionMode((ushort)(originalMode | 0x20), 0xfff);
            if (scenario == "preserve-lock")
            {
                using var unqualified = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor)
                    .Inspect(subject, DarwinSections, ct);
                Assert.Throws<NotSupportedException>(() => DarwinPermissionService.PreviewFlags(unqualified.Snapshot,
                    new ChangePermissionFlags(0, DarwinPermissionService.UserLocked)));
                var refusal = Assert.Throws<NotSupportedException>(() => DarwinPermissionService.Prepare(inspection, item,
                    new(PermissionScope.Objects, [mode]), false, ct));
                Assert.Contains("clearing the active file protection", refusal.Message);
                Assert.Contains("uchg", RunAclTool("stat", "-f", "%Sf", path));
                Assert.Equal((UnixFileMode)originalMode, File.GetUnixFileMode(path));
                Assert.Null(fixture.JournalPath);
                return;
            }
            if (scenario == "flag-drift")
            {
                using var review = DarwinPermissionService.Prepare(inspection, item,
                    new(PermissionScope.Objects, [new ChangePermissionFlags(DarwinPermissionService.UserLocked, DarwinPermissionService.UserLocked)]),
                    false, ct).Review;
                RunAclTool("chflags", "dump", path);
                var conflict = fixture.Manager.SubmitPermissions(review);
                await fixture.Finish(conflict);
                Assert.Equal(PermissionVerdict.Conflict, Assert.Single(conflict.PermissionResults).Verdict);
                string current = RunAclTool("stat", "-f", "%Sf", path);
                Assert.DoesNotContain("uchg", current);
                Assert.DoesNotContain("nodump", current);
                Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
                return;
            }

            var when = new DateTimeOffset(2024, 8, 9, 10, 11, 12, TimeSpan.Zero);
            var created = when.AddDays(-1);
            uint requestedProtection = reverse ? DarwinPermissionService.UserLocked : DarwinPermissionService.UserAppendOnly;
            var flags = new ChangePermissionFlags(requestedProtection | MacFinderHidden.Hidden,
                DarwinPermissionService.UserProtection | MacFinderHidden.Hidden);
            var wouldChangeCreation = Assert.Throws<NotSupportedException>(() => DarwinPermissionService.Prepare(inspection, item,
                new(PermissionScope.Objects, [mode, new ChangePermissionTimes(when, null), flags]), false, ct));
            Assert.Contains("would change the creation time", wouldChangeCreation.Message);
            Assert.Contains(originalProtection, RunAclTool("stat", "-f", "%Sf", path));
            Assert.Null(fixture.JournalPath);
            var draft = new PermissionDraft(PermissionScope.Objects, [mode, new ChangePermissionTimes(when, created), flags]);
            using var operation = DarwinPermissionService.Prepare(inspection, item, draft, false, ct).Review;
            if (scenario == "partial-after-unlock")
            {
                bool drifted = false;
                fixture.Verify = () =>
                {
                    if (drifted || fixture.JournalPath is not { } journal ||
                        !JobJournal.ReadAll(journal).Any(record => record.Type == "permission-outcome")) return;
                    drifted = true;
                    RunAclTool("chflags", "dump", path);
                };
            }
            var job = fixture.Manager.SubmitPermissions(operation);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            if (scenario == "partial-after-unlock")
            {
                Assert.Equal(PermissionVerdict.Partial, result.Verdict);
                Assert.Equal(PermissionVerdict.Verified, result.Components[0].Verdict);
                Assert.Contains("clear Finder Locked", result.Components[0].NativeCall);
                Assert.Equal(PermissionVerdict.Conflict, result.Components[1].Verdict);
                string partial = RunAclTool("stat", "-f", "%Sf", path);
                Assert.DoesNotContain(originalProtection, partial);
                Assert.DoesNotContain(finalProtection, partial);
                Assert.Equal((UnixFileMode)originalMode, File.GetUnixFileMode(path));
                Assert.Single(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
                return;
            }
            Assert.True(result.Verdict == PermissionVerdict.Verified, string.Join(" | ",
                result.Components.Select(step => $"{step.NativeCall} {step.Verdict} errno={step.NativeError} {step.Detail}")
                    .Append(RunAclTool("stat", "-f", "%Sp %Sf %m", path))));
            Assert.Equal(new[] { PermissionSections.Flags, PermissionSections.Mode, PermissionSections.Times, PermissionSections.Flags, PermissionSections.Flags },
                result.Components.Select(component => component.Section));
            Assert.All(result.Components, component => Assert.Equal(PermissionVerification.SameObject, component.Verification));
            string finalFlags = RunAclTool("stat", "-f", "%Sf", path);
            Assert.Contains(finalProtection, finalFlags);
            Assert.Contains("nodump", finalFlags);
            Assert.DoesNotContain(originalProtection, finalFlags);
            Assert.True(MacFinderHidden.Observe(path, ct));
            Assert.Equal((UnixFileMode)mode.Bits, File.GetUnixFileMode(path));
            Assert.Equal(when.UtcDateTime, File.GetLastWriteTimeUtc(path));
            Assert.Equal(created.UtcDateTime, File.GetCreationTimeUtc(path));
            using var after = service.Inspect(subject, DarwinSections, ct);
            Assert.Equal(access.FileSecurity.ToArray(), Assert.IsType<DarwinAcl>(after.Snapshot.Access.Value?.Acl).FileSecurity.ToArray());
        }
        finally { RunAclTool("chflags", "nouchg,nouappnd", path); }
    }

    [Fact]
    public async Task Darwin_descriptor_job_keeps_the_executable_open_after_removing_execute()
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!;
        RunAclTool("chmod", "+a#", "0", "everyone allow read", path);
        RunAclTool("chflags", "nodump", path);
        File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
        var created = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var modified = created.AddDays(2);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyDescriptorWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        Assert.Equal(DarwinBindingRoute.Descriptor, inspection.Snapshot.Binding?.DarwinRoute);
        var access = Assert.IsType<DarwinAcl>(inspection.Snapshot.Access.Value?.Acl);
        var draft = new PermissionDraft(PermissionScope.Objects,
            [new ChangePermissionMode(0x180, 0xfff), new ChangePermissionTimes(modified, created), new ChangePermissionFlags(0x8000, 0x8000)]);
        using var review = DarwinPermissionService.Prepare(inspection, item, draft, false, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        Assert.True(result.Verdict == PermissionVerdict.Verified, JobIssue.Summarize(job.Issues));
        Assert.All(result.Components, component => Assert.Contains("retained descriptor", component.NativeCall));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
        Assert.Equal(created.UtcDateTime, File.GetCreationTimeUtc(path));
        Assert.Equal(modified.UtcDateTime, File.GetLastWriteTimeUtc(path));
        Assert.True(MacFinderHidden.Observe(path, ct));
        Assert.Contains("nodump", RunAclTool("stat", "-f", "%Sf", path));
        using var after = service.Inspect(subject, DarwinSections, ct);
        Assert.Equal(access.FileSecurity.ToArray(), Assert.IsType<DarwinAcl>(after.Snapshot.Access.Value?.Acl).FileSecurity.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Darwin_descriptor_directory_keeps_identity_in_a_shared_parent(bool replaceBeforeDispatch)
    {
        RequireDarwinWriter();
        using var fixture = new Fixture();
        var item = new ItemRef(Location.FileSystem(fixture.Root), "directory", EntryKind.Directory);
        string path = Directory.CreateDirectory(item.FileSystemPath!, (UnixFileMode)0x1c0).FullName;
        string moved = path + ".moved";
        File.SetUnixFileMode(fixture.Root, (UnixFileMode)0x1f8);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new DarwinPermissionService(qualifyParentWrites: true, qualifyDescriptorWrites: true).Inspect(subject, DarwinSections, ct);
        Assert.Equal(DarwinBindingRoute.Descriptor, inspection.Snapshot.Binding?.DarwinRoute);
        using var review = DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x1c8, 0xfff)]), false, ct).Review;
        var target = Assert.Single(review.Targets);
        var component = Assert.Single(target.Components);
        _ = target.Mutation.CheckCurrent(component.Before.Sections, ct);
        if (replaceBeforeDispatch) Replace();
        var call = target.Mutation.Apply(component.Change, ct);
        Assert.Equal(replaceBeforeDispatch, call.BindingConflict);
        Assert.Null(call.NativeError);
        if (!replaceBeforeDispatch) Replace();

        var observed = target.Mutation.Observe(component.Expected.Sections);
        Assert.True(observed.FolderChanged);
        Assert.Equal(PermissionVerification.SameObject, observed.Verification);
        Assert.Equal((UnixFileMode)(replaceBeforeDispatch ? 0x1c0 : 0x1c8), File.GetUnixFileMode(moved));
        Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(path));

        void Replace()
        {
            Directory.Move(path, moved);
            Directory.CreateDirectory(path, (UnixFileMode)0x1c0);
        }
    }

    [Fact]
    public void Darwin_descriptor_mount_root_uses_the_selected_volume_instead_of_the_host_parent()
    {
        RequireDarwinWriter();
        string root = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT")!;
        var subject = new PermissionSubject(Location.FileSystem(root), root, root, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.VolumeRoot, PermissionLinkPolicy.NoFollow);
        using var inspection = new DarwinPermissionService().Inspect(subject, DarwinSections | PermissionSections.VolumePolicy,
            TestContext.Current.CancellationToken);
        Assert.Null(inspection.Snapshot.BindingProblem);
        var binding = Assert.IsType<PermissionBinding>(inspection.Snapshot.Binding);
        Assert.Equal(root, binding.MountRoot);
        Assert.Equal(PermissionObjectKind.VolumeRoot, binding.Identity.Kind);
        Assert.Equal(UnixFiles.Stat(root)!.Value.Identity, binding.Identity.Object);
        Assert.Equal("Darwin fgetattrlist", inspection.Snapshot.Mode.Source);
        Assert.Equal("Darwin fstatfs", inspection.Snapshot.VolumePolicy.Source);
        Assert.Equal(Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_IGNORE_OWNERS") != "1",
            inspection.Snapshot.VolumePolicy.Value?.HonorsOwnership);
    }

    [Fact]
    public void Darwin_descriptor_HFS_refuses_outside_exchange_access_and_reports_a_changed_retained_identity()
    {
        RequireDarwinWriter();
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!, companion = path + ".companion";
        File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyDescriptorWrites: true);
        using var initial = service.Inspect(subject, DarwinSections, ct);
        if (initial.Snapshot.Binding?.FileSystem != "hfs") Assert.Skip("Requires the disposable HFS+ image.");
        Assert.Equal(DarwinBindingRoute.Descriptor, initial.Snapshot.Binding.DarwinRoute);
        Assert.Throws<NotSupportedException>(() => DarwinPermissionService.PreviewMode(initial.Snapshot, new(0x1f8, 0xfff)));
        using var throughParent = new DarwinPermissionService(qualifyParentWrites: true, qualifyDescriptorWrites: true).Inspect(subject, DarwinSections, ct);
        Assert.Equal(DarwinBindingRoute.ParentRelative, throughParent.Snapshot.Binding?.DarwinRoute);
        Assert.Equal((ushort)0x1f8, DarwinPermissionService.PreviewMode(throughParent.Snapshot, new(0x1f8, 0xfff)).Mode.Value);
        RunAclTool("chmod", "+a#", "0", "everyone allow writesecurity", path);
        using var shared = service.Inspect(subject, DarwinSections, ct);
        Assert.Contains("both read-data and write-data", DarwinPermissionService.ModeRefusal(shared.Snapshot));
        RunAclTool("chmod", "-N", path);
        File.WriteAllText(companion, "companion");
        File.SetUnixFileMode(companion, (UnixFileMode)0x180);

        using var inspection = service.Inspect(subject, DarwinSections, ct);
        using var review = DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x180, 0xfff)]), false, ct).Review;
        var target = Assert.Single(review.Targets);
        var component = Assert.Single(target.Components);
        _ = target.Mutation.CheckCurrent(component.Before.Sections, ct);
        var call = target.Mutation.Apply(component.Change, ct);
        Assert.False(call.BindingConflict);
        Assert.Null(call.NativeError);
        Assert.True(ExchangeFixtureData(path, companion, 1) == 0, $"exchangedata errno {Marshal.GetLastPInvokeError()}");
        Assert.Equal(PermissionVerification.Unavailable, target.Mutation.Observe(component.Expected.Sections).Verification);
    }

    [LibraryImport("libc", EntryPoint = "exchangedata", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int ExchangeFixtureData(string first, string second, nuint options);

    [Fact]
    public async Task Darwin_parent_job_repairs_mode_zero_and_verifies_times_and_hidden_with_acl_preserved()
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!;
        RunAclTool("chmod", "+a#", "0", "everyone allow delete", path);
        RunAclTool("chflags", "nodump", path);
        File.SetUnixFileMode(path, 0);
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var modified = created.AddDays(3);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyParentWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        var original = Assert.IsType<DarwinAcl>(inspection.Snapshot.Access.Value?.Acl);
        var requested = new PermissionDraft(PermissionScope.Objects,
            [new ChangePermissionTimes(modified, created), new ChangePermissionFlags(0x8000, 0x8000), new ChangePermissionMode(0x180, 0xfff)]);
        using var review = DarwinPermissionService.Prepare(inspection, item, requested, false, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);

        var outcome = Assert.Single(job.PermissionResults);
        Assert.True(outcome.Verdict == PermissionVerdict.Verified, JobIssue.Summarize(job.Issues) ?? string.Join(" | ", outcome.Components.Select(result =>
            $"{result.Section} {result.Verdict} errno={result.NativeError} {result.Detail}")));
        Assert.Equal([PermissionSections.Mode, PermissionSections.Times, PermissionSections.Flags], outcome.Components.Select(result => result.Section));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
        Assert.Equal(created.UtcDateTime, File.GetCreationTimeUtc(path));
        Assert.Equal(modified.UtcDateTime, File.GetLastWriteTimeUtc(path));
        Assert.True(MacFinderHidden.Observe(path, ct));
        Assert.Contains("nodump", RunAclTool("stat", "-f", "%Sf", path));
        using var after = service.Inspect(subject, DarwinSections, ct);
        Assert.Equal(original.FileSecurity.ToArray(), Assert.IsType<DarwinAcl>(after.Snapshot.Access.Value?.Acl).FileSecurity.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Darwin_parent_admission_refuses_write_grants_to_other_accounts(bool extendedAccess)
    {
        RequireDarwinWriter();
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        if (extendedAccess) RunAclTool("chmod", "+a#", "0", "everyone allow add_file", Path.GetDirectoryName(item.FileSystemPath!)!);
        else File.SetUnixFileMode(Path.GetDirectoryName(item.FileSystemPath!)!, (UnixFileMode)0x1d0);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new DarwinPermissionService(qualifyParentWrites: true).Inspect(subject, DarwinSections, ct);
        var error = Assert.Throws<NotSupportedException>(() => DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]), false, ct));
        Assert.Contains("another account", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(item.FileSystemPath!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Darwin_parent_job_refuses_a_replacement_or_a_newly_shared_parent(bool replace)
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!, original = path + ".original";
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new DarwinPermissionService(qualifyParentWrites: true).Inspect(subject, DarwinSections, ct);
        using var review = DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]), false, ct).Review;
        if (replace)
        {
            File.Move(path, original);
            File.WriteAllText(path, "replacement");
            File.SetUnixFileMode(path, (UnixFileMode)0x180);
        }
        else File.SetUnixFileMode(Path.GetDirectoryName(path)!, (UnixFileMode)0x1d0);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Conflict, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
        if (replace) Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(original));
        Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
    }

    [Fact]
    public void Darwin_parent_watch_reports_unrelated_folder_activity_after_a_verified_call()
    {
        RequireDarwinWriter();
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new DarwinPermissionService(qualifyParentWrites: true).Inspect(subject, DarwinSections, ct);
        using var review = DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]), false, ct).Review;
        var target = Assert.Single(review.Targets);
        var component = Assert.Single(target.Components);
        Assert.Equal(component.Before, PermissionRevision.Read(target.Mutation.CheckCurrent(component.Before.Sections, ct), component.Before.Sections));
        Assert.Null(target.Mutation.Apply(component.Change, ct).NativeError);
        File.WriteAllText(Path.Join(item.Parent.Path, "unrelated"), "fixture");
        var readback = target.Mutation.Observe(component.Expected.Sections);
        Assert.True(readback.FolderChanged);
        Assert.Equal(component.Expected, PermissionRevision.Read(readback.Snapshot, component.Expected.Sections));
    }

    [Fact]
    public void Darwin_parent_readback_is_uncertain_when_the_original_name_is_replaced_after_dispatch()
    {
        RequireDarwinWriter();
        using var fixture = new Fixture();
        var (item, subject) = DarwinSubject(fixture);
        string path = item.FileSystemPath!, moved = path + ".moved";
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new DarwinPermissionService(qualifyParentWrites: true).Inspect(subject, DarwinSections, ct);
        using var review = DarwinPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]), false, ct).Review;
        var target = Assert.Single(review.Targets);
        var component = Assert.Single(target.Components);
        _ = target.Mutation.CheckCurrent(component.Before.Sections, ct);
        var call = target.Mutation.Apply(component.Change, ct);
        Assert.False(call.BindingConflict);
        Assert.Null(call.NativeError);
        File.Move(path, moved);
        File.WriteAllText(path, "replacement");
        File.SetUnixFileMode(path, (UnixFileMode)0x180);

        var observed = target.Mutation.Observe(component.Expected.Sections);
        Assert.Equal(PermissionVerification.Unavailable, observed.Verification);
        Assert.Contains("may have reached another object", observed.Detail);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(moved));
    }

    [Fact]
    public async Task Darwin_parent_can_repair_a_mode_zero_directory_and_requires_a_separate_review_when_it_becomes_shared()
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var item = new ItemRef(Location.FileSystem(fixture.Root), "directory", EntryKind.Directory);
        string path = Directory.CreateDirectory(item.FileSystemPath!, (UnixFileMode)0x1c0).FullName;
        File.SetUnixFileMode(path, 0);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new DarwinPermissionService(qualifyParentWrites: true);
        using var inspection = service.Inspect(subject, DarwinSections, ct);
        var mode = new ChangePermissionMode(0x1d0, 0xfff);
        var compound = new PermissionDraft(PermissionScope.Objects, [mode, new ChangePermissionFlags(0x8000, 0x8000)]);
        Assert.Contains("separately", Assert.Throws<NotSupportedException>(() =>
            DarwinPermissionService.Prepare(inspection, item, compound, false, ct)).Message);

        using var review = DarwinPermissionService.Prepare(inspection, item, compound with { Changes = [mode] }, false, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x1d0, File.GetUnixFileMode(path));
        using var shared = service.Inspect(subject, DarwinSections, ct);
        Assert.NotNull(DarwinPermissionService.ModeRefusal(shared.Snapshot));
    }

    private static void RequireDarwinWriter()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_ACL_ROOT") is not { } volume ||
            !Path.GetTempPath().StartsWith(Path.TrimEndingDirectorySeparator(volume) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            Assert.Skip("Requires an ordinary macOS worker with its temporary directory on the disposable test volume.");
    }

    private static Fixture DarwinJournalFixture() => new(nativeJournal: true, journalRoot: Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_JOURNAL_ROOT"));

    private static (ItemRef Item, PermissionSubject Subject) DarwinSubject(Fixture fixture)
    {
        string parent = Directory.CreateDirectory(Path.Join(fixture.Root, "private"), (UnixFileMode)0x1c0).FullName;
        var item = new ItemRef(Location.FileSystem(parent), "ordinary.txt", EntryKind.File);
        string path = item.FileSystemPath!;
        File.WriteAllText(path, "fixture");
        File.SetUnixFileMode(path, (UnixFileMode)0x180);
        return (item, new(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow));
    }

    [Fact]
    public async Task Ext4_permission_job_repairs_mode_zero_and_sets_time_on_the_same_pin()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var (item, subject) = Ext4Subject(fixture);
        string path = item.FileSystemPath!;
        RunAclTool("setfacl", "-m", "u:12345:r--,g::r--,m::---", path);
        File.SetUnixFileMode(path, 0);
        var atime = File.GetLastAccessTimeUtc(path);
        var modified = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero).AddTicks(4321);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService();
        using var inspection = service.Inspect(subject, PermissionSections.Ordinary | PermissionSections.Times, ct);
        var prepared = LinuxPermissionService.Prepare(inspection, item, new(PermissionScope.Objects,
            [new ChangePermissionTimes(modified, null), new ChangePermissionMode(0x1a0, 0xfff)]), false, ct);
        using var review = prepared.Review;
        var expected = Assert.IsType<PosixAcl>(prepared.Expected.Access.Value?.Acl);
        Assert.Contains(expected.Entries, row => row.Tag == PosixAclTag.Mask && row.Rights == 4);
        Assert.Contains(expected.Entries, row => row.Tag == PosixAclTag.OwningGroup && row.Rights == 4);
        Assert.Contains(expected.Entries, row => row.Tag == PosixAclTag.NamedUser && row.Principal?.Id == "12345");
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var outcome = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.Verified, outcome.Verdict);
        Assert.Equal(new[] { PermissionSections.Mode, PermissionSections.Times }, outcome.Components.Select(component => component.Section));
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(path));
        Assert.Equal(modified.UtcDateTime, File.GetLastWriteTimeUtc(path));
        Assert.Equal(atime, File.GetLastAccessTimeUtc(path));
        Assert.Contains("mask::r--", RunAclTool("getfacl", "-ncp", path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ext4_access_setter_verifies_native_extended_and_mode_only_acl_results(bool withMask)
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var (item, subject) = Ext4Subject(fixture);
        var rows = ImmutableArray.CreateBuilder<PosixAclEntry>();
        rows.Add(new(PosixAclTag.Owner, 6, null));
        if (withMask) rows.Add(new(PosixAclTag.NamedUser, 4, new Principal(PrincipalKind.UnixUser, "12345", subject.Authority.Id)));
        rows.Add(new(PosixAclTag.OwningGroup, 2, null));
        if (withMask) rows.Add(new(PosixAclTag.Mask, 4, null));
        rows.Add(new(PosixAclTag.Other, 0, null));
        CancellationToken ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService();
        using var inspection = service.Inspect(subject, PermissionSections.Ordinary, ct);
        var prepared = LinuxPermissionService.Prepare(inspection, item, new(PermissionScope.Objects,
            [new ChangePosixAcl(false, rows.ToImmutable())]), false, ct);
        using var review = prepared.Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        string path = item.FileSystemPath!;
        Assert.Equal((UnixFileMode)(withMask ? 0x1a0 : 0x190), File.GetUnixFileMode(path));
        string nativeAcl = RunAclTool("getfacl", "-ncp", path);
        Assert.Contains("group::-w-", nativeAcl);
        Assert.Equal(withMask, nativeAcl.Contains("user:12345:r--", StringComparison.Ordinal));
        using var observed = service.Inspect(subject, PermissionSections.Access, ct);
        Assert.Equal(withMask ? PermissionReadStatus.Present : PermissionReadStatus.Absent, observed.Snapshot.Access.Status);
    }

    // A real native refusal: ext4 keeps an access ACL in one attribute block, so one more named user than fits makes
    // setxattr fail. The refusal stops the compound change before its modification time, the job reports the unchanged
    // object with the kernel's error, and the complete history needs no review.
    [Fact]
    public async Task Ext4_refuses_an_access_ACL_beyond_its_attribute_space_and_the_job_reports_the_unchanged_object()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var (item, subject) = Ext4Subject(fixture);
        string path = item.FileSystemPath!;
        int accepted = 0, refused = PermissionSnapshots.MaxAclEntries - 8;
        while (refused - accepted > 1)
        {
            int count = (accepted + refused) / 2;
            if (TrySetNamedUsers(path, count)) accepted = count; else refused = count;
        }
        Assert.InRange(accepted, 8, refused - 1);
        Assert.True(TrySetNamedUsers(path, accepted));
        string before = RunAclTool("getfacl", "-ncp", path);
        var modifiedBefore = File.GetLastWriteTimeUtc(path);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary | PermissionSections.Times, ct);
        var rows = LinuxPermissionAcl.AccessRows(inspection.Snapshot).ToList();
        Assert.Equal(accepted, rows.Count(row => row.Tag == PosixAclTag.NamedUser));
        // The next named user sorts after the existing ones, just before the owning group.
        rows.Insert(rows.FindIndex(row => row.Tag == PosixAclTag.OwningGroup),
            new(PosixAclTag.NamedUser, 4, new Principal(PrincipalKind.UnixUser, (20000 + accepted).ToString(System.Globalization.CultureInfo.InvariantCulture), subject.Authority.Id)));
        using var review = LinuxPermissionService.Prepare(inspection, item, new(PermissionScope.Objects,
            [new ChangePermissionTimes(new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero), null), new ChangePosixAcl(false, [.. rows])]),
            false, ct).Review;
        Assert.Equal([PermissionSections.Access, PermissionSections.Times],
            Assert.Single(review.Targets).Components.Select(planned => planned.Change.Section));
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        var component = Assert.Single(result.Components); // the time component is not attempted after the refusal
        Assert.True(result.Verdict == PermissionVerdict.FailedBeforeChange, $"{result.Verdict}: {component.Detail}");
        Assert.Contains(component.NativeError, new int?[] { 7, 28, 34 }); // E2BIG, ENOSPC or ERANGE, by kernel and features
        Assert.Equal(PermissionVerification.SameObject, component.Verification);
        Assert.Equal(before, RunAclTool("getfacl", "-ncp", path));
        Assert.Equal(modifiedBefore, File.GetLastWriteTimeUtc(path));

        var records = JobJournal.ReadAll(fixture.JournalPath!);
        Assert.Single(records, record => record.Type == "permission-intent");
        var outcome = Assert.Single(records, record => record.Type == "permission-outcome");
        Assert.Equal(nameof(PermissionVerdict.FailedBeforeChange), outcome.Get("verdict"));
        Assert.Equal(component.NativeError, outcome.Node["error"]?.GetValue<int>());
        string history = Path.GetDirectoryName(fixture.JournalPath!)!;
        using var directory = UnixAnalysisPrivateDirectory.OpenPermissionJournalDirectory(Path.GetDirectoryName(history)!);
        Assert.Empty(PermissionJournalRecovery.Scan(directory, history, ct));
        Assert.True(File.Exists(fixture.JournalPath));
    }

    // A device that fails before the filesystem commits a change: ext4 accepts the chmod in memory and reads it back, but
    // the change never reaches the disk. A root harness switches the volume's device-mapper table to an error target once
    // the call's intent is durable; the job must not report the change as verified.
    [Fact]
    public async Task Ext4_change_the_device_cannot_commit_is_not_reported_verified()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_DEVICE_LOSS_ROOT");
        string? control = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_DEVICE_LOSS_CONTROL");
        if (!OperatingSystem.IsLinux() || root is null || control is null || Environment.IsPrivilegedProcess)
            Assert.Skip("Requires the device-mapper failure harness.");
        using var fixture = new Fixture(nativeJournal: true);
        var item = new ItemRef(Location.FileSystem(root), "document", EntryKind.File);
        string path = item.FileSystemPath!;
        File.WriteAllText(path, "fixture");
        File.SetUnixFileMode(path, (UnixFileMode)0x1a0);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
        using var review = LinuxPermissionService.Prepare(inspection, item,
            new(PermissionScope.Objects, [new ChangePermissionMode(0x180, 0xfff)]), false, ct).Review;
        RunAclTool("sync");
        int checks = 0;
        fixture.Verify = () =>
        {
            if (++checks != 4) return; // after the intent flush, before the recheck and dispatch
            File.WriteAllText(Path.Join(control, "ready"), "intent");
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (!File.Exists(Path.Join(control, "failed")))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The harness did not fail the device.");
                Thread.Sleep(100);
            }
        };
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        var component = Assert.Single(result.Components);
        File.WriteAllText(Path.Join(control, "result.txt"), $"{result.Verdict}|{component.Verdict}|{component.NativeCall}|{component.Detail}");
        Assert.NotEqual(PermissionVerdict.Verified, result.Verdict);
        if (component.NativeCall is null) Assert.Equal(PermissionVerdict.FailedBeforeChange, result.Verdict);
        else
        {
            Assert.Equal(PermissionVerdict.Uncertain, result.Verdict);
            Assert.Contains("pending changes to the device", component.Detail);
        }
    }

    // Mixed selection: one reviewed mode delta changes only its touched bit on each object and keeps every other bit. An
    // object that already matches is reported so without an intent or a native call.
    [Fact]
    public async Task Ext4_mixed_selection_changes_only_the_touched_bit_of_each_object()
    {
        RequireExt4Writer();
        await AssertMixedSelection(new LinuxPermissionService(), PermissionSections.Ordinary,
            (inspection, item, draft, ct) => LinuxPermissionService.PrepareTarget(inspection, item, draft, false, ct).Target);
    }

    [Fact]
    public async Task Darwin_mixed_selection_changes_only_the_touched_bit_of_each_object()
    {
        RequireDarwinWriter();
        await AssertMixedSelection(new DarwinPermissionService(qualifyParentWrites: true), DarwinSections,
            (inspection, item, draft, ct) => DarwinPermissionService.PrepareTarget(inspection, item, draft, false, ct).Target);
    }

    private static async Task AssertMixedSelection(IPermissionService service, PermissionSections sections,
        Func<PermissionInspection, ItemRef, PermissionDraft, CancellationToken, ReviewedPermissionTarget> prepare)
    {
        using var fixture = new Fixture(nativeJournal: true);
        CancellationToken ct = TestContext.Current.CancellationToken;
        var draft = new PermissionDraft(PermissionScope.Objects, [new ChangePermissionMode(0x40, 0x40)]); // owner execute on
        (string Name, int Before, int After)[] objects = [("group-read", 0x1a0, 0x1e0), ("other-read", 0x184, 0x1c4), ("already", 0x1e4, 0x1e4)];
        var inspections = new List<PermissionInspection>();
        var targets = ImmutableArray.CreateBuilder<ReviewedPermissionTarget>();
        try
        {
            foreach (var (name, before, _) in objects)
            {
                var item = new ItemRef(Location.FileSystem(fixture.Root), name, EntryKind.File);
                string path = item.FileSystemPath!;
                File.WriteAllText(path, "fixture");
                File.SetUnixFileMode(path, (UnixFileMode)before);
                var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
                    PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
                var inspection = service.Inspect(subject, sections, ct);
                inspections.Add(inspection);
                targets.Add(prepare(inspection, item, draft, ct));
            }
            using var review = new ReviewedPermissionOperation(PermissionScope.Objects, targets.ToImmutable());
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            Assert.Equal([PermissionVerdict.Verified, PermissionVerdict.Verified, PermissionVerdict.AlreadyMatching],
                job.PermissionResults.OrderBy(result => result.SourceIndex).Select(result => result.Verdict));
            foreach (var (name, _, after) in objects)
                Assert.Equal((UnixFileMode)after, File.GetUnixFileMode(Path.Join(fixture.Root, name)));
            Assert.Equal(2, JobJournal.ReadAll(fixture.JournalPath!).Count(record => record.Type == "permission-intent"));
        }
        finally { foreach (var inspection in inspections) inspection.Dispose(); }
    }

    // Replaces the access ACL with the owner, the given number of named users, the owning group, the mask and others.
    private static bool TrySetNamedUsers(string path, int count)
    {
        var spec = new StringBuilder("user::rw-\ngroup::---\nmask::r--\nother::---\n");
        for (int i = 0; i < count; i++) spec.Append("user:").Append(20000 + i).Append(":r--\n");
        string file = path + ".acl";
        File.WriteAllText(file, spec.ToString());
        try
        {
            var start = new ProcessStartInfo("setfacl") { RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("--set-file=" + file);
            start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(10_000));
            return process.ExitCode == 0;
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Ext4_applies_nothing_when_the_reviewed_name_is_replaced()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var (item, subject) = Ext4Subject(fixture);
        string path = item.FileSystemPath!, moved = path + ".moved";
        File.SetUnixFileMode(path, (UnixFileMode)0x180);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var inspection = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
        using var review = LinuxPermissionService.Prepare(inspection, item, new(PermissionScope.Objects,
            [new ChangePermissionMode(0x1a0, 0xfff)]), false, ct).Review;
        File.Move(path, moved);
        File.WriteAllText(path, "replacement");
        File.SetUnixFileMode(path, (UnixFileMode)0x180);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Conflict, Assert.Single(job.PermissionResults).Verdict);
        foreach (string candidate in new[] { path, moved }) Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(candidate));
        Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
    }

    [Fact]
    public async Task Ext4_directory_mode_requires_accepting_set_id_clear_and_preserves_its_default_acl()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var item = new ItemRef(Location.FileSystem(fixture.Root), "directory", EntryKind.Directory);
        string path = item.FileSystemPath!;
        Directory.CreateDirectory(path);
        RunAclTool("setfacl", "-m", "d:u:12345:r--", path);
        File.SetUnixFileMode(path, (UnixFileMode)0x5c0);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService();
        CancellationToken ct = TestContext.Current.CancellationToken;
        var draft = new PermissionDraft(PermissionScope.Objects, [new ChangePermissionMode(0x1c0, 0xfff)]);
        using var unaccepted = service.Inspect(subject, PermissionSections.Ordinary, ct);
        Assert.Equal((ushort)0x5c0, unaccepted.Snapshot.Mode.Value);
        Assert.Throws<InvalidOperationException>(() => LinuxPermissionService.Prepare(unaccepted, item, draft, false, ct));

        using var accepted = service.Inspect(subject, PermissionSections.Ordinary, ct);
        var original = Assert.IsType<PosixAcl>(accepted.Snapshot.Defaults.Value?.Acl);
        using var review = LinuxPermissionService.Prepare(accepted, item, draft, true, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(path));
        using var observed = service.Inspect(subject, PermissionSections.Defaults, ct);
        Assert.Equal(original.Bytes.ToArray(), Assert.IsType<PosixAcl>(observed.Snapshot.Defaults.Value?.Acl).Bytes.ToArray());
    }

    [Fact]
    public async Task Ext4_directory_default_job_sets_and_removes_inheritance_for_future_children_only()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var item = new ItemRef(Location.FileSystem(fixture.Root), "directory", EntryKind.Directory);
        string path = item.FileSystemPath!;
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, (UnixFileMode)0x5c0);
        string existing = Path.Combine(path, "existing");
        File.WriteAllText(existing, "before");
        string existingAcl = RunAclTool("getfacl", "-ncp", existing);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var named = new Principal(PrincipalKind.UnixUser, "12345", subject.Authority.Id);
        var set = new PermissionDraft(PermissionScope.DefaultsForNewChildren,
            [new ChangePosixAcl(true, [new(PosixAclTag.Owner, 7, null), new(PosixAclTag.NamedUser, 4, named),
                new(PosixAclTag.OwningGroup, 0, null), new(PosixAclTag.Mask, 4, null), new(PosixAclTag.Other, 0, null)])]);
        CancellationToken ct = TestContext.Current.CancellationToken;
        using var disabled = new LinuxPermissionService().Inspect(subject, PermissionSections.Ordinary, ct);
        Assert.Throws<NotSupportedException>(() => LinuxPermissionService.Prepare(disabled, item, set, false, ct));

        var service = new LinuxPermissionService(qualifyDefaultWrites: true);
        string firstChild = Path.Combine(path, "during");
        await Apply(set);
        Assert.Contains("default:user:12345:r--", RunAclTool("getfacl", "-ncp", path));
        File.WriteAllText(firstChild, "during");
        Assert.Contains("user:12345:r--", RunAclTool("getfacl", "-ncp", firstChild));

        await Apply(new(PermissionScope.DefaultsForNewChildren, [new ChangePosixAcl(true, [])]));
        Assert.DoesNotContain("default:", RunAclTool("getfacl", "-ncp", path));
        string lastChild = Path.Combine(path, "after");
        File.WriteAllText(lastChild, "after");
        Assert.DoesNotContain("user:12345:", RunAclTool("getfacl", "-ncp", lastChild));
        Assert.Equal(existingAcl, RunAclTool("getfacl", "-ncp", existing));
        Assert.Contains("user:12345:r--", RunAclTool("getfacl", "-ncp", firstChild));

        async Task Apply(PermissionDraft draft)
        {
            using var inspection = service.Inspect(subject, PermissionSections.Ordinary, ct);
            using var review = LinuxPermissionService.Prepare(inspection, item, draft, false, ct).Review;
            var finished = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Manager.JobFinished += Complete;
            Job job;
            try
            {
                job = fixture.Manager.SubmitPermissions(review);
                Assert.Same(job, await finished.Task.WaitAsync(TimeSpan.FromSeconds(15), ct));
            }
            finally { fixture.Manager.JobFinished -= Complete; }
            var result = Assert.Single(job.PermissionResults);
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.Equal(PermissionSections.Defaults, Assert.Single(result.Components).Section);
            Assert.Equal((UnixFileMode)0x5c0, File.GetUnixFileMode(path));

            void Complete(Job completed) => finished.TrySetResult(completed);
        }
    }

    private static void RequireExt4Writer()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64 ||
            Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") != "1")
            Assert.Skip("Requires an explicitly enabled Linux x64 ext4 fixture.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ext4_reviewed_mode_tree_uses_captured_objects_and_applies_directories_last(bool exactExecute)
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var root = TreeSubject(Path.Join(fixture.Root, "tree"), EntryKind.Directory);
        var folder = TreeSubject(Path.Join(root.Subject.RawLocator, "folder"), EntryKind.Directory);
        var document = TreeSubject(Path.Join(folder.Subject.RawLocator, "document"), EntryKind.File);
        string executable = Path.Join(root.Subject.RawLocator, "executable");
        string outside = Path.Join(fixture.Root, "outside");
        string opaque = Path.Join(root.Subject.RawLocator, "unlisted");
        var ownerFull = (UnixFileMode)0x1c0;
        var ownerData = (UnixFileMode)0x180;
        Directory.CreateDirectory(root.Subject.RawLocator, ownerFull);
        Directory.CreateDirectory(folder.Subject.RawLocator, ownerFull);
        Directory.CreateDirectory(opaque, ownerFull);
        foreach (string path in new[] { document.Subject.RawLocator, executable, outside, Path.Join(opaque, "unknown") })
        {
            File.WriteAllText(path, "fixture");
            File.SetUnixFileMode(path, path == executable ? ownerFull : ownerData);
        }
        File.SetUnixFileMode(opaque, (UnixFileMode)0xc0);
        string alias = Path.Join(folder.Subject.RawLocator, "z-alias");
        RunAclTool("ln", document.Subject.RawLocator, alias);
        File.CreateSymbolicLink(Path.Join(root.Subject.RawLocator, "link"), outside);
        string late = Path.Join(folder.Subject.RawLocator, "late");
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var prepared = new LinuxPermissionService().PrepareModeTree([document, folder, root], new(0x8, 0x88), exactExecute, false, ct);
            using var review = prepared.Operation;
            var manifest = Assert.IsType<PermissionTreeManifest>(review.Tree);
            Assert.Equal(1, manifest.RootCount);
            Assert.Equal(1, manifest.AliasCount);
            Assert.Equal(1, manifest.ExcludedEntries);
            Assert.Equal(1, manifest.InaccessibleBranches);
            Assert.Equal(document.Subject.RawLocator, prepared.Rows[0].Subject.RawLocator);
            Assert.Equal(root.Subject.RawLocator, prepared.Rows[^1].Subject.RawLocator);
            Assert.Equal((ushort)(exactExecute ? 0x108 : 0x100), prepared.Rows[0].After);
            File.WriteAllText(late, "created after review");
            File.SetUnixFileMode(late, ownerData);

            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            Assert.Equal(PermissionScope.ReviewedTree, job.PermissionReviewScope);
            Assert.Equal(manifest.Summary, job.ReviewedTreeSummary);
            Assert.Equal(5, job.PermissionResults.Count);
            Assert.All(job.PermissionResults, result => Assert.Equal(PermissionVerdict.Verified, result.Verdict));
            Assert.Equal((UnixFileMode)(exactExecute ? 0x108 : 0x100), File.GetUnixFileMode(alias));
            Assert.Equal((UnixFileMode)0x148, File.GetUnixFileMode(executable));
            Assert.Equal((UnixFileMode)0x148, File.GetUnixFileMode(folder.Subject.RawLocator));
            Assert.Equal((UnixFileMode)0x148, File.GetUnixFileMode(root.Subject.RawLocator));
            Assert.Equal(ownerData, File.GetUnixFileMode(late));
            Assert.Equal(ownerData, File.GetUnixFileMode(outside));
            Assert.Equal("end", JobJournal.ReadAll(fixture.JournalPath!).Last().Type);
            string[] records = File.ReadAllLines(fixture.JournalPath!);
            using var interrupted = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', records[..^1]) + "\n"));
            var receipt = Assert.IsType<InterruptedJob>(PermissionJournalRecovery.Read(interrupted, fixture.JournalPath!));
            var history = Assert.IsType<InterruptedPermissionReview>(receipt.PermissionReview);
            Assert.False(history.Damaged);
            Assert.Equal(PermissionScope.ReviewedTree, history.Scope);
            Assert.Equal(manifest.Summary, history.Tree);
        }
        finally
        {
            File.SetUnixFileMode(root.Subject.RawLocator, ownerFull);
            File.SetUnixFileMode(folder.Subject.RawLocator, ownerFull);
            File.SetUnixFileMode(opaque, ownerFull);
        }
        Assert.Equal(ownerData, File.GetUnixFileMode(Path.Join(opaque, "unknown")));
    }

    [Fact]
    public async Task Ext4_reviewed_mode_tree_refuses_replaced_ancestry_even_with_the_same_leaf_inode()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var root = TreeSubject(Path.Join(fixture.Root, "tree"), EntryKind.Directory);
        string folder = Path.Join(root.Subject.RawLocator, "folder");
        string moved = Path.Join(fixture.Root, "moved");
        string file = Path.Join(folder, "file");
        Directory.CreateDirectory(root.Subject.RawLocator, (UnixFileMode)0x1c0);
        Directory.CreateDirectory(folder, (UnixFileMode)0x1c0);
        File.WriteAllText(file, "original");
        File.SetUnixFileMode(file, (UnixFileMode)0x180);
        // Keep the leaf identity and link count identical after replacing the parent.
        var ct = TestContext.Current.CancellationToken;
        using var review = new LinuxPermissionService().PrepareModeTree([root], new(0x4, 0x4), true, false, ct).Operation;
        Directory.Move(folder, moved);
        Directory.CreateDirectory(folder, (UnixFileMode)0x1c0);
        File.Move(Path.Join(moved, "file"), file);

        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Conflict, job.PermissionResults[0].Verdict);
        Assert.Equal(PermissionVerdict.Conflict, job.PermissionResults[1].Verdict);
        Assert.Equal(PermissionVerdict.Verified, job.PermissionResults[2].Verdict);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(file));
        Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(folder));
        Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(moved));
        Assert.Single(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
    }

    [Fact]
    public void Ext4_mode_tree_counts_the_existing_inspection_leases_against_the_review_limit()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var root = TreeSubject(Path.Join(fixture.Root, "tree"), EntryKind.Directory);
        Directory.CreateDirectory(root.Subject.RawLocator, (UnixFileMode)0x1c0);
        string child = Path.Join(root.Subject.RawLocator, "child");
        File.WriteAllText(child, "fixture");
        File.SetUnixFileMode(child, (UnixFileMode)0x180);
        var service = new LinuxPermissionService();
        var ct = TestContext.Current.CancellationToken;
        var delta = new ChangePermissionMode(4, 4);
        var error = Assert.Throws<NotSupportedException>(() => service.PrepareModeTree([root], delta, false, false, ct,
            retainedInspections: LinuxPermissionService.ModeTreeTargetLimit - 1));
        Assert.Contains("at most 1 captured", error.Message);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(child));
        using var review = service.PrepareModeTree([root], delta, false, false, ct,
            retainedInspections: LinuxPermissionService.ModeTreeTargetLimit - 2).Operation;
        Assert.Equal(2, review.Targets.Length);
    }

    [Fact]
    public async Task Ext4_reviewed_mode_tree_excludes_a_real_nested_bind_mount()
    {
        RequireExt4Writer();
        string treePath = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSION_BIND_TREE") ?? string.Empty;
        if (treePath.Length == 0)
            Assert.Skip("Requires a task-owned ext4 bind mount in a private mount namespace.");
        using var fixture = new Fixture(nativeJournal: true);
        var root = TreeSubject(treePath, EntryKind.Directory);
        var service = new LinuxPermissionService();
        var ct = TestContext.Current.CancellationToken;
        var nested = TreeSubject(Path.Join(root.Subject.RawLocator, "nested"), EntryKind.Directory);
        using var rootInspection = service.Inspect(root.Subject, PermissionSections.None, ct);
        using var nestedInspection = service.Inspect(nested.Subject, PermissionSections.None, ct);
        var rootIdentity = Assert.IsType<PermissionBinding>(rootInspection.Snapshot.Binding).Identity;
        var nestedIdentity = Assert.IsType<PermissionBinding>(nestedInspection.Snapshot.Binding).Identity;
        Assert.Equal(rootIdentity.Object.VolumeSerial, nestedIdentity.Object.VolumeSerial);
        Assert.NotEqual(rootIdentity.MountInstance, nestedIdentity.MountInstance);
        string sibling = Path.Join(treePath, "sibling");
        string witness = Path.Join(treePath, "nested", "witness");
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(sibling));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(witness));

        using var review = service.PrepareModeTree([root], new(4, 4), true, false, ct).Operation;
        Assert.Equal(new PermissionTreeSummary(1, 0, 1, 0), review.Tree!.Summary);
        Assert.Equal(new[] { sibling, treePath }, review.Targets.Select(target => target.Subject.RawLocator));
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.All(job.PermissionResults, result => Assert.Equal(PermissionVerdict.Verified, result.Verdict));
        Assert.Equal((UnixFileMode)0x184, File.GetUnixFileMode(sibling));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(witness));
    }

    private static (ItemRef Item, PermissionSubject Subject) TreeSubject(string path, EntryKind kind)
    {
        var item = ItemRef.ForFileSystemPath(path, kind);
        return (item, new(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            kind == EntryKind.Directory ? PermissionObjectKind.Directory : PermissionObjectKind.File, PermissionLinkPolicy.NoFollow));
    }

    [Theory]
    [InlineData("xfs")]
    [InlineData("btrfs")]
    public async Task Linux_filesystem_variant_verifies_mode_zero_acl_mode_time_and_inheritance(string fileSystem)
    {
        string volume = LinuxTestVolume(fileSystem);
        using var ordinary = new TempDir(volume);
        using var modeJob = new Fixture(nativeJournal: true);
        using var aclJob = new Fixture(nativeJournal: true);
        using var defaultsJob = new Fixture(nativeJournal: true);
        string path = Path.Join(ordinary.Path, "mode-zero");
        File.WriteAllText(path, "fixture");
        RunAclTool("setfacl", "-n", "-m", "u:12345:r--,g::r--,m::---", path);
        File.SetUnixFileMode(path, 0);

        var file = TreeSubject(path, EntryKind.File);
        var directory = TreeSubject(ordinary.Path, EntryKind.Directory);
        var ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService(qualifyDefaultWrites: true, qualifyAccessChecks: true, qualifyAdditionalFileSystems: true);
        using var unqualified = new LinuxPermissionService().Inspect(file.Subject, PermissionSections.Ordinary, ct);
        Assert.Null(unqualified.Snapshot.Binding);
        using var original = service.Inspect(file.Subject, PermissionSections.Ordinary | PermissionSections.Times, ct);
        Assert.Equal(fileSystem, original.Snapshot.Binding?.FileSystem);
        Assert.Equal((ushort)0, original.Snapshot.Mode.Value);
        Assert.Contains(Assert.IsType<PosixAcl>(original.Snapshot.Access.Value?.Acl).Entries, row => row.Principal?.Id == "12345");
        var denied = service.CheckEffectiveAccess(file.Subject, ct);
        Assert.True(denied.NoSetId);
        Assert.All(denied.Checks, check => Assert.Equal(PermissionReadStatus.Denied, check.Status));
        var time = new DateTimeOffset(2024, 6, 1, 2, 3, 4, TimeSpan.Zero);
        using var mode = LinuxPermissionService.Prepare(original, file.Item, new(PermissionScope.Objects,
            [new ChangePermissionMode(0x1a0, 0xfff), new ChangePermissionTimes(time, null)]), false, ct).Review;
        var combined = modeJob.Manager.SubmitPermissions(mode);
        await modeJob.Finish(combined);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(combined.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(path));
        Assert.Equal(time.UtcDateTime, File.GetLastWriteTimeUtc(path));
        Assert.Contains("user:12345:r--", RunAclTool("getfacl", "-ncp", path));
        Assert.Equal(new[] { PermissionReadStatus.Present, PermissionReadStatus.Present, PermissionReadStatus.Denied },
            service.CheckEffectiveAccess(file.Subject, ct).Checks.Select(check => check.Status));

        using var updated = service.Inspect(file.Subject, PermissionSections.Ordinary, ct);
        var replacement = LinuxPermissionAcl.AccessRows(updated.Snapshot).Select(row => row.Tag == PosixAclTag.NamedUser
            ? row with { Principal = new Principal(PrincipalKind.UnixUser, "23456", file.Subject.Authority.Id) } : row).ToImmutableArray();
        using var access = LinuxPermissionService.Prepare(updated, file.Item, new(PermissionScope.Objects,
            [new ChangePosixAcl(false, replacement)]), false, ct).Review;
        var accessChange = aclJob.Manager.SubmitPermissions(access);
        await aclJob.Finish(accessChange);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(accessChange.PermissionResults).Verdict);
        string existingAccess = RunAclTool("getfacl", "-ncp", path);
        Assert.DoesNotContain("user:12345:", existingAccess);
        Assert.Contains("user:23456:r--", existingAccess);

        using var parent = service.Inspect(directory.Subject, PermissionSections.Ordinary, ct);
        Assert.True(service.CanEditDefaults(parent.Snapshot));
        using var defaults = LinuxPermissionService.Prepare(parent, directory.Item, new(PermissionScope.DefaultsForNewChildren,
            [new ChangePosixAcl(true, replacement)]), false, ct).Review;
        var defaultsChange = defaultsJob.Manager.SubmitPermissions(defaults);
        await defaultsJob.Finish(defaultsChange);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(defaultsChange.PermissionResults).Verdict);
        Assert.Contains("default:user:23456:r--", RunAclTool("getfacl", "-ncp", ordinary.Path));
        Assert.Equal(existingAccess, RunAclTool("getfacl", "-ncp", path));
        string child = Path.Join(ordinary.Path, "later");
        File.WriteAllText(child, "fixture");
        Assert.Contains("user:23456:r--", RunAclTool("getfacl", "-ncp", child));
    }

    [Fact]
    public void Linux_btrfs_tree_excludes_an_implicit_subvolume_boundary()
    {
        string volume = LinuxTestVolume("btrfs");
        string subvolume = Path.Join(volume, "subvol");
        Assert.True(Directory.Exists(subvolume));
        var service = new LinuxPermissionService(qualifyAdditionalFileSystems: true);
        var ct = TestContext.Current.CancellationToken;
        var root = TreeSubject(volume, EntryKind.Directory);
        using var excluded = service.Inspect(TreeSubject(subvolume, EntryKind.Directory).Subject, PermissionSections.Ordinary, ct);
        Assert.Null(excluded.Snapshot.Binding);
        using var review = service.PrepareModeTree([root], new(4, 4), false, false, ct).Operation;
        Assert.Equal(1, review.Tree!.ExcludedEntries);
        Assert.DoesNotContain(review.Targets, target => target.Subject.RawLocator.StartsWith(subvolume, StringComparison.Ordinal));
        Assert.Contains(review.Targets, target => target.Subject.RawLocator == Path.Join(volume, "file"));
    }

    [Fact]
    public async Task Linux_btrfs_noacl_reports_disabled_acls_and_applies_bound_mode()
    {
        string volume = LinuxTestVolume("btrfs");
        using var fixture = new Fixture(nativeJournal: true);
        string path = Path.Join(volume, "file");
        var file = TreeSubject(path, EntryKind.File);
        var ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService(qualifyDefaultWrites: true, qualifyAdditionalFileSystems: true);
        using var inspection = service.Inspect(file.Subject, PermissionSections.Ordinary, ct);
        Assert.Equal(PermissionModel.UnixMode, inspection.Snapshot.Model);
        Assert.Equal(PermissionReadStatus.Unsupported, inspection.Snapshot.Access.Status);
        Assert.Contains("noacl", inspection.Snapshot.Access.Reason);
        Assert.Null(inspection.Snapshot.Access.Value);
        Assert.False(service.CanEditAccessAcl(inspection.Snapshot));
        Assert.Throws<NotSupportedException>(() => LinuxPermissionService.PreviewAccessChange(inspection.Snapshot,
            new ChangePosixAcl(false, LinuxPermissionAcl.AccessRows(inspection.Snapshot))));
        using var directory = service.Inspect(TreeSubject(volume, EntryKind.Directory).Subject, PermissionSections.Ordinary, ct);
        Assert.Equal(PermissionReadStatus.Unsupported, directory.Snapshot.Defaults.Status);
        Assert.False(service.CanEditDefaults(directory.Snapshot));

        using var review = LinuxPermissionService.Prepare(inspection, file.Item, new(PermissionScope.Objects,
            [new ChangePermissionMode(0x180, 0xfff)]), false, ct).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path));
    }

    [Fact]
    public async Task Linux_btrfs_acl_policy_change_refuses_the_reviewed_mode_before_dispatch()
    {
        string volume = LinuxTestVolume("btrfs");
        string control = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSION_REMOUNT_CONTROL") ?? string.Empty;
        if (control.Length == 0) Assert.Skip("Requires the private mount namespace remount coordinator.");
        using var fixture = new Fixture(nativeJournal: true);
        string path = Path.Join(volume, "file");
        var file = TreeSubject(path, EntryKind.File);
        var mode = File.GetUnixFileMode(path);
        var ct = TestContext.Current.CancellationToken;
        var service = new LinuxPermissionService(qualifyAdditionalFileSystems: true);
        using var original = service.Inspect(file.Subject, PermissionSections.Ordinary, ct);
        Assert.Equal(PermissionModel.UnixMode, original.Snapshot.Model);
        using var review = LinuxPermissionService.Prepare(original, file.Item, new(PermissionScope.Objects,
            [new ChangePermissionMode(4, 4)]), false, ct).Review;
        File.WriteAllText(Path.Join(control, "ready"), "reviewed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!File.Exists(Path.Join(control, "remounted"))) await Task.Delay(25, timeout.Token);

        using var current = service.Inspect(file.Subject, PermissionSections.Ordinary, ct);
        Assert.Equal(PermissionModel.PosixAcl, current.Snapshot.Model);
        Assert.Equal(original.Snapshot.Binding!.Identity, current.Snapshot.Binding!.Identity);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Conflict, Assert.Single(job.PermissionResults).Verdict);
        Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
        Assert.Equal(mode, File.GetUnixFileMode(path));
    }

    private static string LinuxTestVolume(string fileSystem)
    {
        string volumes = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSION_VOLUMES") ?? string.Empty;
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess || !Path.IsPathFullyQualified(volumes))
            Assert.Skip("Requires the task-owned Linux filesystem images in a private mount namespace.");
        return Path.Join(volumes, fileSystem, "fixture");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ext4_reset_copies_the_captured_default_to_descendant_access_and_preserves_defaults(bool exactExecute)
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var root = TreeSubject(Path.Join(fixture.Root, "reset-source"), EntryKind.Directory);
        string tree = root.Subject.RawLocator;
        string nested = Path.Join(tree, "nested");
        Directory.CreateDirectory(tree, (UnixFileMode)0x1c0);
        Directory.CreateDirectory(nested, (UnixFileMode)0x1c0);
        string document = Path.Join(nested, "document"), executable = Path.Join(nested, "executable");
        File.WriteAllText(document, "owned fixture");
        File.WriteAllText(executable, "owned fixture");
        File.SetUnixFileMode(document, (UnixFileMode)0x180);
        File.SetUnixFileMode(executable, (UnixFileMode)0x1c0);
        RunAclTool("setfacl", "-n", "-m", "u:23456:r--,m::r--", document);
        RunAclTool("setfacl", "-n", "-m", "d:u::rwx,d:g::---,d:o::---", nested);
        RunAclTool("setfacl", "-n", "-m", "u::rwx,u:12345:r-x,g::r-x,m::r-x,o::---,d:u::rwx,d:u:12345:r-x,d:g::r-x,d:m::r-x,d:o::---", tree);
        string beforeRoot = RunAclTool("getfacl", "-ncp", tree);
        string beforeNestedDefaults = RunAclTool("getfacl", "-ndcp", nested);
        var ct = TestContext.Current.CancellationToken;
        var reviewed = new LinuxPermissionService().PrepareAclResetTree(root.Item, root.Subject, exactExecute, false, ct);
        using var operation = reviewed.Operation;
        Assert.Equal(PermissionScope.ResetTree, operation.Scope);
        Assert.Equal(tree, operation.TreeSource!.Subject.RawLocator);
        Assert.Equal(new[] { document, executable, nested }, operation.Targets.Select(target => target.Subject.RawLocator));
        Assert.Contains(reviewed.Rows[0].ResetBefore, row => row.Principal?.Id == "23456");
        Assert.DoesNotContain(reviewed.Rows[0].ResetAfter, row => row.Principal?.Id == "23456");
        Assert.Contains(reviewed.Rows[0].ResetAfter, row => row.Tag == PosixAclTag.Mask && row.Rights == (exactExecute ? 5 : 4));
        string late = Path.Join(nested, "late");
        File.WriteAllText(late, "late fixture");
        File.SetUnixFileMode(late, (UnixFileMode)0x180);

        var job = fixture.Manager.SubmitPermissions(operation);
        await fixture.Finish(job);
        Assert.Equal(PermissionScope.ResetTree, job.PermissionReviewScope);
        Assert.Equal(3, job.PermissionResults.Count);
        Assert.All(job.PermissionResults, result => Assert.Equal(PermissionVerdict.Verified, result.Verdict));
        Assert.Equal(beforeRoot, RunAclTool("getfacl", "-ncp", tree));
        Assert.Equal(beforeNestedDefaults, RunAclTool("getfacl", "-ndcp", nested));
        Assert.Equal((UnixFileMode)(exactExecute ? 0x1e8 : 0x1a0), File.GetUnixFileMode(document));
        Assert.Equal((UnixFileMode)0x1e8, File.GetUnixFileMode(executable));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(late));
        var docAccess = RunAclTool("getfacl", "-ncp", document);
        Assert.Contains("user:12345:r-x", docAccess);
        Assert.DoesNotContain("user:23456:", docAccess);
        if (!exactExecute) Assert.Contains("#effective:r--", docAccess);
        string[] records = File.ReadAllLines(fixture.JournalPath!);
        using var interrupted = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', records[..^1]) + "\n"));
        var retained = Assert.IsType<InterruptedJob>(PermissionJournalRecovery.Read(interrupted, fixture.JournalPath!));
        Assert.Equal(PermissionScope.ResetTree, retained.PermissionReview!.Scope);
        Assert.Equal(tree, retained.PermissionReview.TreeTemplate);
        Assert.False(retained.PermissionReview.Damaged);
    }

    [Fact]
    public async Task Ext4_reset_conflicts_if_the_template_default_acl_changes_after_review()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var root = TreeSubject(Path.Join(fixture.Root, "reset-source"), EntryKind.Directory);
        string tree = root.Subject.RawLocator;
        Directory.CreateDirectory(tree, (UnixFileMode)0x1c0);
        string child = Path.Join(tree, "child");
        File.WriteAllText(child, "owned fixture");
        File.SetUnixFileMode(child, (UnixFileMode)0x180);
        RunAclTool("setfacl", "-m", "d:u::rwx,d:g::r-x,d:o::---", tree);
        var ct = TestContext.Current.CancellationToken;
        using var operation = new LinuxPermissionService().PrepareAclResetTree(root.Item, root.Subject, false, false, ct).Operation;
        RunAclTool("setfacl", "-m", "d:g::r--", tree);

        var job = fixture.Manager.SubmitPermissions(operation);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Conflict, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(child));
        Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
    }

    [Fact]
    public async Task Ext4_reset_excludes_immutable_and_append_only_entries_and_their_subtrees()
    {
        RequireExt4Writer();
        string tree = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSION_RESET_FLAGS") ?? string.Empty;
        if (tree.Length == 0) Assert.Skip("Requires an isolated ext4 fixture with immutable and append-only entries.");
        using var fixture = new Fixture(nativeJournal: true);
        var root = TreeSubject(tree, EntryKind.Directory);
        string sibling = Path.Join(tree, "sibling"), immutable = Path.Join(tree, "immutable"), append = Path.Join(tree, "append");
        string witness = Path.Join(immutable, "witness");
        var ct = TestContext.Current.CancellationToken;
        using var operation = new LinuxPermissionService().PrepareAclResetTree(root.Item, root.Subject, false, false, ct).Operation;
        Assert.Equal(new PermissionTreeSummary(1, 0, 0, 0, ProtectedEntries: 2), operation.Tree!.Summary);
        Assert.Equal(sibling, Assert.Single(operation.Targets).Subject.RawLocator);
        var job = fixture.Manager.SubmitPermissions(operation);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(sibling));
        Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(immutable));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(witness));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(append));
    }

    [Fact]
    public async Task Ext4_group_job_removes_group_write_before_assignment_and_preserves_defaults()
    {
        RequireExt4Writer();
        using var fixture = new Fixture(nativeJournal: true);
        var item = new ItemRef(Location.FileSystem(fixture.Root), "group-review", EntryKind.Directory);
        string path = item.FileSystemPath!;
        Directory.CreateDirectory(path);
        RunAclTool("setfacl", "-m", "d:u:12345:r--", path);
        File.SetUnixFileMode(path, (UnixFileMode)0x5f8);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyGroupWrites: true);
        var ct = TestContext.Current.CancellationToken;
        var authority = LinuxPermissionService.ReadGroupAuthority(ct);
        using var inspection = service.Inspect(subject, PermissionSections.Ordinary, ct);
        var original = inspection.Snapshot;
        var group = PickAnotherGroup(authority, subject.Authority, original.Group.Value);
        Assert.True(service.CanEditGroup(original));
        var defaultAcl = Assert.IsType<PosixAcl>(original.Defaults.Value?.Acl);
        var unknown = Enumerable.Range(12345, 32).Select(id => (uint)id).First(id => !authority.Includes(id));
        Assert.Throws<UnauthorizedAccessException>(() => LinuxPermissionService.PreviewGroupChange(original,
            new(PermissionScope.Objects, [new AssignPermissionPrincipal(false, new(PrincipalKind.UnixGroup, unknown.ToString(), subject.Authority.Id))]), authority));
        var draft = new PermissionDraft(PermissionScope.Objects, [new AssignPermissionPrincipal(false, group), new ChangePermissionMode(1, 0x11)]);
        var plan = LinuxPermissionService.PreviewGroupChange(original, draft, authority);
        Assert.False(plan.NeedsSetIdConsent);
        Assert.Collection(plan.Stages,
            step => { Assert.Equal(PermissionSections.Mode, step.Change.Section); Assert.Equal((ushort)0x5e8, step.Expected.Mode.Value); Assert.Equal(original.Group.Value, step.Expected.Group.Value); },
            step => { Assert.Equal(PermissionSections.Group, step.Change.Section); Assert.Equal((ushort)0x5e8, step.Expected.Mode.Value); Assert.Equal(group, step.Expected.Group.Value); },
            step => { Assert.Equal(PermissionSections.Mode, step.Change.Section); Assert.Equal((ushort)0x5e9, step.Expected.Mode.Value); });
        using var review = LinuxPermissionService.Prepare(inspection, item, draft, false, ct, authority).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.Verified, result.Verdict);
        Assert.Equal(new[] { PermissionSections.Mode, PermissionSections.Group, PermissionSections.Mode }, result.Components.Select(component => component.Section));
        Assert.Equal((UnixFileMode)0x5e9, File.GetUnixFileMode(path));
        Assert.Equal(group.Id, RunAclTool("stat", "-c", "%g", path).Trim());
        using var readback = service.Inspect(subject, PermissionSections.Defaults, ct);
        Assert.Equal(defaultAcl.Bytes.ToArray(), Assert.IsType<PosixAcl>(readback.Snapshot.Defaults.Value?.Acl).Bytes.ToArray());
    }

    [Fact]
    public async Task Ext4_group_job_requires_set_id_consent_and_verifies_capability_clearance()
    {
        RequireExt4Writer();
        string? path = Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_GROUP_CAP_FILE");
        if (string.IsNullOrWhiteSpace(path)) Assert.Skip("Requires a private file-capability fixture.");
        using var fixture = new Fixture(nativeJournal: true);
        var item = new ItemRef(Location.FileSystem(Path.GetDirectoryName(path)!), Path.GetFileName(path), EntryKind.File);
        var subject = new PermissionSubject(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var service = new LinuxPermissionService(qualifyGroupWrites: true);
        var ct = TestContext.Current.CancellationToken;
        var authority = LinuxPermissionService.ReadGroupAuthority(ct);
        using var inspection = service.Inspect(subject, PermissionSections.Ordinary, ct);
        Assert.Equal((ushort)0xDE8, inspection.Snapshot.Mode.Value);
        Assert.Equal(PermissionReadStatus.Present, inspection.Snapshot.FileCapabilities?.Status);
        var group = PickAnotherGroup(authority, subject.Authority, inspection.Snapshot.Group.Value);
        var draft = new PermissionDraft(PermissionScope.Objects, [new AssignPermissionPrincipal(false, group)]);
        var expected = LinuxPermissionService.PreviewGroupChange(inspection.Snapshot, draft, authority);
        Assert.True(expected.NeedsSetIdConsent);
        Assert.Equal((ushort)0x1e8, expected.Expected.Mode.Value);
        Assert.Equal(PermissionReadStatus.Absent, expected.Expected.FileCapabilities?.Status);
        Assert.Throws<InvalidOperationException>(() => LinuxPermissionService.Prepare(inspection, item, draft, false, ct, authority));
        using var review = LinuxPermissionService.Prepare(inspection, item, draft, true, ct, authority).Review;
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal(group.Id, RunAclTool("stat", "-c", "%g", path).Trim());
        Assert.Equal((UnixFileMode)0x1e8, File.GetUnixFileMode(path));
        Assert.Empty(RunAclTool("getcap", path).Trim());
    }

    private static Principal PickAnotherGroup(LinuxGroupAuthority credential, PermissionAuthority authority, Principal? current)
    {
        var choices = credential.Choices(authority).Where(choice => choice != current).ToArray();
        if (choices.Length == 0) Assert.Skip("Requires an ordinary account with a supplementary group.");
        return choices[0];
    }

    private static (ItemRef Item, PermissionSubject Subject) Ext4Subject(Fixture fixture)
    {
        var item = new ItemRef(Location.FileSystem(fixture.Root), "ext4-subject", EntryKind.File);
        string path = item.FileSystemPath!;
        File.WriteAllText(path, "fixture");
        File.SetUnixFileMode(path, (UnixFileMode)0x180);
        return (item, new(item.Parent, path, path, new(PermissionAuthorityKind.Local, "fixture", 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow));
    }

    private static string RunAclTool(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000) && process.ExitCode == 0, error);
        return output;
    }

    [Fact]
    public async Task A_component_that_already_matches_writes_no_intent_and_makes_no_native_call()
    {
        using var fixture = new Fixture();
        var mutation = fixture.Mutation("file", 0x1a0);
        using var review = fixture.Review(mutation, 0x1a0, 0x1a0);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(0, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.AlreadyMatching, result.Verdict);
        var component = Assert.Single(result.Components);
        Assert.Equal(PermissionVerdict.AlreadyMatching, component.Verdict);
        Assert.Null(component.NativeCall);
        Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type is "permission-intent" or "permission-outcome");
    }

    [Fact]
    public async Task An_object_lost_after_its_intent_fails_before_change_with_a_reason_and_no_call()
    {
        using var fixture = new Fixture();
        var mutation = fixture.Mutation("file", 0x1a0);
        // The second check is the recheck after the intent flush; a removed volume fails it with an I/O error.
        mutation.Checking = check => { if (check == 2) throw new IOException("The volume was removed."); };
        using var review = fixture.Review(mutation, 0x1a0, 0x180);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(0, mutation.Calls);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.FailedBeforeChange, result.Verdict);
        var component = Assert.Single(result.Components);
        Assert.Null(component.NativeCall);
        Assert.Null(component.NativeError);
        Assert.Equal("The original object, authority or reviewed values could not be verified.", component.Detail);
        var records = JobJournal.ReadAll(fixture.JournalPath!);
        Assert.Single(records, record => record.Type == "permission-intent");
        Assert.Equal(nameof(PermissionVerdict.FailedBeforeChange), Assert.Single(records, record => record.Type == "permission-outcome").Get("verdict"));
    }

    [Fact]
    public async Task Job_flushes_intent_before_the_write_verifies_and_releases_once()
    {
        using var fixture = new Fixture();
        var mutation = fixture.Mutation("file", 0);
        mutation.Applying = _ =>
        {
            var intent = Assert.Single(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-intent");
            Assert.Equal("1", intent.Node["modeBits"]!.ToJsonString());
            Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-outcome");
        };
        using var review = fixture.Review(mutation, 0, 1);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal(1, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal(new[] { "begin", "permission-intent", "permission-outcome", "permission-object", "end" },
            JobJournal.ReadAll(fixture.JournalPath!).Select(record => record.Type));
        Assert.Throws<InvalidOperationException>(() => fixture.Manager.SubmitPermissions(review));
    }

    [Fact]
    public async Task Changed_security_with_the_same_identity_is_a_conflict_even_after_the_intent_flush()
    {
        using var fixture = new Fixture();
        var mutation = fixture.Mutation("file", 0);
        mutation.Checking = count => { if (count == 2) mutation.Mode = 4; };
        using var review = fixture.Review(mutation, 0, 1);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.Conflict, Assert.Single(job.PermissionResults).Verdict);
        Assert.Equal(0, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
        Assert.Equal("Conflict", Assert.Single(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-outcome").Get("verdict"));
    }

    [Fact]
    public async Task Final_read_reconciles_all_reviewed_components_after_the_last_write()
    {
        using var fixture = new Fixture();
        var mutation = fixture.Mutation("file", 0);
        mutation.Observing = count => { if (count == 3) mutation.Mode = 4; };
        using var review = fixture.Review(mutation, 0, 1, 3);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.Partial, result.Verdict);
        Assert.All(result.Components, component => Assert.Equal(PermissionVerdict.Verified, component.Verdict));
        Assert.Empty(job.CompletedRootIndices);
        Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "end");
    }

    [Fact]
    public async Task Cancellation_during_the_call_preserves_readback_and_stops_later_components()
    {
        using var fixture = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var mutation = fixture.Mutation("file", 0);
        mutation.Applying = _ => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); };
        using var review = fixture.Review(mutation, 0, 1, 3);
        var job = fixture.Manager.SubmitPermissions(review);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            job.Cancel();
            review.Dispose(); // the origin may close while the approved native call still runs
            Assert.Equal(0, mutation.Releases);
        }
        finally { release.Set(); }
        await fixture.Finish(job);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.Partial, result.Verdict);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(result.Components).Verdict);
        Assert.Equal(1, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
    }

    [Fact]
    public async Task Journal_fault_after_a_call_preserves_the_in_memory_result_and_stops_all_later_writes()
    {
        using var fixture = new Fixture();
        var mutation = fixture.Mutation("file", 0);
        fixture.Verify = () => { if (mutation.Calls > 0) throw new IOException("Fixture journal unavailable."); };
        using var review = fixture.Review(mutation, 0, 1, 3);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(JobState.Failed, job.State);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.Uncertain, result.Verdict);
        Assert.Equal(PermissionVerdict.Verified, Assert.Single(result.Components).Verdict);
        Assert.Equal(1, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
        Assert.DoesNotContain(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type is "permission-outcome" or "end");
        using var torn = new MemoryStream();
        torn.Write(File.ReadAllBytes(fixture.JournalPath!));
        torn.Write(Encoding.UTF8.GetBytes("c0000000 {\"t\":\"permission-outcome\""));
        torn.Position = 0;
        var interrupted = Assert.IsType<InterruptedJob>(PermissionJournalRecovery.Read(torn, fixture.JournalPath!));
        Assert.True(interrupted.IsPermissions);
        Assert.False(interrupted.CanContinueTransfer);
        Assert.True(interrupted.PermissionReview!.Damaged);
        Assert.Null(Assert.Single(interrupted.PermissionReview.Calls).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unix_native_journal_requires_private_modes_and_prunes_only_complete_history(bool denyReadback)
    {
        RequireUnixJournalFixture();
        await AssertNativeJournal(denyReadback);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Btrfs_native_journal_retains_uncertain_results_and_prunes_verified_history(bool denyReadback) =>
        await AssertNativeJournal(denyReadback, LinuxTestVolume("btrfs"));

    [Fact]
    public void Btrfs_journal_readers_refuse_a_live_writer_and_revalidate_the_name()
    {
        using var fixture = new TempDir(LinuxTestVolume("btrfs"));
        Assert.Throws<IOException>(() => new UnixAnalysisPrivateDirectory(fixture.Path, "analysis"));
        Assert.False(Directory.Exists(Path.Join(fixture.Path, "analysis")));
        string leaf = PermissionJournalNames.Name(DateTime.UtcNow, Guid.NewGuid());
        using var directory = UnixAnalysisPrivateDirectory.OpenPermissionJournalDirectory(fixture.Path);
        var tracked = UnixAnalysisPrivateDirectory.CreatePermissionJournal(fixture.Path, leaf, TestContext.Current.CancellationToken);
        using (tracked.Owner)
        using (tracked.Stream)
        {
            Assert.Throws<IOException>(() => directory.OpenPermissionJournal(leaf));
            Assert.Throws<IOException>(() => directory.OpenPermissionJournalMaintenance(leaf));
        }
        using var reader = directory.OpenPermissionJournal(leaf);
        reader.Verify();
        string moved = tracked.Path + ".moved";
        File.Move(tracked.Path, moved);
        File.Copy(moved, tracked.Path);
        Assert.Throws<IOException>(reader.Verify);
        File.Delete(tracked.Path);
        File.CreateSymbolicLink(tracked.Path, moved);
        Assert.Throws<IOException>(() => directory.OpenPermissionJournal(leaf));
    }

    [Theory]
    [InlineData("begin", false, false, false)]
    [InlineData("intent", false, true, false)]
    [InlineData("native-return", true, true, false)]
    [InlineData("outcome", true, true, true)]
    public async Task Btrfs_journal_recovers_records_after_abrupt_process_exit(string boundary, bool changed, bool intent, bool outcome)
    {
        using var fixture = new TempDir(LinuxTestVolume("btrfs"));
        await AssertJournalCrash(fixture, boundary, changed, intent, outcome, route: null);
    }

    // The macOS parent-relative and retained-descriptor writers, killed at the same journal boundaries.
    [Theory]
    [InlineData("begin", false, false, false, "darwin-parent")]
    [InlineData("intent", false, true, false, "darwin-parent")]
    [InlineData("native-return", true, true, false, "darwin-parent")]
    [InlineData("outcome", true, true, true, "darwin-parent")]
    [InlineData("begin", false, false, false, "darwin-descriptor")]
    [InlineData("intent", false, true, false, "darwin-descriptor")]
    [InlineData("native-return", true, true, false, "darwin-descriptor")]
    [InlineData("outcome", true, true, true, "darwin-descriptor")]
    public async Task Darwin_journal_recovers_records_after_abrupt_process_exit(string boundary, bool changed, bool intent, bool outcome, string route)
    {
        RequireDarwinWriter();
        using var fixture = new TempDir();
        await AssertJournalCrash(fixture, boundary, changed, intent, outcome, route);
    }

    private static async Task AssertJournalCrash(TempDir fixture, string boundary, bool changed, bool intent, bool outcome, string? route)
    {
        string harness = Path.Combine(AppContext.BaseDirectory, "FileCat.CoreCrashHarness.dll");
        Assert.True(File.Exists(harness));
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { harness, "--permissions-journal-crash", fixture.Path, boundary })
            start.ArgumentList.Add(argument);
        if (route is not null) start.ArgumentList.Add(route);
        using var child = Process.Start(start)!;
        var ct = TestContext.Current.CancellationToken;
        var errors = child.StandardError.ReadToEndAsync(ct);
        try
        {
            string? marker = await child.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(15), ct);
            Assert.True(marker == "PERMISSION_JOURNAL_READY", $"Child marker: {marker}. {(child.HasExited ? $"Exit {child.ExitCode}. {await errors}" : "Child still running.")}");
            child.Kill();
            await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); child.WaitForExit(10_000); }
        }

        string document = Path.Join(fixture.Path, "document");
        // The descriptor writer needs an executable document (0750 to 0700); the others change 0640 to 0600.
        int before = route == "darwin-descriptor" ? 0x1e8 : 0x1a0, after = route == "darwin-descriptor" ? 0x1c0 : 0x180;
        Assert.Equal((UnixFileMode)(changed ? after : before), File.GetUnixFileMode(document));
        using var directory = UnixAnalysisPrivateDirectory.OpenPermissionJournalDirectory(fixture.Path);
        var interrupted = Assert.Single(PermissionJournalRecovery.Scan(directory,
            Path.Join(fixture.Path, PermissionJournalNames.Directory), ct, keepFinished: 0));
        Assert.False(interrupted.CanContinueTransfer);
        var history = Assert.IsType<InterruptedPermissionReview>(interrupted.PermissionReview);
        Assert.False(history.Damaged);
        if (intent)
            Assert.Equal(outcome ? PermissionVerdict.Verified : (PermissionVerdict?)null, Assert.Single(history.Calls).Outcome);
        else
            Assert.Empty(history.Calls);
        Assert.True(File.Exists(interrupted.JournalPath));
    }

    private static async Task AssertNativeJournal(bool denyReadback, string? journalRoot = null)
    {
        using var fixture = new Fixture(nativeJournal: true, journalRoot: journalRoot);
        var mutation = fixture.Mutation("file", 0);
        if (denyReadback) mutation.Observing = _ => throw new UnauthorizedAccessException("Fixture verification refusal.");
        using var review = fixture.Review(mutation, 0, 1);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(1, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
        Assert.Equal(denyReadback ? PermissionVerdict.Uncertain : PermissionVerdict.Verified, Assert.Single(job.PermissionResults).Verdict);
        string root = Path.GetDirectoryName(fixture.JournalPath!)!;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.JournalPath!));
        using var directory = UnixAnalysisPrivateDirectory.OpenPermissionJournalDirectory(Path.GetDirectoryName(root)!);
        var history = PermissionJournalRecovery.Scan(directory, root, TestContext.Current.CancellationToken, keepFinished: 0);
        if (denyReadback)
        {
            var pending = Assert.Single(history);
            Assert.Equal(PermissionVerdict.Uncertain, Assert.Single(pending.PermissionReview!.Calls).Outcome);
            Assert.False(pending.PermissionReview.Damaged);
            Assert.Empty(pending.OpenIntents);
            Assert.True(File.Exists(fixture.JournalPath));
        }
        else
        {
            Assert.Empty(history);
            Assert.False(File.Exists(fixture.JournalPath));
        }
    }

    [Fact]
    public async Task Native_journal_refuses_a_directory_open_to_other_users()
    {
        RequireUnixJournalFixture();
        using var fixture = new Fixture(nativeJournal: true);
        string root = Path.Combine(fixture.Root, PermissionJournalNames.Directory);
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        var mutation = fixture.Mutation("file", 0);
        using var review = fixture.Review(mutation, 0, 1);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(0, mutation.Calls);
        Assert.Equal(PermissionVerdict.Unattempted, Assert.Single(job.PermissionResults).Verdict);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    private static void RequireUnixJournalFixture()
    {
        if (!(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_EXT4") == "1") &&
            !(OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("FILECAT_TEST_PERMISSIONS_JOURNAL") == "1"))
            Assert.Skip("Requires an explicitly enabled local Unix permissions journal fixture.");
    }

    [Fact]
    public async Task History_access_failure_prevents_any_permissions_call()
    {
        using var fixture = new Fixture();
        fixture.Verify = () => throw new UnauthorizedAccessException("Fixture security refusal.");
        var mutation = fixture.Mutation("file", 0);
        using var review = fixture.Review(mutation, 0, 1);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(0, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
    }

    [Fact]
    public void Submission_requires_live_bindings_and_protected_history()
    {
        using var fixture = new Fixture();
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), fixture.Root);
        Assert.Throws<ArgumentException>(() => manager.Submit(new JobRequest { Kind = JobKind.Permissions }));
        var mutation = fixture.Mutation("file", 0);
        using var review = fixture.Review(mutation, 0, 1);
        Assert.Throws<InvalidOperationException>(() => manager.SubmitPermissions(review));
        Assert.Equal(0, mutation.Calls);
        Assert.Equal(1, mutation.Releases);
    }

    [Theory]
    [InlineData(PermissionSections.Mode, false, PermissionVerdict.Verified)]
    [InlineData(PermissionSections.Owner, false, PermissionVerdict.Uncertain)]
    [InlineData(PermissionSections.None, false, PermissionVerdict.Uncertain)]
    [InlineData(PermissionSections.None, true, PermissionVerdict.FailedBeforeChange)]
    public async Task A_held_object_broker_receipt_requires_the_exact_sections_and_preserves_the_durable_outcome(
        PermissionSections scope, bool refused, PermissionVerdict verdict)
    {
        using var fixture = new Fixture();
        var original = fixture.Mutation("file", 0);
        var snapshot = original.Snapshot(0);
        var before = PermissionRevision.Read(snapshot, PermissionSections.Mode)!.Value;
        var expected = PermissionRevision.Read(original.Snapshot(1), PermissionSections.Mode)!.Value;
        var unread = snapshot with { Mode = snapshot.Mode with { Status = PermissionReadStatus.Denied, Value = null } };
        var receipt = new PermissionReadBack(unread, scope == PermissionSections.None ?
            PermissionVerification.Unavailable : PermissionVerification.SameObject, AttestedRevision: new PermissionRevision(scope, expected.Digest));
        var component = new ReviewedPermissionComponent(new ChangePermissionMode(1, 1), before, expected, "fixture broker");
        bool intentWasFlushed = false;
        var mutation = new BrokerReceiptMutation(snapshot, receipt, refused, () =>
        {
            var pending = JobJournal.ReadAll(fixture.JournalPath!);
            Assert.Single(pending, record => record.Type == "permission-intent");
            Assert.DoesNotContain(pending, record => record.Type == "permission-outcome");
            intentWasFlushed = true;
        });
        using var review = new ReviewedPermissionOperation(PermissionScope.Objects,
            [new(original.Item, original.Subject, original.Binding, [component], expected, mutation)]);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);

        Assert.True(intentWasFlushed);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(verdict, result.Verdict);
        var call = Assert.Single(result.Components);
        Assert.Equal(refused ? null : component.NativeCall, call.NativeCall);
        Assert.Equal(verdict == PermissionVerdict.Verified ? expected.Digest : null, call.ObservedDigest);
        var outcome = Assert.Single(JobJournal.ReadAll(fixture.JournalPath!), record => record.Type == "permission-outcome");
        Assert.Equal(verdict.ToString(), outcome.Get("verdict"));
        Assert.Equal(call.ObservedDigest, outcome.Get("observed"));
    }

    [Fact]
    public async Task Native_propagation_keeps_an_unchanged_root_after_dispatch_uncertain_in_the_journal()
    {
        using var fixture = new Fixture();
        var item = new ItemRef(Location.FileSystem(fixture.Root), "selected", EntryKind.Directory);
        Directory.CreateDirectory(item.FileSystemPath!);
        var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "local", 0);
        var subject = new PermissionSubject(item.Parent, item.FileSystemPath!, item.FileSystemPath!, authority,
            PermissionObjectKind.Directory, PermissionLinkPolicy.NoFollow);
        var binding = new PermissionBinding(new(new FileIdentity(4, "root"), "mount-1", subject.Kind, authority), "NTFS", fixture.Root, 1, false);
        var principal = new Principal(PrincipalKind.WindowsSid, "S-1-5-32-545", authority.Id);
        const PermissionSections sections = PermissionSections.Owner | PermissionSections.Group | PermissionSections.Access | PermissionSections.Inheritance;
        var now = DateTimeOffset.UtcNow;
        var original = PermissionSnapshots.Create(subject, PermissionModel.WindowsDescriptor, binding, sections, now, "fixture") with
        {
            Owner = new(PermissionReadStatus.Present, "fixture", now, principal),
            Group = new(PermissionReadStatus.Present, "fixture", now, principal),
            Access = new(PermissionReadStatus.Present, "fixture", now, new PermissionAclValue(new OpaquePermissionAcl([1]))),
            Inheritance = new(PermissionReadStatus.Present, "fixture", now, new WindowsAclControl(0x8404, 1))
        };
        var edited = original with { Access = original.Access with { Value = new PermissionAclValue(new OpaquePermissionAcl([2])) } };
        var before = PermissionRevision.Read(original, sections)!.Value;
        var expected = PermissionRevision.Read(edited, sections)!.Value;
        var addition = new AddWindowsAce(new WindowsAce(0, 3, WindowsAceKind.Allow, 1, principal, []));
        using var mutation = new UnchangedPropagationRoot(original);
        var component = new ReviewedPermissionComponent(addition, before, expected, "Windows propagation fixture");
        var target = new ReviewedPermissionTarget(item, subject, binding, [component], expected, mutation);
        Assert.Throws<NotSupportedException>(() => new ReviewedPermissionOperation(PermissionScope.NativePropagation,
            [target with { Components = [component with { Expected = before }], Final = before }]));
        Assert.Throws<ArgumentException>(() => new ReviewedPermissionOperation(PermissionScope.NativePropagation,
            [target with { Components = [component, component] }]));
        using var review = new ReviewedPermissionOperation(PermissionScope.NativePropagation, [target]);
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);

        Assert.Null(job.PermissionOperation);
        Assert.Equal(PermissionScope.NativePropagation, job.PermissionReviewScope);
        Assert.Equal(1, mutation.Calls);
        Assert.Empty(job.CompletedRootIndices);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.Uncertain, result.Verdict);
        var call = Assert.Single(result.Components);
        Assert.Equal(PermissionVerification.SameObject, call.Verification);
        Assert.Equal(before.Digest, call.ObservedDigest);
        Assert.Equal(5, call.NativeError);
        using var journal = File.OpenRead(fixture.JournalPath!);
        var retained = Assert.IsType<InterruptedJob>(PermissionJournalRecovery.Read(journal, fixture.JournalPath!));
        Assert.Equal(1, retained.SourceCount);
        var history = Assert.IsType<InterruptedPermissionReview>(retained.PermissionReview);
        Assert.False(history.Damaged);
        Assert.Equal(PermissionScope.NativePropagation, history.Scope);
        Assert.Equal(PermissionVerdict.Uncertain, Assert.Single(history.Calls).Outcome);
        Assert.False(retained.CanContinueTransfer);
    }

    [Fact]
    public void Tree_manifest_deduplicates_reviewed_aliases_and_refuses_crossings()
    {
        using var fixture = new Fixture();
        var file = fixture.Mutation("target", 0);
        var directory = file.Binding with { Identity = file.Binding.Identity with { Kind = PermissionObjectKind.Directory, Object = new FileIdentity(4, "root") } };
        var original = file.Subject with { Kind = PermissionObjectKind.Directory, RawLocator = fixture.Root };
        var revision = PermissionRevision.Read(file.Snapshot(0), PermissionSections.Mode)!.Value;
        var draft = new PermissionDraft(PermissionScope.ReviewedTree, [new ChangePermissionMode(1, 1)]);
        var root = new PermissionTreeEntry(-1, 0, original, directory, revision, draft);
        var target = new PermissionTreeEntry(0, 1, file.Subject, file.Binding, revision, draft);
        var alias = target with { Subject = file.Subject with { RawLocator = Path.Join(fixture.Root, "alias") } };
        var ct = TestContext.Current.CancellationToken;
        var tree = PermissionTreeManifest.Capture([root, target, alias], 1, ct);
        Assert.Equal(2, tree.Entries.Length);
        Assert.Equal(1, tree.RootCount);
        Assert.Equal(1, tree.InaccessibleBranches);
        Assert.Equal(1, tree.AliasCount);
        Assert.Throws<ArgumentException>(() => PermissionTreeManifest.Capture([root,
            target with { Binding = target.Binding! with { Identity = target.Binding!.Identity with { MountInstance = "other" } } }], 0, ct));
        Assert.Throws<ArgumentException>(() => PermissionTreeManifest.Capture([root, target,
            alias with { Draft = draft with { Changes = [new ChangePermissionMode(0, 1)] } }], 0, ct));
        Assert.Throws<NotSupportedException>(() => new ReviewedPermissionOperation(PermissionScope.ReviewedTree, []));
    }

    private sealed class UnchangedPropagationRoot(PermissionSnapshot snapshot) : IBoundPermissionMutation
    {
        internal int Calls { get; private set; }
        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation) => snapshot;
        public PermissionMutationReturn Apply(PermissionDelta change) { Calls++; return new(5, RejectedBeforeChange: true); }
        public PermissionReadBack Observe(PermissionSections sections) => new(snapshot, PermissionVerification.SameObject);
        public void Dispose() { }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _temporary;
        private readonly TempDir? _journalTemporary;
        private readonly TaskCompletionSource<Job> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Fixture(bool nativeJournal = false, string? journalRoot = null)
        {
            if (ReviewedPermissionOperation.ProcessRefusal() is { } reason) Assert.Skip(reason);
            _temporary = new();
            _journalTemporary = journalRoot is null ? null : new(journalRoot);
            string history = _journalTemporary?.Path ?? Root;
            Manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), history, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, cancellation) =>
                {
                    if (nativeJournal)
                    {
                        var tracked = UnixAnalysisPrivateDirectory.CreatePermissionJournal(history, leaf, cancellation);
                        JournalPath = tracked.Path;
                        return tracked with { Verify = () => { tracked.Verify(); Verify?.Invoke(); } };
                    }
                    JournalPath = Path.Combine(history, leaf);
                    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
                    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    var file = new FileStream(JournalPath, options);
                    return new(JournalPath, file, () => Verify?.Invoke(), file);
                });
            Manager.JobFinished += job => _finished.TrySetResult(job);
        }

        internal string Root => _temporary.Path;
        internal string? JournalPath { get; private set; }
        internal Action? Verify { get; set; }
        internal JobManager Manager { get; }
        internal async Task Finish(Job job) => Assert.Same(job, await _finished.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

        internal TestMutation Mutation(string name, ushort mode)
        {
            var item = new ItemRef(Location.FileSystem(Root), name, EntryKind.File);
            File.WriteAllText(item.FileSystemPath!, "fixture");
            var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "fixture", 1);
            var subject = new PermissionSubject(item.Parent, item.FileSystemPath!, item.FileSystemPath!, authority, PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
            return new TestMutation(item, subject, new PermissionBinding(new(new FileIdentity(4, name), "mount-1", PermissionObjectKind.File, authority), "fixture", Root, 1, false), mode);
        }

        internal ReviewedPermissionOperation Review(TestMutation mutation, params ushort[] expected)
        {
            var components = ImmutableArray.CreateBuilder<ReviewedPermissionComponent>();
            for (int i = 1; i < expected.Length; i++)
                components.Add(new(new ChangePermissionMode(expected[i], 0xfff), Revision(expected[i - 1]), Revision(expected[i]), "fixture-mode"));
            return new(PermissionScope.Objects, [new(mutation.Item, mutation.Subject, mutation.Binding, components.ToImmutable(), Revision(expected[^1]), mutation)]);
            PermissionRevision Revision(ushort value) => PermissionRevision.Read(mutation.Snapshot(value), PermissionSections.Mode)!.Value;
        }

        public void Dispose() { _temporary.Dispose(); _journalTemporary?.Dispose(); }
    }

    private sealed class BrokerReceiptMutation(PermissionSnapshot original, PermissionReadBack receipt, bool refused, Action onApply)
        : IBoundPermissionMutation
    {
        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation) => original;
        public PermissionMutationReturn Apply(PermissionDelta change) { onApply(); return new(FailedWithoutDispatch: refused); }
        public PermissionReadBack Observe(PermissionSections sections) => receipt;
        public void Dispose() { }
    }

    private sealed class TestMutation(ItemRef item, PermissionSubject subject, PermissionBinding binding, ushort mode) : IBoundPermissionMutation
    {
        internal ItemRef Item => item;
        internal PermissionSubject Subject => subject;
        internal PermissionBinding Binding => binding;
        internal ushort Mode = mode;
        internal int Calls, Releases;
        private int _checks, _observations;
        internal Action<int>? Applying, Checking, Observing;

        internal PermissionSnapshot Snapshot(ushort value)
        {
            var now = DateTimeOffset.UtcNow;
            return PermissionSnapshots.Create(subject, PermissionModel.UnixMode, binding, PermissionSections.Mode, now, "fixture") with
            {
                Mode = new(PermissionReadStatus.Present, "fixture", now, value)
            };
        }
        public PermissionSnapshot CheckCurrent(PermissionSections sections, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Checking?.Invoke(++_checks); return Snapshot(Mode); }
        public PermissionMutationReturn Apply(PermissionDelta change)
        { Calls++; Applying?.Invoke(Calls); Mode = ((ChangePermissionMode)change).Bits; return new(); }
        public PermissionReadBack Observe(PermissionSections sections)
        { Observing?.Invoke(++_observations); return new(Snapshot(Mode), PermissionVerification.SameObject); }
        public void Dispose() => Interlocked.Increment(ref Releases);
    }
}
