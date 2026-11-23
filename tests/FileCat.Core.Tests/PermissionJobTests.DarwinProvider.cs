using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Records;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class PermissionJobTests
{
    // G-MAC (ordinary, D1) provider-related writer refusals under the fetch sentinel: in the disposable File Provider
    // domain (FILECAT_TEST_MAC_PROVIDER_ROOT), each Darwin writer route meets the dataless items with downloads disabled: a
    // file at the domain root, a file inside an enumerated folder and a folder never enumerated. Each route inspects
    // without materializing, offers no change (the placeholder reason) and refuses to prepare one; the parent-relative
    // route offered a mode change on such a placeholder before this check. A mode-tree review over the domain root
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
                if (inspection.Snapshot.Binding is not null) Assert.Contains("cloud placeholder that is not downloaded", refusal);
                Assert.DoesNotContain(inspection.Snapshot.Capabilities, capability =>
                    capability is { Action: PermissionAction.Change, Availability: PermissionAvailability.Available });
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

    // G-MAC (ordinary, D1) a write dispatched to a File Provider item under the fetch sentinel: in the writable fixture
    // domain (FILECAT_TEST_MAC_PROVIDER_WRITABLE_ROOT) the harness downloads writable.txt first and leaves sentinel.bin a
    // placeholder. With downloads disabled, mode jobs alternately add and remove owner execute on the downloaded file,
    // each verified on the same object: the checked-parent service adds it, the descriptor service (which opens only
    // folders and executable files) removes it, and the combined service adds it through the checked parent and removes
    // it through the descriptor. A pause after each job lets the system pass the change to the provider. The file keeps
    // its identity, the sentinel stays a dataless placeholder with its identity, and the caller's materialization policy
    // is restored. The harness reads the provider's log: the changes reach the provider as modify requests carrying the
    // file-system flags, and nothing is fetched.
    [Fact]
    public async Task Live_Mac_File_Provider_mode_jobs_change_a_downloaded_item_without_fetching()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_PROVIDER_WRITABLE_ROOT");
        if (!OperatingSystem.IsMacOS() || Environment.IsPrivilegedProcess || string.IsNullOrWhiteSpace(root))
            Assert.Skip("Requires an ordinary macOS worker and the writable File Provider fixture.");
        var reader = new UnixFileRecords();
        CancellationToken ct = TestContext.Current.CancellationToken;
        string file = Path.Join(root, "writable.txt"), sentinel = Path.Join(root, "sentinel.bin");
        var fileBefore = State(file);
        var sentinelBefore = State(sentinel);
        Assert.False(fileBefore.Dataless, "The harness downloads writable.txt before the run.");
        Assert.True(sentinelBefore.Dataless, "sentinel.bin must still be a placeholder.");
        int policy = MacMaterializationPolicy.Get();
        var original = (ushort)((int)File.GetUnixFileMode(file) & 0xfff);
        Assert.Equal(0, original & 0x40);
        var executable = (ushort)(original | 0x40);
        (bool Parent, bool Descriptor, ushort Mode, DarwinBindingRoute Route)[] jobs =
        [
            (true, false, executable, DarwinBindingRoute.ParentRelative),
            (false, true, original, DarwinBindingRoute.Descriptor),
            (true, true, executable, DarwinBindingRoute.ParentRelative),
            (true, true, original, DarwinBindingRoute.Descriptor),
        ];
        foreach (var (parent, descriptor, mode, route) in jobs)
        {
            using var fixture = DarwinJournalFixture(); // one job per fixture
            var (item, subject) = TreeSubject(file, EntryKind.File);
            var service = new DarwinPermissionService(qualifyParentWrites: parent, qualifyDescriptorWrites: descriptor);
            using var inspection = service.Inspect(subject, DarwinSections, ct);
            Assert.Null(DarwinPermissionService.ModeRefusal(inspection.Snapshot));
            Assert.Equal(route, inspection.Snapshot.Binding?.DarwinRoute);
            using var review = DarwinPermissionService.Prepare(inspection, item,
                new(PermissionScope.Objects, [new ChangePermissionMode(mode, 0xfff)]), false, ct).Review;
            var job = fixture.Manager.SubmitPermissions(review);
            await fixture.Finish(job);
            var result = Assert.Single(job.PermissionResults);
            string steps = $"{job.State}; {JobIssue.Summarize(job.Issues)}; " +
                string.Join(" | ", result.Components.Select(step => $"{step.Verdict} {step.NativeCall} {step.NativeError} {step.Detail}"));
            TestContext.Current.SendDiagnosticMessage($"parent {parent}, descriptor {descriptor}, route {route}: " +
                $"{Convert.ToString(mode, 8)} -> {result.Verdict}: {steps}");
            Assert.True(result.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift, steps);
            Assert.All(result.Components, component => Assert.Equal(PermissionVerification.SameObject, component.Verification));
            Assert.Equal(mode, (ushort)((int)File.GetUnixFileMode(file) & 0xfff));
            Assert.Equal(policy, MacMaterializationPolicy.Get());
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        var fileAfter = State(file);
        Assert.Equal(fileBefore.Identity, fileAfter.Identity);
        Assert.False(fileAfter.Dataless);
        var sentinelAfter = State(sentinel);
        Assert.Equal(sentinelBefore.Identity, sentinelAfter.Identity);
        Assert.True(sentinelAfter.Dataless, "sentinel.bin was downloaded.");

        (DetailsIdentity? Identity, bool Dataless) State(string path)
        {
            var summary = reader.ReadSummary(path, ct);
            Assert.Equal(FactState.Known, summary.NativeAttributes.State);
            return (summary.Identity.Value, (summary.NativeAttributes.Value & MacMaterializationPolicy.DatalessFlag) != 0);
        }
    }
}
