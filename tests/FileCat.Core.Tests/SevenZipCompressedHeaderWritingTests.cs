using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class SevenZipCompressedHeaderWritingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compressed_headers_handle_empty_and_directory_only_archives(bool directory)
    {
        using var stage = new MemoryStream();
        var members = directory ? new[] { new SevenZipWriteEntry("empty-界", ArchiveMemberType.Directory, new(1700000000, 123456700), 0x1ed) } : [];
        var result = SevenZipArchiveWriter.Create(stage, members, new Dictionary<int, SevenZipInputDescriptor>(),
            _ => throw new InvalidOperationException(), new(SevenZipCodec.Copy), Ct, SevenZipHeaderMode.Lzma2);
        Assert.Equal(SevenZipHeaderMode.Lzma2, result.HeaderMode);
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, "empty.7z"); File.WriteAllBytes(path, stage.ToArray());
        using var reader = SharpArchiveReader.Open(path); var actual = reader.List(message => Assert.Fail(message), Ct).ToArray();
        Assert.Equal(directory ? 1 : 0, actual.Length);
        if (directory) { Assert.Equal("empty-界", actual[0].Path.TrimEnd('/')); Assert.Equal(MemberKind.Directory, actual[0].Kind); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Headers_larger_than_the_initial_reader_buffer_stream_through_bounded_writes_and_preserve_metadata(bool compressed)
    {
        var random = new Random(42829301); var entries = new List<SevenZipWriteEntry>(); byte[] name = new byte[150];
        for (int i = 0; i < 2048; i++)
        {
            random.NextBytes(name);
            entries.Add(new($"dir-{i:0000}-" + Convert.ToHexString(name), ArchiveMemberType.Directory,
                new(1700000000 + i, 123456700), i % 2 == 0 ? 0x1ed : 0x1c0, Created: i % 3 == 0 ? new(0, 100) : null,
                Accessed: i % 5 == 0 ? new(-1, 200) : null));
        }
        Assert.True(entries.Sum(e => e.Name.Length * 2L + 2) > 1 << 20);
        using var stage = new BoundedStage();
        SevenZipArchiveWriter.Create(stage, entries, new Dictionary<int, SevenZipInputDescriptor>(),
            _ => throw new InvalidOperationException(), new(SevenZipCodec.Copy), Ct, compressed ? SevenZipHeaderMode.Lzma2 : SevenZipHeaderMode.Raw);
        Assert.True(stage.Writes > 2); Assert.True(stage.MaximumWrite <= ArchiveIoAuthority.MaximumReadBytes);
        byte[] bytes = stage.ToArray();
        long next = 32 + BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(12));
        Assert.Equal(compressed ? 0x17 : 1, bytes[(int)next]);
        using var dir = new TempDir(); string path = Path.Combine(dir.Path, "many.7z"); File.WriteAllBytes(path, bytes);
        using var reader = SharpArchiveReader.Open(path); var actual = reader.List(message => Assert.Fail(message), Ct).ToArray();
        Assert.Equal(entries.Count, actual.Length);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].Name, actual[i].Path.TrimEnd('/')); Assert.Equal(MemberKind.Directory, actual[i].Kind);
            Assert.Equal(entries[i].Modified.ToDateTimeOffset().UtcDateTime, actual[i].ModifiedUtc);
        }
        string? root = Environment.GetEnvironmentVariable("FILECAT_SEVENZIP_CODEC_EXPORT_ROOT");
        if (root is not null)
        {
            Assert.True(Path.IsPathFullyQualified(root)); string folder = Path.Combine(root, compressed ? "header-compressed-many-dirs" : "header-raw-many-dirs"); Directory.CreateDirectory(folder);
            using (var output = new FileStream(Path.Combine(folder, "members.7z"), FileMode.CreateNew)) stage.WriteTo(output);
            File.WriteAllText(Path.Combine(folder, "members.json"), JsonSerializer.Serialize(entries.Select(entry => new
            {
                name = entry.Name, directory = true, mode = entry.Mode, length = 0, sha256 = Convert.ToHexString(SHA256.HashData([])),
                mtime_seconds = entry.Modified.Seconds, mtime_nanos = entry.Modified.Nanoseconds,
                ctime_seconds = entry.Created?.Seconds, ctime_nanos = entry.Created?.Nanoseconds,
                atime_seconds = entry.Accessed?.Seconds, atime_nanos = entry.Accessed?.Nanoseconds,
            })));
        }
    }

    [Fact]
    public void Invalid_header_mode_refuses_before_stage_io()
    {
        using var stage = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() =>
            SevenZipArchiveWriter.Create(stage, [], new Dictionary<int, SevenZipInputDescriptor>(),
                _ => throw new InvalidOperationException(), new(SevenZipCodec.Copy), Ct, (SevenZipHeaderMode)99)).Reason);
        Assert.Equal(0, stage.Length);
    }

    private sealed class BoundedStage : Stream
    {
        private readonly MemoryStream _inner = new();
        internal int Writes, MaximumWrite;
        internal byte[] ToArray() => _inner.ToArray();
        internal void WriteTo(Stream output) => _inner.WriteTo(output);
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Assert.True(buffer.Length <= ArchiveIoAuthority.MaximumReadBytes); Writes++; MaximumWrite = Math.Max(MaximumWrite, buffer.Length);
            _inner.Write(buffer);
        }
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
