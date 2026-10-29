using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>D-56: what the Cloud Files API says of a placeholder and its sync root, read from synthetic structures.</summary>
public sealed class CloudPlaceholderTests
{
    /// <summary>CF_PLACEHOLDER_STANDARD_INFO: four sizes, pin and in-sync states, two file IDs, the identity's length, then the identity.</summary>
    private static byte[] Placeholder(byte[] identity, uint? declared = null, int pin = 2, int inSync = 1)
    {
        var info = new byte[CloudPlaceholderInfo.FixedSize + identity.Length];
        BinaryPrimitives.WriteInt64LittleEndian(info, 4096);
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(8), 4096);
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(16), 0);
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(24), 12);
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(32), pin);
        BinaryPrimitives.WriteInt32LittleEndian(info.AsSpan(36), inSync);
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(40), 0x0003_0000_0000_1234);
        BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(48), 0x0005_0000_0000_0042);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(56), declared ?? (uint)identity.Length);
        identity.CopyTo(info, CloudPlaceholderInfo.FixedSize);
        return info;
    }

    [Fact]
    public void A_placeholders_standard_information_reads_with_its_identity()
    {
        var problems = new List<string>();
        var info = CloudPlaceholderInfo.Parse(Placeholder("item-42"u8.ToArray()), problems);
        Assert.Empty(problems);
        Assert.NotNull(info);
        Assert.Equal((4096L, 4096L, 0L, 12L), (info.OnDiskDataSize, info.ValidatedDataSize, info.ModifiedDataSize, info.PropertiesSize));
        Assert.Equal((0x0003_0000_0000_1234L, 0x0005_0000_0000_0042L), (info.FileId, info.SyncRootFileId));
        Assert.Equal("item-42"u8.ToArray(), info.Identity);
        Assert.Equal("unpinned (to be freed up: kept online only)", CloudPlaceholderInfo.PinStateText(info.PinState));
        Assert.Equal("yes", CloudPlaceholderInfo.InSyncText(info.InSyncState));
        Assert.Equal("unknown (9)", CloudPlaceholderInfo.PinStateText(9));
    }

    [Fact]
    public void A_placeholders_identity_longer_than_what_came_or_than_allowed_is_said_not_read_past()
    {
        var problems = new List<string>();
        var shorter = CloudPlaceholderInfo.Parse(Placeholder("abc"u8.ToArray(), declared: 300), problems);
        Assert.Equal("abc"u8.ToArray(), shorter!.Identity);
        Assert.Contains("only 3 came with it", Assert.Single(problems), StringComparison.Ordinal);

        problems.Clear();
        var huge = CloudPlaceholderInfo.Parse(Placeholder(new byte[16], declared: 1_000_000), problems);
        Assert.Empty(huge!.Identity);
        Assert.Contains($"more than the {4096:N0}", Assert.Single(problems), StringComparison.Ordinal);

        problems.Clear();
        Assert.Null(CloudPlaceholderInfo.Parse(new byte[59], problems));
        Assert.Contains("fewer than the 60", Assert.Single(problems), StringComparison.Ordinal);
    }

    /// <summary>CF_SYNC_ROOT_STANDARD_INFO: file ID, policies, provider status, name and version (256 UTF-16 characters each), identity length.</summary>
    private static byte[] SyncRoot(string name, string version, uint status, uint identity = 0, int extra = 0)
    {
        var info = new byte[CloudSyncRootInfo.FixedSize + extra];
        BinaryPrimitives.WriteInt64LittleEndian(info, 0x42);
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(8), 1);   // progressive
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(10), 2);  // streaming allowed
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(12), 3);  // always full
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(24), status);
        Encoding.Unicode.GetBytes(name).CopyTo(info, 28);
        Encoding.Unicode.GetBytes(version).CopyTo(info, 540);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(1052), identity);
        return info;
    }

    [Fact]
    public void A_sync_roots_provider_and_policies_read_in_words()
    {
        var problems = new List<string>();
        var root = CloudSyncRootInfo.Parse(SyncRoot("FileCat test", "1.2.3", 1, identity: 4, extra: 4), problems);
        Assert.Empty(problems);
        Assert.NotNull(root);
        Assert.Equal(("FileCat test", "1.2.3", 0x42L), (root.ProviderName, root.ProviderVersion, root.FileId));
        Assert.Equal("idle", root.StatusText);
        Assert.Equal("progressive (the parts read first, then the rest); streaming allowed", root.HydrationText);
        Assert.Equal("always full (folders are always complete on this device)", root.PopulationText);
        Assert.Equal("not connected", (root with { ProviderStatus = 0 }).StatusText);
        Assert.Equal("fetching content, offline (connectivity lost)", (root with { ProviderStatus = 0x48 }).StatusText);
    }

    [Fact]
    public void A_sync_roots_damaged_information_is_said_not_read_past()
    {
        var problems = new List<string>();
        Assert.Null(CloudSyncRootInfo.Parse(new byte[1000], problems));
        Assert.Contains($"fewer than the {1056:N0}", Assert.Single(problems), StringComparison.Ordinal);

        // A name that fills its array without a NUL stops at the array's end; control characters show as '?'.
        problems.Clear();
        var full = CloudSyncRootInfo.Parse(SyncRoot(new string('n', 256), "1\u001b[2J", 0, identity: 70_000), problems);
        Assert.Equal(new string('n', 256), full!.ProviderName);
        Assert.Equal("1?[2J", full.ProviderVersion);
        Assert.Contains("only 0 came with it", Assert.Single(problems), StringComparison.Ordinal);
    }
}
