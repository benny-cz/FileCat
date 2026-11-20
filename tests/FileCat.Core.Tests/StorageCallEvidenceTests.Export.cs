using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed partial class StorageCallEvidenceTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("corrupt")]
    [InlineData("changed-during-copy")]
    [InlineData("cancel-before-create")]
    [InlineData("cancel-after-create")]
    [InlineData("publication-collision")]
    public void Bound_export_only_publishes_complete_verified_bytes_and_retires_both_owners(string scenario)
    {
        using var temp = new TempDir(); using var data = new MemoryStream(); var fixture = Create(temp, data);
        using var writer = fixture.Writer; for (int i = 1; i <= 70; i++) Call(writer, i);
        var prefix = Prefix(writer); byte[] expected = data.ToArray(), input = expected.ToArray();
        if (scenario == "corrupt") input[^12] ^= 1;
        var owner = new ExportHistory(input); using var stop = new CancellationTokenSource();
        if (scenario == "cancel-before-create") stop.Cancel();
        owner.Creating = output =>
        {
            if (scenario == "changed-during-copy") { owner.Bytes.Position = 0; owner.Bytes.WriteByte((byte)'!'); }
            if (scenario == "cancel-after-create") stop.Cancel();
            output.Collision = scenario == "publication-collision";
        };
        var backend = new StorageEvidenceBackend((_, _, _) => throw new NotSupportedException(), (_, _) => throw new NotSupportedException(),
            (_, _, _) => throw new NotSupportedException(), (_, _, _) => throw new NotSupportedException(), (_, _, _) => owner);
        StorageExportResult? result = null;
        var error = Record.Exception(() => result = JournalRecovery.ExportStorageCalls(fixture.Path, prefix, @"C:\exports\snapshot.fcj", backend, stop.Token));
        if (scenario == "success")
        {
            Assert.Null(error); Assert.Equal(@"C:\exports\snapshot.fcj", result!.Destination);
            Assert.True(owner.Output!.Published); Assert.Equal(expected, owner.Output.Bytes.ToArray());
        }
        else
        {
            Assert.NotNull(error);
            if (scenario.StartsWith("cancel-", StringComparison.Ordinal)) Assert.IsAssignableFrom<OperationCanceledException>(error);
            else Assert.IsAssignableFrom<IOException>(error);
            Assert.Null(result); Assert.False(owner.Output?.Published ?? false);
        }
        if (scenario is "corrupt" or "cancel-before-create") Assert.Null(owner.Output);
        if (owner.Output is { } created) Assert.True(created.Disposed);
        Assert.True(owner.Disposed);
    }

    private sealed class ExportHistory(byte[] bytes) : StorageHistoryEvidence
    {
        internal readonly MemoryStream Bytes = new(bytes, writable: true);
        internal ExportOutput? Output;
        internal Action<ExportOutput>? Creating;
        internal bool Disposed;
        internal override string DirectoryIdentity => "bound-private-directory";
        internal override string Identity => "bound-journal";
        internal override Stream Stream => Bytes;
        internal override StorageEvidenceExport CreateExport(string destination, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Output = new(destination); Creating?.Invoke(Output); return Output;
        }
        public override void Dispose() { if (Output is not null) Assert.True(Output.Disposed); Disposed = true; Bytes.Dispose(); }
    }

    private sealed class ExportOutput(string destination) : StorageEvidenceExport
    {
        internal readonly MemoryStream Bytes = new();
        internal bool Published, Disposed, Collision;
        internal override Stream Stream => Bytes;
        internal override StorageExportResult Publish(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Collision) throw new IOException("A competing destination already exists.");
            Published = true; return new(destination, null);
        }
        public override void Dispose() { Disposed = true; Bytes.Dispose(); }
    }
}
