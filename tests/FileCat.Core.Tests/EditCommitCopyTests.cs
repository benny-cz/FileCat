using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class EditCommitCopyTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();
    private object Prepare(EditSessionStore store, EditSessionRecord record, string temp, CancellationToken ct = default)
    {
        var method = typeof(EditSessionStore).GetMethod("PrepareCommit"); Assert.NotNull(method);
        try { return method.Invoke(store, [record, temp, ct])!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    private static T Value<T>(object copy, string name) => (T)copy.GetType().GetProperty(name)!.GetValue(copy)!;

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(2097153)]
    public void Frozen_commit_bytes_hash_crc_time_and_origin_survive_later_editor_saves(int size)
    {
        var ops = new Marks(); var store = new EditSessionStore(Path.Join(_dir.Path, "sessions"), ops);
        string working = _dir.File("working.txt", ""); byte[] bytes = Enumerable.Range(0, size).Select(i => (byte)(i * 31)).ToArray(); File.WriteAllBytes(working, bytes);
        DateTime time = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc); File.SetLastWriteTimeUtc(working, time); ops.Values[working] = "owned origin";
        var record = new EditSessionRecord { WorkingPath = working }; string temp = Path.Join(_dir.Path, "temp");
        var copy = Prepare(store, record, temp); string path = Value<string>(copy, "Path");
        try
        {
            File.WriteAllText(working, "new editor contents");
            Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), Value<string>(copy, "Sha256"));
            Assert.Equal(Crc32.Append(0, bytes), Value<uint>(copy, "Crc32")); Assert.Equal(size, Value<long>(copy, "Length")); Assert.Equal(time, File.GetLastWriteTimeUtc(path)); Assert.Equal("owned origin", ops.Values[path]);
            output.WriteLine("EDIT_COMMIT_COPY " + JsonSerializer.Serialize(new { size, hash = Value<string>(copy, "Sha256"), crc = Value<uint>(copy, "Crc32"), LaterWorkingBytesPreserved = true, NativeOrPhysicalSource = false }));
        }
        finally { ((IDisposable)copy).Dispose(); ((IDisposable)copy).Dispose(); }
        Assert.False(Directory.Exists(Path.GetDirectoryName(path))); Assert.Equal("new editor contents", File.ReadAllText(working));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("oversize")]
    [InlineData("cancel")]
    public void Refused_capture_removes_only_its_unpublished_snapshot(string action)
    {
        var store = new EditSessionStore(Path.Join(_dir.Path, "sessions"), new PortableFileOperations()); string path = _dir.File("working.txt", "mine");
        if (action == "missing") File.Delete(path);
        if (action == "oversize") { using var stream = File.OpenWrite(path); stream.SetLength(EditSessionStore.MaxMemberBytes + 1); }
        string temp = Directory.CreateDirectory(Path.Join(_dir.Path, "temp")).FullName; string retained = Path.Join(temp, "retained.txt"); File.WriteAllText(retained, "unrelated owned data");
        Assert.NotNull(typeof(EditSessionStore).GetMethod("PrepareCommit"));
        using var stop = new CancellationTokenSource(); if (action == "cancel") stop.Cancel();
        Assert.ThrowsAny<Exception>(() => Prepare(store, new EditSessionRecord { WorkingPath = path }, temp, stop.Token));
        Assert.Equal([retained], Directory.GetFileSystemEntries(temp)); Assert.Equal("unrelated owned data", File.ReadAllText(retained));
        output.WriteLine("EDIT_COMMIT_COPY " + JsonSerializer.Serialize(new { action, UnrelatedFilePreserved = true, NoUnpublishedSnapshot = true }));
    }

    [Fact]
    public async Task A_committed_archive_baseline_describes_written_bytes_even_when_the_editor_has_saved_again()
    {
        string archive = Path.Join(_dir.Path, "owned.zip"); using (var z = ZipFile.Open(archive, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("notes.txt").Open()); w.Write("base"); }
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); var zip = new ZipProvider(Path.Join(_dir.Path, "zip-temp")); providers.Register(zip);
        var fs = new PortableFileOperations(); var store = new EditSessionStore(Path.Join(_dir.Path, "sessions"), fs);
        var session = store.Create(zip, new ItemRef(zip.GetContainerLocation(archive)!, "notes.txt", EntryKind.File)); zip.Release(archive);
        File.WriteAllText(session.WorkingPath, "mine"); string sha = EditSessionStore.Hash(session.WorkingPath);
        var jobs = new JobManager(fs, providers, Path.Join(_dir.Path, "journal")); var job = jobs.Submit(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = store.CommitPlan(session, false) });
        while (!job.State.IsFinished()) await Task.Delay(10, TestContext.Current.CancellationToken); Assert.Equal(JobState.Completed, job.State);
        File.WriteAllText(session.WorkingPath, "later editor changes"); var saved = store.Committed(session, sha);
        output.WriteLine("EDIT_COMMIT_COPY " + JsonSerializer.Serialize(new { saved.MemberLength, saved.MemberCrc32, LaterWorkingBytesPreserved = true }));
        Assert.Equal(4, saved.MemberLength); Assert.Equal(Crc32.Append(0, "mine"u8), saved.MemberCrc32); Assert.Equal(EditState.Modified, store.StateOf(saved)); Assert.Equal("later editor changes", File.ReadAllText(saved.WorkingPath));
    }
    private sealed class Marks : PortableFileOperations
    {
        public readonly Dictionary<string, string> Values = new();
        public override string? ReadOriginMark(string path) => Values.GetValueOrDefault(path);
        public override bool WriteOriginMark(string path, string mark) { Values[path] = mark; return true; }
    }
}
