using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class AnalysisOwnedStoreTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");

    [Fact]
    public void CatalogLockCreationPreservesTheSpaceFailureWhenNoConcurrentWinnerExists()
    {
        Ordinary(); using var directory = new ProbeDirectory { LowSpace = true }; using var store = new AnalysisPrivateInventoryStore(directory);
        var failure = Assert.Throws<IOException>(() => store.OpenCatalogLock(Cancellation));
        Assert.Contains("Not enough private destination space", failure.Message); Assert.Empty(directory.Files);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("header")]
    public void ProfileAndCatalogCreationPreserveTheirOriginalFailureWithoutAConcurrentWinner(string control)
    {
        Ordinary(); using var directory = new ProbeDirectory(); using var store = new AnalysisPrivateInventoryStore(directory);
        if (control == "header") { using var writing = store.OpenCatalogLock(Cancellation); }
        directory.LowSpace = true;
        var failure = Assert.Throws<IOException>(() =>
        {
            if (control == "profile") new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
            else new AnalysisSavedCatalog(store).SaveNew("refused.fcdsa", AnalysisAccountingTests.Golden(), Guid.NewGuid(), AnalysisSavedView.Default, Cancellation);
        });
        Assert.Contains("Not enough private destination space", failure.Message);
        Assert.DoesNotContain(AnalysisPrivateNames.PrivateState, directory.Files.Keys); Assert.DoesNotContain(AnalysisPrivateNames.Catalog, directory.Files.Keys);
        Assert.DoesNotContain("refused.fcdsa", directory.Files.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectionStreamsEveryByteWithoutImportingAWholeGraphAndPreservesProvenance(bool privileged)
    {
        Ordinary(); using var directory = new ProbeDirectory(); using var io = new DeviceIoScheduler();
        var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with
        {
            Taint = privileged ? snapshot.Taint | AnalysisTaint.PrivilegedDerived : snapshot.Taint,
            Coverage = privileged ? NamespaceCoverage.Partial : snapshot.Coverage,
            Nodes = snapshot.Nodes.AddRange(Enumerable.Range(0, 1800).Select(i => new AnalysisNode((ulong)i + 7, "r", 1,
                "extra-" + i.ToString("D4") + new string('x', 160), AnalysisKind.File, "X")))
        };
        Guid id = Save(directory, snapshot); byte[] bytes = directory.Files["owned.fcdsa"].ToArray();
        Assert.True(bytes.Length > AnalysisLimits.BatchBytes);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "owned-test", () => directory, Cancellation);
        var inspected = await owned.InspectAsync(id, Cancellation);
        Assert.Equal(id, inspected.Entry.InventoryId); Assert.Equal(bytes.Length, inspected.Bytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), inspected.Sha256);
        Assert.Equal(snapshot.Identity.StartedUtc, inspected.StartedUtc); Assert.Equal(snapshot.Identity.ObservedUtc, inspected.ObservedUtc);
        Assert.Equal(snapshot.Coverage, inspected.Entry.Coverage); Assert.True(inspected.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        Assert.Equal(privileged, inspected.Taint.HasFlag(AnalysisTaint.PrivilegedDerived)); Assert.True(inspected.Records > (ulong)snapshot.Nodes.Length);
        Assert.True(directory.DataReads > 1); Assert.InRange(directory.LargestRead, 1, 64 * 1024); Assert.True(directory.DataClosed);
        Assert.Equal(bytes, directory.Files["owned.fcdsa"]);
        using var catalog = await owned.ReadCatalogAsync(Cancellation); Assert.Equal(inspected.Entry, Assert.Single(catalog.Entries));
        Assert.Throws<ArgumentException>(() => { _ = owned.InspectAsync(Guid.Empty, Cancellation); });
        await Assert.ThrowsAsync<FileNotFoundException>(() => owned.InspectAsync(Guid.NewGuid(), Cancellation));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("identity")]
    [InlineData("truncated")]
    [InlineData("extra")]
    [InlineData("future")]
    [InlineData("catalog-changed")]
    [InlineData("profile-changed")]
    public async Task InspectionRefusesChangedOrCorruptOwnedDataWithoutRepairingIt(string fault)
    {
        Ordinary(); using var directory = new ProbeDirectory(); using var io = new DeviceIoScheduler();
        var snapshot = AnalysisAccountingTests.Golden(); Guid id = Save(directory, snapshot);
        await using var owned = await OwnedAnalysisStore.OpenAsync(io, "owned-fault", () => directory, Cancellation);
        byte[] data = directory.Files["owned.fcdsa"];
        if (fault == "identity")
        {
            using var stream = new MemoryStream(); AnalysisInventoryFormat.Write(stream, snapshot, Guid.NewGuid(), Cancellation);
            directory.Files["owned.fcdsa"] = stream.ToArray(); Assert.Equal(data.Length, stream.Length);
        }
        else if (fault == "truncated") directory.Files["owned.fcdsa"] = data[..^1];
        else if (fault == "extra") directory.Files["owned.fcdsa"] = [.. data, (byte)'\n'];
        else if (fault == "future") directory.Files["owned.fcdsa"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(data).Replace("\"schema\":\"1\"", "\"schema\":\"9\"", StringComparison.Ordinal));
        else if (fault == "checksum")
        {
            string text = Encoding.UTF8.GetString(data); int at = text.LastIndexOf("\"sha256\":\"", StringComparison.Ordinal) + 10;
            Assert.True(at >= 10); var changed = text.ToCharArray(); changed[at] = changed[at] == '0' ? '1' : '0';
            directory.Files["owned.fcdsa"] = Encoding.UTF8.GetBytes(changed);
        }
        else directory.BeforeDataRead = () =>
        {
            directory.BeforeDataRead = null;
            if (fault == "catalog-changed") directory.Files[AnalysisPrivateNames.Catalog] = Encoding.UTF8.GetBytes("{\"schema\":\"9\",\"kind\":\"catalog\",\"catalog\":\"" + Guid.NewGuid().ToString("D") + "\"}");
            else directory.Files[AnalysisPrivateNames.PrivateState] = Encoding.UTF8.GetBytes("{\"schema\":\"1\",\"kind\":\"analysis-private-state\",\"profile\":\"" + Guid.NewGuid().ToString("D") + "\"}");
        };
        byte[] expected = directory.Files["owned.fcdsa"].ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => owned.InspectAsync(id, Cancellation));
        Assert.Equal(expected, directory.Files["owned.fcdsa"]); Assert.True(directory.DataClosed);
    }

    [Fact]
    public async Task FutureCatalogIsRefusedBeforeCreatingAnyProfileOrControlFile()
    {
        Ordinary(); using var directory = new ProbeDirectory(); using var io = new DeviceIoScheduler();
        byte[] original = Encoding.UTF8.GetBytes("{\"schema\":\"9\",\"kind\":\"catalog\",\"catalog\":\"" + Guid.NewGuid().ToString("D") + "\"}");
        directory.Files[AnalysisPrivateNames.Catalog] = original;
        await Assert.ThrowsAsync<InvalidDataException>(() => OwnedAnalysisStore.OpenAsync(io, "future-owned", () => directory, Cancellation));
        Assert.Equal(AnalysisPrivateNames.Catalog, Assert.Single(directory.Files).Key); Assert.Equal(original, directory.Files[AnalysisPrivateNames.Catalog]); Assert.True(directory.Closed);
    }

    [Fact]
    public async Task CanceledInspectionAndOwnerCloseWaitForTheActualOutstandingNativeRead()
    {
        Ordinary(); using var directory = new ProbeDirectory(); using var io = new DeviceIoScheduler();
        using var release = new ManualResetEventSlim(); using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        Guid id = Save(directory, AnalysisAccountingTests.Golden());
        var owned = await OwnedAnalysisStore.OpenAsync(io, "blocked-owned", () => directory, Cancellation);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        directory.BeforeDataRead = () => { directory.BeforeDataRead = null; entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(20)); };
        var inspecting = owned.InspectAsync(id, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); stop.Cancel(); var closing = owned.DisposeAsync().AsTask();
            Assert.False(inspecting.IsCompleted); Assert.False(closing.IsCompleted); Assert.False(directory.Closed); Assert.False(directory.DataClosed);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspecting); await closing.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            Assert.True(directory.Closed); Assert.True(directory.DataClosed);
        }
        finally { release.Set(); try { await inspecting; } catch (OperationCanceledException) { } await owned.DisposeAsync(); }
    }

    private static Guid Save(ProbeDirectory directory, AnalysisSnapshot snapshot)
    {
        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
        Guid id = Guid.NewGuid(); new AnalysisSavedCatalog(store).SaveNew("owned.fcdsa", snapshot, id, AnalysisSavedView.Default, Cancellation); return id;
    }
    private sealed class ProbeDirectory : AnalysisPrivateDirectory
    {
        private readonly AnalysisCatalogTests.MemoryDirectory _inner = new();
        internal Dictionary<string, byte[]> Files => _inner.Files;
        internal Action? BeforeDataRead;
        internal bool Closed, DataClosed;
        internal bool LowSpace;
        internal int DataReads, LargestRead;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => LowSpace ? (1UL << 40, 0) : _inner.Space();
        internal override AnalysisPrivateFile CreateStaging() => _inner.CreateStaging();
        internal override string[] Entries(CancellationToken cancellation) => _inner.Entries(cancellation);
        internal override AnalysisPrivateFile OpenRead(string name)
        {
            Verify(); var file = _inner.OpenRead(name);
            return name.EndsWith(".fcdsa", StringComparison.Ordinal) ? new ProbeFile(this, file) : file;
        }
        public override void Dispose() { Closed = true; _inner.Dispose(); }
        private sealed class ProbeFile(ProbeDirectory owner, AnalysisPrivateFile inner) : AnalysisPrivateFile
        {
            private readonly Stream _stream = new ProbeStream(owner, inner.Stream);
            public override Stream Stream => _stream;
            public override void Verify() { owner.Verify(); inner.Verify(); }
            public override void Flush() => inner.Flush();
            public override void PublishNew(string name) => throw new NotSupportedException();
            public override void Dispose() { owner.DataClosed = true; inner.Dispose(); }
        }
        private sealed class ProbeStream(ProbeDirectory owner, Stream inner) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
            public override int Read(Span<byte> buffer) { owner.DataReads++; owner.LargestRead = Math.Max(owner.LargestRead, buffer.Length); owner.BeforeDataRead?.Invoke(); return inner.Read(buffer); }
            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long length) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
