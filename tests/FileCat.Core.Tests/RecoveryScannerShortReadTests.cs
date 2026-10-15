using System.Buffers.Binary;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

public sealed class RecoveryScannerShortReadTests
{
    [Theory]
    [InlineData("fat32")]
    [InlineData("exfat")]
    public void A_short_positive_directory_read_does_not_hide_deleted_files(string image)
    {
        using var source = new ShortReadSource(RecoveryFixtures.Image(image), image);
        var boot = source.ReadExactly(0, 512);
        var slot = new VolumeSlot(1, 0, source.Length, null);
        var volume = image == "fat32"
            ? FatScanner.Scan(source, boot, slot, TestContext.Current.CancellationToken)
            : ExFatScanner.Scan(source, boot, slot, TestContext.Current.CancellationToken);

        Assert.True(source.ShortReads > 0);
        Assert.NotNull(RecoveryFixtures.Find(volume.Root, "docs/report.txt"));
    }

    [Theory]
    [InlineData("fat32")]
    [InlineData("exfat")]
    public void A_directory_that_stops_early_is_not_treated_as_an_empty_listing(string image)
    {
        using var source = new ShortReadSource(RecoveryFixtures.Image(image), image, stopAfterFirst: true);
        var boot = source.ReadExactly(0, 512);
        var slot = new VolumeSlot(1, 0, source.Length, null);

        Assert.Throws<IOException>(() =>
        {
            if (image == "fat32") FatScanner.Scan(source, boot, slot, TestContext.Current.CancellationToken);
            else ExFatScanner.Scan(source, boot, slot, TestContext.Current.CancellationToken);
        });
    }

    private sealed class ShortReadSource : IBlockSource
    {
        private readonly ImageFileSource _inner;
        private readonly long _rootOffset;
        private readonly int _rootBytes;
        private readonly bool _stopAfterFirst;

        public ShortReadSource(string path, string image, bool stopAfterFirst = false)
        {
            _inner = new ImageFileSource(path);
            _stopAfterFirst = stopAfterFirst;
            var boot = _inner.ReadExactly(0, 512);
            int sector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(11));
            if (image == "fat32")
            {
                int cluster = sector * boot[13];
                int reserved = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(14));
                int fatSectors = (int)BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(36));
                int first = (int)BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(44));
                _rootOffset = ((long)reserved + boot[16] * fatSectors) * sector + (long)(first - 2) * cluster;
                _rootBytes = cluster;
            }
            else
            {
                int cluster = (1 << boot[108]) << boot[109];
                int first = (int)BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(96));
                _rootOffset = (long)BinaryPrimitives.ReadUInt32LittleEndian(boot.AsSpan(88)) * (1 << boot[108]) + (long)(first - 2) * cluster;
                _rootBytes = cluster;
            }
        }

        public string Description => _inner.Description;
        public long Length => _inner.Length;
        public int ShortReads { get; private set; }

        public int Read(long offset, Span<byte> buffer)
        {
            if (_stopAfterFirst && offset >= _rootOffset + 16 && offset < _rootOffset + _rootBytes)
                return 0;
            if (offset >= _rootOffset && offset < _rootOffset + _rootBytes && buffer.Length > 16)
            {
                ShortReads++;
                buffer = buffer[..16];
            }
            return _inner.Read(offset, buffer);
        }

        public void Dispose() => _inner.Dispose();
    }
}
