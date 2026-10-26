using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class EditSessionSaveTests
{
    private sealed class RefusingMoves : PortableFileOperations
    {
        public int Refusals;
        public int Attempts;
        public bool Unauthorized;

        public override void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
        {
            Attempts++;
            if (Refusals-- > 0)
            {
                if (Unauthorized) throw new UnauthorizedAccessException("Test-owned transient destination lock");
                throw new IOException("Test-owned transient destination lock");
            }
            base.Move(source, destination, replaceExisting, writeThrough);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_transient_record_replacement_denial_does_not_lose_a_completed_remote_commit(bool unauthorized)
    {
        using var temp = new TempDir();
        var ops = new RefusingMoves { Unauthorized = unauthorized };
        var store = new EditSessionStore(Path.Combine(temp.Path, "sessions"), ops);
        string id = Guid.NewGuid().ToString("N");
        string folder = Directory.CreateDirectory(Path.Combine(store.Root, id)).FullName;
        string working = Path.Combine(folder, "remote.txt");
        File.WriteAllText(working, "old");
        var original = new EditSessionRecord
        {
            Id = id, Kind = EditSessionRecord.RemoteKind, ProfileId = "owned",
            RemotePath = "/remote.txt", WorkingPath = working,
            BaseSha256 = EditSessionStore.Hash(working),
            RemoteBaseline = new ContentRevision(3, 1),
        };
        store.Save(original);
        File.WriteAllText(working, "hello, edited");
        ops.Refusals = 1;
        int before = ops.Attempts;

        var committed = store.CommittedRemote(original, EditSessionStore.Hash(working),
            new ContentRevision(13, 2));

        Assert.Equal(2, ops.Attempts - before);
        Assert.NotNull(committed.LastCommitUtc);
        var reloaded = Assert.Single(store.LoadAll());
        Assert.Equal(committed.BaseSha256, reloaded.BaseSha256);
        Assert.Equal(committed.RemoteBaseline, reloaded.RemoteBaseline);
        Assert.Equal(EditState.Unchanged, store.StateOf(reloaded));
        Assert.Empty(Directory.EnumerateFiles(folder, "session.json.*.tmp"));
    }

    [Fact]
    public void A_persistent_record_replacement_denial_keeps_the_prior_baseline_and_no_temporary_records()
    {
        using var temp = new TempDir();
        var ops = new RefusingMoves { Unauthorized = true };
        var store = new EditSessionStore(Path.Combine(temp.Path, "sessions"), ops);
        string id = Guid.NewGuid().ToString("N");
        string folder = Directory.CreateDirectory(Path.Combine(store.Root, id)).FullName;
        string working = Path.Combine(folder, "remote.txt");
        File.WriteAllText(working, "old");
        var original = new EditSessionRecord
        {
            Id = id, Kind = EditSessionRecord.RemoteKind, ProfileId = "owned",
            RemotePath = "/remote.txt", WorkingPath = working,
            BaseSha256 = EditSessionStore.Hash(working),
            RemoteBaseline = new ContentRevision(3, 1),
        };
        store.Save(original);
        File.WriteAllText(working, "hello, edited");
        ops.Refusals = 6;
        int before = ops.Attempts;

        Assert.Throws<UnauthorizedAccessException>(() => store.CommittedRemote(original,
            EditSessionStore.Hash(working), new ContentRevision(13, 2)));

        Assert.Equal(6, ops.Attempts - before);
        var reloaded = Assert.Single(store.LoadAll());
        Assert.Null(reloaded.LastCommitUtc);
        Assert.Equal(original.BaseSha256, reloaded.BaseSha256);
        Assert.Equal(EditState.Modified, store.StateOf(reloaded));
        Assert.Empty(Directory.EnumerateFiles(folder, "session.json.*.tmp"));
    }
}
