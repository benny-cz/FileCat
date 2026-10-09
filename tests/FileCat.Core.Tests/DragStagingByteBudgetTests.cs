using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class DragStagingByteBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void Healthy_small_staging_keeps_complete_read_only_copies() => Observe("healthy-small");
    [Fact]
    public void Healthy_exact_aggregate_cap_keeps_complete_read_only_copies() => Observe("healthy-exact");
    [Fact]
    public void Oversized_actual_member_is_refused_after_its_output_closes() => Observe("single-over");
    [Fact]
    public void Actual_bytes_across_members_share_the_staging_cap() => Observe("aggregate-over");
    [Fact]
    public void Read_failure_retires_the_partial_copy_and_owned_source() => Observe("read-error");
    [Fact]
    public void Cancellation_retires_the_partial_copy_and_keeps_its_token() => Observe("cancel");

    void Observe(string mode)
    {
        string root = Directory.CreateTempSubdirectory("filecat-drag-budget-").FullName;
        try
        {
            long half = DragStaging.MaxBytes / 2;
            long[] lengths = mode switch
            {
                "healthy-exact" => [half, half],
                "single-over" => [DragStaging.MaxBytes + 1],
                "aggregate-over" => [half + 1, half + 1],
                "healthy-small" => [4096, 4096],
                _ => [256 * 1024],
            };
            long[] declared = mode switch
            {
                "single-over" => [DragStaging.MaxBytes],
                "aggregate-over" => [half, half],
                _ => lengths,
            };
            string source = Path.Combine(root, "owned-source.bin");
            WritePattern(source, lengths.Max()); string before = Hash(source);
            string staging = Path.Combine(root, "staging"); Directory.CreateDirectory(staging);
            using var cts = new CancellationTokenSource();
            var provider = new OwnedProvider(source, lengths, mode, cts);
            var registry = new ProviderRegistry(); registry.Register(provider);
            var location = new Location(Schemes.Archive, "", Location.FileSystem(source));
            var items = declared.Select((n, i) => new ItemRef(location, "owned-" + i + ".bin", EntryKind.File, n)).ToArray();
            IReadOnlyList<string>? paths = null; string? refusal = null; Exception? failure = null;
            try { (paths, refusal) = DragStaging.Stage(items, registry, new PortableFileOperations(), staging, cts.Token); }
            catch (Exception ex) { failure = ex; }
            string[] actual = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            var files = actual.Select(p => new
            {
                Path = p, Bytes = new FileInfo(p).Length, SHA256 = Hash(p),
                ExactPatternBytes = PatternExact(p), ExclusiveOpenAfterStage = Exclusive(p),
                ReadOnly = (File.GetAttributes(p) & FileAttributes.ReadOnly) != 0,
            }).ToArray();
            bool healthy = mode.StartsWith("healthy", StringComparison.Ordinal);
            bool sourceClosed = Exclusive(source);
            int directories = Directory.Exists(Path.Combine(staging, "drag")) ? Directory.GetDirectories(Path.Combine(staging, "drag")).Length : 0;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Mode = mode, ActualOwnedFixtureRoot = root, ActualSourcePath = source, ActualStagingRoot = staging,
                ControlledLocalProviderMetadataMismatch = mode is "single-over" or "aggregate-over",
                NoNativeArchiveDecoderOrGuiFailureIncidenceClaim = true,
                DeclaredLengths = declared, ActualSourceLengths = lengths,
                DeclaredAggregateBytes = declared.Sum(), ActualSourceAggregateBytes = lengths.Sum(),
                ActualReturnedPaths = paths, ActualRefusal = refusal, ActualErrorType = failure?.GetType().Name,
                ActualErrorMessage = failure?.Message,
                ActualCancellationTokenRetained = failure is OperationCanceledException cancelled && cancelled.CancellationToken == cts.Token,
                ActualRetainedFiles = files, ActualRetainedAggregateBytes = files.Sum(v => v.Bytes), ActualStagingSubfolderCount = directories,
                ActualSourceOpenCalls = provider.OpenCalls, ActualSourceDisposeCalls = provider.DisposeCalls,
                ActualSourceBytesRead = provider.BytesRead, ActualSourceExclusiveOpenAfterStage = sourceClosed,
                ActualInputBytes = new FileInfo(source).Length, ActualInputOriginalSHA256 = before, ActualInputFinalSHA256 = Hash(source),
                ActualInjectedReadError = provider.ReadError.Message, ExpectedAggregateCap = DragStaging.MaxBytes,
            }));
            Assert.True(sourceClosed); Assert.Equal(provider.OpenCalls, provider.DisposeCalls); Assert.Equal(before, Hash(source));
            if (healthy)
            {
                Assert.Null(failure); Assert.Null(refusal); Assert.NotNull(paths); Assert.Equal(lengths.Length, paths.Count);
                Assert.Equal(lengths.Sum(), files.Sum(v => v.Bytes)); Assert.True(files.Sum(v => v.Bytes) <= DragStaging.MaxBytes);
                Assert.All(files, v => { Assert.True(v.ExactPatternBytes); Assert.True(v.ExclusiveOpenAfterStage); Assert.True(v.ReadOnly); Assert.Equal(before, v.SHA256); });
            }
            else
            {
                Assert.Null(paths); Assert.Empty(files); Assert.Equal(0, directories);
                if (mode == "cancel") { var observedCancellation = Assert.IsAssignableFrom<OperationCanceledException>(failure); Assert.Equal(cts.Token, observedCancellation.CancellationToken); Assert.Null(refusal); }
                else { Assert.Null(failure); Assert.NotNull(refusal); Assert.Contains(mode == "read-error" ? provider.ReadError.Message : "larger than its listing", refusal, StringComparison.Ordinal); }
            }
        }
        finally
        {
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
    }

    static void WritePattern(string path, long length)
    {
        byte[] block = new byte[65536]; for (int i = 0; i < block.Length; i++) block[i] = (byte)(i * 29 + 7);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        while (length > 0) { int n = (int)Math.Min(length, block.Length); stream.Write(block, 0, n); length -= n; }
    }
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    static bool Exclusive(string path) { try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; } catch (IOException) { return false; } }
    static bool PatternExact(string path)
    {
        using var stream = File.OpenRead(path); byte[] buffer = new byte[65536]; long at = 0; int n;
        while ((n = stream.Read(buffer)) > 0) { for (int i = 0; i < n; i++) if (buffer[i] != (byte)((at + i) * 29 + 7)) return false; at += n; }
        return true;
    }
    sealed class OwnedProvider(string path, long[] lengths, string mode, CancellationTokenSource cts) : ResourceProvider
    {
        public int OpenCalls, DisposeCalls; public long BytesRead;
        public IOException ReadError { get; } = new("owned-drag-read-failure");
        public override string Scheme => Schemes.Archive;
        public override string GetDisplayPath(Location location) => "owned staging control";
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource OpenContent(ItemRef item) => new OwnedWindow(this, new FileContentSource(path), lengths[OpenCalls++], mode, cts);
        sealed class OwnedWindow(OwnedProvider owner, FileContentSource source, long length, string mode, CancellationTokenSource cts) : IContentSource
        {
            bool _disposed;
            public string DisplayName => "owned native-file window";
            public long Length => length; public bool CanSeek => true; public string? LocalPath => null;
            public ContentRevision? GetRevision() => new(length, 0);
            public int Read(long offset, Span<byte> buffer)
            {
                if (offset >= 65536 && mode == "read-error") throw owner.ReadError;
                if (offset >= 65536 && mode == "cancel") cts.Cancel();
                if (offset >= length) return 0;
                int n = source.Read(offset, buffer[..(int)Math.Min(65536, Math.Min(buffer.Length, length - offset))]); owner.BytesRead += n; return n;
            }
            public void Dispose() { if (_disposed) return; _disposed = true; owner.DisposeCalls++; source.Dispose(); }
        }
    }
}
