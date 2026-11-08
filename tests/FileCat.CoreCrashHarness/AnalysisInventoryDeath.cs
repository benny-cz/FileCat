using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

internal static class AnalysisInventoryDeath
{
    internal static async Task<int> RunAsync(string parent, string boundary)
    {
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        if (!Path.GetFileName(parent).StartsWith("inventory-death-", StringComparison.Ordinal) ||
            !Directory.Exists(parent) || Directory.EnumerateFileSystemEntries(parent).Any())
            throw new ArgumentException("An empty, generated inventory-death fixture directory is required.");
        var probe = new Probe(boundary); using var io = new DeviceIoScheduler(); string device = PathUtil.GetDeviceKey(parent);
        var directory = await AnalysisIo.Run(io, device, _ =>
        {
            var native = new BoundaryDirectory(ScratchScale.Directory(parent), probe);
            using var store = new AnalysisPrivateInventoryStore(native, leaveOpen: true); new AnalysisPrivateProfileState(store).LoadOrCreate();
            var catalog = new AnalysisSavedCatalog(store); probe.OldId = Guid.NewGuid();
            catalog.SaveNew("owned.fcdsa", Snapshot(), probe.OldId, AnalysisSavedView.Default);
            catalog.SaveNew("other.fcdsa", Snapshot(), Guid.NewGuid(), AnalysisSavedView.Default);
            using var index = catalog.Read(); probe.CatalogId = index.CatalogId!.Value;
            probe.OldSha = Digest(native, "owned.fcdsa"); probe.OtherSha = Digest(native, "other.fcdsa");
            native.Writes = 0; return native;
        });
        if (boundary is "stage-created" or "stage-flushed")
        {
            await AnalysisIo.Run(io, device, _ =>
            {
                using var stage = directory.CreateStaging();
                probe.Hit("stage-created"); stage.Stream.Write(new byte[128 * 1024]); stage.Flush(); probe.Hit("stage-flushed"); return true;
            });
            throw new InvalidOperationException("The owned death boundary was not reached.");
        }
        var owned = await OwnedAnalysisStore.OpenAsync(io, device, () => directory);
        var review = await owned.PrepareReplacementAsync(probe.OldId, Snapshot(), AnalysisSavedView.Default with { Chart = "Table" });
        probe.NewId = review.Incoming.InventoryId; probe.NewSha = review.Incoming.Sha256;
        probe.Hit("review-ready"); probe.Armed = true;
        var result = await owned.ReplaceAsync(review);
        if (result.State != OwnedAnalysisReplacementState.Replaced) throw new InvalidDataException("The child transaction did not finish normally.");
        await review.DisposeAsync(); await owned.DisposeAsync(); probe.Hit("completed");
        throw new InvalidOperationException("The requested owned death boundary was not reached: " + boundary);
    }

    private static AnalysisSnapshot Snapshot() => new(
        new(Guid.Parse("c71d692b-5046-490e-9d4d-8dfe08b5b596"), 1, 1, "death-fixture", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
        [new("r", "file", "Captured root", "Captured root", "fixture-device", IdentityAssurance.Verified, "Captured root")],
        [new(1, "r", 0, "Captured root", AnalysisKind.Directory), new(2, "r", 1, "data.bin", AnalysisKind.File, "object")],
        [new("object", "fixture-device:object", AnalysisMeasurement.Known(128), AnalysisMeasurement.Known(1))],
        [new("fact", "object", null, "primary", "fixture-v1", AnalysisMeasurement.Known(128), "fixture", DateTimeOffset.UnixEpoch, [])],
        [], new(), AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);
    private static string Digest(AnalysisPrivateDirectory directory, string name)
    { using var file = directory.OpenRead(name); return Convert.ToHexStringLower(SHA256.HashData(file.Stream)); }

    private sealed class Probe(string boundary)
    {
        internal Guid CatalogId, OldId, NewId;
        internal string? OldSha, NewSha, OtherSha;
        internal bool Armed;
        internal void Hit(string point)
        {
            if (point != boundary) return;
            Console.WriteLine("INVENTORY_DEATH_READY " + JsonSerializer.Serialize(new
            { pid = Environment.ProcessId, boundary, catalog = CatalogId, previous = OldId, incoming = NewId, previousSha256 = OldSha, incomingSha256 = NewSha, otherSha256 = OtherSha }));
            Console.Out.Flush();
            // This is deliberately an actual owned worker blocked at the boundary. The parent
            // kills this process, so no managed finally or Dispose can establish cleanup.
            using var never = new ManualResetEventSlim(); never.Wait(TimeSpan.FromMinutes(2));
            Environment.FailFast("The crash-test parent did not terminate its owned child.");
        }
    }

    private sealed class BoundaryDirectory(AnalysisPrivateDirectory inner, Probe probe) : AnalysisPrivateDirectory
    {
        internal int Writes;
        internal override void Verify() => inner.Verify();
        internal override (UInt128 Capacity, UInt128 Available) Space() => inner.Space();
        internal override string[] Entries(CancellationToken cancellation) => inner.Entries(cancellation);
        internal override AnalysisPrivateFile CreateStaging() => new File(this, inner.CreateStaging(), ++Writes);
        internal override AnalysisPrivateFile OpenRead(string name) => new File(this, inner.OpenRead(name), name);
        internal override AnalysisPrivateFile OpenMaintenance(string name) => new File(this, inner.OpenMaintenance(name), name);
        internal override AnalysisPrivateFile OpenReplacementFile(string name) => new File(this, inner.OpenReplacementFile(name), name);
        public override void Dispose() => inner.Dispose();
        private sealed class File : AnalysisPrivateFile
        {
            private readonly BoundaryDirectory _owner;
            private readonly AnalysisPrivateFile _inner;
            private readonly int _write;
            private string? _name;
            internal File(BoundaryDirectory owner, AnalysisPrivateFile inner, int write) { _owner = owner; _inner = inner; _write = write; }
            internal File(BoundaryDirectory owner, AnalysisPrivateFile inner, string name) { _owner = owner; _inner = inner; _name = name; }
            private void Hit(string point) { if (_ownerProbe.Armed) _ownerProbe.Hit(point); }
            private Probe _ownerProbe => _owner.Probe;
            public override Stream Stream => _inner.Stream;
            internal override string Identity => _inner.Identity;
            internal override string PublishedIdentity => _inner.PublishedIdentity;
            internal override string? MaintenanceName => _inner.MaintenanceName;
            internal override bool RemovalRequested => _inner.RemovalRequested;
            public override void Verify() => _inner.Verify();
            internal override bool TryLockCapacity() => _inner.TryLockCapacity();
            public override void Flush()
            {
                _inner.Flush();
                if (_write == 2) Hit("backup-flushed");
                if (_write == 3) Hit("catalog-flushed");
            }
            public override void PublishNew(string name)
            {
                _inner.PublishNew(name); _name = name;
                if (AnalysisPrivateNames.IsReplacementPayload(name)) Hit(name.StartsWith("incoming-", StringComparison.Ordinal) ? "incoming-published" : "backup-published");
                if (IsCurrent(name)) Hit("catalog-published");
            }
            private bool IsCurrent(string? name) => _ownerProbe.NewId != Guid.Empty && name == AnalysisPrivateNames.CatalogEntry(_ownerProbe.CatalogId, _ownerProbe.NewId);
            internal override void MoveOwnedNew(string name)
            { _inner.MoveOwnedNew(name); _name = name; if (AnalysisPrivateNames.IsPendingReplacement(name)) Hit("marker-moved"); }
            internal override void ReplaceOwned(AnalysisPrivateFile destination)
            {
                if (destination is not File target || !ReferenceEquals(_owner, target._owner)) throw new ArgumentException("Both wrapped leases must share this directory.");
                Hit("before-switch"); _inner.ReplaceOwned(target._inner); _name = _inner.MaintenanceName; Hit("after-switch");
            }
            internal override void RemoveOwned()
            {
                string? name = _name; _inner.RemoveOwned();
                if (name?.StartsWith("previous-", StringComparison.Ordinal) == true) Hit("previous-removed");
                if (name is not null && AnalysisPrivateNames.IsPendingReplacement(name)) Hit("marker-removed");
            }
            public override void Dispose()
            {
                _inner.Dispose();
                if (_write == 1 && _name?.StartsWith("incoming-", StringComparison.Ordinal) == true) Hit("incoming-closed");
                if (_write == 2 && _name?.StartsWith("previous-", StringComparison.Ordinal) == true) Hit("backup-closed");
                if (_write == 0 && _name == "owned.fcdsa" && !_inner.RemovalRequested) Hit("main-closed");
                if (IsCurrent(_name)) Hit("catalog-closed");
                if (_name == AnalysisPrivateNames.CatalogWriteLock) Hit("writer-closed");
            }
        }
        private Probe Probe => probe;
    }
}
