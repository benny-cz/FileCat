using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Core.Records;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // G-MAC (ordinary, D1) provider-related writer refusals under the fetch sentinel: in the disposable File Provider
    // domain (FILECAT_TEST_MAC_PROVIDER_ROOT), each Darwin writer route meets the dataless items with downloads disabled: a
    // file at the domain root, a file inside an enumerated folder and a folder never enumerated. Each route inspects
    // without materializing, offers no mode change and refuses to prepare one. A mode-tree review over the domain root
    // leaves every dataless member out without opening it, and is released without being applied. Every item keeps its
    // identity and dataless state, and the caller's materialization policy is restored. The harness reads the provider's
    // log for fetch and enumeration requests over the run.
    [Fact]
    public void Live_Mac_File_Provider_writers_refuse_dataless_items_without_fetching()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROVIDER_ROOT");
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || string.IsNullOrWhiteSpace(root))
            Assert.Skip("Requires an ordinary macOS worker and the disposable File Provider fixture.");
        var reader = new UnixFileRecords();
        CancellationToken ct = TestContext.Current.CancellationToken;
        int policy = MacMaterializationPolicy.Get();
        (string Path, EntryKind Kind)[] items =
        [
            (Path.Join(root, "dataless-file.bin"), EntryKind.File),
            (Path.Join(root, "dataless-folder", "child.bin"), EntryKind.File),
            (Path.Join(root, "unvisited-folder"), EntryKind.Directory),
        ];
        var before = items.ToDictionary(entry => entry.Path, entry => Dataless(entry.Path));
        foreach (var (path, kind) in items)
        {
            var (item, subject) = TreeSubject(path, kind);
            foreach (var (parent, descriptor) in new[] { (true, false), (false, true), (true, true) })
            {
                var service = new DarwinPermissionService(qualifyParentWrites: parent, qualifyDescriptorWrites: descriptor,
                    qualifyProtectionWrites: true, qualifyAclWrites: true, qualifyGroupWrites: true);
                using var inspection = service.Inspect(subject, DarwinSections, ct);
                string? refusal = DarwinPermissionService.ModeRefusal(inspection.Snapshot);
                TestContext.Current.SendDiagnosticMessage($"{Path.GetFileName(path)} (parent {parent}, descriptor {descriptor}): " +
                    $"mode {inspection.Snapshot.Mode.Status}, flags {inspection.Snapshot.Flags.Value?.Bits:x}; refusal: {refusal}");
                Assert.NotNull(refusal);
                var problem = Record.Exception(() => DarwinPermissionService.Prepare(inspection, item,
                    new(PermissionScope.Objects, [new ChangePermissionMode(0x1c0, 0x1c0)]), false, ct).Review.Dispose());
                Assert.NotNull(problem);
                Assert.Equal(policy, MacMaterializationPolicy.Get());
            }
        }

        var tree = new DarwinPermissionService(qualifyParentWrites: true, qualifyModeTrees: true);
        var (rootItem, rootSubject) = TreeSubject(root, EntryKind.Directory);
        try
        {
            using var review = tree.PrepareModeTree([(rootItem, rootSubject)], new(0x100, 0x100), false, false, ct).Operation;
            var targets = review.Targets.Select(target => target.Subject.RawLocator).ToArray();
            var summary = review.Tree!.Summary;
            TestContext.Current.SendDiagnosticMessage($"Tree review over the domain root: {targets.Length} targets " +
                $"({string.Join(", ", targets.Select(Path.GetFileName))}); {summary.ExcludedEntries} excluded, " +
                $"{summary.InaccessibleBranches} inaccessible.");
            foreach (var (path, _) in items) Assert.DoesNotContain(path, targets);
            Assert.True(summary.ExcludedEntries >= 2, $"{summary.ExcludedEntries} excluded entries.");
        }
        catch (NotSupportedException refused)
        {
            TestContext.Current.SendDiagnosticMessage("Tree review over the domain root refused: " + refused.Message);
        }
        Assert.Equal(policy, MacMaterializationPolicy.Get());

        foreach (var (path, _) in items)
        {
            var (identity, dataless) = Dataless(path);
            Assert.Equal(before[path].Identity, identity);
            Assert.True(dataless, $"{path} is no longer dataless.");
        }

        (DetailsIdentity? Identity, bool Dataless) Dataless(string path)
        {
            var summary = reader.ReadSummary(path, ct);
            Assert.Equal(FactState.Known, summary.NativeAttributes.State);
            return (summary.Identity.Value, (summary.NativeAttributes.Value & MacMaterializationPolicy.DatalessFlag) != 0);
        }
    }
}
