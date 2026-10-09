using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DiscUtils.Iso9660;
using FileCat.Archives;
using Xunit;

namespace FileCat.Core.Tests;

public sealed class DiscImageRetirementFailureTests(ITestOutputHelper output)
{
    private static readonly object FixtureGate = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Disc_library_close_failure_still_retires_the_actual_source_file(int fault)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-owned-disc-close-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "owned.iso");
        byte[] member = Enumerable.Range(0, 4096).Select(n => (byte)((n * 37 + 13) & 255)).ToArray();
        string? masterRoot = Environment.GetEnvironmentVariable("FILECAT_OWNED_DISC_MASTER");
        lock (FixtureGate)
        {
            if (!string.IsNullOrEmpty(masterRoot))
            {
                Directory.CreateDirectory(masterRoot);
                string master = Path.Combine(masterRoot, "owned-positive.iso");
                if (!File.Exists(master)) BuildImage(master, member);
                File.Copy(master, path);
            }
            else BuildImage(path, member);
        }
        byte[] originalImage = File.ReadAllBytes(path);
        string imageHash = Convert.ToHexString(SHA256.HashData(originalImage));
        FileStream? native = null;
        IMemberReader? reader = null;
        FaultingCdReader? adapter = null;
        bool rootGone = false;
        try
        {
            reader = ArchiveFormats.Open(path, ArchiveKind.DiscImage, "owned.iso");
            var members = reader.List(_ => { }, CancellationToken.None).ToList();
            var selected = Assert.Single(members.Where(v => v.Path.Replace('\\', '/').EndsWith("member.bin", StringComparison.Ordinal)));
            byte[] actualMember;
            using (var entry = reader.Open(selected.Index, CancellationToken.None))
            using (var copy = new MemoryStream())
            {
                entry.CopyTo(copy);
                actualMember = copy.ToArray();
            }
            Assert.Equal(member, actualMember);
            Type type = reader.GetType();
            native = (FileStream)type.GetField("_file", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(reader)!;
            var field = type.GetField("_fs", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ((IDisposable)field.GetValue(reader)!).Dispose();
            Assert.True(native.CanRead);
            Assert.False(native.SafeFileHandle.IsClosed);
            adapter = new FaultingCdReader(new MemoryStream(originalImage, writable: false), fault);
            field.SetValue(reader, adapter);
            bool beforeExclusive = Exclusive(path);
            Exception? first = null;
            try { reader.Dispose(); }
            catch (Exception ex) { first = ex; }
            bool handleClosed = native.SafeFileHandle.IsClosed;
            bool canRead = native.CanRead;
            bool afterExclusive = Exclusive(path);
            int firstCalls = adapter.CloseCalls;
            bool inputExact = File.ReadAllBytes(path).SequenceEqual(originalImage);
            Exception? repeated = null;
            try { reader.Dispose(); }
            catch (Exception ex) { repeated = ex; }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Fault = fault,
                ActualOwnedFixtureRoot = root,
                ActualOwnedImagePath = path,
                ActualFullImageBytes = originalImage.Length,
                ActualFullImageSHA256 = imageHash,
                ActualFullImageUnchangedBeforeFixtureCleanup = inputExact,
                ActualPositiveMemberBytes = actualMember.Length,
                ActualPositiveMemberSHA256 = Convert.ToHexString(SHA256.HashData(actualMember)),
                ActualCompletePositiveMemberRead = actualMember.SequenceEqual(member),
                ActualProductConstructorAndListingExecuted = true,
                ActualLibraryFieldReplacedByControlledCdReaderAfterHealthyConstruction = true,
                ActualOriginalLibraryDisposedBeforeReplacementWithSourceStillOpen = true,
                ActualNativeSourceHandleClosedBeforeFixtureCleanup = handleClosed,
                ActualNativeSourceCanReadAfterDispose = canRead,
                ActualWindowsExclusiveBefore = OperatingSystem.IsWindows() ? (bool?)beforeExclusive : null,
                ActualWindowsExclusiveAfter = OperatingSystem.IsWindows() ? (bool?)afterExclusive : null,
                ActualLibraryCloseCallsAtFirstReturn = firstCalls,
                ActualLibraryCloseCallsAfterRepeatedDispose = adapter.CloseCalls,
                ActualErrorType = first?.GetType().Name,
                ActualErrorMessage = first?.Message,
                ActualErrorStack = first?.StackTrace,
                ActualOriginalErrorObjectRetained = ReferenceEquals(first, adapter.Error),
                ActualRepeatedDisposeErrorType = repeated?.GetType().Name,
                ActualRepeatedDisposeOriginalErrorObject = repeated is not null && ReferenceEquals(repeated, adapter.Error),
                OwnedControlledLibraryFaultWithActualNativeSourceOnly = true,
                NoNativeDecoderFaultOrPhysicalSourceIncidenceClaim = true,
            }));
            Assert.True(inputExact);
            Assert.Equal(1, firstCalls);
            Assert.True(ReferenceEquals(first, adapter.Error));
            Assert.True(handleClosed, "Disc library close failure left the actual native image source open.");
            Assert.False(canRead);
            if (OperatingSystem.IsWindows())
            {
                Assert.False(beforeExclusive);
                Assert.True(afterExclusive);
            }
            Assert.Null(repeated);
            if (fault != 0) Assert.Equal(1, adapter.CloseCalls);
        }
        finally
        {
            adapter?.RetireFixtureState();
            native?.Dispose();
            if (reader is not null)
            {
                try { reader.Dispose(); }
                catch { }
            }
            Directory.Delete(root, recursive: true);
            rootGone = !Directory.Exists(root);
        }
        Assert.True(rootGone);
    }

    private static void BuildImage(string path, byte[] member)
    {
        var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "OWNEDCLOSE" };
        builder.AddFile(@"docs\member.bin", member);
        builder.Build(path);
    }

    private static bool Exclusive(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
    }

    private sealed class FaultingCdReader(Stream input, int fault) : CDReader(input, joliet: true)
    {
        public Exception? Error { get; } = fault switch
        {
            0 => null,
            1 => new IOException("owned-disc-library-close"),
            2 => new UnauthorizedAccessException("owned-disc-library-close"),
            3 => new ObjectDisposedException("owned-disc-library-close"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        public int CloseCalls { get; private set; }
        protected override void Dispose(bool disposing)
        {
            CloseCalls++;
            if (Error is not null) throw Error;
            base.Dispose(disposing);
        }
        public void RetireFixtureState()
        {
            base.Dispose(true);
            input.Dispose();
        }
    }
}
