using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.App.ViewModels;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

public sealed class EditAdmissionRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, bool> Cases
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (string primary in new[] { "cancel", "invalid", "healthy" })
                foreach (string cleanup in new[] { "none", "io", "denied", "invalid" })
                    foreach (bool seek in new[] { false, true }) data.Add(primary, cleanup, seek);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Edit_admission_preserves_its_primary_error_and_retires_unpublished_content(string primary, string cleanup, bool seek)
    {
        string root = Directory.CreateTempSubdirectory("fc-edit-admission-retirement-").FullName;
        string path = Path.Combine(root, "owned.dat");
        File.WriteAllText(path, "owned edit admission bytes\n");
        string before = Hash(path);
        using var token = new CancellationTokenSource();
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromMinutes(1), threadsPerDevice: 1);
        Exception? primaryError = primary == "cancel" ? new OperationCanceledException("Owned edit admission cancelled", token.Token)
            : primary == "invalid" ? new InvalidOperationException("Owned edit admission check failed") : null;
        Exception? closeError = cleanup switch { "io" => new IOException("Owned close failure"), "denied" => new UnauthorizedAccessException("Owned close failure"), "invalid" => new InvalidOperationException("Owned close failure"), _ => null };
        var provider = new Provider(path, seek, closeError, () => { if (primary == "cancel") token.Cancel(); });
        int checks = 0;
        void Check()
        {
            checks++;
            if (checks != 2 || primaryError is null) return;
            if (primary == "cancel") Assert.True(token.IsCancellationRequested);
            throw primaryError;
        }
        var type = typeof(MainViewModel).GetNestedType("EditPreparationProvider", BindingFlags.NonPublic)!;
        var wrapper = (ResourceProvider)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [provider, (Action)Check], culture: null)!;
        IContentSource? published = null;
        Exception? actual = null;
        bool blockedWhilePublished = false;
        string? actualBytes = null;
        try
        {
            try
            {
                await io.Run(provider.Scheme, IoPriority.Normal, _ =>
                {
                    published = wrapper.OpenContent(new ItemRef(new Location(provider.Scheme, root), "owned.dat", EntryKind.File));
                    Assert.NotNull(published);
                    blockedWhilePublished = !Exclusive(path);
                    Assert.Equal(seek, published.CanSeek);
                    Assert.Equal(path, published.LocalPath);
                    var bytes = new byte[checked((int)published.Length)];
                    Assert.Equal(bytes.Length, published.Read(0, bytes));
                    actualBytes = Convert.ToHexStringLower(SHA256.HashData(bytes));
                    published.Dispose();
                }).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex) { actual = ex; }
            var source = Assert.Single(provider.Sources);
            bool released = Exclusive(path);
            int other = await io.Run("owned-edit-admission-other", IoPriority.Normal, _ => 42).WaitAsync(TimeSpan.FromSeconds(5));
            Exception? expected = primaryError ?? closeError;
            bool retained = ReferenceEquals(expected, actual);
            output.WriteLine("EDIT_ADMISSION_RETIREMENT " + JsonSerializer.Serialize(new
            {
                primary, cleanup, seek, Checks = checks, PrimaryObjectRetained = retained,
                ActualType = actual?.GetType().Name, ErrorStack = actual?.StackTrace,
                CancellationTokenRetained = primary != "cancel" || actual is OperationCanceledException e && e.CancellationToken == token.Token,
                Published = published is not null, BlockedWhilePublished = blockedWhilePublished, ActualBytes = actualBytes,
                source.Closes, Released = released, Before = before, After = Hash(path), SourcePath = path,
                Calls = provider.Calls.ToArray(), OtherWorker = other, ActualOwnedReadOnlyFileStream = true,
                PrivateAdmissionBoundaryOnly = true, NativeDesktopEditorOrProviderFaultIncidenceOrCandidateQualified = false,
            }));
            Assert.True(retained);
            Assert.Equal(primary == "healthy", published is not null);
            Assert.Equal(1, source.Closes);
            Assert.True(released);
            Assert.Equal(before, Hash(path));
            Assert.All(provider.Calls, c => Assert.StartsWith("FileCat I/O ", c.Thread));
            if (primary == "healthy") { Assert.True(blockedWhilePublished); Assert.Equal(before, actualBytes); }
            else Assert.Contains("Check", actual!.StackTrace);
        }
        finally
        {
            foreach (var source in provider.Sources) source.Cleanup();
            string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(temp, Path.GetFullPath(root));
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool Exclusive(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private sealed record Call(string Operation, string Thread);
    private sealed class Provider(string path, bool seek, Exception? closeError, Action opened) : ResourceProvider
    {
        public List<Source> Sources { get; } = [];
        public List<Call> Calls { get; } = [];
        public override string Scheme => "ownededitadmission";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public void Record(string operation) => Calls.Add(new(operation, Thread.CurrentThread.Name ?? ""));
        public override IContentSource OpenContent(ItemRef item)
        {
            Record("open"); var source = new Source(path, seek, this, closeError); Sources.Add(source); opened(); return source;
        }
    }
    private sealed class Source(string path, bool seek, Provider provider, Exception? closeError) : IContentSource
    {
        private readonly FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        private bool closed;
        public int Closes;
        public string DisplayName => path;
        public string? LocalPath => path;
        public bool CanSeek => seek;
        public long Length => stream.Length;
        public ContentRevision? GetRevision() => new(stream.Length, 1);
        public int Read(long offset, Span<byte> buffer) { provider.Record("read"); stream.Position = offset; return stream.Read(buffer); }
        public void Dispose() { provider.Record("close"); Closes++; Cleanup(); if (closeError is not null) throw closeError; }
        public void Cleanup() { if (closed) return; closed = true; stream.Dispose(); }
    }
}
