using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;

// Test-only, portable structured replay. The Linux libFuzzer driver remains the
// coverage-guided lane; this adds bounded CPU-time and state-parser replay on RIDs
// where that native driver has not been qualified.
internal static class StructuredMutation
{
    private static readonly string[] Targets =
    [
        "tar-strict", "sevenzip-strict", "rar5-strict", "sevenzip-password", "rar5-password",
        "gzip-strict", "bzip2-strict", "zstd-strict", "session-record", "capture-record",
        "save-copy-intent", "save-copy-receipt", "journal-record", "journal-state", "manifest-member",
        "worker-frames", "verify-output", "create-inputs", "update-changes", "stream-faults", "member-keys", "repeated-cancel",
        "standalone-streams", "zip-archives", "publication-races", "sevenzip-writer", "rar5-writer"
    ];

    internal static int Run(string target, string seedDirectory, string secondsText, string randomSeedText, string reportPath)
    {
        if (!Targets.Contains(target, StringComparer.Ordinal) ||
            !int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) ||
            seconds is < 1 or > 86400 ||
            !int.TryParse(randomSeedText, NumberStyles.None, CultureInfo.InvariantCulture, out int randomSeed))
            throw new ArgumentException("Choose a known target, 1..86400 seconds, and an unsigned 32-bit seed.");
        string directory = Path.GetFullPath(seedDirectory);
        string report = Path.GetFullPath(reportPath);
        if (!Directory.Exists(directory) || !Directory.Exists(Path.GetDirectoryName(report)) ||
            File.Exists(report) || File.Exists(report + ".tmp"))
            throw new ArgumentException("The seed directory and fresh report parent must exist.");

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !Path.GetFileName(path).Equals("manifest.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        if (files.Length is < 1 or > 256 || Directory.EnumerateDirectories(directory).Any())
            throw new ArgumentException("The corpus must contain 1..256 ordinary seed files without child directories.");
        var corpus = new List<byte[]>();
        var inventory = new List<object>();
        long total = 0;
        foreach (string file in files)
        {
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length is < 1 or > 1048576 ||
                (total += info.Length) > 16 * 1048576)
                throw new ArgumentException("The seed corpus exceeds its file, link, or total budget.");
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length != info.Length) throw new IOException("A seed changed during the bounded read.");
            corpus.Add(bytes);
            inventory.Add(new { name = Path.GetFileName(file), bytes = bytes.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        }
        if (target.EndsWith("-strict", StringComparison.Ordinal) || target.EndsWith("-password", StringComparison.Ordinal))
            Format.Bind(target);

        using var process = Process.GetCurrentProcess();
        TimeSpan initialCpu = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();
        var random = new Random(randomSeed);
        long calls = 0, accepted = 0, refused = 0;
        long maxPrivateBytes = 0;
        bool finished = false;
        string? failure = null;
        // Live managed heap after each published interval: growth across executions is a leak finding (FZ12).
        var heap = new List<object>(); TimeSpan published = TimeSpan.Zero;
        void Publish()
        {
            process.Refresh();
            maxPrivateBytes = Math.Max(maxPrivateBytes, process.PrivateMemorySize64);
            published = timer.Elapsed;
            if (heap.Count == 256) heap.RemoveAt(128);
            heap.Add(new { calls, seconds = Math.Round(timer.Elapsed.TotalSeconds, 1), live_bytes = GC.GetTotalMemory(true),
                committed_bytes = GC.GetGCMemoryInfo().TotalCommittedBytes });
            var value = new { target, random_seed = randomSeed, requested_wall_seconds = seconds,
                elapsed_wall_seconds = timer.Elapsed.TotalSeconds,
                process_cpu_seconds = (process.TotalProcessorTime - initialCpu).TotalSeconds,
                calls, accepted, refused, max_private_bytes = maxPrivateBytes, oracle_skipped = Stats.OracleSkipped,
                managed_heap = heap, seed_inventory = inventory, complete = finished, failure };
            File.WriteAllText(report + ".tmp", JsonSerializer.Serialize(value));
            File.Move(report + ".tmp", report, true);
        }
        try
        {
            foreach (byte[] seed in corpus)
            {
                if (Exercise(target, seed)) accepted++; else refused++;
                calls++;
            }
            Publish();
            while (timer.Elapsed.TotalSeconds < seconds)
            {
                byte[] input = Mutate(corpus[random.Next(corpus.Count)], random);
                try
                {
                    if (Exercise(target, input)) accepted++; else refused++;
                }
                catch
                {
                    string reproducer = report + ".failure.bin";
                    File.WriteAllBytes(reproducer, input);
                    throw;
                }
                calls++;
                if ((calls & 4095) == 0 || timer.Elapsed - published > TimeSpan.FromSeconds(15)) Publish();
            }
            finished = true;
            Publish();
            Console.WriteLine(JsonSerializer.Serialize(new { target, calls, accepted, refused,
                cpu_seconds = (process.TotalProcessorTime - initialCpu).TotalSeconds }));
            return 0;
        }
        catch (Exception error)
        {
            failure = error.ToString();
            Publish();
            throw;
        }
    }

    private static bool Exercise(string target, byte[] input)
    {
        Heap.Settle();
        try
        {
            return target switch
            {
                "session-record" => Session.Exercise(input),
                "capture-record" => Capture.Exercise(input),
                "save-copy-intent" => Intent.Exercise(input),
                "save-copy-receipt" => Receipt.Exercise(input),
                "journal-record" => Journal.Exercise(input),
                "journal-state" => JournalState.Exercise(input),
                "manifest-member" => ManifestMember.Exercise(input),
                "worker-frames" => WorkerFrames.Exercise(input),
                "verify-output" => VerifyOutput.Exercise(input),
                "create-inputs" => CreateInputs.Exercise(input),
                "update-changes" => UpdateChanges.Exercise(input),
                "stream-faults" => StreamFaults.Exercise(input),
                "member-keys" => MemberKeys.Exercise(input),
                "repeated-cancel" => RepeatedCancellation.Exercise(input),
                "standalone-streams" => StandaloneStreams.Exercise(input),
                "zip-archives" => ZipArchives.Exercise(input),
                "publication-races" => PublicationRaces.Exercise(input),
                "sevenzip-writer" => ContainerWriters.Exercise(input, rar: false),
                "rar5-writer" => ContainerWriters.Exercise(input, rar: true),
                _ => Format.Exercise(input, target)
            };
        }
        catch (ArchiveWriteException) { return false; }
    }

    private static byte[] Mutate(byte[] seed, Random random)
    {
        byte[] value = (byte[])seed.Clone();
        int offset = random.Next(value.Length);
        switch (random.Next(8))
        {
            case 0: value[offset] ^= (byte)(1 << random.Next(8)); break;
            case 1: value[offset] = (byte)(random.Next(2) == 0 ? 0 : 255); break;
            case 2: value = value[..(offset + 1)]; break;
            case 3:
                if (value.Length < 1048576)
                    value = [.. value.AsSpan(0, offset), (byte)random.Next(256), .. value.AsSpan(offset)];
                break;
            case 4:
                if (value.Length > 1) value = [.. value.AsSpan(0, offset), .. value.AsSpan(offset + 1)];
                break;
            case 5:
                if (value.Length <= 1048576 - Math.Min(16, value.Length - offset))
                {
                    int length = Math.Min(16, value.Length - offset);
                    value = [.. value.AsSpan(0, offset), .. value.AsSpan(offset, length), .. value.AsSpan(offset)];
                }
                break;
            case 6:
                for (int index = offset; index < Math.Min(value.Length, offset + 8); index++)
                    value[index] = (byte)(random.Next(2) == 0 ? 0 : 255);
                break;
            default:
                int count = Math.Min(random.Next(1, 9), value.Length - offset);
                random.NextBytes(value.AsSpan(offset, count));
                break;
        }
        return value;
    }
}
