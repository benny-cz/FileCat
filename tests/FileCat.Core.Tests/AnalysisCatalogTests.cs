using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisCatalogTests
{
    private static readonly CancellationToken Cancellation = TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");

    [Fact]
    public void UncertainHeaderDurabilityDoesNotBecomeAConfirmedInventorySave()
    {
        Ordinary(); using var directory = new MemoryDirectory { UncertainHeader = true }; using var store = new AnalysisPrivateInventoryStore(directory);
        Assert.Throws<IOException>(() => new AnalysisSavedCatalog(store).SaveNew("not-saved.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation));
        Assert.Equal([AnalysisPrivateNames.Catalog], directory.Files.Keys);
    }

    [Fact]
    public void CatalogMemoryRemainsReservedUntilResultOwnersClose()
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        using var first = catalog.Read(Cancellation); using var second = catalog.Read(Cancellation);
        Assert.Throws<InvalidOperationException>(() => catalog.Read(Cancellation));
        second.Dispose(); second.Dispose(); using var third = catalog.Read(Cancellation); Assert.Empty(third.Entries);
    }

    [Fact]
    public void CatalogRoundTripRestoresOnlyInertDataAndIdentifiesUnindexedInventories()
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory);
        var catalog = new AnalysisSavedCatalog(store); var id = Guid.NewGuid();
        var view = new AnalysisSavedView(AnalysisMetric.Files, "Sunburst", "name:*.bin", "1", "n:1", true, false, AnalysisTableSort.MeasurementState, false)
            { ColorDimension = AnalysisColorDimension.ModifiedAge };
        var receipt = catalog.SaveNew("saved 😀.fcdsa", AnalysisAccountingTests.Golden(), id, view, Cancellation);
        store.SaveNew("legacy.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), Cancellation);
        using var result = catalog.Read(Cancellation); var entry = Assert.Single(result.Entries);
        Assert.NotNull(result.CatalogId); Assert.Equal(id, entry.InventoryId); Assert.Equal(receipt.Bytes, entry.Bytes); Assert.Equal(view, entry.View);
        Assert.Equal(["legacy.fcdsa"], result.UnindexedNames);
        using var loaded = catalog.Load(entry, Cancellation);
        Assert.Equal(AnalysisOrigin.ImportedInventory, loaded.Snapshot.Origin);
        Assert.True(loaded.Snapshot.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        byte[] before = directory.Files[entry.Name].ToArray();
        Assert.Throws<IOException>(() => catalog.SaveNew("SAVED 😀.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), view, Cancellation));
        Assert.Equal(before, directory.Files[entry.Name]);
        Assert.Throws<InvalidDataException>(() => catalog.Load(entry with { InventoryId = Guid.NewGuid() }, Cancellation));
    }

    [Theory]
    [InlineData("future")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("numeric")]
    [InlineData("direction")]
    [InlineData("duplicate")]
    [InlineData("legacy-extra")]
    [InlineData("color-missing")]
    [InlineData("color-unknown")]
    [InlineData("color-numeric")]
    [InlineData("color-duplicate")]
    [InlineData("rule-future")]
    [InlineData("rule-numeric")]
    [InlineData("rule-noncanonical")]
    [InlineData("rule-missing")]
    public void InvalidSavedOrderingIsRefusedWithoutChangingExistingMetadata(string fault)
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory);
        var catalog = new AnalysisSavedCatalog(store); catalog.SaveNew("order.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
        string name = directory.Files.Keys.Single(n => n.StartsWith("catalog-", StringComparison.Ordinal));
        string json = Encoding.UTF8.GetString(directory.Files[name]);
        json = fault switch
        {
            "future" => json.Replace("\"schema\":\"3\"", "\"schema\":\"4\"", StringComparison.Ordinal),
            "missing" => json.Replace(",\"sort\":\"ActiveMetric\"", "", StringComparison.Ordinal),
            "unknown" => json.Replace("ActiveMetric", "ArbitraryPath", StringComparison.Ordinal),
            "numeric" => json.Replace("\"sort\":\"ActiveMetric\"", "\"sort\":0", StringComparison.Ordinal),
            "direction" => json.Replace("\"sortDescending\":true", "\"sortDescending\":\"true\"", StringComparison.Ordinal),
            "duplicate" => json.Replace("\"sortDescending\":true", "\"sortDescending\":true,\"sortDescending\":false", StringComparison.Ordinal),
            "color-missing" => json.Replace(",\"color\":\"FileType\"", "", StringComparison.Ordinal),
            "color-unknown" => json.Replace("\"color\":\"FileType\"", "\"color\":\"Growth\"", StringComparison.Ordinal),
            "color-numeric" => json.Replace("\"color\":\"FileType\"", "\"color\":0", StringComparison.Ordinal),
            "color-duplicate" => json.Replace("\"color\":\"FileType\"", "\"color\":\"FileType\",\"color\":\"ModifiedAge\"", StringComparison.Ordinal),
            "rule-future" => json.Replace("\"colorRule\":\"1\"", "\"colorRule\":\"2\"", StringComparison.Ordinal),
            "rule-numeric" => json.Replace("\"colorRule\":\"1\"", "\"colorRule\":1", StringComparison.Ordinal),
            "rule-noncanonical" => json.Replace("\"colorRule\":\"1\"", "\"colorRule\":\"01\"", StringComparison.Ordinal),
            "rule-missing" => json.Replace(",\"colorRule\":\"1\"", "", StringComparison.Ordinal),
            _ => json.Replace("\"schema\":\"3\"", "\"schema\":\"1\"", StringComparison.Ordinal)
        };
        directory.Files[name] = Encoding.UTF8.GetBytes(json);
        Assert.Throws<InvalidDataException>(() => catalog.Read(Cancellation)); Assert.Equal(json, Encoding.UTF8.GetString(directory.Files[name]));
        using var data = store.Load("order.fcdsa", Cancellation); Assert.Equal(AnalysisOrigin.ImportedInventory, data.Snapshot.Origin);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public void LegacySavedViewUsesItsOriginalDefaultsWithoutRewritingTheEntry(string schema)
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory);
        var catalog = new AnalysisSavedCatalog(store); catalog.SaveNew("old.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
        string name = directory.Files.Keys.Single(n => n.StartsWith("catalog-", StringComparison.Ordinal));
        string json = Encoding.UTF8.GetString(directory.Files[name]).Replace("\"schema\":\"3\"", "\"schema\":\"" + schema + "\"", StringComparison.Ordinal)
            .Replace(",\"color\":\"FileType\",\"colorRule\":\"1\"", "", StringComparison.Ordinal);
        if (schema == "1") json = json.Replace(",\"sort\":\"ActiveMetric\",\"sortDescending\":true", "", StringComparison.Ordinal);
        directory.Files[name] = Encoding.UTF8.GetBytes(json);
        using var result = catalog.Read(Cancellation); var entry = Assert.Single(result.Entries);
        Assert.Equal(AnalysisSavedView.Default, entry.View); Assert.Equal(json, Encoding.UTF8.GetString(directory.Files[name]));
    }

    [Theory]
    [InlineData("{\"schema\":\"2\",\"kind\":\"catalog\",\"catalog\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"schema\":\"1\",\"schema\":\"1\",\"kind\":\"catalog\",\"catalog\":\"11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"schema\":\"1\",\"kind\":\"catalog\",\"catalog\":\"11111111-1111-1111-1111-111111111111\",\"path\":\"elsewhere\"}")]
    [InlineData("{\"schema\":\"1\",\"kind\":\"catalog\",\"catalog\":\"00000000-0000-0000-0000-000000000000\"}")]
    public void UnsupportedOrPoisonedHeaderRefusesWritesWithoutChangingAnyBytes(string json)
    {
        Ordinary(); using var directory = new MemoryDirectory(); directory.Files[AnalysisPrivateNames.Catalog] = Encoding.UTF8.GetBytes(json);
        using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        Assert.Throws<InvalidDataException>(() => catalog.SaveNew("refused.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation));
        Assert.Single(directory.Files); Assert.Equal(json, Encoding.UTF8.GetString(directory.Files[AnalysisPrivateNames.Catalog]));
    }

    [Theory]
    [InlineData("number")]
    [InlineData("duplicate")]
    [InlineData("path")]
    [InlineData("identity")]
    [InlineData("view")]
    public void PoisonedEntryIsRefusedWithoutRepairOrSourceAccess(string fault)
    {
        Ordinary(); using var directory = new MemoryDirectory(); using var store = new AnalysisPrivateInventoryStore(directory);
        var catalog = new AnalysisSavedCatalog(store); catalog.SaveNew("one.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
        string name = directory.Files.Keys.Single(n => n.StartsWith("catalog-", StringComparison.Ordinal));
        string json = Encoding.UTF8.GetString(directory.Files[name]);
        json = fault switch
        {
            "number" => json.Replace("\"bytes\":\"", "\"bytes\":\"0", StringComparison.Ordinal),
            "duplicate" => json.Replace("\"kind\":\"named\"", "\"kind\":\"named\",\"kind\":\"named\"", StringComparison.Ordinal),
            "path" => json.Replace("one.fcdsa", "../one.fcdsa", StringComparison.Ordinal),
            "identity" => json.Replace("\"inventory\":\"", "\"inventory\":\"a", StringComparison.Ordinal),
            _ => json.Replace("Treemap", "Executable", StringComparison.Ordinal)
        };
        directory.Files[name] = Encoding.UTF8.GetBytes(json);
        Assert.Throws<InvalidDataException>(() => catalog.Read(Cancellation));
        using var offline = store.Load("one.fcdsa", Cancellation); Assert.Equal(AnalysisOrigin.ImportedInventory, offline.Snapshot.Origin);
        Assert.Equal(json, Encoding.UTF8.GetString(directory.Files[name]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedIndexReportsPublishedInventoryAndAllowsFilenameRecovery(bool cancel)
    {
        Ordinary(); using var cts = new CancellationTokenSource();
        using var directory = new MemoryDirectory { FailEntry = !cancel, AfterInventory = cancel ? cts.Cancel : null };
        using var store = new AnalysisPrivateInventoryStore(directory); var catalog = new AnalysisSavedCatalog(store);
        var error = Assert.Throws<AnalysisCatalogUpdateException>(() => catalog.SaveNew("recover.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, cts.Token));
        Assert.Equal("recover.fcdsa", error.Receipt.Name);
        using var loaded = store.Load(error.Receipt.Name, Cancellation); Assert.Equal(error.Receipt.InventoryId, loaded.InventoryId);
        using var result = catalog.Read(Cancellation); Assert.Empty(result.Entries); Assert.Equal(["recover.fcdsa"], result.UnindexedNames);
    }

    [Fact]
    public void NativeUnixCatalogCanBeReopenedAndNeverFollowsMetadataLinks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix native catalog fixture."); Ordinary();
        string root = Path.Combine(Environment.CurrentDirectory, ".tmp", "catalog-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string saved = Path.Combine(root, "saved");
            using (var store = new AnalysisPrivateInventoryStore(new UnixAnalysisPrivateDirectory(root, "saved")))
            {
                var catalog = new AnalysisSavedCatalog(store); var receipt = catalog.SaveNew("first.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
                for (int i = 0; i < 1000; i++) File.WriteAllBytes(Path.Combine(saved, $"unindexed-{i:D4}.bin"), []);
                using var first = catalog.Read(Cancellation); Assert.Equal(receipt.InventoryId, Assert.Single(first.Entries).InventoryId);
                using var second = catalog.Read(Cancellation); Assert.Single(second.Entries); // independent enumeration cursor
                using var loaded = catalog.Load(first.Entries[0], Cancellation); Assert.Equal(receipt.InventoryId, loaded.InventoryId);
                Assert.All(Directory.GetFiles(saved).Where(p => p.EndsWith(".json", StringComparison.Ordinal) || p.EndsWith(".fcdsa", StringComparison.Ordinal)), path => Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path)));
            }
            string metadata = Directory.GetFiles(saved, "catalog-*.json").Single(); File.Delete(metadata);
            File.CreateSymbolicLink(metadata, Path.Combine(saved, "first.fcdsa"));
            using var reopened = new AnalysisPrivateInventoryStore(new UnixAnalysisPrivateDirectory(root, "saved"));
            Assert.Throws<IOException>(() => new AnalysisSavedCatalog(reopened).Read(Cancellation));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public bool FailEntry, UncertainHeader; public Action? AfterInventory;
        internal override void Verify() { }
        internal override (UInt128 Capacity, UInt128 Available) Space() => (1UL << 40, 1UL << 40);
        internal override AnalysisPrivateFile CreateStaging() => new MemoryFile(this, new(), true);
        internal override AnalysisPrivateFile OpenRead(string name) => new MemoryFile(this, new(Files[name]), false);
        internal override string[] Entries(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Files.Keys.ToArray(); }
        public override void Dispose() { }
        private sealed class MemoryFile(MemoryDirectory directory, MemoryStream stream, bool writable) : AnalysisPrivateFile
        {
            public override Stream Stream => stream;
            public override void Verify() { }
            public override void Flush() { }
            public override void PublishNew(string name)
            {
                if (!writable) throw new InvalidOperationException();
                if (directory.FailEntry && name.StartsWith("catalog-", StringComparison.Ordinal)) throw new IOException("Injected index publication failure.");
                if (!directory.Files.TryAdd(name, stream.ToArray())) throw new IOException("Existing name.");
                if (directory.UncertainHeader && name == AnalysisPrivateNames.Catalog) throw new AnalysisPublicationUncertainException(name, new IOException("Injected header durability failure."));
                if (name.EndsWith(".fcdsa", StringComparison.Ordinal)) directory.AfterInventory?.Invoke();
            }
            public override void Dispose() => stream.Dispose();
        }
    }
}
