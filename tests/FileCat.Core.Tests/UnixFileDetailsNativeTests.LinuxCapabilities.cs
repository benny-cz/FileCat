using System.Buffers.Binary;
using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Linux_file_capability_parser_requires_the_complete_known_attribute()
    {
        Span<byte> attribute = stackalloc byte[24];
        attribute.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(attribute, 0x03000001);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute[4..], 1u << 10);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute[20..], 424242);
        var observed = UnixFileRecords.ParseLinuxFileCapabilities(attribute);
        Assert.Equal(FactState.Known, observed.State);
        Assert.Equal(FactScope.Entry, observed.Scope);
        Assert.Equal("cap_net_bind_service=ep [rootid=424242]", observed.Value);
        Assert.Equal(FactState.Failed, UnixFileRecords.ParseLinuxFileCapabilities(attribute[..20]).State);
        Assert.Equal(FactState.Failed, UnixFileRecords.ParseLinuxFileCapabilities(attribute[..3]).State);

        BinaryPrimitives.WriteUInt32LittleEndian(attribute, 0x02000000);
        Assert.Equal("cap_net_bind_service=p", UnixFileRecords.ParseLinuxFileCapabilities(attribute[..20]).Value);
        Assert.Equal(FactState.Failed, UnixFileRecords.ParseLinuxFileCapabilities(attribute).State);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute, 0x02000004);
        Assert.Equal(FactState.Unsupported, UnixFileRecords.ParseLinuxFileCapabilities(attribute[..20]).State);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute, 0x04000000);
        Assert.Equal(FactState.Unsupported, UnixFileRecords.ParseLinuxFileCapabilities(attribute).State);
    }

    [Fact]
    public void Linux_ext4_full_details_report_only_the_held_files_recorded_capabilities()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("FILECAT_EXT4_CAPABILITIES_FIXTURE") is { Length: > 0 },
            "Requires the isolated ext4 file-capability fixture.");
        string root = Environment.GetEnvironmentVariable("FILECAT_EXT4_CAPABILITIES_FIXTURE")!;
        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        CheckFile("ordinary.bin", "None returned");
        CheckFile("capabilities-v2.bin", "cap_net_bind_service=ep");
        CheckFile("capabilities-v3.bin", "cap_net_bind_service=p [rootid=424242]");

        var link = reader.ReadFull(Path.Join(root, "link-to-v2"), FileDetailsPolicy.MetadataOnly, ct);
        Assert.Equal(FileObjectKind.SymbolicLink, link.Summary.Kind);
        Assert.DoesNotContain(link.Technical.Sections, section => section.Title == "File capabilities");

        void CheckFile(string name, string expected)
        {
            string path = Path.Join(root, name);
            var summary = reader.ReadSummary(path, ct);
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal("ext4", full.Summary.FileSystem.Value);
            Assert.Equal(summary.Identity, full.Summary.Identity);
            Assert.DoesNotContain("File capabilities", summary.ToReport().ToText());
            Assert.DoesNotContain("file capabilities fixture payload", full.ToText());
            var capabilities = Assert.Single(full.Technical.Sections, section => section.Title == "File capabilities");
            Assert.Contains(("Recorded execution attributes", expected), capabilities.Fields);
            Assert.Contains(("Observation", "security.capability from fgetxattr on the held ext4 file"), capabilities.Fields);
            Assert.Equal(name != "ordinary.bin", capabilities.Lines.Any(line =>
                line.Contains("nosuid", StringComparison.Ordinal) && line.Contains("no_new_privs", StringComparison.Ordinal)));
        }
    }
}
