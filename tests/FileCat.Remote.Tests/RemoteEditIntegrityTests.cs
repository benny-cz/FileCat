using System.Text.Json;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class RemoteEditIntegrityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("length")]
    [InlineData("time")]
    [InlineData("gone")]
    [InlineData("directory")]
    public void An_open_source_queries_the_current_remote_revision_instead_of_repeating_its_opening_stat(string change)
    {
        using var f = new Fixture(); using var source = f.Open(); var before = source.GetRevision();
        f.Change(change); var after = source.GetRevision();
        Emit(change, "revision", new { before, after });
        if (change is "gone" or "directory") Assert.Null(after);
        else { Assert.NotNull(after); Assert.NotEqual(before, after); }
    }

    [Theory]
    [InlineData("length")]
    [InlineData("time")]
    [InlineData("gone")]
    [InlineData("directory")]
    public void A_change_during_a_remote_copy_refuses_the_edit_and_removes_its_unpublished_files(string change)
    {
        using var f = new Fixture(); using var original = f.Open(); var baseline = original.GetRevision()!.Value;
        using var source = new MutatingSource(original, () => f.Change(change));
        var error = Record.Exception(() => f.Store.CreateRemote(f.Profile.Id, f.Profile.Display, "/owned/notes.txt", source, baseline, null));
        Emit(change, "copy", new { accepted = error is null, error = error?.GetType().Name, source.Reads });
        Assert.IsAssignableFrom<IOException>(error); Assert.Empty(f.Store.LoadAll());
        if (Directory.Exists(f.Store.Root)) Assert.Empty(Directory.EnumerateFileSystemEntries(f.Store.Root));
    }

    private void Emit(string change, string control, object detail) => output.WriteLine("EDIT_REMOTE_INTEGRITY " + JsonSerializer.Serialize(new
    { change, control, detail, InMemoryConnector = true, RealNetworkServer = false }));

    private sealed class MutatingSource(IContentSource inner, Action mutate) : IContentSource
    {
        public int Reads; public string DisplayName => inner.DisplayName; public long Length => inner.Length;
        public bool CanSeek => inner.CanSeek; public string? LocalPath => inner.LocalPath; public ContentRevision? GetRevision() => inner.GetRevision();
        public int Read(long offset, Span<byte> buffer) { int n = inner.Read(offset, buffer); if (++Reads == 1) mutate(); return n; }
        public void Dispose() { } // this adapter borrows the source; the outer fixture owns its lease
    }
    private sealed class Fixture : IDisposable
    {
        public readonly FakeSftpServer Server = new(); public readonly RemoteProfile Profile = new() { Host = "owned.invalid", User = "test" };
        public readonly EditSessionStore Store; private readonly SftpConnections _connections; private readonly SftpProvider _provider;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-remote-edit-integrity", Guid.NewGuid().ToString("N"));
        public Fixture()
        {
            Server.File("/owned/notes.txt", "owned bytes"); var ui = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce }; ui.Secrets.Enqueue("secret");
            _connections = new SftpConnections(id => id == Profile.Id ? Profile : null, new FakeConnector(Server), new HostKeyTrust(Path.Combine(_root, "known_hosts"), []), new SessionSecretStore(), ui);
            _provider = new SftpProvider(_connections, () => [Profile], p => p); Store = new EditSessionStore(Path.Combine(_root, "sessions"), new PortableFileOperations());
        }
        public IContentSource Open() => _provider.OpenContent(new ItemRef(SftpProvider.At(Profile, "/owned"), "notes.txt", EntryKind.File))!;
        public void Change(string change)
        {
            if (change == "length") Server.File("/owned/notes.txt", "different, longer bytes");
            else if (change == "time") Server.Dir("/owned").Children["notes.txt"].Modified = DateTime.UtcNow;
            else if (change == "directory") Server.Dir("/owned").Children["notes.txt"].IsDirectory = true;
            else Server.Dir("/owned").Children.Remove("notes.txt");
        }
        public void Dispose() { _connections.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
