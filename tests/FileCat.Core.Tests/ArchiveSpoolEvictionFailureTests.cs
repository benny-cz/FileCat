using System.Collections;
using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveSpoolEvictionFailureTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (bool compressed in new[] { false, true })
        foreach (string fault in new[] { "none", "io", "denied", "disposed" }) yield return [compressed, fault];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void Evicting_a_nested_archive_retires_its_spool_even_when_the_index_close_fails(bool compressed, string fault)
    {
        string root = Directory.CreateTempSubdirectory("filecat-spool-retirement-").FullName;
        string outer = Path.Combine(root, compressed ? "owned.tar.gz" : "owned.tar");
        string temp = Path.Combine(root, "spools");
        byte[] expected = Enumerable.Range(0, 4096).Select(n => (byte)(n * 17 + 11)).ToArray();
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        var provider = new ArchiveProvider(temp, registry); registry.Register(provider);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var spools = (ConcurrentDictionary<string, (string Path, FileStream Holder, DateTime Used)>)typeof(ArchiveProvider).GetField("_spools", flags)!.GetValue(provider)!;
        var cache = (IDictionary)typeof(ArchiveProvider).GetField("_cache", flags)!.GetValue(provider)!;
        FileStream? evictedHolder = null;
        try
        {
            byte[] nested;
            using (var stream = new MemoryStream())
            {
                using (var writer = new TarWriter(stream, TarEntryFormat.Ustar, leaveOpen: true))
                using (var bytes = new MemoryStream(expected, writable: false))
                    writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = bytes, ModificationTime = DateTimeOffset.UnixEpoch });
                nested = stream.ToArray();
            }
            using (var stream = File.Create(outer))
            using (var gzip = compressed ? new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true) : null)
            using (var writer = new TarWriter(gzip is null ? stream : gzip, TarEntryFormat.Ustar, leaveOpen: true))
                for (int i = 0; i < 5; i++)
                {
                    using var bytes = new MemoryStream(nested, writable: false);
                    writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"nested-{i}.tar") { DataStream = bytes, ModificationTime = DateTimeOffset.UnixEpoch });
                }
            string outerHash = Hash(ReadShared(outer));
            Location Member(int n) => new(Schemes.Archive, $"nested-{n}.tar", Location.FileSystem(outer));
            Location Nested(int n) => new(Schemes.Archive, string.Empty, Member(n));
            byte[] ReadMember(int n)
            {
                using var content = provider.OpenContent(new ItemRef(Nested(n), "payload.bin", EntryKind.File))!;
                byte[] bytes = new byte[4096]; int offset = 0;
                while (offset < bytes.Length) { int count = content.Read(offset, bytes.AsSpan(offset)); if (count <= 0) break; offset += count; }
                Assert.Equal(bytes.Length, offset); return bytes;
            }
            string oldPath = provider.Spool(Member(0)); Assert.Equal(nested, ReadShared(oldPath));
            byte[] positive = ReadMember(0); Assert.Equal(expected, positive);
            object? oldIndex = null;
            var cachedEntries = cache.GetEnumerator();
            while (cachedEntries.MoveNext()) if (((string)cachedEntries.Key).StartsWith(oldPath + "|", StringComparison.OrdinalIgnoreCase)) oldIndex = cachedEntries.Value;
            Assert.NotNull(oldIndex);
            var readerField = oldIndex.GetType().GetField("_reader", flags)!;
            var actualReader = (IMemberReader)readerField.GetValue(oldIndex)!;
            var actualReaderSource = (FileStream)actualReader.GetType().GetField("_plain", flags)!.GetValue(actualReader)!;
            Exception? injected = fault switch { "none" => null, "io" => new IOException("owned spool eviction close failure"), "denied" => new UnauthorizedAccessException("owned spool eviction close failure"), "disposed" => new ObjectDisposedException("owned spool eviction close failure"), _ => throw new ArgumentOutOfRangeException(nameof(fault)) };
            var wrapper = new CloseReader(actualReader, injected); readerField.SetValue(oldIndex, wrapper);
            var oldest = spools.Values.Single(); evictedHolder = oldest.Holder;
            Assert.True(SpinWait.SpinUntil(() => DateTime.UtcNow > oldest.Used, 1000));
            for (int i = 1; i < 4; i++) Assert.Equal(nested, ReadShared(provider.Spool(Member(i))));
            var before = spools.Values.ToArray(); Assert.Equal(4, before.Length);
            bool actuallyOldest = before.Where(v => v.Path != oldPath).All(v => v.Used > oldest.Used); Assert.True(actuallyOldest);
            bool holderInitiallyOpen = !evictedHolder.SafeFileHandle.IsClosed && evictedHolder.CanRead;
            bool readerInitiallyOpen = !actualReaderSource.SafeFileHandle.IsClosed && actualReaderSource.CanRead;
            Exception? error = null;
            try { provider.Spool(Member(4)); } catch (Exception failure) { error = failure; }
            bool holderClosed = evictedHolder.SafeFileHandle.IsClosed && !evictedHolder.CanRead;
            bool readerClosed = actualReaderSource.SafeFileHandle.IsClosed && !actualReaderSource.CanRead;
            bool oldGone = !File.Exists(oldPath);
            var added = spools.Values.Single(v => before.All(p => p.Path != v.Path));
            bool newFullBytes = ReadShared(added.Path).SequenceEqual(nested);
            string retry = provider.Spool(Member(4)); byte[] recovered = ReadMember(4);
            bool unchanged = outerHash == Hash(ReadShared(outer));
            output.WriteLine(JsonSerializer.Serialize(new
            {
                CompressedOuter = compressed, Fault = fault, ActualOldestSpoolObserved = actuallyOldest,
                ActualNativeSpoolInitiallyOpen = holderInitiallyOpen, ActualNativeIndexSourceInitiallyOpen = readerInitiallyOpen,
                ActualNativeSpoolClosedBeforeFixtureCleanup = holderClosed, ActualNativeIndexSourceClosedBeforeFixtureCleanup = readerClosed,
                ActualEvictedSpoolPathGoneBeforeFixtureCleanup = oldGone, ActualOriginalCloseErrorObjectRetained = ReferenceEquals(error, injected),
                ActualErrorType = error?.GetType().FullName, ActualErrorMessage = error?.Message, ActualErrorStack = error?.StackTrace,
                ActualIndexCloseCalls = wrapper.Closes, ActualRetainedSpoolCount = spools.Count,
                ActualNewSpoolBytes = nested.Length, ActualNewSpoolSHA256 = Hash(ReadShared(added.Path)), ActualExpectedNestedSHA256 = Hash(nested), ActualNewSpoolAllBytesExact = newFullBytes,
                ActualRetryUsesSameNewSpool = retry == added.Path, ActualInitialMemberBytes = positive.Length, ActualInitialMemberSHA256 = Hash(positive),
                ActualRecoveredMemberBytes = recovered.Length, ActualRecoveredMemberSHA256 = Hash(recovered), ActualExpectedMemberSHA256 = Hash(expected),
                ActualWholeOuterSHA256 = outerHash, ActualWholeOuterUnchanged = unchanged,
                ActualOwnedFixtureRoot = root, ActualOwnedOuterPath = outer, ActualEvictedSpoolPath = oldPath, ActualNewSpoolPath = added.Path,
                ActualPublicSpoolAndRealTarGzipAndNestedTarDecodersUsed = true, ControlledIndexCloseErrorAfterActualSourceCloseOnly = true, NoNativeFailureOrPhysicalSourceClaim = true
            }));
            Assert.True(holderInitiallyOpen && readerInitiallyOpen && holderClosed && readerClosed && oldGone && unchanged && newFullBytes);
            Assert.Same(injected, error); Assert.Equal(1, wrapper.Closes); Assert.Equal(4, spools.Count);
            Assert.Equal(retry, added.Path); Assert.Equal(expected, recovered);
        }
        finally
        {
            foreach (string key in cache.Keys.Cast<string>().ToArray()) if (cache[key] is IDisposable index) try { index.Dispose(); } catch { }
            foreach (var spool in spools.Values) spool.Holder.Dispose();
            evictedHolder?.Dispose(); Directory.Delete(root, recursive: true);
        }
    }
    private static byte[] ReadShared(string path)
    {
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var destination = new MemoryStream(); source.CopyTo(destination); return destination.ToArray();
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private sealed class CloseReader(IMemberReader inner, Exception? error) : IMemberReader
    {
        public int Closes { get; private set; }
        public string Format => inner.Format;
        public bool SharesCursor => inner.SharesCursor;
        public IEnumerable<FileCat.Archives.MemberInfo> List(Action<string> warn, CancellationToken ct) => inner.List(warn, ct);
        public Stream Open(int index, CancellationToken ct) => inner.Open(index, ct);
        public void Dispose() { Closes++; inner.Dispose(); if (error is not null) throw error; }
    }
}
