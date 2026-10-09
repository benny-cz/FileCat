using System.Collections;
using System.Formats.Tar;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveCacheReleaseFailureTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string operation in new[] { "release", "invalidate" })
        foreach (string fault in new[] { "none", "io", "denied", "disposed", "multiple" })
        foreach (int position in fault == "none" ? new[] { -1 } : fault == "multiple" ? new[] { 0 } : new[] { 0, 1, 2 })
            yield return [operation, fault, position];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Retiring_matching_archive_indexes_attempts_every_source_and_retains_the_first_error(string operation, string fault, int position)
    {
        string root = Directory.CreateTempSubdirectory("filecat-cache-retirement-").FullName;
        string path = Path.Combine(root, "owned.tar");
        string unrelated = path + ".other.tar";
        byte[] expected = Enumerable.Range(0, 4096).Select(n => (byte)(n * 31 + 13)).ToArray();
        using (var file = File.Create(path))
        using (var tar = new TarWriter(file, TarEntryFormat.Ustar, leaveOpen: true))
        using (var bytes = new MemoryStream(expected, writable: false))
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "payload.bin") { DataStream = bytes, ModificationTime = DateTimeOffset.UnixEpoch });
        File.Copy(path, unrelated);
        string before = Hash(File.ReadAllBytes(path));
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        var provider = new ArchiveProvider(Path.Combine(root, "spools"), registry); registry.Register(provider);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var cache = (IDictionary)typeof(ArchiveProvider).GetField("_cache", flags)!.GetValue(provider)!;
        var sources = new Dictionary<string, FileStream>();
        var wrappers = new Dictionary<string, CloseReader>();
        var positiveHashes = new List<string>();
        Exception? injected = fault switch
        {
            "none" => null, "io" or "multiple" => new IOException("owned cache source close failure"),
            "denied" => new UnauthorizedAccessException("owned cache source close failure"),
            "disposed" => new ObjectDisposedException("owned cache source close failure"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        try
        {
            byte[] PublicRead(string archive)
            {
                using var content = provider.OpenContent(new ItemRef(ArchiveProvider.ForFile(archive), "payload.bin", EntryKind.File))!;
                byte[] bytes = new byte[4096]; int offset = 0;
                while (offset < bytes.Length) { int n = content.Read(offset, bytes.AsSpan(offset)); if (n <= 0) break; offset += n; }
                Assert.Equal(bytes.Length, offset); Assert.Equal(expected, bytes); return bytes;
            }
            positiveHashes.Add(Hash(PublicRead(path))); positiveHashes.Add(Hash(PublicRead(unrelated)));
            Type indexType = typeof(ArchiveFormats).Assembly.GetType("FileCat.Archives.ArchiveIndex", true)!;
            MethodInfo build = indexType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;
            string originalKey = cache.Keys.Cast<string>().Single(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase));
            object originalIndex = cache[originalKey]!; cache.Remove(originalKey);
            cache.Add(path + "|controlled-stale-index-0", originalIndex);
            for (int i = 1; i <= 2; i++)
            {
                IMemberReader reader = ArchiveFormats.Open(path, ArchiveKind.Tar, "owned.tar");
                var listing = reader.List(_ => { }, CancellationToken.None).ToArray();
                using (var member = reader.Open(0, CancellationToken.None))
                using (var all = new MemoryStream())
                {
                    member.CopyTo(all); Assert.Single(listing); Assert.Equal(expected, all.ToArray()); positiveHashes.Add(Hash(all.ToArray()));
                }
                object index;
                try { index = build.Invoke(null, [reader, new FileInfo(path).Length, CancellationToken.None])!; }
                catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
                cache.Add(path + "|controlled-stale-index-" + i, index);
            }
            string[] keys = cache.Keys.Cast<string>().Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToArray(); Assert.Equal(3, keys.Length);
            foreach (string key in cache.Keys.Cast<string>().ToArray())
            {
                object index = cache[key]!;
                FieldInfo field = indexType.GetField("_reader", flags)!;
                var inner = (IMemberReader)field.GetValue(index)!;
                sources.Add(key, (FileStream)inner.GetType().GetField("_plain", flags)!.GetValue(inner)!);
                Exception? failure = position >= 0 && key == keys[position] ? injected
                    : fault == "multiple" && key == keys[1] ? new UnauthorizedAccessException("owned secondary cache source close failure") : null;
                var wrapper = new CloseReader(inner, failure);
                wrappers.Add(key, wrapper); field.SetValue(index, wrapper);
            }
            bool initiallyOpen = sources.Values.All(s => !s.SafeFileHandle.IsClosed && s.CanRead);
            bool? exclusiveBefore = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            Exception? error = null; byte[]? firstRead = null;
            try { if (operation == "release") provider.Release(path); else firstRead = PublicRead(path); }
            catch (Exception ex) { error = ex; }
            string[] remaining = keys.Where(k => cache.Contains(k)).ToArray();
            int closed = keys.Count(k => sources[k].SafeFileHandle.IsClosed && !sources[k].CanRead);
            int closeCalls = keys.Sum(k => wrappers[k].Closes);
            bool unrelatedOpen = sources.Where(v => !keys.Contains(v.Key)).All(v => !v.Value.SafeFileHandle.IsClosed && v.Value.CanRead);
            bool? exclusiveAfter = OperatingSystem.IsWindows() ? Exclusive(path) : null;
            bool wholeUnchanged = before == Hash(File.ReadAllBytes(path));
            Exception? repeatedError = null; byte[]? retryRead = null;
            try { if (operation == "release") provider.Release(path); else retryRead = PublicRead(path); }
            catch (Exception ex) { repeatedError = ex; }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Operation = operation, Fault = fault, Position = position, ActualTargetKeysInObservedReleaseOrder = keys,
                ActualInitialPositiveMemberBytes = positiveHashes.Count * expected.Length, ActualInitialPositiveSHA256 = positiveHashes, ActualExpectedMemberSHA256 = Hash(expected),
                ActualInitiallyOpen = initiallyOpen, ActualRemainingControlledTargetKeysAtFirstReturn = remaining,
                ActualTargetSourcesClosedAtFirstReturn = closed, ActualTargetCloseCallsAtFirstReturn = closeCalls,
                ActualUnrelatedSourceStillOpen = unrelatedOpen, ActualOriginalErrorObjectRetained = ReferenceEquals(error, injected),
                ActualErrorType = error?.GetType().FullName, ActualErrorMessage = error?.Message, ActualErrorStack = error?.StackTrace,
                ActualWindowsExclusiveBefore = exclusiveBefore, ActualWindowsExclusiveAfterFirstReturn = exclusiveAfter,
                ActualRepeatedOperationErrorType = repeatedError?.GetType().FullName, ActualTargetCloseCallsAfterRepeatedOperation = keys.Sum(k => wrappers[k].Closes),
                ActualFirstReadBytes = firstRead?.Length, ActualFirstReadSHA256 = firstRead is null ? null : Hash(firstRead),
                ActualRetryReadBytes = retryRead?.Length, ActualRetryReadSHA256 = retryRead is null ? null : Hash(retryRead),
                ActualWholeArchiveSHA256 = before, ActualWholeArchiveUnchanged = wholeUnchanged, ActualOwnedFixtureRoot = root, ActualOwnedArchivePath = path,
                ActualPublicReleaseOrCacheMissOpenAndRealTarConstructorListingCompleteDecoderPositives = true,
                ControlledAdditionalTwoRealIndexesAndStaleCacheKeys = true, ControlledPostActualSourceCloseFailureOnly = true,
                NoNaturallyObservedConcurrentCacheRaceOrNativeLibraryFailureOrPhysicalSourceClaim = true
            }));
            Assert.True(initiallyOpen && unrelatedOpen && wholeUnchanged); Assert.Same(injected, error);
            Assert.Equal(3, closed); Assert.Equal(3, closeCalls); Assert.Empty(remaining); Assert.Null(repeatedError);
            Assert.Equal(3, keys.Sum(k => wrappers[k].Closes));
            if (injected is not null) Assert.Contains("CloseReader.Dispose", error!.StackTrace);
            if (operation == "invalidate")
            {
                Assert.Equal(expected, retryRead);
                if (injected is null) Assert.Equal(expected, firstRead); else Assert.Null(firstRead);
            }
            if (OperatingSystem.IsWindows())
            {
                Assert.False(exclusiveBefore);
                Assert.Equal(operation == "release" || injected is not null, exclusiveAfter);
            }
        }
        finally
        {
            foreach (var wrapper in wrappers.Values) try { wrapper.Dispose(); } catch { }
            foreach (var index in cache.Values.Cast<IDisposable>()) try { index.Dispose(); } catch { }
            foreach (var source in sources.Values) source.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static bool Exclusive(string path)
    {
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private sealed class CloseReader(IMemberReader inner, Exception? failure) : IMemberReader
    {
        private bool closed;
        public int Closes { get; private set; }
        public string Format => inner.Format;
        public bool SharesCursor => inner.SharesCursor;
        public IEnumerable<FileCat.Archives.MemberInfo> List(Action<string> warn, CancellationToken ct) => inner.List(warn, ct);
        public Stream Open(int index, CancellationToken ct) => inner.Open(index, ct);
        // The assertion below requires the injected throw site even in optimized builds.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Dispose() { if (closed) return; closed = true; Closes++; inner.Dispose(); if (failure is not null) throw failure; }
    }
}
