using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.Filters;

namespace FileCat.Core.Tests;

public sealed class SevenZipFilterEncodingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IEnumerable<object[]> Filters()
    {
        foreach (int distance in new[] { 1, 2, 7, 256 }) yield return [1, distance];
        for (int filter = 2; filter <= 11; filter++) yield return [filter, 1];
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void Encoding_is_chunk_invariant_keeps_caller_bytes_and_roundtrips_small_reads(int filterId, int distance)
    {
        var filter = (SevenZipFilter)filterId; byte[] data = Corpus(), untouched = (byte[])data.Clone();
        byte[] expected = Encode(data.Length);
        Assert.NotEqual(data, expected);
        foreach (int chunk in new[] { 1, 2, 3, 5, 7, 8191, 65536 }) Assert.Equal(expected, Encode(chunk));
        Assert.Equal(untouched, data);
        foreach (int chunk in new[] { 1, 3, 8191 })
        {
            using var input = Make(filter, false, new MemoryStream(expected, false), distance);
            using var decoded = new MemoryStream(); byte[] buffer = new byte[chunk]; int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { Ct.ThrowIfCancellationRequested(); decoded.Write(buffer, 0, read); Assert.True(decoded.Length <= data.Length); }
            Assert.Equal(data, decoded.ToArray());
        }
        string? root = Environment.GetEnvironmentVariable("FILECAT_SEVENZIP_FILTER_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); Directory.CreateDirectory(root);
            string prefix = Path.Combine(root, filter + "-" + distance);
            using (var file = new FileStream(prefix + ".input", FileMode.CreateNew)) file.Write(data);
            using (var file = new FileStream(prefix + ".encoded", FileMode.CreateNew)) file.Write(expected);
        }

        byte[] Encode(int chunk)
        {
            using var output = new MemoryStream();
            using (var encoder = Make(filter, true, output, distance))
                for (int at = 0; at < data.Length; at += chunk)
                {
                    Ct.ThrowIfCancellationRequested(); encoder.Write(data.AsSpan(at, Math.Min(chunk, data.Length - at)));
                    encoder.Flush(); // must never force an incomplete instruction out before the next write
                }
            return output.ToArray();
        }
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void Filter_graphs_preserve_content_through_lzma2_and_native_fixture_export(int filterId, int distance)
    {
        var filter = (SevenZipFilter)filterId; byte[] data = Corpus(); using var stage = new MemoryStream();
        var entry = new SevenZipWriteEntry("filtered.bin", ArchiveMemberType.File, new(1700000000, 123456700), 0x1a4, 0);
        var descriptor = new SevenZipInputDescriptor(0, data.Length, Convert.ToHexString(SHA256.HashData(data)));
        var manifest = SevenZipArchiveWriter.Create(stage, [entry], new Dictionary<int, SevenZipInputDescriptor> { [0] = descriptor },
            _ => new MemoryStream(data, false), new(SevenZipCodec.Lzma2, DictionaryBytes: 1 << 20, Filter: filter, DeltaDistance: distance), Ct);
        var content = Assert.Single(manifest.Members).Content!;
        Assert.Equal(2, content.Coders.Count); Assert.Single(content.Bindings); Assert.Equal(new SevenZipBinding(1, 0), content.Bindings[0]);
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, "filtered.7z"); File.WriteAllBytes(path, stage.ToArray());
        using var reader = SharpArchiveReader.Open(path); var member = Assert.Single(reader.List(message => Assert.Fail(message), Ct));
        using var decoded = reader.Open(member.Index, Ct); Assert.Equal(descriptor.Sha256, Convert.ToHexString(SHA256.HashData(decoded)));
        string? root = Environment.GetEnvironmentVariable("FILECAT_SEVENZIP_CODEC_EXPORT_ROOT");
        if (root is not null)
        {
            string folder = Path.Combine(root, "filter-" + filter + "-" + distance); Directory.CreateDirectory(folder);
            using (var output = new FileStream(Path.Combine(folder, "members.7z"), FileMode.CreateNew)) stage.WriteTo(output);
            File.WriteAllText(Path.Combine(folder, "members.json"), JsonSerializer.Serialize(new[] { new
            {
                name = entry.Name, directory = false, mode = entry.Mode, length = data.Length, sha256 = descriptor.Sha256,
                mtime_seconds = entry.Modified.Seconds, mtime_nanos = entry.Modified.Nanoseconds,
                ctime_seconds = (long?)null, ctime_nanos = (int?)null, atime_seconds = (long?)null, atime_nanos = (int?)null,
            } }));
        }
    }

    // 7z PPMd has no end marker: the browse reader must end that coder's output at its recorded unpack size, or the next
    // filter transforms an incomplete final unit with decoder overrun bytes. Found by the sevenzip-writer fuzz target: a
    // 61-byte Swap4 member read back with a changed last byte, and FileCat's CRC check would report the archive damaged.
    [Theory]
    [MemberData(nameof(Filters))]
    public void Ppmd_filter_graphs_read_back_exactly_through_the_browse_reader(int filterId, int distance)
    {
        var filter = (SevenZipFilter)filterId;
        foreach (byte[] data in new[] { "a container payload, a container payload, a container payload"u8.ToArray(), Corpus() })
        {
            using var stage = new MemoryStream();
            var entry = new SevenZipWriteEntry("filtered.bin", ArchiveMemberType.File, new(1700000000, 0), 0x1a4, 0);
            var descriptor = new SevenZipInputDescriptor(0, data.Length, Convert.ToHexString(SHA256.HashData(data)));
            var manifest = SevenZipArchiveWriter.Create(stage, [entry], new Dictionary<int, SevenZipInputDescriptor> { [0] = descriptor },
                _ => new MemoryStream(data, false), new(SevenZipCodec.Ppmd7, ModelOrder: 6, ModelMemoryBytes: 1 << 20, Filter: filter,
                    DeltaDistance: distance), Ct);
            Assert.Equal(2, Assert.Single(manifest.Members).Content!.Coders.Count);
            using var dir = new TempDir(); string path = Path.Combine(dir.Path, "filtered.7z"); File.WriteAllBytes(path, stage.ToArray());
            using var reader = SharpArchiveReader.Open(path); var member = Assert.Single(reader.List(message => Assert.Fail(message), Ct));
            using var decoded = reader.Open(member.Index, Ct); Assert.Equal(descriptor.Sha256, Convert.ToHexString(SHA256.HashData(decoded)));
        }
    }

    [Theory]
    [InlineData(2, 2048)]
    [InlineData(2, 65536)]
    [InlineData(4, 1048576)]
    [InlineData(6, 8388608)]
    [InlineData(64, 1048576)]
    public void Ppmd_model_options_produce_exact_content_and_export_independent_sdk_vectors(int order, int memory)
    {
        byte[] data = Corpus(); using var packed = new MemoryStream();
        var options = new SevenZipCodecOptions(SevenZipCodec.Ppmd7, ModelOrder: order, ModelMemoryBytes: memory);
        var content = SevenZipCodecEncoder.Encode(new MemoryStream(data, false), data.Length, packed, options, Ct);
        Assert.Equal(data.Length, content.Length); Assert.True(packed.Length > 5); Assert.Equal(new byte[] { 3, 4, 1 }, content.Coders[0].MethodId);
        Assert.Equal((byte)order, content.Coders[0].Properties[0]);
        Assert.Equal((uint)memory, BinaryPrimitives.ReadUInt32LittleEndian(content.Coders[0].Properties.AsSpan(1)));
        string? root = Environment.GetEnvironmentVariable("FILECAT_SEVENZIP_FILTER_EXPORT_ROOT");
        if (root is not null)
        {
            Directory.CreateDirectory(root); string prefix = Path.Combine(root, "Ppmd7-" + order + "-" + memory);
            using (var file = new FileStream(prefix + ".input", FileMode.CreateNew)) file.Write(data);
            using (var file = new FileStream(prefix + ".encoded", FileMode.CreateNew)) packed.WriteTo(file);
        }
    }

    private static Filter Make(SevenZipFilter kind, bool encode, Stream stream, int distance) => kind switch
    {
        SevenZipFilter.Delta => new DeltaFilter(encode, stream, [(byte)(distance - 1)]),
        SevenZipFilter.X86 => new BCJFilter(encode, stream), SevenZipFilter.Arm => new BCJFilterARM(encode, stream),
        SevenZipFilter.ArmThumb => new BCJFilterARMT(encode, stream), SevenZipFilter.Arm64 => new BCJFilterARM64(encode, stream),
        SevenZipFilter.Ppc => new BCJFilterPPC(encode, stream), SevenZipFilter.Ia64 => new BCJFilterIA64(encode, stream),
        SevenZipFilter.Sparc => new BCJFilterSPARC(encode, stream), SevenZipFilter.RiscV => new BCJFilterRISCV(encode, stream),
        SevenZipFilter.Swap2 => new SwapFilter(encode, stream, 2), SevenZipFilter.Swap4 => new SwapFilter(encode, stream, 4),
        _ => throw new InvalidOperationException(),
    };

    private static byte[] Corpus()
    {
        byte[] bytes = new byte[131089]; new Random(42829300).NextBytes(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x94000001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x90000000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0xeb000001);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 0x48000001);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 0x40000001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 0x000000ef);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 0x00000197);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 0x000180e7);
        bytes[65534] = 0xe8; bytes.AsSpan(65535, 4).Clear();
        bytes[^5] = 0xe9; bytes.AsSpan(bytes.Length - 4).Clear();
        return bytes;
    }
}
