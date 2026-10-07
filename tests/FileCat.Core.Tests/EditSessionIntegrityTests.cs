using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class EditSessionIntegrityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Duplicate_archive_members_cannot_create_an_ambiguous_write_back_session(int ordinal)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-edit-duplicates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string path = Path.Join(root, "owned.zip"); var zip = new ZipProvider(Path.Join(root, "tmp"));
        try
        {
            using (var catalog = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (string text in new[] { "first owned member", "second owned member" }) { using var w = new StreamWriter(catalog.CreateEntry("notes.txt").Open()); w.Write(text); }
            var before = SHA256.HashData(File.ReadAllBytes(path)); var store = new EditSessionStore(Path.Join(root, "sessions"), new PortableFileOperations());
            var error = Record.Exception(() => store.Create(zip, new ItemRef(zip.GetContainerLocation(path)!, "notes.txt", EntryKind.File) { Ordinal = ordinal }));
            output.WriteLine("EDIT_ARCHIVE_INTEGRITY " + JsonSerializer.Serialize(new { ordinal, accepted = error is null, error = error?.GetType().Name, SourceUnchanged = before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), OwnedZip = true }));
            Assert.IsAssignableFrom<IOException>(error); Assert.Empty(store.LoadAll());
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            if (Directory.Exists(store.Root)) Assert.Empty(Directory.EnumerateFileSystemEntries(store.Root));
        }
        finally { zip.Release(path); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("empty")]
    [InlineData("fragmented")]
    public void Complete_copies_keep_exact_bytes_and_leave_the_borrowed_source_open(string scenario)
    {
        using var f = new Fixture(scenario);
        var session = f.Create();
        var bytes = File.ReadAllBytes(session.WorkingPath);
        Emit(f, new { accepted = true, session.BaseSha256, actual = Convert.ToHexString(SHA256.HashData(bytes)) });
        Assert.Equal(f.Source.Bytes, bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), session.BaseSha256);
        Assert.Equal(EditState.Unchanged, f.Store.StateOf(session));
        Assert.Equal(session, Assert.Single(f.Store.LoadAll()));
        Assert.Equal(0, f.Source.Disposals);
        f.Store.Discard(session); Assert.Empty(Directory.EnumerateFileSystemEntries(f.Store.Root));
    }

    [Theory]
    [InlineData("early-eof")]
    [InlineData("negative-count")]
    [InlineData("large-count")]
    [InlineData("unknown-length")]
    [InlineData("over-limit")]
    [InlineData("revision-length")]
    [InlineData("missing-revision")]
    [InlineData("missing-final-revision")]
    [InlineData("changed-revision")]
    [InlineData("changed-native-id")]
    [InlineData("changed-length")]
    [InlineData("extra-byte")]
    [InlineData("partial-before")]
    [InlineData("partial-after")]
    [InlineData("caveat")]
    [InlineData("read-io")]
    [InlineData("read-access")]
    public void Uncertain_or_incomplete_sources_never_publish_a_session_or_leave_a_working_copy(string scenario)
    {
        using var f = new Fixture(scenario);
        var error = Record.Exception(() => f.Create());
        Emit(f, new { accepted = error is null, error = error?.GetType().Name, sessions = f.Store.LoadAll().Count });
        Assert.NotNull(error);
        Assert.True(error is IOException or UnauthorizedAccessException or InvalidDataException, error.ToString());
        Assert.Empty(f.Store.LoadAll());
        if (Directory.Exists(f.Store.Root)) Assert.Empty(Directory.EnumerateFileSystemEntries(f.Store.Root));
        Assert.Equal(0, f.Source.Disposals);
        if (scenario is "unknown-length" or "over-limit" or "revision-length" or "missing-revision" or "partial-before" or "caveat") Assert.Equal(0, f.Source.Reads);
        Assert.InRange(f.Source.Delivered, 0, 11);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_before_or_during_copy_cleans_only_the_unpublished_session(bool during)
    {
        using var f = new Fixture("ordinary"); using var stop = new CancellationTokenSource();
        if (during) f.Source.AfterRead = stop.Cancel; else stop.Cancel();
        var error = Record.Exception(() => f.Create(stop.Token));
        Emit(f, new { during, accepted = error is null, error = error?.GetType().Name });
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Empty(f.Store.LoadAll());
        if (Directory.Exists(f.Store.Root)) Assert.Empty(Directory.EnumerateFileSystemEntries(f.Store.Root));
        Assert.Equal(0, f.Source.Disposals);
        Assert.Equal(during ? 1 : 0, f.Source.Reads);
    }

    private void Emit(Fixture f, object detail) => output.WriteLine("EDIT_INTEGRITY " + JsonSerializer.Serialize(new
    {
        f.Scenario, f.Source.Reads, f.Source.Delivered, f.Source.Disposals, detail,
        SyntheticBorrowedSource = true, PhysicalOrNetworkSource = false,
    }));

    private sealed class Fixture : IDisposable
    {
        public readonly string Scenario; public readonly EditSessionStore Store; public readonly Source Source;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-edit-integrity", Guid.NewGuid().ToString("N"));
        public Fixture(string scenario) { Scenario = scenario; Source = new Source(scenario); Store = new EditSessionStore(_root, new PortableFileOperations()); }
        public EditSessionRecord Create(CancellationToken ct = default)
        {
            // The baseline has no cancellation parameter. Invoke its old signature unchanged, so the cancellation
            // controls reproduce the omission rather than failing to compile against the old producer.
            var method = typeof(EditSessionStore).GetMethods().Single(m => m.Name == "CreateRemote");
            object?[] args = ["owned-profile", "owned.example", "/owned/notes.txt", Source, Source.Expected, null];
            if (method.GetParameters().Length == 7) args = [.. args, ct];
            try { return (EditSessionRecord)method.Invoke(Store, args)!; }
            catch (System.Reflection.TargetInvocationException e) when (e.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private sealed class Source(string scenario) : IContentSource, IPartialContent
    {
        public byte[] Bytes { get; } = scenario == "empty" ? [] : "0123456789"u8.ToArray();
        public int Reads, Disposals, Delivered; public Action? AfterRead;
        public ContentRevision Expected => new(scenario == "revision-length" ? 9 : Bytes.Length, 123, "owned-id");
        public string DisplayName => "owned synthetic edit source";
        public bool CanSeek => true; public string? LocalPath => null;
        public long Length => scenario switch { "unknown-length" => -1, "over-limit" => EditSessionStore.MaxMemberBytes + 1, "changed-length" when Reads > 0 => 9, _ => Bytes.Length };
        public ContentRevision? GetRevision() => scenario switch
        {
            "missing-revision" => null,
            "missing-final-revision" when Reads > 0 => null,
            "changed-revision" when Reads > 0 => Expected with { ModifiedTicks = 124 },
            "changed-native-id" when Reads > 0 => Expected with { NativeId = "different-id" },
            _ => new ContentRevision(Length, 123, "owned-id"),
        };
        public IReadOnlyList<(long Offset, long Length)> MissingRanges => scenario == "partial-before" || scenario == "partial-after" && Reads > 0 ? [(2, 3)] : [];
        public string? Caveat => scenario == "caveat" ? "owned bytes may belong to another item" : null;
        public int Read(long offset, Span<byte> buffer)
        {
            Reads++;
            if (scenario == "read-io") throw new IOException("owned read failed");
            if (scenario == "read-access") throw new UnauthorizedAccessException("owned read denied");
            if (scenario == "negative-count") return -1;
            if (scenario == "large-count") return buffer.Length + 1;
            if (scenario == "early-eof" && offset >= 3) return 0;
            if (scenario == "extra-byte" && offset == Bytes.Length) { buffer[0] = 42; Delivered++; return 1; }
            if (offset >= Bytes.Length) return 0;
            int n = (int)Math.Min(buffer.Length, Bytes.Length - offset);
            if (scenario == "fragmented") n = Math.Min(n, 1);
            if (scenario == "early-eof") n = Math.Min(n, 3);
            Bytes.AsSpan((int)offset, n).CopyTo(buffer); Delivered += n; AfterRead?.Invoke(); return n;
        }
        public void Dispose() => Disposals++;
    }
}
