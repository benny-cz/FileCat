using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Plan §9's huge-hex budgets (FILECAT_HEX_BENCH=1): a warm first page within 250 ms and local random seeks within
/// 100 ms at the 95th percentile (nearest rank), on a sparse file of several terabytes with data in it and on a file of
/// real data, through the viewer's reader (F3: the file's content source) and the editor's (F4: the protected file
/// under its patch overlay). The file of real data was just written, so it is read from the system's cache: warm.
/// </summary>
public sealed partial class HexBenchmark
{
    [Fact]
    public void Huge_files_open_at_once_and_random_seeks_stay_quick()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The editor's protected file is Windows'.");
        if (Environment.GetEnvironmentVariable("FILECAT_HEX_BENCH") is not { Length: > 0 }) Assert.Skip("Set FILECAT_HEX_BENCH=1 to measure huge-file hex access.");
        var log = TestContext.Current.TestOutputHelper;
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-hexbench", Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            var rng = new Random(1601);
            // 4 TiB, sparse, with 1,000 islands of 64 KiB of random bytes in it.
            string sparse = Path.Combine(dir, "sparse-4TiB.bin");
            const long Huge = 4L << 40;
            const int Island = 64 * 1024;
            var islands = new List<long>();
            using (var handle = File.OpenHandle(sparse, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                if (!SetSparse(handle)) Assert.Skip("This volume keeps no sparse files.");
                RandomAccess.SetLength(handle, Huge);
                var bytes = new byte[Island];
                for (int i = 0; i < 1000; i++)
                {
                    long at = (long)(rng.NextDouble() * (Huge - Island)) & ~4095L;
                    rng.NextBytes(bytes);
                    RandomAccess.Write(handle, bytes, at);
                    islands.Add(at);
                }
            }
            // 2 GiB of random bytes, every one on the disk.
            string dense = Path.Combine(dir, "dense-2GiB.bin");
            using (var stream = File.Create(dense))
            {
                var block = new byte[4 << 20];
                for (long done = 0; done < 2L << 30; done += block.Length)
                {
                    rng.NextBytes(block);
                    stream.Write(block);
                }
            }

            var failures = new List<string>();
            foreach (var (name, path, at) in new (string, string, Func<long, long>)[]
                     {
                         ("sparse 4 TiB, anywhere (mostly holes)", sparse, length => (long)(rng.NextDouble() * (length - 4096))),
                         ("sparse 4 TiB, in its data", sparse, _ => islands[rng.Next(islands.Count)] + rng.Next(Island - 4096)),
                         ("dense 2 GiB", dense, length => (long)(rng.NextDouble() * (length - 4096))),
                     })
                foreach (var (how, open) in new (string, Func<IContentSource>)[]
                         {
                             ("viewer", () => new FileContentSource(path)),
                             ("editor", () => new HexPatchOverlay(new ProtectedHexFile(path))),
                         })
                {
                    var cold = FirstPage(open);
                    var warm = FirstPage(open);
                    var seeks = Seeks(open, 300, at);
                    double p95 = Rank(seeks, 0.95);
                    log?.WriteLine($"{name}, {how}: first page {cold.TotalMilliseconds:F1} ms, again {warm.TotalMilliseconds:F1} ms; " +
                                   $"300 random seeks p50 {Rank(seeks, 0.5):F2} ms, p95 {p95:F2} ms, p99 {Rank(seeks, 0.99):F2} ms, max {seeks.Max():F2} ms");
                    if (warm > TimeSpan.FromMilliseconds(250)) failures.Add($"{name}, {how}: first page {warm.TotalMilliseconds:F0} ms");
                    if (p95 > 100) failures.Add($"{name}, {how}: seek p95 {p95:F0} ms");
                }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }
        finally
        {
            for (int i = 0; i < 5 && Directory.Exists(dir); i++)
            {
                try { Directory.Delete(dir, true); }
                catch (IOException) { Thread.Sleep(200); }
            }
        }
    }

    /// <summary>Open, read the first 4 KiB, as the view's first frame does.</summary>
    private static TimeSpan FirstPage(Func<IContentSource> open)
    {
        var clock = Stopwatch.StartNew();
        using var reader = new PagedReader(open());
        var page = new byte[4096];
        int read = reader.Read(0, page);
        clock.Stop();
        Assert.True(read > 0);
        return clock.Elapsed;
    }

    /// <summary>Jumps to random places in one open file, as Ctrl+G or dragging the scroll bar does, timing each read.</summary>
    private static List<double> Seeks(Func<IContentSource> open, int count, Func<long, long> at)
    {
        using var reader = new PagedReader(open());
        var buffer = new byte[4096];
        var times = new List<double>(count);
        for (int i = 0; i < count; i++)
        {
            long offset = at(reader.Length);
            var clock = Stopwatch.StartNew();
            reader.Read(offset, buffer);
            times.Add(clock.Elapsed.TotalMilliseconds);
        }
        return times;
    }

    /// <summary>The nearest-rank percentile (§9's sampling rule).</summary>
    private static double Rank(List<double> values, double q)
    {
        var sorted = values.Order().ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];
    }

    private static unsafe bool SetSparse(SafeFileHandle handle) => DeviceIoControl(handle, 0x000900C4, null, 0, null, 0, out _, IntPtr.Zero);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, out uint returned, IntPtr overlapped);
}
