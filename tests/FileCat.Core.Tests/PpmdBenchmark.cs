using System.Diagnostics;
using FileCat.Archives;
using FileCat.Archives.Rar;

namespace FileCat.Core.Tests;

/// <summary>
/// Throughput of FileCat's PPMd variant H decoding on RAR 2.9 data: synthetic members made by Rar3PpmdTestStream (text
/// in a large model, and noise that restarts a 1 MiB model again and again) and libarchive's real RAR 3 PPMd archive.
/// Set FILECAT_PPMD_BENCH=1 to run; FILECAT_PPMD_BENCH_MB sets the text size (16 MiB by default). Each case is decoded
/// five times and the fastest run is reported, in MiB of output per second, to the test output.
/// </summary>
public sealed class PpmdBenchmark
{
    private const int Runs = 5;

    [Fact]
    public void Rar_ppmd_decoding_throughput()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_PPMD_BENCH") != "1") Assert.Skip("Set FILECAT_PPMD_BENCH=1 to run the PPMd benchmark.");
        int megabytes = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_PPMD_BENCH_MB"), out int mb) && mb > 0 ? mb : 16;

        byte[] text = Text(megabytes << 20, 7);
        Report($"RAR PPMd text, order 6, 16 MiB model, {megabytes} MiB", text, Encode(text, order: 6, modelMegabytes: 16));
        byte[] deep = Text(megabytes << 19, 8);
        Report($"RAR PPMd text, order 16, 64 MiB model, {megabytes / 2} MiB", deep, Encode(deep, order: 16, modelMegabytes: 64));
        byte[] noise = new byte[4 << 20];
        new Random(9).NextBytes(noise);
        Report("RAR PPMd noise, order 4, 1 MiB model (restarts), 4 MiB", noise, Encode(noise, order: 4, modelMegabytes: 1));

        string real = Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", "libarchive", "test_read_format_rar_ppmd_lzss_conversion.rar");
        long bytes = 0;
        var best = TimeSpan.MaxValue;
        for (int run = 0; run < Runs * 4; run++)
        {
            var clock = Stopwatch.StartNew();
            bytes = 0;
            using (var reader = RarReaderTests.OpenOurs(real))
            {
                foreach (var member in reader.List(_ => { }, CancellationToken.None).Where(m => m.Kind == MemberKind.File).ToList())
                {
                    using var content = reader.Open(member.Index, CancellationToken.None);
                    bytes += Drain(content);
                }
            }
            clock.Stop();
            if (clock.Elapsed < best) best = clock.Elapsed;
        }
        Log($"libarchive test_read_format_rar_ppmd_lzss_conversion.rar ({bytes:N0} bytes out): best {best.TotalMilliseconds:F1} ms, {bytes / best.TotalSeconds / (1 << 20):F1} MiB/s");
    }

    private static byte[] Encode(byte[] content, int order, int modelMegabytes)
    {
        var stream = new Rar3PpmdTestStream();
        stream.StartBlock(order, modelMegabytes, reset: true);
        stream.Literals(content);
        stream.EndFile();
        return stream.ToArray();
    }

    private static void Report(string name, byte[] content, byte[] packed)
    {
        var best = TimeSpan.MaxValue;
        for (int run = 0; run < Runs; run++)
        {
            var clock = Stopwatch.StartNew();
            byte[] output = Decode(packed, content.Length);
            clock.Stop();
            Assert.True(output.AsSpan().SequenceEqual(content), name + ": decoded bytes differ");
            if (clock.Elapsed < best) best = clock.Elapsed;
        }
        Log($"{name} ({packed.Length:N0} bytes packed): best {best.TotalMilliseconds:F0} ms, {content.Length / best.TotalSeconds / (1 << 20):F2} MiB/s");
    }

    private static byte[] Decode(byte[] packed, int size)
    {
        var bits = new RarBitReader();
        bits.Reset(new MemoryStream(packed));
        var decoder = new Rar3Decoder();
        decoder.StartMember(bits, solid: false);
        var member = new RarMember { Name = "x", Kind = MemberKind.File, UnpackedSize = size, Method = 3, Version = 29, Dictionary = 4 << 20 };
        using var stream = RarMemberStream.Decoded(member, decoder, new RarWindow(4 << 20, size), CancellationToken.None);
        var output = new byte[size];
        int total = 0;
        while (total < size)
        {
            int read = stream.Read(output, total, size - total);
            if (read == 0) break;
            total += read;
        }
        Assert.Equal(size, total);
        return output;
    }

    private static long Drain(Stream stream)
    {
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) total += read;
        return total;
    }

    /// <summary>Text from a seeded vocabulary with punctuation and some binary noise.</summary>
    internal static byte[] Text(int size, int seed)
    {
        var random = new Random(seed);
        var words = new byte[2000][];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = new byte[random.Next(1, 12)];
            for (int j = 0; j < words[i].Length; j++) words[i][j] = (byte)('a' + random.Next(26));
        }
        var text = new byte[size];
        int n = 0;
        while (n < size)
        {
            int roll = random.Next(1000);
            if (roll < 5)
            {
                for (int k = random.Next(1, 30); k > 0 && n < size; k--) text[n++] = (byte)random.Next(256);
                continue;
            }
            byte[] word = words[(int)Math.Min(words.Length - 1, Math.Abs(random.NextDouble() * random.NextDouble() * words.Length))];
            for (int k = 0; k < word.Length && n < size; k++) text[n++] = word[k];
            if (n < size) text[n++] = roll < 80 ? (byte)'\n' : roll < 160 ? (byte)',' : (byte)' ';
        }
        return text;
    }

    private static void Log(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);
}
