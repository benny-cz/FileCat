using System.Text;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Darwin_acl_tree_derives_ordered_children_from_native_inheritance(bool descriptor, bool reset)
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Join(fixture.Root, "tree"), (UnixFileMode)0x1c0).FullName;
        Rule(root, "deny writeattr,file_inherit,directory_inherit");
        string member = Path.Join(root, "file"), alias = Path.Join(root, "z-alias"), folder = Path.Join(root, "folder");
        NewFile(member, (UnixFileMode)0x9c0);
        RunAclTool("ln", member, alias);
        Rule(member, "allow readsecurity");
        Directory.CreateDirectory(folder, (UnixFileMode)0x1c0);
        Rule(folder, "allow readattr,file_inherit");
        string inner = Path.Join(folder, "grandchild");
        NewFile(inner, (UnixFileMode)0x1c0);
        string protectedFolder = Directory.CreateDirectory(Path.Join(root, "protected-folder"), (UnixFileMode)0x1c0).FullName;
        string protectedFile = Path.Join(protectedFolder, "untouched");
        NewFile(protectedFile, (UnixFileMode)0x1c0);
        SeedDarwinTestAcl(protectedFolder, DarwinPermissionAcl.Header(0, DarwinPermissionAcl.NoInheritance));
        var protectedBefore = ReadTreeAcl(protectedFile, false, ct);

        RunAclTool("chmod", "-N", root);
        string oracle = Directory.CreateDirectory(Path.Join(fixture.Root, "oracle"), (UnixFileMode)0x1c0).FullName;
        foreach (string source in new[] { root, oracle })
        {
            Rule(source, "allow readattr,file_inherit");
            Rule(source, "allow readextattr,directory_inherit");
            Rule(source, "allow writeextattr,file_inherit,directory_inherit,limit_inherit");
            Rule(source, "allow readsecurity,file_inherit,directory_inherit,only_inherit");
        }
        string nativeFile = Path.Join(oracle, "native-file"), nativeFolder = Path.Join(oracle, "native-folder");
        NewFile(nativeFile, (UnixFileMode)0x1c0);
        Directory.CreateDirectory(nativeFolder, (UnixFileMode)0x1c0);
        if (!reset) RunAclTool("chmod", "+a#", "0", "everyone allow readattr,file_inherit", nativeFolder);
        string nativeGrandchild = Path.Join(nativeFolder, "grandchild");
        NewFile(nativeGrandchild, (UnixFileMode)0x1c0);

        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
            qualifyAclWrites: true, qualifyAclTrees: true);
        var selected = TreeSubject(root, EntryKind.Directory);
        using var inspection = service.Inspect(selected.Subject, DarwinSections, ct);
        Assert.True(service.CanEditAclTree(inspection.Snapshot));
        var beforeRoot = ReadTreeAcl(root, true, ct);
        DateTime modified = File.GetLastWriteTimeUtc(member);
        var prepared = service.PrepareAclTree(selected.Item, selected.Subject, reset, ct, retainedInspections: 1);
        using var operation = prepared.Operation;
        Assert.Equal(new PermissionTreeSummary(1, 0, 0, 1, 1), operation.Tree!.Summary);
        Assert.Equal(reset ? PermissionScope.ResetTree : PermissionScope.ReviewedTree, operation.Scope);
        Assert.Equal(root, operation.TreeSource!.Subject.RawLocator);
        Assert.DoesNotContain(operation.Targets, target => target.Subject.RawLocator == root || target.Subject.RawLocator == protectedFile);
        Assert.DoesNotContain(operation.Targets, target => target.Subject.RawLocator.StartsWith(oracle + "/", StringComparison.Ordinal));
        Assert.All(operation.Targets, target => Assert.Equal(descriptor ? DarwinBindingRoute.Descriptor : DarwinBindingRoute.ParentRelative,
            target.Binding!.DarwinRoute));
        var row = Assert.Single(prepared.Rows, row => row.Subject.RawLocator == member);
        Assert.Contains(row.DarwinBefore!.Entries, entry => (entry.Flags & DarwinPermissionAcl.Inherited) == 0);
        Assert.Equal(!reset, row.DarwinAfter!.Entries.Any(entry => (entry.Flags & DarwinPermissionAcl.Inherited) == 0));
        Assert.True(row.Changed);

        string late = Path.Join(folder, "late");
        NewFile(late, (UnixFileMode)0x1c0);
        var lateAcl = ReadTreeAcl(late, false, ct);
        var job = fixture.Manager.SubmitPermissions(operation);
        await fixture.Finish(job);
        Assert.Contains(job.PermissionResults, item => item.Verdict == PermissionVerdict.AppliedWithFolderDrift);
        Assert.All(job.PermissionResults, item => Assert.True(item.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift or PermissionVerdict.AlreadyMatching,
            $"{item.SourceIndex} {item.Verdict} {string.Join(" | ", item.Components.Select(step => $"{step.NativeCall} {step.NativeError} {step.Detail}"))}"));

        CompareInherited(nativeFile, alias, false);
        CompareInherited(nativeFolder, folder, true);
        CompareInherited(nativeGrandchild, inner, false);
        Assert.Equal(beforeRoot!.FileSecurity.ToArray(), ReadTreeAcl(root, true, ct)!.FileSecurity.ToArray());
        Assert.Equal(DarwinPermissionAcl.NoInheritance, ReadTreeAcl(protectedFolder, true, ct)!.Flags);
        Assert.Equal(protectedBefore!.FileSecurity.ToArray(), ReadTreeAcl(protectedFile, false, ct)!.FileSecurity.ToArray());
        Assert.Equal(lateAcl!.FileSecurity.ToArray(), ReadTreeAcl(late, false, ct)!.FileSecurity.ToArray());
        Assert.Equal((ushort)0x9c0, ReadDarwinGroupMode(member).Mode);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(member));
        var lines = File.ReadAllLines(fixture.JournalPath!);
        using var interrupted = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', lines[..^1]) + "\n"));
        var history = Assert.IsType<InterruptedJob>(PermissionJournalRecovery.Read(interrupted, fixture.JournalPath!)).PermissionReview!;
        Assert.False(history.Damaged);
        Assert.Equal(root, history.TreeTemplate);
        Assert.Equal(operation.Scope, history.Scope);

        void CompareInherited(string oracle, string actual, bool directory)
        {
            var expected = ReadTreeAcl(oracle, directory, ct);
            var observed = ReadTreeAcl(actual, directory, ct);
            Assert.Equal(expected!.Entries.Where(rule => (rule.Flags & DarwinPermissionAcl.Inherited) != 0),
                observed!.Entries.Where(rule => (rule.Flags & DarwinPermissionAcl.Inherited) != 0));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Darwin_acl_tree_preserves_the_distinction_between_absent_and_empty_acl(bool descriptor)
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Join(fixture.Root, "tree"), (UnixFileMode)0x1c0).FullName;
        string absent = Path.Join(root, "absent"), explicitFile = Path.Join(root, "explicit");
        NewFile(absent, (UnixFileMode)0x1c0);
        NewFile(explicitFile, (UnixFileMode)0x1c0);
        Rule(explicitFile, "allow readattr");
        var selected = TreeSubject(root, EntryKind.Directory);
        var service = new DarwinPermissionService(qualifyParentWrites: !descriptor, qualifyDescriptorWrites: descriptor,
            qualifyAclWrites: true, qualifyAclTrees: true);
        using var operation = service.PrepareAclTree(selected.Item, selected.Subject, reset: true, ct).Operation;
        var job = fixture.Manager.SubmitPermissions(operation);
        await fixture.Finish(job);
        Assert.Equal(PermissionVerdict.AlreadyMatching, job.PermissionResults[0].Verdict);
        Assert.Equal(PermissionVerdict.Verified, job.PermissionResults[1].Verdict);
        Assert.Null(ReadTreeAcl(absent, false, ct));
        Assert.Empty(ReadTreeAcl(explicitFile, false, ct)!.Entries);
    }

    [Fact]
    public async Task Darwin_acl_tree_refuses_a_source_change_after_intent()
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Join(fixture.Root, "tree"), (UnixFileMode)0x1c0).FullName;
        string member = Path.Join(root, "file");
        NewFile(member, (UnixFileMode)0x180);
        Rule(root, "allow readattr,file_inherit");
        var selected = TreeSubject(root, EntryKind.Directory);
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyAclWrites: true, qualifyAclTrees: true);
        using var operation = service.PrepareAclTree(selected.Item, selected.Subject, reset: false, ct).Operation;
        bool drift = false;
        fixture.Verify = () =>
        {
            if (drift || fixture.JournalPath is not { } journal || !JobJournal.ReadAll(journal).Any(record => record.Type == "permission-intent")) return;
            drift = true;
            Rule(root, "allow readsecurity,file_inherit");
        };
        var job = fixture.Manager.SubmitPermissions(operation);
        await fixture.Finish(job);
        Assert.True(drift);
        var result = Assert.Single(job.PermissionResults);
        Assert.Equal(PermissionVerdict.Conflict, result.Verdict);
        Assert.Null(Assert.Single(result.Components).NativeCall);
        Assert.Null(ReadTreeAcl(member, false, ct));
    }

    [Fact]
    public void Darwin_acl_tree_refuses_hard_link_names_with_conflicting_inheritance()
    {
        RequireDarwinWriter();
        using var fixture = DarwinJournalFixture();
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Join(fixture.Root, "tree"), (UnixFileMode)0x1c0).FullName;
        string first = Directory.CreateDirectory(Path.Join(root, "first"), (UnixFileMode)0x1c0).FullName;
        string second = Directory.CreateDirectory(Path.Join(root, "second"), (UnixFileMode)0x1c0).FullName;
        string member = Path.Join(first, "file");
        NewFile(member, (UnixFileMode)0x180);
        RunAclTool("ln", member, Path.Join(second, "alias"));
        Rule(first, "allow readattr,file_inherit");
        var selected = TreeSubject(root, EntryKind.Directory);
        var service = new DarwinPermissionService(qualifyParentWrites: true, qualifyAclWrites: true, qualifyAclTrees: true);
        var refusal = Assert.Throws<ArgumentException>(() => service.PrepareAclTree(selected.Item, selected.Subject, reset: false, ct));
        Assert.Contains("hard-link names require different inherited ACLs", refusal.Message);
        Assert.Null(fixture.JournalPath);
        Assert.Null(ReadTreeAcl(member, false, ct));
    }

    private static void Rule(string path, string rule) => RunAclTool("chmod", "+a", "everyone " + rule, path);

    private static void NewFile(string path, UnixFileMode mode)
    {
        File.WriteAllText(path, "fixture");
        File.SetUnixFileMode(path, mode);
    }

    private static DarwinAcl? ReadTreeAcl(string path, bool directory, CancellationToken ct)
    {
        var subject = TreeSubject(path, directory ? EntryKind.Directory : EntryKind.File).Subject;
        using var inspection = new DarwinPermissionService().Inspect(subject, PermissionSections.Access, ct);
        Assert.True(inspection.Snapshot.Access.Status is PermissionReadStatus.Present or PermissionReadStatus.Absent,
            inspection.Snapshot.BindingProblem ?? inspection.Snapshot.Access.Reason);
        return inspection.Snapshot.Access.Value?.Acl as DarwinAcl;
    }
}
