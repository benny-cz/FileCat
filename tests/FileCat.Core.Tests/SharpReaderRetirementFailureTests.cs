using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace FileCat.Core.Tests;

public sealed class SharpReaderRetirementFailureTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int readerFault in Enumerable.Range(0, 4))
        foreach (int archiveFault in Enumerable.Range(0, 4))
        foreach (int volumes in new[] { 1, 3 })
            yield return [readerFault, archiveFault, volumes];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Cleanup_attempts_every_owned_volume_and_preserves_the_first_library_error(int readerFault, int archiveFault, int volumes)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-sharp-retire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] bytes = Enumerable.Range(0, 256).Select(n => (byte)(n * 31 + 11)).ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        var files = new List<FileStream>();
        var paths = new List<string>();
        IReader reader = DispatchProxy.Create<IReader, OwnedArchiveCleanupProxy>();
        IArchive archive = DispatchProxy.Create<IArchive, OwnedArchiveCleanupProxy>();
        var readerProxy = (OwnedArchiveCleanupProxy)(object)reader;
        var archiveProxy = (OwnedArchiveCleanupProxy)(object)archive;
        readerProxy.Failure = Fault(readerFault, "owned reader");
        archiveProxy.Failure = Fault(archiveFault, "owned archive");
        Exception? expected = readerProxy.Failure ?? archiveProxy.Failure;
        try
        {
            for (int i = 0; i < volumes; i++)
            {
                string path = Path.Combine(root, "owned-volume-" + i + ".bin");
                File.WriteAllBytes(path, bytes);
                paths.Add(path);
                files.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            }
            bool handlesInitiallyOpen = files.All(f => !f.SafeFileHandle.IsClosed && f.CanRead);
            bool? exclusiveBefore = OperatingSystem.IsWindows() ? paths.All(Exclusive) : null;
            var type = Assembly.Load("FileCat.Archives").GetTypes().Single(t => t.Name == "SharpArchiveReader");
            object target = RuntimeHelpers.GetUninitializedObject(type);
            type.GetField("_reader", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, reader);
            type.GetField("_archive", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, archive);
            type.GetField("_volumes", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, new List<FileStream>(files));
            Exception? failure = null;
            try { ((IDisposable)target).Dispose(); }
            catch (Exception ex) { failure = ex; }
            // Record actual native handle state before any owned fixture cleanup.
            bool allClosed = files.All(f => f.SafeFileHandle.IsClosed && !f.CanRead);
            bool? exclusiveAfter = OperatingSystem.IsWindows() ? paths.All(Exclusive) : null;
            bool unchanged = paths.All(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) == hash);
            bool sameError = ReferenceEquals(expected, failure);
            int observedReaderCloses = readerProxy.DisposeCalls, observedArchiveCloses = archiveProxy.DisposeCalls;
            TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(new
            {
                ReaderFault = readerFault, ArchiveFault = archiveFault, OwnedVolumeCount = volumes,
                ActualReaderDisposeCalls = observedReaderCloses, ActualArchiveDisposeCalls = observedArchiveCloses,
                ActualNativeHandlesInitiallyOpen = handlesInitiallyOpen, ActualNativeHandlesClosedBeforeFixtureCleanup = allClosed,
                ActualWindowsExclusiveBefore = exclusiveBefore, ActualWindowsExclusiveAfter = exclusiveAfter,
                ActualOriginalErrorObjectRetained = sameError, ActualErrorType = failure?.GetType().Name,
                ActualErrorMessage = failure?.Message, ActualErrorStack = failure?.StackTrace,
                ActualFullInputHashesUnchanged = unchanged, InputOriginalSHA256 = hash,
                ActualFixtureRoot = root, ActualVolumePaths = paths,
                OwnedControlledLibraryDisposeFaultOnly = true, NoNativeDecoderOrPhysicalSourceIncidenceClaim = true,
            }));
            Assert.True(handlesInitiallyOpen);
            Assert.Equal(1, observedReaderCloses);
            Assert.Equal(1, observedArchiveCloses);
            Assert.True(sameError);
            Assert.True(allClosed, "Every native volume handle must close at the library failure boundary.");
            Assert.True(unchanged);
            if (OperatingSystem.IsWindows()) { Assert.False(exclusiveBefore); Assert.True(exclusiveAfter); }
            if (failure is not null) Assert.False(string.IsNullOrWhiteSpace(failure.StackTrace));
        }
        finally
        {
            // These are the fixture's exact owned streams; no production retry or GC hides the baseline observation.
            foreach (var file in files) file.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static Exception? Fault(int kind, string message) => kind switch
    {
        0 => null,
        1 => new IOException(message),
        2 => new UnauthorizedAccessException(message),
        3 => new ObjectDisposedException(message),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool Exclusive(string path)
    {
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }

    public class OwnedArchiveCleanupProxy : DispatchProxy
    {
        public Exception? Failure { get; set; }
        public int DisposeCalls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IDisposable.Dispose)) throw new InvalidOperationException("Unexpected owned library call: " + targetMethod?.Name);
            DisposeCalls++;
            if (Failure is not null) throw Failure;
            return null;
        }
    }
}
