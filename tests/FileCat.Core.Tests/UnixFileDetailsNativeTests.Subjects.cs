using System.Runtime.InteropServices;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>
/// N-LINUX / N-MAC core subset: a missing or denied subject is a typed error with no invented snapshot, and special
/// objects (device nodes, FIFOs) are described from metadata without being opened for data.
/// </summary>
public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Unix_details_refuse_a_missing_or_denied_subject_with_a_typed_error_and_no_snapshot()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) { Assert.Skip("Unix native metadata fixture."); return; }
        var ct = TestContext.Current.CancellationToken;
        var reader = new UnixFileRecords();
        string root = Directory.CreateTempSubdirectory("filecat-details-subjects-").FullName;
        string locked = Path.Combine(root, "locked");
        try
        {
            string missing = Path.Combine(root, "never-created");
            Assert.Throws<FileNotFoundException>(() => reader.ReadSummary(missing, ct));
            Assert.Throws<FileNotFoundException>(() => reader.ReadFull(missing, FileDetailsPolicy.MetadataOnly, ct));
            string removed = Path.Combine(root, "removed.txt");
            File.WriteAllText(removed, "deleted before inspection");
            File.Delete(removed);
            Assert.Throws<FileNotFoundException>(() => reader.ReadSummary(removed, ct));

            if (SubjectEuid() == 0) return; // root bypasses the search-permission denial below
            Directory.CreateDirectory(locked);
            string inside = Path.Combine(locked, "inside.txt");
            File.WriteAllText(inside, "payload-sentinel-behind-a-closed-directory");
            Assert.Equal(0, SubjectChmod(locked, 0)); // no search permission on the parent
            Assert.Throws<UnauthorizedAccessException>(() => reader.ReadSummary(inside, ct));
            Assert.Throws<UnauthorizedAccessException>(() => reader.ReadFull(inside, FileDetailsPolicy.MetadataOnly, ct));
            // The closed directory itself is still described from its own metadata.
            var directory = reader.ReadSummary(locked, ct);
            Assert.Equal(FileObjectKind.Directory, directory.Kind);
            Assert.Equal(FactState.Known, directory.Identity.State);
        }
        finally
        {
            if (Directory.Exists(locked)) SubjectChmod(locked, 0x1C0);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unix_details_describe_a_character_device_from_metadata_without_opening_it()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) { Assert.Skip("Unix native metadata fixture."); return; }
        var ct = TestContext.Current.CancellationToken;
        var reader = new UnixFileRecords();
        // /dev/zero and /dev/null are character devices on Linux and macOS; reading either would return data immediately,
        // so a report that contains no payload and no extent section shows no data open was needed.
        foreach (string device in new[] { "/dev/null", "/dev/zero" })
        {
            var summary = reader.ReadSummary(device, ct);
            Assert.Equal(FileObjectKind.CharacterDevice, summary.Kind);
            Assert.Equal(FactState.Known, summary.Identity.State);
            var full = reader.ReadFull(device, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.Equal(FileObjectKind.CharacterDevice, full.Summary.Kind);
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "Main-stream extent records");
        }
    }

    [Fact]
    public void Mac_details_describe_a_FIFO_without_opening_it_or_waiting_for_a_writer()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("Darwin FIFO fixture; Linux has its own O_PATH case."); return; }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-mac-fifo-details-").FullName;
        try
        {
            string fifo = Path.Combine(root, "fifo");
            Assert.Equal(0, MakeFifo(fifo, 0x180));
            var reader = new UnixFileRecords();
            var started = DateTime.UtcNow;
            var summary = reader.ReadSummary(fifo, ct);
            var full = reader.ReadFull(fifo, FileDetailsPolicy.MetadataOnly, ct);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "Inspecting a FIFO must not wait for a writer.");
            Assert.Equal((FileObjectKind.Fifo, FileObjectKind.Fifo), (summary.Kind, full.Summary.Kind));
            Assert.Equal(FactState.Known, summary.Identity.State);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint SubjectEuid();

    [LibraryImport("libc", EntryPoint = "chmod", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SubjectChmod(string path, uint mode);
}
