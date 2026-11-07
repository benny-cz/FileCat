using System.Text.Json;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Job_planning_routes_same_spelled_roots_and_renamed_folder_additions(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        using var temp = new TempDir(); var profile = WorkflowProfile(format, mode);
        var request = new ArchivePlan(Path.Combine(temp.Path, "target"), null,
        [new(ArchiveChangeKind.AddFile, "Case", Path.Combine(temp.Path, "a", "payload")),
         new(ArchiveChangeKind.AddFile, "case", Path.Combine(temp.Path, "b", "payload")),
         new(ArchiveChangeKind.AddFolder, "renamed", Path.Combine(temp.Path, "a", "tree")),
         new(ArchiveChangeKind.CreateFolderEntry, "empty/")], Profile: profile);
        var planning = new ArchiveJobPlanning(request, Ct); var opened = new List<Tree>();
        using var selection = ArchiveSelectionForest.Capture(planning.Sources, parent =>
        {
            var tree = new Tree { Root = name => new(name, name == "tree" ? ArchiveSelectionKind.Directory : ArchiveSelectionKind.File, parent + "/" + name),
                Children = (_, visit) => visit(new("child", ArchiveSelectionKind.File, "child")) };
            opened.Add(tree); return tree;
        }, Ct, planning.RetainedNameChars, planning.RetainedHeapBytes);
        var review = planning.Review(null, selection, selection.Inputs, Ct);
        Assert.Equal(format == ArchiveWriteFormat.SevenZip ? 1 : 0, review.InputTimePrecisionChanges);
        var plan = review.CreatePlan(false, false, Ct, acceptTimestampPrecision: true);
        Assert.Equal(3, plan.Inputs.Count); Assert.Single(plan.Directories);
        Assert.Equal(new[] { "Case", "case", "empty", "renamed", "renamed/child" },
            plan.Expected.Members.Select(member => member.Name.TrimEnd('/')).Order(StringComparer.Ordinal));
        Assert.Equal(2, opened.Count); selection.Dispose(); Assert.All(opened, tree => Assert.Equal(1, tree.Disposals));
    }

    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Job_planning_binds_duplicate_recursive_scopes_and_ordinal_case(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        using var temp = new TempDir(); var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile, duplicates: true, implicitFolder: true);
        var request = new ArchivePlan(Path.Combine(temp.Path, "target"), new(source.SourceLength, 0, source.SourceSha256),
            [new(ArchiveChangeKind.Rename, "tree", NewMemberPath: "moved"), new(ArchiveChangeKind.Delete, "Case")], Profile: profile);
        var planning = new ArchiveJobPlanning(request, Ct); using var captured = new ArchiveCapturedInputs();
        captured.RetainSelection(planning.RetainedNameChars, planning.RetainedHeapBytes); captured.Seal(Ct);
        var result = planning.Review(source, null, captured, Ct).CreatePlan(false, false, Ct);
        Assert.Equal(2, result.Expected.Members.Count(member => member.Name == "moved/old"));
        Assert.DoesNotContain(result.Expected.Members, member => member.Name == "Case" || member.Name.StartsWith("tree/", StringComparison.Ordinal));
        Assert.Contains(result.Expected.Members, member => member.Name == "case");
        Assert.Contains(result.Expected.Members, member => member.Name == "treehouse/kept");
        Assert.Contains(result.Expected.Members, member => member.Name == "é"); Assert.Contains(result.Expected.Members, member => member.Name == "é");
    }

    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Job_planning_replaces_only_an_explicit_duplicate_occurrence(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        using var temp = new TempDir(); var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile, duplicates: true);
        var request = new ArchivePlan(Path.Combine(temp.Path, "target"), new(source.SourceLength, 0, source.SourceSha256),
            [new(ArchiveChangeKind.Replace, "tree/old", Path.Combine(temp.Path, "payload"), Ordinal: 1)], Profile: profile);
        var planning = new ArchiveJobPlanning(request, Ct); var tree = new Tree { Root = name => new(name, ArchiveSelectionKind.File, name) };
        using var selection = ArchiveSelectionForest.Capture(planning.Sources, _ => tree, Ct, planning.RetainedNameChars, planning.RetainedHeapBytes);
        var result = planning.Review(source, selection, selection.Inputs, Ct).CreatePlan(false, false, Ct);
        var edited = Assert.Single(result.Edits); Assert.Equal(1, edited.Key.Occurrence);
        var copies = result.Expected.Members.Where(member => member.Name == "tree/old").ToArray(); Assert.Equal(2, copies.Length);
        Assert.Equal(source.Manifest.Members.First(member => member.Name == "tree/old").Sha256, copies[0].Sha256);
        Assert.Equal(selection.Inputs.Bindings[edited.InputId!.Value].Reviewed.Version.Sha256, copies[1].Sha256);
        var ambiguous = request with { Changes = [request.Changes[0] with { Ordinal = null }] };
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => new ArchiveJobPlanning(ambiguous, Ct)
            .Review(source, selection, selection.Inputs, Ct)).Reason);
    }

    [Fact]
    public void Job_exclusion_review_uses_the_captured_selection_count()
    {
        using var temp = new TempDir(); var request = new ArchivePlan(Path.Combine(temp.Path, "target"), null,
            [new(ArchiveChangeKind.AddFile, "kept", Path.Combine(temp.Path, "file")), new(ArchiveChangeKind.AddFile, "excluded", Path.Combine(temp.Path, "link"))],
            Profile: new(ArchiveWriteFormat.Tar));
        var planning = new ArchiveJobPlanning(request, Ct); var tree = new Tree { Root = name => new(name,
            name == "link" ? ArchiveSelectionKind.Link : ArchiveSelectionKind.File, name) };
        using var selection = ArchiveSelectionForest.Capture(planning.Sources, _ => tree, Ct, planning.RetainedNameChars, planning.RetainedHeapBytes);
        var review = planning.Review(null, selection, selection.Inputs, Ct); Assert.Equal(1, review.ExcludedCount);
        Assert.Throws<ArchiveWriteException>(() => review.CreatePlan(false, false, Ct));
        Assert.Equal("kept", Assert.Single(review.CreatePlan(false, true, Ct).Expected.Members).Name);
        Assert.Equal(2, Assert.Single(selection.ExclusionSamples).RootSelector);
    }

    [Fact]
    public void Forest_freezes_every_root_before_opening_any_parent()
    {
        using var temp = new TempDir(); ArchiveSelectionSource[] requests =
            [new(Path.Combine(temp.Path, "a", "first"), "first", ArchiveSelectionKind.File), new(Path.Combine(temp.Path, "b", "second"), "second", ArchiveSelectionKind.File)];
        var parents = new List<string>(); var trees = new List<Tree>();
        using var selection = ArchiveSelectionForest.Capture(requests, parent =>
        {
            parents.Add(parent); requests[1] = new(Path.Combine(temp.Path, "outside", "changed"), "changed", ArchiveSelectionKind.Directory);
            var tree = new Tree { Root = name => new(name, ArchiveSelectionKind.File, parent + name) }; trees.Add(tree); return tree;
        }, Ct);
        Assert.Equal(new[] { Path.Combine(temp.Path, "a"), Path.Combine(temp.Path, "b") }, parents);
        Assert.Equal(new[] { "first", "second" }, selection.Members.Select(member => member.Name).Order(StringComparer.Ordinal));
        selection.Dispose(); Assert.All(trees, tree => Assert.Equal(1, tree.Disposals));
    }

    [Fact]
    public void Forest_destination_spelling_is_separate_from_native_ancestor_paths()
    {
        using var temp = new TempDir(); var paths = new List<(string Native, string Output)>();
        var tree = new Tree { Children = (node, visit) =>
        {
            paths.Add((node.NativeRelativePath, node.MemberName));
            if (node.Parent is null) visit(new("sub", ArchiveSelectionKind.Directory, "sub"));
            else visit(new("file", ArchiveSelectionKind.File, "file"));
        } };
        using var selection = ArchiveSelectionForest.Capture([new(Path.Combine(temp.Path, "tree"), "published/alias", ArchiveSelectionKind.Directory)], _ => tree, Ct);
        Assert.Equal(new[] { ("tree", "published/alias"), ("tree/sub", "published/alias/sub") }, paths);
        Assert.Contains(selection.Members, member => member.Name == "published/alias/sub/file");
    }

    [Theory]
    [InlineData((int)ArchiveSelectionKind.File, (int)ArchiveSelectionKind.Directory)]
    [InlineData((int)ArchiveSelectionKind.Directory, (int)ArchiveSelectionKind.File)]
    public void Forest_root_kind_changes_refuse_before_capturing_or_enumerating(int expected, int actual)
    {
        using var temp = new TempDir(); var tree = new Tree { Root = name => new(name, (ArchiveSelectionKind)actual, name), Children = (_, _) => Assert.Fail("Must not enumerate a changed root.") };
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => ArchiveSelectionForest.Capture(
            [new(Path.Combine(temp.Path, "source"), "source", (ArchiveSelectionKind)expected)], _ => tree, Ct)).Reason);
        Assert.Equal(0, tree.Captures); Assert.Equal(1, tree.Disposals);
    }

    [Fact]
    public void Forest_parent_and_retained_budget_limits_refuse_before_native_opens()
    {
        using var temp = new TempDir(); int opened = 0; var roots = Enumerable.Range(0, ArchiveSelectionForest.MaximumParents + 1)
            .Select(i => new ArchiveSelectionSource(Path.Combine(temp.Path, "parent-" + i, "file"), "file-" + i, ArchiveSelectionKind.File)).ToArray();
        IArchiveSelectionTree Open(string _) { opened++; return new Tree(); }
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => ArchiveSelectionForest.Capture(roots, Open, Ct)).Reason);
        Assert.Equal(0, opened);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => ArchiveSelectionForest.Capture(
            [roots[0]], Open, Ct, retainedHeapBytes: ArchiveWritePolicy.MaxRetainedHeapBytes)).Reason);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void Native_baseline_authority_is_not_serialized_with_a_queued_plan()
    {
        using var temp = new TempDir(); var baseline = new Source(91).Observation();
        var plan = new ArchivePlan(Path.Combine(temp.Path, "target"), null, [], Profile: new(ArchiveWriteFormat.Tar)) { NativeBaseline = baseline };
        string serialized = JsonSerializer.Serialize(plan);
        Assert.DoesNotContain("NativeBaseline", serialized); Assert.DoesNotContain(baseline.Version.Identity, serialized);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Job_review_refuses_a_capture_from_another_source_specification(bool spelling)
    {
        using var temp = new TempDir(); var request = new ArchivePlan(Path.Combine(temp.Path, "target"), null,
            [new(ArchiveChangeKind.AddFile, "output", Path.Combine(temp.Path, "input"))], Profile: new(ArchiveWriteFormat.Tar));
        var planning = new ArchiveJobPlanning(request, Ct); var desired = planning.Sources[0];
        var wrong = spelling ? desired with { MemberName = "other" } : desired with { Path = Path.Combine(temp.Path, "elsewhere", "input") };
        var tree = new Tree { Root = name => new(name, ArchiveSelectionKind.File, name) };
        using var selection = ArchiveSelectionForest.Capture([wrong], _ => tree, Ct);
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => planning.Review(null, selection, selection.Inputs, Ct)).Reason);
    }

    [Fact]
    public void Job_planning_checks_generated_destination_lengths_before_allocating_them()
    {
        using var temp = new TempDir(); var request = new ArchivePlan(Path.Combine(temp.Path, "target"), null,
            [new(ArchiveChangeKind.AddFolder, new string('x', ArchiveWritePolicy.MaxNameChars - 4), Path.Combine(temp.Path, "tree"))], Profile: new(ArchiveWriteFormat.Tar));
        var planning = new ArchiveJobPlanning(request, Ct); var tree = new Tree { Children = (_, visit) => visit(new("child", ArchiveSelectionKind.File, "child")) };
        using var selection = ArchiveSelectionForest.Capture(planning.Sources, _ => tree, Ct, planning.RetainedNameChars, planning.RetainedHeapBytes);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => planning.Review(null, selection, selection.Inputs, Ct)).Reason);
    }

    [Fact]
    public void Job_planning_charges_catalog_indexes_with_the_live_capture_owner()
    {
        using var temp = new TempDir(); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); var source = WorkflowSource(profile);
        var request = new ArchivePlan(Path.Combine(temp.Path, "target"), new(source.SourceLength, 0, source.SourceSha256),
            [new(ArchiveChangeKind.Delete, "Case")], Profile: profile);
        var planning = new ArchiveJobPlanning(request, Ct); using var captured = new ArchiveCapturedInputs();
        captured.RetainSelection(0, ArchiveWritePolicy.MaxRetainedHeapBytes - 8192); captured.Seal(Ct);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => planning.Review(source, null, captured, Ct)).Reason);
    }

    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Job_planning_rejects_partial_duplicate_relocation(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        using var temp = new TempDir(); var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile, duplicates: true);
        var members = source.Manifest.Members.ToList(); var duplicate = members.First(member => member.Name == "tree/old");
        members.Add(new(duplicate.Name, duplicate.Type, 2, duplicate.Length, duplicate.Sha256, duplicate.Metadata));
        source = source with { Manifest = new(profile, members, source.Manifest.Features) };
        var request = new ArchivePlan(Path.Combine(temp.Path, "target"), new(source.SourceLength, 0, source.SourceSha256),
            [new(ArchiveChangeKind.Rename, "tree/old", NewMemberPath: "moved", Ordinal: 0),
             new(ArchiveChangeKind.Rename, "tree/old", NewMemberPath: "moved", Ordinal: 1)], Profile: profile);
        var planning = new ArchiveJobPlanning(request, Ct); using var captured = new ArchiveCapturedInputs();
        captured.RetainSelection(planning.RetainedNameChars, planning.RetainedHeapBytes); captured.Seal(Ct);
        var review = planning.Review(source, null, captured, Ct);
        Assert.Throws<ArchiveWriteException>(() => review.CreatePlan(false, false, Ct));
    }

    [Theory, MemberData(nameof(WorkflowProfiles))]
    public void Job_planning_rejects_merging_distinct_existing_names(ArchiveWriteFormat format, ArchiveEncryptionMode mode)
    {
        using var temp = new TempDir(); var profile = WorkflowProfile(format, mode); var source = WorkflowSource(profile);
        var request = new ArchivePlan(Path.Combine(temp.Path, "target"), new(source.SourceLength, 0, source.SourceSha256),
            [new(ArchiveChangeKind.Rename, "Case", NewMemberPath: "combined", Ordinal: 0), new(ArchiveChangeKind.Rename, "case", NewMemberPath: "combined", Ordinal: 0)], Profile: profile);
        var planning = new ArchiveJobPlanning(request, Ct); using var captured = new ArchiveCapturedInputs();
        captured.RetainSelection(planning.RetainedNameChars, planning.RetainedHeapBytes); captured.Seal(Ct);
        Assert.Throws<ArchiveWriteException>(() => planning.Review(source, null, captured, Ct).CreatePlan(false, false, Ct));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Job_planning_handles_large_duplicate_scopes_without_repeated_group_scans(bool directories)
    {
        using var temp = new TempDir(); const int count = 10000; var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var template = WorkflowCreationPlan(profile).Expected.Members.First(member => member.Type == (directories ? ArchiveMemberType.Directory : ArchiveMemberType.File));
        var members = Enumerable.Range(0, count).Select(i => new ArchiveManifestMember(template.Name, template.Type, i, template.Length, template.Sha256, template.Metadata)).ToArray();
        string revision = new('A', 64); var source = new ArchiveAssessment(new(profile, members, WorkflowCreationPlan(profile).Expected.Features), revision, 200, revision);
        var changes = Enumerable.Range(0, count).Select(i => new ArchiveChange(ArchiveChangeKind.Delete, template.Name.TrimEnd('/'), Ordinal: directories ? null : i)).ToArray();
        var request = new ArchivePlan(Path.Combine(temp.Path, "target"), new(200, 0, revision), changes, Profile: profile);
        var planning = new ArchiveJobPlanning(request, Ct); using var captured = new ArchiveCapturedInputs();
        captured.RetainSelection(planning.RetainedNameChars, planning.RetainedHeapBytes); captured.Seal(Ct);
        if (directories) Assert.Throws<ArchiveWriteException>(() => planning.Review(source, null, captured, Ct));
        else Assert.Empty(planning.Review(source, null, captured, Ct).CreatePlan(false, false, Ct).Expected.Members);
    }
}
