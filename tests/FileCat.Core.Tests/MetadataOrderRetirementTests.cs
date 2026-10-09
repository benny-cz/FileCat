using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class MetadataOrderRetirementTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 16)]
    [InlineData(false, 512)]
    [InlineData(false, 8192)]
    [InlineData(true, 16)]
    [InlineData(true, 512)]
    [InlineData(true, 8192)]
    public void Retiring_metadata_drops_its_order_records_without_disturbing_other_values(bool invalidateAll, int repetitions)
    {
        using var dir = new TempDir();
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io);
        string path = Path.Combine(dir.Path, "owned.bin");
        File.WriteAllText(path, "owned metadata bytes");
        byte[] original = File.ReadAllBytes(path);
        var entry = new EntryData("owned.bin", EntryKind.File, original.Length, File.GetLastWriteTimeUtc(path).Ticks);
        int retiredCalls = 0, otherCalls = 0;
        metadata.Register(new MetadataField("retired-owned", "Owned", MetadataCost.Cheap, _ => true, (p, ct) =>
        { ct.ThrowIfCancellationRequested(); retiredCalls++; return File.ReadAllText(p); }, v => (string)v!));
        metadata.Register(new MetadataField("other-owned", "Other", MetadataCost.Cheap, _ => true, (p, ct) =>
        { ct.ThrowIfCancellationRequested(); otherCalls++; return File.ReadAllText(p); }, v => (string)v!));
        int Count(string field)
        {
            object value = typeof(MetadataService).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(metadata)!;
            return (int)value.GetType().GetProperty("Count")!.GetValue(value)!;
        }
        try
        {
            var other = metadata.Compute("other-owned", path, entry, TestContext.Current.CancellationToken);
            Assert.Equal(MetadataState.Available, other.State); Assert.Equal("owned metadata bytes", other.Value);
            for (int i = 0; i < repetitions; i++)
            {
                var value = metadata.Compute("retired-owned", path, entry, TestContext.Current.CancellationToken);
                Assert.Equal(MetadataState.Available, value.State); Assert.Equal("owned metadata bytes", value.Value);
                if (invalidateAll) metadata.Invalidate(); else metadata.Forget("retired-owned", path);
            }
            int cacheBefore = Count("_cache"), orderBefore = Count("_order");
            Assert.Equal(repetitions, retiredCalls);
            Assert.Equal(invalidateAll ? 0 : 1, cacheBefore);
            var otherAfter = metadata.Compute("other-owned", path, entry, TestContext.Current.CancellationToken);
            Assert.Equal(other, otherAfter); Assert.Equal(invalidateAll ? 2 : 1, otherCalls);
            var retry = metadata.Compute("retired-owned", path, entry, TestContext.Current.CancellationToken);
            Assert.Equal(other, retry); Assert.Equal(repetitions + 1, retiredCalls);
            int cacheAfter = Count("_cache"), orderAfter = Count("_order");
            Assert.Equal(2, cacheAfter); Assert.Equal(original, File.ReadAllBytes(path));
            output.WriteLine("METADATA_ORDER_RETIREMENT " + JsonSerializer.Serialize(new { invalidateAll, repetitions, cacheBefore, orderBefore, cacheAfter, orderAfter, retiredCalls, otherCalls,
                onlyActualPublicComputeForgetInvalidateUsed = true, ownedSourceAllBytesUnchanged = true, sourceSHA256 = Convert.ToHexStringLower(SHA256.HashData(original)), sourceBytes = original.Length, root = dir.Path }));
            Assert.Equal(cacheBefore, orderBefore); Assert.Equal(cacheAfter, orderAfter);
        }
        finally { metadata.Invalidate(); Assert.Equal(0, Count("_cache")); Assert.Equal(0, Count("_order")); }
    }
}
