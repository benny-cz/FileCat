using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class DragStagingDeclaredBudgetTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases => new[] { "empty", "zero", "small", "exact-single", "exact-pair", "one-over", "unknown", "maximum-single", "maximum-plus-one", "two-maximum", "overflow-before-unknown", "unknown-before-overflow", "maximum-cap-overflow", "hundred-maximum" }.Select(n => new object[] { n });

    [Theory, MemberData(nameof(Cases))]
    public void Declared_sizes_refuse_before_opening_without_overflow(string mode)
    {
        long[] declared = mode switch
        {
            "empty" => [], "zero" => [0], "small" => [257], "exact-single" => [DragStaging.MaxBytes],
            "exact-pair" => [DragStaging.MaxBytes / 2, DragStaging.MaxBytes / 2],
            "one-over" => [DragStaging.MaxBytes + 1], "unknown" => [-1], "maximum-single" => [long.MaxValue],
            "maximum-plus-one" => [long.MaxValue, 1], "two-maximum" => [long.MaxValue, long.MaxValue],
            "overflow-before-unknown" => [long.MaxValue, 1, -1], "unknown-before-overflow" => [-1, long.MaxValue, 1],
            "maximum-cap-overflow" => [long.MaxValue, DragStaging.MaxBytes + 1],
            "hundred-maximum" => Enumerable.Repeat(long.MaxValue, DragStaging.MaxItems).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        bool refuse = mode is not ("empty" or "zero" or "small" or "exact-single" or "exact-pair");
        string root = Path.Combine(Path.GetTempPath(), "filecat-declared-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string input = Path.Combine(root, "source.bin"), stage = Path.Combine(root, "stage");
        byte[] bytes = Enumerable.Range(0, 257).Select(n => (byte)(n * 43 + 17)).ToArray();
        File.WriteAllBytes(input, bytes);
        string before = Hash(input);
        var provider = new OwnedProvider(input); var registry = new ProviderRegistry(); registry.Register(provider);
        var location = new Location(Schemes.Archive, "", Location.FileSystem(input));
        var items = declared.Select((length, i) => new ItemRef(location, "member-" + i + ".bin", EntryKind.File, length)).ToArray();
        object? observation = null;
        try
        {
            IReadOnlyList<string>? paths = null; string? refusal = null; Exception? error = null;
            try { (paths, refusal) = DragStaging.Stage(items, registry, new PortableFileOperations(), stage, TestContext.Current.CancellationToken); }
            catch (Exception ex) { error = ex; }
            var files = (Directory.Exists(stage) ? Directory.GetFiles(stage, "*", SearchOption.AllDirectories) : []).Select(p => new
            {
                Path = p, Bytes = new FileInfo(p).Length, SHA256 = Hash(p), ExactPatternBytes = File.ReadAllBytes(p).SequenceEqual(bytes),
                ActualExclusiveOpenAfterStage = Exclusive(p), ActualReadOnly = (File.GetAttributes(p) & FileAttributes.ReadOnly) != 0,
            }).ToArray();
            observation = new
            {
                Group = "drag-staging-declared-preflight", Case = mode, DeclaredLengths = declared, ExpectedRefusal = refuse,
                DeclaredNonnegativeSum = declared.Aggregate(0m, (sum, n) => sum + Math.Max(0, n)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ControlledListingMetadataNotNativeArchiveOrGuiIncidence = true,
                ActualLoadedCoreSHA256 = Hash(typeof(DragStaging).Assembly.Location), ActualReturnedPaths = paths, ActualRefusal = refusal,
                ActualErrorType = error?.GetType().Name, ActualErrorMessage = error?.Message,
                ActualRetainedFiles = files, ActualSourceOpenCalls = provider.OpenCalls, ActualSourceDisposeCalls = provider.DisposeCalls,
                ActualSourceBytesRead = provider.BytesRead, ActualSourceExclusiveOpenAfterStage = Exclusive(input),
                ActualInputUnchanged = Hash(input) == before, ActualInputSHA256 = before, ActualInputBytes = bytes.Length,
                ExpectedAggregateCap = DragStaging.MaxBytes, ActualOwnedFixtureRoot = root,
            };
            Assert.Null(error); Assert.Equal(before, Hash(input)); Assert.True(Exclusive(input));
            Assert.Equal(provider.OpenCalls, provider.DisposeCalls);
            if (refuse)
            {
                Assert.Null(paths); Assert.NotNull(refusal); Assert.Contains("64 MiB", refusal);
                Assert.Equal(0, provider.OpenCalls); Assert.Equal(0, provider.BytesRead); Assert.Empty(files);
                Assert.False(Directory.Exists(stage));
            }
            else if (declared.Length == 0)
            {
                Assert.Null(paths); Assert.Null(refusal); Assert.Empty(files); Assert.Equal(0, provider.OpenCalls);
            }
            else
            {
                Assert.Null(refusal); Assert.NotNull(paths); Assert.Equal(declared.Length, paths.Count);
                Assert.Equal(declared.Length, provider.OpenCalls); Assert.Equal(declared.Length * 257L, provider.BytesRead);
                Assert.Equal(declared.Length, files.Length);
                Assert.All(files, f => { Assert.Equal(257, f.Bytes); Assert.Equal(before, f.SHA256); Assert.True(f.ExactPatternBytes); Assert.True(f.ActualReadOnly); Assert.True(f.ActualExclusiveOpenAfterStage); });
            }
        }
        finally
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
            output.WriteLine(JsonSerializer.Serialize(new { Group = "drag-staging-declared-preflight", Observation = observation, ActualOwnedFixtureRootRemoved = !Directory.Exists(root) }));
        }
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static bool Exclusive(string path) { try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; } catch (IOException) { return false; } }
    private sealed class OwnedProvider(string path) : ResourceProvider
    {
        public int OpenCalls, DisposeCalls; public long BytesRead;
        public override string Scheme => Schemes.Archive;
        public override string GetDisplayPath(Location location) => "owned declared-size boundary";
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource OpenContent(ItemRef item) { OpenCalls++; return new Source(this, path); }
        private sealed class Source(OwnedProvider owner, string path) : IContentSource
        {
            private readonly FileContentSource file = new(path); private bool disposed;
            public string DisplayName => "owned native source";
            public long Length => file.Length; public bool CanSeek => true; public string? LocalPath => null;
            public ContentRevision? GetRevision() => file.GetRevision();
            public int Read(long offset, Span<byte> buffer) { int n = file.Read(offset, buffer); owner.BytesRead += n; return n; }
            public void Dispose() { if (disposed) return; disposed = true; owner.DisposeCalls++; file.Dispose(); }
        }
    }
}
