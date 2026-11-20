using System.Runtime.InteropServices;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Mac_full_details_distinguish_APFS_shared_private_and_partly_shared_files()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "Requires macOS ARM64 and a disposable APFS fixture.");
        string? prepared = Environment.GetEnvironmentVariable("FILECAT_TEST_APFS_SHARING_ROOT");
        if (string.IsNullOrWhiteSpace(prepared)) Assert.Skip("Requires a prepared APFS test folder.");
        string root = Directory.CreateDirectory(Path.Combine(prepared, "sharing-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var reader = new UnixFileRecords();
            var ct = TestContext.Current.CancellationToken;
            const int rewrite = 65_536;
            var bytes = new byte[524_288];
            new Random(42).NextBytes(bytes);
            string source = Path.Combine(root, "source"), clone = Path.Combine(root, "clone"),
                ordinary = Path.Combine(root, "ordinary"), changed = Path.Combine(root, "changed");
            File.WriteAllBytes(source, bytes);
            File.WriteAllBytes(ordinary, bytes);
            Assert.Equal(0, CloneFileForDetails(source, clone, 0));
            Assert.Equal(0, CloneFileForDetails(source, changed, 0));
            Array.Clear(bytes, 0, rewrite);
            using (var stream = new FileStream(changed, FileMode.Open, FileAccess.Write))
            {
                stream.Write(bytes.AsSpan(0, rewrite));
                stream.Flush(flushToDisk: true);
            }

            string Check(string path, string expected, long privateSize)
            {
                var summary = reader.ReadSummary(path, ct);
                Assert.Equal(FactState.Known, summary.Identity.State);
                Assert.DoesNotContain("APFS file sharing", summary.ToReport().ToText());
                var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
                Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
                Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
                var section = Assert.Single(full.Technical.Sections, s => s.Title == "APFS file sharing");
                Assert.Contains(("Sharing", expected), section.Fields);
                Assert.Contains(("Sparse representation flag", "Clear"), section.Fields);
                Assert.Contains(("Private bytes reported", RecordText.Bytes(privateSize)), section.Fields);
                return Assert.Single(section.Fields, field => field.Name == "Clone ID").Value;
            }

            string cloneId = Check(source, "All blocks reported shared", 0);
            Assert.StartsWith("0x", cloneId);
            Assert.Equal(cloneId, Check(clone, "All blocks reported shared", 0));
            Assert.NotEqual(cloneId, Check(ordinary, "No sharing flag reported", bytes.Length));
            Check(changed, "May share blocks", rewrite);

            string sparse = Path.Combine(root, "sparse");
            const long sparseOffset = 64L << 20;
            using (var stream = new FileStream(sparse, FileMode.CreateNew, FileAccess.Write))
            {
                stream.WriteByte(1);
                stream.Position = sparseOffset;
                stream.WriteByte(2);
                stream.Flush(flushToDisk: true);
            }
            var sparseReport = reader.ReadFull(sparse, FileDetailsPolicy.MetadataOnly, ct);
            var sparseSection = Assert.Single(sparseReport.Technical.Sections, s => s.Title == "APFS file sharing");
            Assert.Contains(("Sparse representation flag", "Set"), sparseSection.Fields);
            Assert.Equal(sparseOffset + 1, new FileInfo(sparse).Length);
            string link = Path.Combine(root, "link");
            File.CreateSymbolicLink(link, "source");
            foreach (string path in new[] { root, link })
                Assert.DoesNotContain(reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct).Technical.Sections,
                    section => section.Title == "APFS file sharing");
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(clone));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(ordinary));
            Assert.Equal(bytes, File.ReadAllBytes(changed));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("libc", EntryPoint = "clonefile", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int CloneFileForDetails(string source, string destination, int flags);
}
