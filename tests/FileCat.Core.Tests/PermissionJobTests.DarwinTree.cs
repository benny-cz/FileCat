using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Darwin_tree_mode_captures_members_and_leaves_late_entries_and_protection_alone(bool exactExecute)
    {
        RequireDarwinWriter();
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        string path = Path.Join(fixture.Root, "tree");
        string nested = Path.Join(path, "folder");
        string opaque = Path.Join(path, "unlisted");
        string lockedDirectory = Path.Join(path, "locked-folder");
        foreach (string directory in new[] { path, nested, opaque, lockedDirectory }) Directory.CreateDirectory(directory, (UnixFileMode)0x1c0);
        File.SetUnixFileMode(path, (UnixFileMode)0x1c8);
        string plain = Path.Join(nested, "plain"), executable = Path.Join(path, "executable"), alias = Path.Join(nested, "z-alias");
        string outside = Path.Join(fixture.Root, "outside"), hidden = Path.Join(opaque, "unknown");
        string late = Path.Join(nested, "late"), lockedFile = Path.Join(path, "append-only"), lockedChild = Path.Join(lockedDirectory, "protected-child");
        foreach (string file in new[] { plain, outside, hidden, executable, lockedFile, lockedChild })
        {
            File.WriteAllText(file, "fixture");
            File.SetUnixFileMode(file, (UnixFileMode)(file == executable ? 0x1c0 : 0x180));
        }
        RunAclTool("ln", plain, alias);
        File.CreateSymbolicLink(Path.Join(path, "link"), outside);
        RunAclTool("mkfifo", Path.Join(path, "fifo"));
        RunAclTool("chmod", "+a#", "0", "everyone allow readattr", plain);
        RunAclTool("chflags", "uappnd", lockedFile);
        RunAclTool("chflags", "uchg", lockedDirectory);
        File.SetUnixFileMode(opaque, (UnixFileMode)0x40);
        try
        {
            var root = TreeSubject(path, EntryKind.Directory);
            var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyDescriptorWrites: true, qualifyModeTrees: true);
            using var inspection = service.Inspect(root.Subject, DarwinSections, ct);
            Assert.True(service.CanEditModeTree(inspection.Snapshot));
            Assert.False(new DarwinPermissionService().CanEditModeTree(inspection.Snapshot));
            var draft = new ChangePermissionMode(8, 8);
            using var review = service.PrepareModeTree([TreeSubject(plain, EntryKind.File), TreeSubject(nested, EntryKind.Directory), root],
                draft, exactExecute, false, ct, retainedInspections: 1).Operation;
            Assert.Equal(new PermissionTreeSummary(1, 1, 2, 1, 2), review.Tree!.Summary);
            Assert.Equal(plain, review.Targets[0].Subject.RawLocator);
            Assert.Equal(path, review.Targets[^1].Subject.RawLocator);
            Assert.Contains(DarwinBindingRoute.Descriptor, review.Targets.Select(target => target.Binding!.DarwinRoute));
            Assert.Contains(DarwinBindingRoute.ParentRelative, review.Targets.Select(target => target.Binding!.DarwinRoute));
            File.WriteAllText(late, "arrived after review");
            File.SetUnixFileMode(late, (UnixFileMode)0x180);
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            Assert.Equal(PermissionScope.ReviewedTree, job.PermissionReviewScope);
            Assert.Equal(review.Tree.Summary, job.ReviewedTreeSummary);
            var results = job.PermissionResults;
            Assert.Equal(5, results.Count);
            Assert.All(results, result => Assert.True(result.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift or PermissionVerdict.AlreadyMatching,
                $"{result.SourceIndex}: {result.Verdict} {result.Detail} {string.Join(" | ", result.Components.Select(step => step.Detail))}"));
            Assert.Equal(PermissionVerdict.AlreadyMatching, results[^1].Verdict);
            Assert.Contains(results, result => result.Verdict == PermissionVerdict.AppliedWithFolderDrift);
            foreach (string untouched in new[] { outside, hidden, late, lockedFile, lockedChild })
                Assert.Equal((ushort)0x180, ReadDarwinGroupMode(untouched).Mode);
            Assert.Equal((ushort)(exactExecute ? 0x188 : 0x180), ReadDarwinGroupMode(alias).Mode);
            Assert.Equal((ushort)0x1c8, ReadDarwinGroupMode(executable).Mode);
            Assert.Equal((ushort)0x1c8, ReadDarwinGroupMode(nested).Mode);
            Assert.Contains("everyone allow readattr", RunAclTool("ls", "-led", plain));
            Assert.Contains("uappnd", RunAclTool("stat", "-f", "%Sf", lockedFile));
            Assert.Contains("uchg", RunAclTool("stat", "-f", "%Sf", lockedDirectory));
        }
        finally
        {
            File.SetUnixFileMode(opaque, (UnixFileMode)0x1c0);
            RunAclTool("chflags", "nouappnd", lockedFile);
            RunAclTool("chflags", "nouchg", lockedDirectory);
        }
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("lose-search")]
    [InlineData("disappear")]
    public async Task Darwin_tree_mode_stops_at_changed_ancestry_or_a_vanished_name(string scenario)
    {
        RequireDarwinWriter();
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        string path = Path.Join(fixture.Root, "tree"), nested = Path.Join(path, "nested"), moved = Path.Join(path, "original-nested");
        Directory.CreateDirectory(path, (UnixFileMode)0x1c0);
        Directory.CreateDirectory(nested, (UnixFileMode)0x1c0);
        string child = Path.Join(nested, "child"), sibling = Path.Join(path, "sibling");
        foreach (string file in new[] { child, sibling })
        {
            File.WriteAllText(file, "fixture");
            File.SetUnixFileMode(file, (UnixFileMode)0x180);
        }
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyModeTrees: true);
        using var review = service.PrepareModeTree([TreeSubject(path, EntryKind.Directory)], new(4, 4), false, false, ct).Operation;
        var childIdentity = Assert.Single(review.Targets, target => target.Subject.RawLocator == child).Binding!.Identity;
        if (scenario == "replace")
        {
            Directory.Move(nested, moved);
            Directory.CreateDirectory(nested, (UnixFileMode)0x1c0);
            File.WriteAllText(child, "replacement");
            File.SetUnixFileMode(child, (UnixFileMode)0x180);
        }
        else if (scenario == "lose-search") File.SetUnixFileMode(nested, (UnixFileMode)0x180);
        else File.Delete(child);
        try
        {
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults, result => result.Identity == childIdentity);
            Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
            var component = Assert.Single(result.Components);
            Assert.Null(component.NativeCall);
            if (scenario == "disappear") Assert.Contains("no longer exists at its original name", component.Detail);
            Assert.Equal((ushort)0x184, ReadDarwinGroupMode(sibling).Mode);
        }
        finally { if (scenario == "lose-search") File.SetUnixFileMode(nested, (UnixFileMode)0x1c0); }
        if (scenario != "disappear") Assert.Equal((ushort)0x180, ReadDarwinGroupMode(child).Mode);
        if (scenario == "replace") Assert.Equal((ushort)0x180, ReadDarwinGroupMode(Path.Join(moved, "child")).Mode);
    }

    [Fact]
    public void Darwin_tree_mode_refuses_an_over_capacity_review_and_releases_its_pins()
    {
        RequireDarwinWriter();
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        string path = Directory.CreateDirectory(Path.Join(fixture.Root, "tree"), (UnixFileMode)0x1c0).FullName;
        for (int index = 0; index < 4; index++) File.WriteAllText(Path.Join(path, $"file-{index}"), "fixture");
        var root = TreeSubject(path, EntryKind.Directory);
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyModeTrees: true);
        var problem = Assert.Throws<NotSupportedException>(() => service.PrepareModeTree([root], new(4, 4), false, false, ct,
            retainedInspections: DarwinPermissionService.ModeTreeTargetLimit - 4));
        Assert.Contains("at most 4 captured names", problem.Message);
        Assert.Null(fixture.JournalPath);
        using var fresh = service.PrepareModeTree([root], new(4, 4), false, false, ct).Operation;
        Assert.Equal(5, fresh.Targets.Length);
    }

    [Fact]
    public async Task Darwin_tree_mode_keeps_unattempted_members_unchanged_when_cancelled_after_intent()
    {
        RequireDarwinWriter();
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        string path = Directory.CreateDirectory(Path.Join(fixture.Root, "tree"), (UnixFileMode)0x1c0).FullName;
        string file = Path.Join(path, "file");
        File.WriteAllText(file, "fixture");
        File.SetUnixFileMode(file, (UnixFileMode)0x180);
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyModeTrees: true);
        using var review = service.PrepareModeTree([TreeSubject(path, EntryKind.Directory)], new(4, 4), false, false, ct).Operation;
        bool cancelled = false;
        fixture.Verify = () =>
        {
            if (cancelled || fixture.JournalPath is not { } journal || !JobJournal.ReadAll(journal).Any(record => record.Type == "permission-intent")) return;
            cancelled = true;
            fixture.Manager.Jobs.Single().Cancel();
        };
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.True(cancelled);
        Assert.All(job.PermissionResults, result => Assert.Equal(PermissionVerdict.Unattempted, result.Verdict));
        Assert.Null(Assert.Single(job.PermissionResults[0].Components).NativeCall);
        Assert.Equal((ushort)0x180, ReadDarwinGroupMode(file).Mode);
        Assert.Equal((ushort)0x1c0, ReadDarwinGroupMode(path).Mode);
    }

    [Fact]
    public async Task Darwin_tree_mode_excludes_an_independently_mounted_descendant()
    {
        RequireDarwinWriter();
        string root = Environment.GetEnvironmentVariable("FILECAT_TEST_DARWIN_NESTED_IMAGE_ROOT") ?? string.Empty;
        if (!Path.IsPathFullyQualified(root))
            Assert.Skip("Requires the disposable nested Darwin mount fixture.");
        var ct = TestContext.Current.CancellationToken;
        using var fixture = DarwinJournalFixture();
        string nested = Path.Join(root, "nested"), outside = Path.Join(nested, "witness"), member = Path.Join(root, "reviewed");
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyDescriptorWrites: true, qualifyModeTrees: true);
        using var rootInspection = service.Inspect(TreeSubject(root, EntryKind.Directory).Subject, PermissionSections.None, ct);
        using var nestedInspection = service.Inspect(TreeSubject(nested, EntryKind.Directory).Subject, PermissionSections.None, ct);
        var first = Assert.IsType<PermissionBinding>(rootInspection.Snapshot.Binding);
        var boundary = Assert.IsType<PermissionBinding>(nestedInspection.Snapshot.Binding);
        Assert.NotEqual(first.Identity.MountInstance, boundary.Identity.MountInstance);
        Assert.Equal(PermissionObjectKind.VolumeRoot, boundary.Identity.Kind);
        Assert.Equal((ushort)0x180, ReadDarwinGroupMode(outside).Mode);
        Assert.Equal((ushort)0x180, ReadDarwinGroupMode(member).Mode);
        using var review = service.PrepareModeTree([TreeSubject(root, EntryKind.Directory)], new(4, 4), false, false, ct, retainedInspections: 2).Operation;
        Assert.Equal(new PermissionTreeSummary(1, 0, 1, 0), review.Tree!.Summary);
        Assert.Equal(new[] { member, root }, review.Targets.Select(target => target.Subject.RawLocator));
        var job = fixture.Manager.SubmitPermissions(review);
        await fixture.Finish(job);
        Assert.All(job.PermissionResults, result => Assert.Equal(PermissionVerdict.Verified, result.Verdict));
        Assert.Equal((ushort)0x184, ReadDarwinGroupMode(member).Mode);
        Assert.Equal((ushort)0x180, ReadDarwinGroupMode(outside).Mode);
    }
}
