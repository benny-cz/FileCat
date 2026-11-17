using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.HiddenData;

namespace FileCat.Core.Tests;

public sealed partial class UnixHiddenInventoryNativeTests
{
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
}
