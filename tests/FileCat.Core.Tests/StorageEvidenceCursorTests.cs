using FileCat.Core.Jobs;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class StorageEvidenceCursorTests
{
    [Fact]
    public void Changed_or_disappeared_namespace_cannot_become_a_successful_empty_census()
    {
        var cursor = new Cursor("replacement", []);
        var backend = Backend((_, _, _) => cursor);
        Assert.Throws<IOException>(() => StorageEvidenceCursor.Entries(backend, "history", false, "reviewed-root", TestContext.Current.CancellationToken).ToArray());
        Assert.True(cursor.Closed); Assert.Equal(0, cursor.Reads);
        backend = Backend((_, _, _) => throw new FileNotFoundException());
        Assert.Throws<FileNotFoundException>(() => StorageEvidenceCursor.Entries(backend, "history", false, "reviewed-root", TestContext.Current.CancellationToken).ToArray());
        Assert.Empty(StorageEvidenceCursor.Entries(backend, "history", false, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Incremental_consumer_and_cancellation_close_the_bound_cursor_without_reading_the_remainder()
    {
        var cursor = new Cursor("root", [new("first", "id-1", 7), new("second", "id-2", 8)]);
        var backend = Backend((_, _, _) => cursor);
        Assert.Single(StorageEvidenceCursor.Entries(backend, "history", false, "root", TestContext.Current.CancellationToken).Take(1));
        Assert.True(cursor.Closed); Assert.Equal(1, cursor.Reads);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cursor = new Cursor("root", []); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => StorageEvidenceCursor.Entries(backend, "history", true, "root", stop.Token).ToArray());
        Assert.True(cursor.Closed); Assert.Equal(0, cursor.Reads);
    }

    [Theory]
    [InlineData("root", "entry-id", false)]
    [InlineData("replacement-root", "entry-id", true)]
    [InlineData("root", "replacement-file", true)]
    public void Qualified_header_pages_require_both_native_namespace_and_entry_identity(string directoryIdentity, string fileIdentity, bool damaged)
    {
        Guid id = Guid.NewGuid(); string leaf = StorageJournalPolicy.FileName(DateTime.UnixEpoch, id);
        var observation = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(7, "source"), false, null);
        var header = new StorageJournalHeader(id, DateTime.UnixEpoch, StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid(),
            [new(0, 0, @"C:\source\file", observation)]);
        byte[] bytes = StorageTransformJournalWriter.Encode("storage-begin", 1, id, header);
        var cursor = new Cursor("root", [new(leaf, "entry-id", bytes.Length)]);
        var history = new History(directoryIdentity, fileIdentity, bytes);
        var backend = Backend((_, _, _) => cursor) with { OpenHistory = (_, _, _) => history };
        using var discovery = JournalRecovery.OpenStorageHistory("history", backend, TestContext.Current.CancellationToken);
        var page = discovery.ReadPage(TestContext.Current.CancellationToken);
        var row = Assert.Single(page.Rows);
        Assert.Equal(damaged, row.Status.Contains("damaged", StringComparison.Ordinal));
        Assert.True(history.Closed); Assert.False(page.More); Assert.Equal(1, page.Examined);
        discovery.Dispose(); Assert.True(cursor.Closed);
    }

    private static StorageEvidenceBackend Backend(Func<string, bool, CancellationToken, StorageEvidenceCursor> open) => new(
        (_, _, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException(),
        (_, _, _) => throw new InvalidOperationException(), open);

    private sealed class Cursor(string identity, IReadOnlyList<StorageReviewArtifactInfo> batch) : StorageEvidenceCursor
    {
        internal bool Closed;
        internal int Reads;
        internal override string DirectoryIdentity => identity;
        internal override IReadOnlyList<StorageReviewArtifactInfo> ReadBatch(CancellationToken ct) => Reads++ == 0 ? batch : [];
        public override void Dispose() => Closed = true;
    }

    private sealed class History(string directory, string identity, byte[] bytes) : StorageHistoryEvidence
    {
        private readonly MemoryStream _stream = new(bytes);
        internal bool Closed;
        internal override string DirectoryIdentity => directory;
        internal override string Identity => identity;
        internal override Stream Stream => _stream;
        public override void Dispose() { Closed = true; _stream.Dispose(); }
    }
}
