using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed partial class UnixHiddenInventoryNativeTests
{
    [Fact]
    public void Mac_directory_inventory_and_value_report_bind_to_a_held_directory()
    {
        Assert.SkipWhen(!OperatingSystem.IsMacOS(), "Mac held-directory xattr fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-mac-inventory-").FullName;
        try
        {
            byte[] payload = "directory-marker"u8.ToArray();
            Assert.Equal(0, MacSet(root, "com.filecat.bound-probe", payload, payload.Length, 0, 0));
            var hidden = new UnixHiddenData();
            var compression = hidden.ReadInventory(root, new(ShowCompressionBacking: true), TestContext.Current.CancellationToken);
            Assert.True(compression.IsComplete);
            Assert.True(compression.BoundToObject);
            Assert.True(compression.CanAct);
            Assert.True(compression.CanDelete);
            Assert.NotNull(compression.Identity);
            Assert.Contains("compression-visible", compression.Categories[0].Scope);
            Assert.Contains("included in the requested API view", compression.Notice);
            var item = Assert.Single(compression.Items, i => i.Name == "com.filecat.bound-probe");
            Assert.Equal(payload.Length, item.Size);
            Assert.Equal(payload, hidden.Read(root, item, 32));
            using (var value = hidden.Open(root, item))
            {
                using var bytes = new MemoryStream();
                value.CopyTo(bytes);
                Assert.Equal(payload, bytes.ToArray());
            }
            var provider = new HiddenDataProvider(hidden);
            Assert.True(provider.GetCapabilities(HiddenDataProvider.Of(root)).HasFlag(LocationCapabilities.Delete));
            var selected = new ItemRef(HiddenDataProvider.Of(root), item.Name, EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Attribute), SupplementarySourceIdentity = compression.Identity };
            var report = provider.ReadValueReport(selected, compression.Identity!.Value, TestContext.Current.CancellationToken);
            Assert.True(report.CanPublish);
            Assert.Equal(compression.Identity, Assert.Single(report.Origin.Contributors).Identity);
            Assert.Contains("directory-marker", report.Text);
            var output = (IUnprotectedContentOutput)provider;
            output.RequireClearOutputSource(HiddenDataProvider.Of(root), ContentOutputKind.Extraction);
            using (var source = output.OpenUnprotectedOutput(selected, ContentOutputKind.Extraction, out var provenance)!)
            {
                byte[] exported = new byte[payload.Length];
                Assert.Equal(exported.Length, source.Read(0, exported));
                Assert.Equal(payload, exported);
                Assert.NotNull(provenance);
                var receipt = provenance.Freeze();
                Assert.False(receipt.HasUnknownInput);
                Assert.Equal(compression.Identity, Assert.Single(receipt.Contributors).Identity);
            }

            provider.DeleteReviewedValue(selected, compression.Identity!.Value, TestContext.Current.CancellationToken);
            Assert.Empty(hidden.ReadInventory(root, new(), TestContext.Current.CancellationToken).Items);

            var ordinary = hidden.ReadInventory(root, new(ShowCompressionBacking: false), TestContext.Current.CancellationToken);
            Assert.True(ordinary.IsComplete);
            Assert.True(ordinary.BoundToObject);
            Assert.True(ordinary.CanAct);
            Assert.False(ordinary.CanDelete);
            Assert.Contains("ordinary view", ordinary.Categories[0].Scope);
            Assert.Contains("not requested", ordinary.Notice);

            string fifo = Path.Combine(root, "fifo"), link = Path.Combine(root, "directory-link");
            Assert.Equal(0, Fifo(fifo, 0x180));
            string nested = Directory.CreateDirectory(Path.Combine(root, "real-directory")).FullName;
            File.CreateSymbolicLink(link, nested);
            Assert.False(hidden.ReadInventory(fifo, new(), TestContext.Current.CancellationToken).BoundToObject);
            Assert.False(hidden.ReadInventory(link, new(), TestContext.Current.CancellationToken).BoundToObject);
            Assert.Throws<IOException>(() => hidden.Read(link, item, 32));

            string selectedPath = Directory.CreateDirectory(Path.Combine(root, "selected")).FullName;
            Assert.Equal(0, MacSet(selectedPath, "com.filecat.bound-probe", payload, payload.Length, 0, 0));
            var original = hidden.ReadInventory(selectedPath, new(), TestContext.Current.CancellationToken);
            var selectedRef = new ItemRef(HiddenDataProvider.Of(selectedPath), item.Name, EntryKind.File)
                { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Attribute), SupplementarySourceIdentity = original.Identity };
            Directory.Move(selectedPath, Path.Combine(root, "previous"));
            Directory.CreateDirectory(selectedPath);
            byte[] replacement = "replacement-marker"u8.ToArray();
            Assert.Equal(0, MacSet(selectedPath, "com.filecat.bound-probe", replacement, replacement.Length, 0, 0));
            Assert.Contains("different directory", Assert.Throws<IOException>(() =>
                provider.ReadValueReport(selectedRef, original.Identity!.Value, TestContext.Current.CancellationToken)).Message);
            Assert.Contains("different directory", Assert.Throws<IOException>(() => provider.OpenContent(selectedRef)).Message);
            Assert.Contains("different directory", Assert.Throws<IOException>(() =>
                output.OpenUnprotectedOutput(selectedRef, ContentOutputKind.Extraction)).Message);
            Assert.Contains("different directory", Assert.Throws<IOException>(() =>
                provider.DeleteReviewedValue(selectedRef, original.Identity!.Value, TestContext.Current.CancellationToken)).Message);
            Assert.Contains(hidden.ReadInventory(selectedPath, new(), TestContext.Current.CancellationToken).Items,
                observed => observed.Name == "com.filecat.bound-probe");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Linux_bound_inventory_and_values_use_the_inode_and_refuse_special_objects()
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "Linux held-inode xattr fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-inventory-").FullName;
        try
        {
            var hidden = new UnixHiddenData();
            string file = Path.Combine(root, "source"), fifo = Path.Combine(root, "fifo"), link = Path.Combine(root, "link");
            File.WriteAllText(file, "main-sentinel");
            byte[] payload = "attribute-sentinel"u8.ToArray();
            Assert.Equal(0, Set(file, "user.filecat.payload", payload, payload.Length, 0));
            var inventory = hidden.ReadInventory(file, new(), TestContext.Current.CancellationToken);
            Assert.True(inventory.IsComplete); Assert.True(inventory.BoundToObject); Assert.False(inventory.ValueBearingQueryIssued); Assert.NotNull(inventory.Identity);
            var item = Assert.Single(inventory.Items); Assert.Equal(payload.Length, item.Size);
            Assert.Equal(payload[..4], hidden.Read(file, item with { Size = 1 }, 4));
            using (var stream = hidden.Open(file, item)) Assert.Equal(payload.Length, stream.Length);
            File.CreateSymbolicLink(link, "source");
            Assert.Throws<NotSupportedException>(() => hidden.ReadInventory(link, new(), TestContext.Current.CancellationToken));
            Assert.Equal(0, Fifo(fifo, 0x180));
            Assert.Throws<NotSupportedException>(() => hidden.ReadInventory(fifo, new(), TestContext.Current.CancellationToken));
            Assert.Throws<NotSupportedException>(() => hidden.ReadInventory("/dev/null", new(), TestContext.Current.CancellationToken));
            hidden.Delete(file, item);
            Assert.Empty(hidden.ReadInventory(file, new(), TestContext.Current.CancellationToken).Items);
            Assert.Equal("main-sentinel", File.ReadAllText(file));
        }
        finally { Directory.Delete(root, true); }
    }

    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Set(string path, string name, byte[] value, nint size, int flags);
    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Fifo(string path, uint mode);
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(string path, string name, byte[] value, nint size, uint position, int options);
}
