using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ArchiveWriteBoundaryTests
{
    private static readonly Guid Nonce = new("AB8C0E20-7FD8-47DB-A624-3544F3FCD305");
    private static readonly string Revision = new('A', 64);

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute")]
    [InlineData("a\\b")]
    [InlineData("NUL.txt")]
    [InlineData("dir/COM¹.log")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("hidden\u007fcontrol")]
    [InlineData("a//b")]
    public void Unsafe_names_are_refused_without_normalizing_them(string name) =>
        Assert.Equal(ArchiveWriteReason.InvalidRequest,
            Assert.Throws<ArchiveWriteException>(() => new ArchiveMemberKey(Revision, name, ArchiveMemberType.File, 0)).Reason);

    [Fact]
    public void Identities_keep_case_unicode_and_duplicate_occurrences_distinct()
    {
        var key = new ArchiveMemberKey(Revision, "É.txt", ArchiveMemberType.File, 1);
        Assert.NotEqual(key, new ArchiveMemberKey(Revision, "é.txt", ArchiveMemberType.File, 1));
        Assert.NotEqual(key, new ArchiveMemberKey(Revision, "E\u0301.txt", ArchiveMemberType.File, 1));
        Assert.NotEqual(key, new ArchiveMemberKey(Revision, "É.txt", ArchiveMemberType.File, 0));
        Assert.NotEqual(key, new ArchiveMemberKey(new string('B', 64), "É.txt", ArchiveMemberType.File, 1));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveMemberKey(Revision, new string('\ud800', 1), ArchiveMemberType.File, 0));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveMemberKey(Revision, "valid", ArchiveMemberType.File, -1));
    }

    [Fact]
    public void Budgets_are_cumulative_checked_and_unchanged_after_a_refusal()
    {
        var budget = new ArchiveWriteBudget();
        budget.AddLogicalBytes(ArchiveWritePolicy.MaxLogicalBytes - 1);
        Assert.Throws<ArchiveWriteException>(() => budget.AddLogicalBytes(long.MaxValue));
        Assert.Equal(ArchiveWritePolicy.MaxLogicalBytes - 1, budget.LogicalBytes);
        budget.AddLogicalBytes(1);
        Assert.Throws<ArchiveWriteException>(() => budget.AddLogicalBytes(1));
        Assert.Throws<ArchiveWriteException>(() => budget.AddMetadata(1, 1, -1));
        Assert.Equal(0, budget.MetadataBytes);
        for (int i = 0; i < 128; i++) budget.AddMetadata(1 << 20, 1 << 20);
        Assert.Throws<ArchiveWriteException>(() => budget.AddMetadata(1, 1));
        for (int i = 0; i < ArchiveWritePolicy.MaxMembers; i++) budget.AddMember();
        Assert.Throws<ArchiveWriteException>(budget.AddMember);
    }

    [Fact]
    public void Unknown_profiles_and_ineffective_options_are_refused()
    {
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile((ArchiveWriteFormat)999));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.Tar, version: 2));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.Tar, compressionLevel: 1));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.SevenZip, dictionaryBytes: int.MaxValue));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.Gzip, dictionaryBytes: 16 << 20));
    }

    [Fact]
    public void Framing_handles_short_reads_and_preserves_order()
    {
        using var encoded = new MemoryStream();
        var sender = new ArchiveWorkerProtocol(Nonce, 100);
        sender.Write(encoded, ArchiveWorkerMessage.Hello, "hello"u8);
        sender.Write(encoded, ArchiveWorkerMessage.Data, "payload"u8);
        encoded.Position = 0;
        using var shortReads = new ShortReads(encoded);
        var receiver = new ArchiveWorkerProtocol(Nonce, 100);
        Assert.Equal("hello", Encoding.UTF8.GetString(receiver.Read(shortReads).Payload));
        Assert.Equal("payload", Encoding.UTF8.GetString(receiver.Read(shortReads).Payload));
        Assert.Throws<EndOfStreamException>(() => receiver.Read(shortReads));
        encoded.Position = 0;
        Assert.Throws<ArchiveWriteException>(() => receiver.Read(encoded));
    }

    [Theory]
    [InlineData(0)] // magic
    [InlineData(4)] // version
    [InlineData(6)] // opcode
    [InlineData(7)] // flags
    [InlineData(8)] // nonce
    [InlineData(24)] // sequence
    [InlineData(32)] // reserved
    public void Invalid_headers_are_rejected_before_a_payload_read(int offset)
    {
        var bytes = Frame("payload"u8);
        bytes[offset] ^= 0xff;
        using var stream = new MemoryStream(bytes);
        var receiver = new ArchiveWorkerProtocol(Nonce, 100);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => receiver.Read(stream)).Reason);
        Assert.Equal(ArchiveWorkerProtocol.HeaderBytes, stream.Position);
    }

    [Fact]
    public void Oversized_frames_do_not_allocate_the_claimed_payload_and_poison_the_channel()
    {
        byte[] bytes = Frame([]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), uint.MaxValue);
        using var input = new MemoryStream(bytes);
        var receiver = new ArchiveWorkerProtocol(Nonce, ArchiveWritePolicy.MaxPhysicalBytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ArchiveWriteException>(() => receiver.Read(input));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 64 << 10);
        input.Position = 0;
        Assert.Throws<ArchiveWriteException>(() => receiver.Read(input));
        Assert.Equal(0, input.Position);
    }

    [Fact]
    public void Replayed_frames_and_cumulative_payload_overruns_are_refused()
    {
        byte[] bytes = Frame("12345"u8);
        using var input = new MemoryStream(bytes.Concat(bytes).ToArray());
        var receiver = new ArchiveWorkerProtocol(Nonce, 10);
        receiver.Read(input);
        Assert.Throws<ArchiveWriteException>(() => receiver.Read(input));
        using var output = new MemoryStream();
        var sender = new ArchiveWorkerProtocol(Nonce, 5);
        sender.Write(output, ArchiveWorkerMessage.Data, "12345"u8);
        long length = output.Length;
        Assert.Throws<ArchiveWriteException>(() => sender.Write(output, ArchiveWorkerMessage.Data, "1"u8));
        Assert.Equal(length, output.Length);
    }

    [Fact]
    public void Every_truncation_and_mutated_length_is_bounded()
    {
        byte[] valid = Frame("test data"u8);
        for (int length = 0; length < valid.Length; length++)
        {
            using var truncated = new MemoryStream(valid, 0, length);
            Assert.Throws<EndOfStreamException>(() => new ArchiveWorkerProtocol(Nonce, 100).Read(truncated));
        }
        foreach (uint length in new uint[] { 65_537, 1_048_577, int.MaxValue, uint.MaxValue })
        {
            byte[] mutated = (byte[])valid.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(mutated.AsSpan(28), length);
            using var input = new MemoryStream(mutated);
            Assert.Throws<ArchiveWriteException>(() => new ArchiveWorkerProtocol(Nonce, 100).Read(input));
        }
    }

    [Fact]
    public void Stage_proxy_checks_length_offsets_and_amplification_before_io()
    {
        using var stage = new MemoryStream();
        var proxy = new ArchiveStageProxy(stage, 20, 30);
        proxy.WriteAt(0, "1234567890"u8);
        proxy.WriteAt(0, "1234567890"u8);
        proxy.WriteAt(0, "1234567890"u8);
        var before = stage.ToArray();
        Assert.Throws<ArchiveWriteException>(() => proxy.WriteAt(0, "x"u8));
        Assert.Equal(before, stage.ToArray());
        Assert.Throws<ArchiveWriteException>(() => proxy.SetLength(0)); // a rejected worker cannot continue

        foreach (long offset in new long[] { -1, 21, long.MaxValue })
        {
            using var empty = new MemoryStream();
            var other = new ArchiveStageProxy(empty, 20, 30);
            Assert.Throws<ArchiveWriteException>(() => other.WriteAt(offset, "x"u8));
            Assert.Equal(0, empty.Length);
        }
        using var sparse = new MemoryStream();
        var bounded = new ArchiveStageProxy(sparse, 20, 30);
        bounded.SetLength(20);
        bounded.SetLength(0);
        Assert.Throws<ArchiveWriteException>(() => bounded.SetLength(20));
        Assert.Equal(0, sparse.Length);
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Tar)]
    [InlineData(ArchiveWriteFormat.SevenZip)]
    [InlineData(ArchiveWriteFormat.Zip)]
    public async Task Explicit_profiles_never_fall_back_to_the_legacy_zip_writer(ArchiveWriteFormat format)
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "untouched.zip");
        byte[] original = "this must not be opened as ZIP"u8.ToArray();
        File.WriteAllBytes(path, original);
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), dir.Dir("journals"));
        var job = manager.Submit(new JobRequest
        {
            Kind = JobKind.ArchiveUpdate,
            Archive = new ArchivePlan(path, ArchiveBaseline.Of(path), [], Profile: new ArchiveWriteProfile(format)),
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!job.State.IsFinished()) await Task.Delay(10, timeout.Token);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains(job.Issues, issue => issue.Message.Contains("qualified", StringComparison.Ordinal));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(dir.Path, ".filecat-*"));
    }

    private static byte[] Frame(ReadOnlySpan<byte> payload)
    {
        using var output = new MemoryStream();
        new ArchiveWorkerProtocol(Nonce, 100).Write(output, ArchiveWorkerMessage.Data, payload);
        return output.ToArray();
    }

    private sealed class ShortReads(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => inner.Read(buffer[..Math.Min(buffer.Length, 1)]);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
