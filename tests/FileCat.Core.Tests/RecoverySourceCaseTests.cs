using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>Unix device names are case-sensitive; admission and cached scans must retain that distinction.</summary>
public sealed class RecoverySourceCaseTests(ITestOutputHelper output)
{
    private sealed class Sink : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) { }
        public void ReportIssue(string message) { }
    }

    private async Task Observe(string mode, bool first)
    {
        if (mode != "repeat" && OperatingSystem.IsWindows())
        {
            Assert.Skip("Case-distinct Unix recovery device paths; Windows device aliases ignore case.");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "filecat-recovery-case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string image = Path.Combine(root, "owned-fat12.img");
        var bytes = new byte[64 * 512];
        bytes[0] = 0xEB; bytes[1] = 0x3C; bytes[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), 512); bytes[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1); bytes[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(17), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(19), 64); bytes[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1); bytes[510] = 0x55; bytes[511] = 0xAA;
        File.WriteAllBytes(image, bytes);
        var provider = new RecoveryProvider();
        var opened = new List<(string Device, string Name)>();
        provider.OpenDevice = (device, name, _) =>
        {
            opened.Add((device, name));
            return new ImageFileSource(image);
        };
        // These are typed device references only; the recording reader opens solely the owned regular image.
        const string a = "/dev/mapper/FC_CASE_control", b = "/dev/mapper/fc_case_control";
        bool denied = false, unchanged = false;
        try
        {
            var chosenA = provider.ForDevice(a, "owned source A", volume: 1, length: bytes.Length);
            Task List(Location location) => provider.EnumerateAsync(location, new Sink(), TestContext.Current.CancellationToken);
            if (mode == "admission")
            {
                if (first) await List(chosenA);
                var other = new Location(Schemes.Recovery, string.Empty, new Location(Schemes.Device, b), "1");
                try { await List(other); }
                catch (UnauthorizedAccessException) { denied = true; }
            }
            else if (mode == "sessions")
            {
                if (first) await List(chosenA);
                var chosenB = provider.ForDevice(b, "owned source B", volume: 1, length: bytes.Length);
                if (!first) await List(chosenA);
                await List(chosenB);
            }
            else
            {
                Assert.Equal("repeat", mode);
                await List(chosenA);
                if (first) provider.Forget(chosenA);
                await List(chosenA);
            }
            unchanged = File.ReadAllBytes(image).SequenceEqual(bytes);
            output.WriteLine("RECOVERY_SOURCE_CASE " + JsonSerializer.Serialize(new
            {
                mode, first, denied, unchanged,
                opened = opened.Select(x => new { device = x.Device, name = x.Name }).ToArray(),
                ownedImageBytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                noDeviceOpened = true,
            }));
            Assert.True(unchanged);
            if (mode == "admission")
            {
                Assert.True(denied, "An unchosen case-distinct Unix device cannot inherit another source's admission or cached scan.");
                Assert.Equal(first ? new[] { a } : [], opened.Select(x => x.Device));
            }
            else if (mode == "sessions")
            {
                Assert.Equal(new[] { a, b }, opened.Select(x => x.Device));
                Assert.Equal(new[] { "owned source A", "owned source B" }, opened.Select(x => x.Name));
            }
            else Assert.Equal(new[] { a }, opened.Select(x => x.Device));
        }
        finally
        {
            provider.CloseAll();
            File.Delete(image); Directory.Delete(root, recursive: false);
            output.WriteLine("RECOVERY_SOURCE_CASE_RESTORATION " + JsonSerializer.Serialize(new { root, ownedRootAbsent = !Directory.Exists(root) }));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task An_unchosen_case_distinct_Unix_device_is_refused(bool scanFirst) => Observe("admission", scanFirst);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Chosen_case_distinct_Unix_devices_have_independent_sessions(bool scanFirst) => Observe("sessions", scanFirst);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task The_same_source_keeps_one_reader_when_repeated_or_rescanned(bool rescan) => Observe("repeat", rescan);
}
