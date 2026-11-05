using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    private static AnalysisSnapshot StorageSample() => new(new(Guid.NewGuid(), 1, 1, "fixture", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        [new("r", "file", "Root", "Root", "device", IdentityAssurance.Verified, "Root")],
        [new(1, "r", 0, "Root", AnalysisKind.Directory)], [], [], [], new(), AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);

    [Fact]
    public void PrivateNativeCatalogEnumeratesAgainAndReopensWithSavedView()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        using var fixture = new PrivateFixture(); var view = AnalysisSavedView.Default with { Chart = "Table", Query = "name:*.txt" };
        Guid id;
        using (var store = new AnalysisPrivateInventoryStore(new WindowsAnalysisPrivateDirectory(fixture.Path, "saved")))
        {
            var catalog = new AnalysisSavedCatalog(store);
            id = catalog.SaveNew("catalog 😀.fcdsa", StorageSample(), Guid.NewGuid(), view, TestContext.Current.CancellationToken).InventoryId;
            for (int i = 0; i < 1000; i++) File.WriteAllBytes(Path.Combine(fixture.Path, "saved", $"unindexed-{i:D4}.bin"), []);
            using var first = catalog.Read(TestContext.Current.CancellationToken); Assert.Equal(id, Assert.Single(first.Entries).InventoryId);
            using var second = catalog.Read(TestContext.Current.CancellationToken); Assert.Single(second.Entries); // RestartScan resets held cursor
        }
        using var reopened = new AnalysisPrivateInventoryStore(new WindowsAnalysisPrivateDirectory(fixture.Path, "saved"));
        var fresh = new AnalysisSavedCatalog(reopened); using var result = fresh.Read(TestContext.Current.CancellationToken);
        var entry = Assert.Single(result.Entries); Assert.Equal(view, entry.View);
        using var loaded = fresh.Load(entry, TestContext.Current.CancellationToken); Assert.Equal(id, loaded.InventoryId);
        string metadata = Directory.GetFiles(Path.Combine(fixture.Path, "saved"), "catalog-*.json").Single(), alias = Path.Combine(fixture.Path, "metadata-alias");
        Assert.True(HardLink(alias, metadata, 0));
        Assert.Throws<IOException>(() => fresh.Read(TestContext.Current.CancellationToken)); File.Delete(alias);
    }

    [Fact]
    public void PrivateNativePublicationKeepsConflictsAndClosesUnpublishedStaging()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        using var fixture = new PrivateFixture(); string saved = Path.Combine(fixture.Path, "saved"), target = Path.Combine(saved, "one.fcdsa");
        var id = Guid.NewGuid();
        using var directory = new WindowsAnalysisPrivateDirectory(fixture.Path, "saved");
        var space = directory.Space();
        Assert.Equal((UInt128)(ulong)new DriveInfo(fixture.Path).TotalSize, space.Capacity);
        Assert.InRange(space.Available, (UInt128)0, space.Capacity);
        using (var store = new AnalysisPrivateInventoryStore(directory))
        {
            var receipt = store.SaveNew("one.fcdsa", StorageSample(), id, TestContext.Current.CancellationToken);
            Assert.Equal(new FileInfo(target).Length, receipt.Bytes);
            byte[] original = File.ReadAllBytes(target);
            using (var loaded = store.Load("one.fcdsa", TestContext.Current.CancellationToken))
            { Assert.Equal(id, loaded.InventoryId); Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Snapshot.Origin); }
            Assert.Throws<IOException>(() => store.SaveNew("one.fcdsa", StorageSample(), Guid.NewGuid(), TestContext.Current.CancellationToken));
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.Throws<InvalidDataException>(() => store.SaveNew("invalid.fcdsa", StorageSample() with { Roots = [] }, id, TestContext.Current.CancellationToken));
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => store.SaveNew("cancel.fcdsa", StorageSample(), id, cancel.Token));
            Assert.Equal(new[] { "one.fcdsa" }, Directory.GetFiles(saved).Select(Path.GetFileName));
            Assert.Throws<IOException>(() => Directory.Move(fixture.Path, fixture.Path + "-moved"));
        }
        using var reopened = new AnalysisPrivateInventoryStore(new WindowsAnalysisPrivateDirectory(fixture.Path, "saved"));
        using var reloaded = reopened.Load("one.fcdsa", TestContext.Current.CancellationToken); Assert.Equal(id, reloaded.InventoryId);
        var unicode = reopened.SaveNew("地图😀.fcdsa", StorageSample(), Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var unicodeReloaded = reopened.Load(unicode.Name, TestContext.Current.CancellationToken); Assert.Equal(unicode.InventoryId, unicodeReloaded.InventoryId);
    }

    [Fact]
    public void PrivateNativeRefusesHardlinksBroadAclAndReparseParents()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows process.");
        using var fixture = new PrivateFixture(); string saved = Path.Combine(fixture.Path, "saved");
        using (var store = new AnalysisPrivateInventoryStore(new WindowsAnalysisPrivateDirectory(fixture.Path, "saved")))
        {
            store.SaveNew("one.fcdsa", StorageSample(), Guid.NewGuid(), TestContext.Current.CancellationToken);
            string target = Path.Combine(saved, "one.fcdsa"), alias = Path.Combine(fixture.Path, "alias");
            Assert.True(HardLink(alias, target, 0));
            Assert.Throws<IOException>(() => store.Load("one.fcdsa", TestContext.Current.CancellationToken)); File.Delete(alias);
            var info = new FileInfo(target); var security = info.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            info.SetAccessControl(security);
            Assert.Throws<IOException>(() => store.Load("one.fcdsa", TestContext.Current.CancellationToken));
        }
        string junction = Path.Combine(fixture.Path, "redirect");
        Junction.Create(junction, saved);
        try { Assert.Throws<IOException>(() => new WindowsAnalysisPrivateDirectory(junction, "new")); }
        finally { Assert.True(RemoveDirectory(junction)); }
        string broad = Path.Combine(fixture.Path, "broad"); Directory.CreateDirectory(broad);
        Assert.Throws<IOException>(() => new WindowsAnalysisPrivateDirectory(fixture.Path, "broad"));
        Assert.Empty(Directory.GetFileSystemEntries(broad));
    }

    private sealed class PrivateFixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-private-" + Guid.NewGuid().ToString("N"));
        public PrivateFixture() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
