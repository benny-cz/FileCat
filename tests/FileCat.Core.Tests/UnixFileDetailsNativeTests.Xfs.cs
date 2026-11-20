using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.HiddenData;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Linux_XFS_reports_reflink_sharing_sparse_ranges_and_unwritten_extents()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires Linux x64 XFS.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_XFS_EXTENTS_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires a prepared XFS extent fixture.");

        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        foreach (string name in new[] { "source", "clone", "ordinary", "partial", "sparse", "reserved" })
        {
            string path = Path.Join(root, name);
            var summary = reader.ReadSummary(path, ct);
            Assert.Equal("xfs", summary.FileSystem.Value);
            Assert.DoesNotContain("Main-stream extent records", summary.ToReport().ToText());
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            var section = Assert.Single(full.Technical.Sections, s => s.Title == "Main-stream extent records");
            Assert.Contains(("Inventory", "Complete"), section.Fields);
            var rows = Assert.IsType<FileCat.Core.Inspect.InspectionTable>(section.Table).Rows;
            Assert.NotEmpty(rows);
            var flags = rows.Select(row => Convert.ToUInt32(row[3][2..], 16)).ToArray();
            string sharing = name is "source" or "clone" or "partial" ? "Shared extents reported" : "No returned record reports sharing";
            Assert.Contains(("Extent sharing", sharing), section.Fields);
            if (name == "partial")
            {
                Assert.Contains(flags, bits => (bits & 0x2000) != 0);
                Assert.Contains(flags, bits => (bits & 0x2000) == 0);
            }
            if (name == "sparse")
            {
                Assert.Equal(FactState.Known, summary.Allocated.State);
                Assert.Equal(1_048_577, summary.LogicalSize.Value);
                Assert.InRange(summary.Allocated.Value, 1, summary.LogicalSize.Value - 1);
                Assert.Equal([0L, 1_048_576], rows.Select(row => long.Parse(row[0], NumberStyles.AllowThousands, CultureInfo.CurrentCulture)));
            }
            if (name == "reserved")
            {
                Assert.All(flags, bits => Assert.NotEqual(0u, bits & 0x800)); // FIEMAP_EXTENT_UNWRITTEN
                Assert.All(rows, row => Assert.Equal("Physical location unavailable", row[2]));
            }
        }

        var inventory = new UnixHiddenData().ReadInventory(Path.Join(root, "source"), new(), ct);
        Assert.True(inventory.BoundToObject);
        Assert.True(inventory.IsComplete);
        Assert.False(inventory.ValueBearingQueryIssued);
        var attribute = Assert.Single(inventory.Items, item => item.Name == "user.filecat.fixture");
        Assert.Equal(18, attribute.Size);
    }
}
