using System.Runtime.InteropServices;
using System.Text;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>NTFS's LZNT1: FileCat's decompressor against Windows' own compressor, and against garbage.</summary>
public sealed partial class Lznt1Tests
{
    [Fact]
    public void Whatever_Windows_compresses_comes_back_exactly()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' own LZNT1 compressor is the reference.");
        var random = new Random(7);
        var samples = new List<byte[]>
        {
            Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 3000).Select(i => $"line {i:D6}: the quick brown fox\n"))),
            Enumerable.Repeat((byte)0, 65535).Append((byte)1).ToArray(), // nearly all zeros: long back-references (all zeros is stored sparse)
            Enumerable.Range(0, 65536).Select(i => (byte)(i % 7)).ToArray(),
            Enumerable.Range(0, 65536).Select(_ => (byte)random.Next(256)).ToArray(), // incompressible: stored chunks
            Encoding.UTF8.GetBytes("abc"),
            Enumerable.Range(0, 40000).Select(i => i % 5000 < 2500 ? (byte)'x' : (byte)random.Next(256)).ToArray(),
        };
        foreach (var sample in samples)
        {
            var compressed = Compress(sample);
            var output = new byte[sample.Length];
            int n = Lznt1.Decompress(compressed, output);
            Assert.Equal(sample.Length, n);
            Assert.Equal(sample, output);
        }
    }

    [Fact]
    public void Garbage_is_refused_or_bounded_never_read_past()
    {
        var random = new Random(11);
        var output = new byte[65536];
        for (int round = 0; round < 3000; round++)
        {
            var input = new byte[random.Next(1, 5000)];
            random.NextBytes(input);
            if (round % 2 == 0) input[1] |= 0x80; // mostly "compressed" chunks
            try
            {
                int n = Lznt1.Decompress(input, output);
                Assert.InRange(n, 0, output.Length);
            }
            catch (InvalidDataException) { }
        }
    }

    private static byte[] Compress(byte[] data)
    {
        const ushort Lznt1Format = 2;
        Assert.Equal(0, RtlGetCompressionWorkSpaceSize(Lznt1Format, out uint workspaceSize, out _));
        var workspace = new byte[workspaceSize];
        var output = new byte[data.Length + data.Length / 8 + 4096];
        Assert.Equal(0, RtlCompressBuffer(Lznt1Format, data, (uint)data.Length, output, (uint)output.Length, 4096, out uint size, workspace));
        return output[..(int)size];
    }

    [LibraryImport("ntdll.dll")]
    private static partial int RtlGetCompressionWorkSpaceSize(ushort format, out uint bufferWorkSpaceSize, out uint fragmentWorkSpaceSize);

    [LibraryImport("ntdll.dll")]
    private static partial int RtlCompressBuffer(ushort format, byte[] uncompressed, uint uncompressedSize, byte[] compressed, uint compressedSize,
        uint chunkSize, out uint finalCompressedSize, byte[] workspace);
}
