using System.Buffers.Binary;
using System.Diagnostics;
using FileCat.Archives;
using FileCat.Archives.Rar;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Xunit;

namespace FileCat.Core.Tests;

public sealed class RarFilterBudgetTests
{
    private static readonly byte[] E8Program = Convert.FromHexString(
        "971B012807069808000000D13A101592EC50CB9920B925F0291915530312AE511035592B6004156D4066AB023449043602523E9700");

    [Fact]
    public async Task Chained_filters_have_an_aggregate_work_budget_on_direct_and_public_routes()
    {
        foreach (var (count, expanded) in new[] { (0, 1 << 20), (64, 1 << 20), (256, 1 << 20), (1024, 1 << 20), (8192, 4 << 20) })
        {
            var stream = new Rar3PpmdTestStream();
            stream.StartBlock(4, 1, reset: true);
            for (int i = 0; i < count; i++)
            {
                var bits = new BitWriter();
                if (i == 0) bits.WriteRar3UInt32(0);
                bits.WriteRar3UInt32(0);
                bits.WriteRar3UInt32((uint)expanded);
                if (i == 0) bits.WriteProgram(E8Program);
                byte[] fields = bits.ToArray();
                byte[] record = fields.Length <= 6
                    ? [(byte)((i == 0 ? 0x80 : 0) | 0x20 | (fields.Length - 1)), .. fields]
                    : fields.Length - 7 <= 255
                        ? [(byte)((i == 0 ? 0x80 : 0) | 0x20 | 6), (byte)(fields.Length - 7), .. fields]
                        : throw new Exception("Unexpected filter record size");
                stream.Filter(record);
            }
            stream.Literal(0);
            int left = expanded - 1;
            while (left >= 4)
            {
                int n = Math.Min(left, 259);
                stream.Run(n);
                left -= n;
            }
            while (left-- > 0) stream.Literal(0);
            stream.EndFile();
            byte[] packed = stream.ToArray();
            string folder = Path.Combine(Path.GetTempPath(), "filecat-rar-filter-audit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = Path.Combine(folder, "owned.rar");
                using (var archive = File.Create(path))
                {
                    archive.Write("Rar!\x1A\x07\x00"u8);
                    WriteBlock(archive, 0x73, 0, new byte[6]);
                    using var header = new MemoryStream();
                    using (var writer = new BinaryWriter(header, System.Text.Encoding.UTF8, leaveOpen: true))
                    {
                        writer.Write((uint)packed.Length);
                        writer.Write((uint)expanded);
                        writer.Write((byte)2);
                        writer.Write(Crc32.HashToUInt32(new byte[expanded]));
                        writer.Write((uint)0);
                        writer.Write((byte)29);
                        writer.Write((byte)0x33);
                        writer.Write((ushort)9);
                        writer.Write(0x20u);
                        writer.Write("owned.bin"u8);
                    }
                    WriteBlock(archive, 0x74, (ushort)(0x8000 | (6 << 5)), header.ToArray());
                    archive.Write(packed);
                    WriteBlock(archive, 0x7B, 0x4000, []);
                }
                var directClock = Stopwatch.StartNew();
                using (var reader = RarReader.Open(path))
                using (var member = reader.Open(0, CancellationToken.None))
                {
                    if (count == 8192)
                    {
                        var ex = Assert.Throws<InvalidDataException>(() => member.CopyTo(Stream.Null));
                        Assert.Contains("filter-work limit", ex.Message);
                        Assert.True(RarData.IsDamage(ex));
                    }
                    else
                    {
                        using var decoded = new MemoryStream();
                        member.CopyTo(decoded);
                        Assert.Equal(new byte[expanded], decoded.ToArray());
                    }
                }
                directClock.Stop();

                var providers = new ProviderRegistry();
                providers.Register(new LocalFileSystemProvider());
                var archives = new ArchiveProvider(Path.Combine(folder, "temp"), providers);
                providers.Register(archives);
                var root = archives.GetContainerLocation(path)!;
                var sink = new AuditSink();
                await archives.EnumerateAsync(root, sink, CancellationToken.None);
                var item = archives.GetItemRef(root, Assert.Single(sink.Entries));
                var publicClock = Stopwatch.StartNew();
                if (count == 8192)
                {
                    var ex = Assert.Throws<InvalidDataException>(() => archives.OpenContent(item));
                    Assert.Contains("filter-work limit", ex.Message);
                    Assert.True(RarData.IsDamage(ex));
                    var temp = Path.Combine(folder, "temp");
                    if (Directory.Exists(temp)) Assert.Empty(Directory.GetFiles(temp, "*", SearchOption.AllDirectories));
                }
                else
                {
                    using var content = archives.OpenContent(item);
                    Assert.NotNull(content);
                    Assert.Equal(expanded, content.Length);
                    var decoded = new byte[expanded];
                    Assert.Equal(expanded, content.Read(0, decoded));
                    Assert.Equal(new byte[expanded], decoded);
                }
                publicClock.Stop();
                TestContext.Current.TestOutputHelper?.WriteLine($"filters={count} packed={packed.Length} decoded={expanded} filterWorkBytes={(long)count * expanded} directMs={directClock.ElapsedMilliseconds} publicMs={publicClock.ElapsedMilliseconds}");
                if (count == 8192) Assert.True(publicClock.Elapsed < TimeSpan.FromSeconds(2));
            }
            finally { Directory.Delete(folder, recursive: true); }
        }
    }

    [Fact]
    public void Filter_budget_persists_across_a_solid_member_boundary()
    {
        const int blockSize = 1 << 20;
        var window = new RarWindow(blockSize, blockSize + 1);
        for (int i = 0; i < 1024; i++)
            window.AddFilter(new RarFilter { Type = RarFilterType.E8, Start = 0, Length = blockSize });
        for (int i = 0; i < blockSize; i++) window.Put(0);
        var first = new byte[blockSize];
        Assert.Equal(blockSize, window.ReadOutput(first, blockSize, memberStart: 0, ct: TestContext.Current.CancellationToken));
        Assert.Equal(RarLimits.MaxFilterWorkBytes, window.FilterWorkBytes);
        Assert.Equal(new byte[blockSize], first);

        window.AddFilter(new RarFilter { Type = RarFilterType.E8, Start = blockSize, Length = 1 });
        window.Put(0);
        var ex = Assert.Throws<InvalidDataException>(() => window.ReadOutput(new byte[1], blockSize + 1, memberStart: blockSize, ct: TestContext.Current.CancellationToken));
        Assert.True(RarData.IsDamage(ex));
        Assert.Contains("filter-work limit", ex.Message);
        Assert.Equal(RarLimits.MaxFilterWorkBytes, window.FilterWorkBytes);
        Assert.Equal(blockSize, window.Read);
    }

    private sealed class AuditSink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Entries.Add(entry);
        }
        public void ReportIssue(string message) { }
    }

    private static void WriteBlock(Stream archive, byte type, ushort flags, ReadOnlySpan<byte> body)
    {
        var header = new byte[7 + body.Length];
        header[2] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(3), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), (ushort)header.Length);
        body.CopyTo(header.AsSpan(7));
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)Crc32.HashToUInt32(header.AsSpan(2)));
        archive.Write(header);
    }
}
