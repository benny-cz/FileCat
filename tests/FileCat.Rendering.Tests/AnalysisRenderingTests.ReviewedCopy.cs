using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public async Task NativeReviewedCopyPreservesStreamsAndKeepsExistingFiles()
    {
        OrdinaryPreparation();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
                new WindowsAnalysisPrivateDirectory.ReviewedOutputFolder(Path.GetTempPath(), null));
            return;
        }
        using var fixture = new PreparationFixture();
        string work = Path.Combine(Path.GetTempPath(), "filecat-reviewed-copy-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(work, "source"), output = Path.Combine(work, "destination");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        string path = Path.Combine(input, "source.bin"), copied = Path.Combine(output, "copy.bin");
        byte[] data = new byte[AnalysisLimits.BatchBytes + 15];
        new Random(42).NextBytes(data);
        byte[] extra = [9, 8, 7, 0, 6], mark = "[ZoneTransfer]\r\nZoneId=3\r\n"u8.ToArray();
        File.WriteAllBytes(path, data);
        File.WriteAllBytes(path + ":fixture-extra", extra);
        File.WriteAllBytes(path + ":Zone.Identifier", mark);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        File.SetAttributes(path, FileAttributes.Archive | FileAttributes.Hidden);
        var token = TestContext.Current.CancellationToken;
        var devices = new AnalysisIoDevices(PathUtil.GetDeviceKey(input), PathUtil.GetDeviceKey(output));
        int prior = AnalysisNativeHandles.Count;
        try
        {
            await AnalysisIo.Run(fixture.Services.Io, devices, cancellation =>
            {
                using var source = new WindowsAnalysisBackend(input);
                var root = source.RootEntry;
                var child = Assert.Single(source.Children(root, cancellation));
                var directory = new AnalysisBranchComponent(root.Name, root.Identity!, root.CreatedUtc!.Value, root.CreatedPrecisionTicks!.Value);
                var target = new AnalysisPrimaryContentTarget(2, directory, [], child.Name, child.Identity!, child.Version,
                    child.CreatedUtc!.Value, child.CreatedPrecisionTicks!.Value, (long)child.LogicalLength.Value!.Value);
                var review = source.ReviewCopy(target, cancellation);
                Assert.Equal(3, review.Streams.Length);
                using var destination = new WindowsAnalysisPrivateDirectory.ReviewedOutputFolder(output, null);
                Assert.Empty(Directory.EnumerateFileSystemEntries(output));
                Assert.ThrowsAny<IOException>(() => Directory.Move(output, output + "-renamed"));
                Assert.ThrowsAny<IOException>(() => source.CopyReviewed(review, destination, "protected.bin",
                    new HashSet<string>(StringComparer.Ordinal) { destination.Identity }, cancellation));
                Assert.Empty(Directory.EnumerateFileSystemEntries(output));

                var receipt = source.CopyReviewed(review, destination, "copy.bin", new HashSet<string>(StringComparer.Ordinal), cancellation);
                Assert.Equal(data.Length + mark.Length + extra.Length, receipt.Bytes);
                Assert.Equal(3, receipt.Streams);
                Assert.Throws<AnalysisExportTargetExistsException>(() =>
                    source.CopyReviewed(review, destination, "copy.bin", new HashSet<string>(StringComparer.Ordinal), cancellation));
                using (var changed = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    changed.SetLength(0);
                    changed.Write([1, 2, 3]);
                }
                Assert.Throws<AnalysisSourceChangedException>(() =>
                    source.CopyReviewed(review, destination, "stale.bin", new HashSet<string>(StringComparer.Ordinal), cancellation));
                return true;
            }, token);
            Assert.Equal(data, File.ReadAllBytes(copied));
            Assert.Equal(extra, File.ReadAllBytes(copied + ":fixture-extra"));
            Assert.Equal(mark, File.ReadAllBytes(copied + ":Zone.Identifier"));
            Assert.Equal(FileAttributes.Archive | FileAttributes.Hidden, File.GetAttributes(copied));
            Assert.Equal(new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc), File.GetLastWriteTimeUtc(copied));
            Assert.Equal(copied, Assert.Single(Directory.EnumerateFileSystemEntries(output)));
            Assert.Equal(prior, AnalysisNativeHandles.Count);
        }
        finally { Directory.Delete(work, recursive: true); }
    }
}
