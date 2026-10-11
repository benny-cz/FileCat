using System.Collections;
using System.Reflection;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

public sealed class SpoolSourceCloseFailureTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_source_close_error_cannot_abandon_a_return_lease(bool fault)
    {
        string root = Directory.CreateTempSubdirectory("filecat-spool-source-close-").FullName;
        string path = Path.Combine(root, "owned-source.tar");
        byte[] expected = Enumerable.Range(0, 4096).Select(i => (byte)(i * 23 + 71)).ToArray(); File.WriteAllBytes(path, expected);
        var registry = new ProviderRegistry(); var files = new IdentityAwareTestFiles();
        var archive = new ArchiveProvider(root, registry) { GetSourceIdentity = files.GetFileIdentity, GetOpenedSourceIdentity = files.GetHandleIdentity };
        var spools = (IDictionary)typeof(ArchiveProvider).GetField("_spools", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(archive)!;
        var injected = new IOException("owned source close failure after native close");
        string firstPath = ""; byte[] copied = []; FileStream? oldHolder = null; Exception? error = null;
        int firstCloses = 0; bool sourceClosed = false, gone = false, unchanged = false;
        using var source = new FileContentSource(path);
        var sourceHandle = (SafeFileHandle)typeof(FileContentSource).GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source)!;
        var provider = new OwnedProvider(path, () => new ClosingSource(source, () =>
        {
            firstCloses++; sourceClosed = sourceHandle.IsClosed;
            firstPath = Directory.GetFiles(root, "nested-*.tar").Single(); copied = ReadShared(firstPath);
            if (spools.Count != 0)
            {
                object value = spools.Values.Cast<object>().Single();
                oldHolder = value.GetType().GetProperty("Holder")?.GetValue(value) as FileStream
                    ?? value.GetType().GetField("Item2")?.GetValue(value) as FileStream;
            }
            if (fault) throw injected;
        })); registry.Register(provider);
        Location Member(int n) => new(provider.Scheme, $"source-{n}.tar", Location.FileSystem(path));
        try
        {
            try { firstPath = archive.Spool(Member(0)); } catch (Exception ex) { error = ex; }
            Assert.Equal(expected, copied); Assert.Equal(1, firstCloses); Assert.True(sourceClosed);
            Assert.True(SpinWait.SpinUntil(() => DateTime.UtcNow > File.GetLastWriteTimeUtc(path), 1000));
            for (int i = 1; i <= 5; i++) Assert.Equal(expected, ReadShared(archive.Spool(Member(i))));
            gone = !File.Exists(firstPath); unchanged = File.ReadAllBytes(path).SequenceEqual(expected);
        }
        finally
        {
            foreach (object value in spools.Values)
                if (value is IDisposable disposable) disposable.Dispose();
                else ((FileStream)value.GetType().GetField("Item2")!.GetValue(value)!).Dispose();
            oldHolder?.Dispose(); Directory.Delete(root, recursive: true);
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Fault = fault, ActualOriginalCloseErrorObjectRetained = ReferenceEquals(error, fault ? injected : null),
            ActualErrorType = error?.GetType().FullName, ActualErrorMessage = error?.Message, ActualErrorStack = error?.StackTrace,
            ActualSourceCloseCalls = firstCloses, ActualNativeSourceHandleClosed = sourceClosed,
            ActualCopiedBytesBase64 = Convert.ToBase64String(copied), ActualSourceBytesBase64 = Convert.ToBase64String(expected),
            ActualSpoolPathAtSourceClose = firstPath, ActualSpoolGoneAfterCachePressureBeforeFixtureCleanup = gone,
            ActualWholeSourceUnchanged = unchanged, ActualOwnedRoot = root, ActualOwnedRootGone = !Directory.Exists(root),
            ControlledSourceCloseErrorAfterActualNativeCloseOnly = true, NoNativeFormatFailureOrPhysicalSourceClaim = true
        }));
        Assert.Same(fault ? injected : null, error); Assert.True(gone && unchanged && !Directory.Exists(root));
    }
    private static byte[] ReadShared(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); file.CopyTo(bytes); return bytes.ToArray();
    }
    private sealed class ClosingSource(IContentSource inner, Action afterClose) : IContentSource
    {
        public string DisplayName => inner.DisplayName;
        public long Length => inner.Length;
        public bool CanSeek => inner.CanSeek;
        public string? LocalPath => inner.LocalPath;
        public ContentRevision? GetRevision() => inner.GetRevision();
        public int Read(long offset, Span<byte> buffer) => inner.Read(offset, buffer);
        public void Dispose() { inner.Dispose(); afterClose(); }
    }
    private sealed class OwnedProvider(string path, Func<IContentSource> first) : ResourceProvider
    {
        public override string Scheme => "owned-spool-source";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => location.Container;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource OpenContent(ItemRef item) => item.Name == "source-0.tar" ? first() : new FileContentSource(path);
    }
}
