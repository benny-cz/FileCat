using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveManifestSidecarTests
{
    private static readonly Guid Nonce = Guid.Parse("45129e41-729b-4b45-bf39-aeb7365a218e");
    private static string Empty => Convert.ToHexString(SHA256.HashData([]));
    private static ArchiveWriteManifest Fixture(ArchiveWriteFormat format = ArchiveWriteFormat.Tar) => new(
        format == ArchiveWriteFormat.Rar5 ? ArchiveWriteProfile.ForRar5() : new(format),
        [new("é/name", ArchiveMemberType.File, 0, 3, Convert.ToHexString(SHA256.HashData([1, 2, 3])), [1, 0, 7]),
            new("dir/", ArchiveMemberType.Directory, 0, 0, Empty, [1, 2]),
            new("é/name", ArchiveMemberType.File, 1, 0, Empty, [])], [1, 2, 3]);
    public static IEnumerable<object[]> Scopes() =>
        from format in Enum.GetValues<ArchiveWriteFormat>() from purpose in Enum.GetValues<ArchiveManifestPurpose>() select new object[] { format, purpose };

    [Fact]
    public void Streamed_hashing_preserves_the_existing_schema_one_canonical_vectors()
    {
        // Independently encoded from the pre-sidecar schema: little-endian lengths/integers and exact UTF-8.
        var manifest = Fixture();
        Assert.Equal("AFC6D235C440C0CA0ED58356A4DE9C736BD861C0DA19C89B08AF4C149DC9301C", manifest.Digest);
        Assert.Equal("1847552740645BEAD11903CF141AC155E32B5F37C5E721C69A890E04B755E654", manifest.ContentDigest);
        Assert.Equal("E4130FBDD87325D9BBBBF7BEE75C4A35B58E397A6FF23FDE6DDBF88760DAA988", manifest.MetadataDigest);
    }

    [Theory]
    [MemberData(nameof(Scopes))]
    public void Every_explicit_profile_and_role_round_trips_without_becoming_verified_before_eof(ArchiveWriteFormat format, ArchiveManifestPurpose purpose)
    {
        var manifest = Fixture(format);
        using var storage = new MemoryStream();
        var info = ArchiveManifestSidecar.Write(storage, Nonce, purpose, manifest, TestContext.Current.CancellationToken);
        Assert.Equal(storage.Length, info.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(storage.ToArray())), info.Sha256);
        storage.Position = 0;
        using (var reader = ArchiveManifestSidecar.OpenRead(storage, info, Nonce, purpose, manifest.Profile, TestContext.Current.CancellationToken))
        {
            Assert.Equal(manifest.Features.ToArray(), reader.Features.ToArray());
            Assert.Equal(manifest.Members.Count, reader.DeclaredMembers);
            Assert.Equal(manifest.LogicalBytes, reader.DeclaredLogicalBytes);
            foreach (var expected in manifest.Members)
            {
                Assert.False(reader.IsComplete); Assert.Null(reader.VerifiedManifestDigest);
                var observed = Assert.IsType<ArchiveManifestMember>(reader.ReadNext());
                Assert.Equal(expected.Name, observed.Name); Assert.Equal(expected.Type, observed.Type);
                Assert.Equal(expected.Occurrence, observed.Occurrence); Assert.Equal(expected.Length, observed.Length);
                Assert.Equal(expected.Sha256, observed.Sha256); Assert.Equal(expected.Metadata.ToArray(), observed.Metadata.ToArray());
            }
            Assert.False(reader.IsComplete);
            Assert.Null(reader.ReadNext());
            Assert.True(reader.IsComplete);
            Assert.Equal(manifest.Digest, reader.VerifiedManifestDigest);
            Assert.Equal(manifest.ContentDigest, reader.VerifiedContentDigest);
            Assert.Equal(manifest.MetadataDigest, reader.VerifiedMetadataDigest);
            reader.Complete(); Assert.Null(reader.ReadNext());
        }
        Assert.True(storage.CanRead); // A reader owns neither the file lease nor the caller's stream lifetime.
    }

    [Fact]
    public void Empty_manifest_requires_its_complete_header_and_terminal_marker()
    {
        var manifest = new ArchiveWriteManifest(new(ArchiveWriteFormat.Tar), [], []);
        using var storage = new MemoryStream();
        var info = ArchiveManifestSidecar.Write(storage, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, TestContext.Current.CancellationToken);
        Assert.Equal(160, info.Length);
        storage.Position = 0;
        using var reader = Open(storage, info, manifest);
        Assert.False(reader.IsComplete); Assert.Null(reader.ReadNext()); Assert.True(reader.IsComplete);
        Assert.Equal(manifest.Digest, reader.VerifiedManifestDigest);
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("purpose")]
    [InlineData("profile")]
    public void A_valid_other_transaction_role_or_profile_is_not_reused(string difference)
    {
        var (manifest, bytes, info) = Encoded();
        using var storage = new MemoryStream(bytes);
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => ArchiveManifestSidecar.OpenRead(storage, info,
            difference == "nonce" ? Guid.NewGuid() : Nonce,
            difference == "purpose" ? ArchiveManifestPurpose.VerifiedOutput : ArchiveManifestPurpose.ExpectedOutput,
            difference == "profile" ? new(ArchiveWriteFormat.TarGzip) : manifest.Profile, TestContext.Current.CancellationToken)).Reason);
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("schema")]
    [InlineData("reserved")]
    [InlineData("members")]
    [InlineData("logical")]
    [InlineData("features")]
    [InlineData("member-length")]
    [InlineData("member-schema")]
    [InlineData("member-type")]
    [InlineData("member-occurrence")]
    [InlineData("member-name")]
    [InlineData("member-metadata")]
    [InlineData("member-digest")]
    [InlineData("manifest-digest")]
    [InlineData("content-digest")]
    [InlineData("metadata-digest")]
    [InlineData("end")]
    [InlineData("extra")]
    public void Structural_and_semantic_corruption_is_refused_even_with_a_repaired_external_checksum(string mutation)
    {
        var (manifest, original, _) = Encoded(); byte[] bytes = original.ToArray();
        int prefix = ArchiveManifestSidecar.HeaderBytes + manifest.Features.Length, member = prefix + 4;
        int nameLength = ArchiveWritePolicy.Utf8.GetByteCount(manifest.Members[0].Name);
        switch (mutation)
        {
            case "magic": bytes[0] ^= 1; break;
            case "schema": bytes[8] = 2; break;
            case "reserved": bytes[11] = 1; break;
            case "members": Put(44, int.MaxValue); break;
            case "logical": BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(48), manifest.LogicalBytes + 1); break;
            case "features": Put(56, int.MaxValue); break;
            case "member-length": Put(prefix, int.MaxValue); break;
            case "member-schema": bytes[member] = 2; break;
            case "member-type": Put(member + 6 + nameLength, int.MaxValue); break;
            case "member-occurrence": Put(member + 10 + nameLength, 1); break;
            case "member-name": bytes[member + 6] = 0xFF; break;
            case "member-metadata": bytes[member + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(prefix)) - 1] ^= 1; break;
            case "member-digest": bytes[member + 22 + nameLength] ^= 1; break;
            case "manifest-digest": bytes[60 + manifest.Features.Length] ^= 1; break;
            case "content-digest": bytes[92 + manifest.Features.Length] ^= 1; break;
            case "metadata-digest": bytes[124 + manifest.Features.Length] ^= 1; break;
            case "end": bytes[^1] ^= 1; break;
            case "extra": bytes = [.. bytes, 0]; break;
        }
        using var storage = new MemoryStream(bytes);
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
        { using var reader = Open(storage, Seal(bytes), manifest); Drain(reader); }).Reason);
        void Put(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
    }

    [Fact]
    public void Every_truncation_and_a_file_truncated_after_open_remain_unverified()
    {
        var (manifest, bytes, info) = Encoded();
        for (int length = 0; length < bytes.Length; length++)
        {
            using var truncated = new MemoryStream(bytes, 0, length);
            Assert.Throws<ArchiveWriteException>(() => { using var reader = Open(truncated, info, manifest); Drain(reader); });
        }
        for (int length = ArchiveManifestSidecar.HeaderBytes + manifest.Features.Length; length < bytes.Length; length++)
        {
            using var changed = new MemoryStream(bytes.ToArray(), writable: true);
            using var reader = Open(changed, info, manifest);
            changed.SetLength(length);
            Assert.Throws<ArchiveWriteException>(() => Drain(reader));
            Assert.False(reader.IsComplete); Assert.Null(reader.VerifiedManifestDigest);
            Assert.Throws<ArchiveWriteException>(() => reader.ReadNext());
        }
    }

    [Fact]
    public void The_external_file_digest_is_checked_separately_from_all_semantic_digests()
    {
        var (manifest, bytes, info) = Encoded();
        using var input = new MemoryStream(bytes);
        using var reader = Open(input, new(info.Length, new string('A', 64)), manifest);
        foreach (var _ in manifest.Members) Assert.NotNull(reader.ReadNext());
        Assert.Throws<ArchiveWriteException>(() => reader.Complete());
        Assert.False(reader.IsComplete); Assert.Null(reader.VerifiedManifestDigest);
    }

    [Fact]
    public void A_partial_or_cancelled_scan_cannot_resume_or_claim_verification()
    {
        var (manifest, bytes, info) = Encoded();
        using var input = new MemoryStream(bytes);
        using var reader = Open(input, info, manifest);
        Assert.NotNull(reader.ReadNext());
        Assert.Throws<ArchiveWriteException>(() => reader.Complete());
        Assert.Throws<ArchiveWriteException>(() => reader.ReadNext());
        Assert.False(reader.IsComplete);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var second = new MemoryStream(bytes);
        using var cancelled = ArchiveManifestSidecar.OpenRead(second, info, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest.Profile, stop.Token);
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => cancelled.ReadNext());
        Assert.Throws<ArchiveWriteException>(() => cancelled.ReadNext());
        Assert.Null(cancelled.VerifiedManifestDigest);
    }

    [Fact]
    public void Precancelled_or_nonempty_output_is_not_modified_and_reader_leaves_ownership_with_the_caller()
    {
        var manifest = Fixture(); using var output = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => ArchiveManifestSidecar.Write(output, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, new(true)));
        Assert.Equal(0, output.Length);
        output.WriteByte(42); output.Position = 0;
        Assert.Throws<ArchiveWriteException>(() => ArchiveManifestSidecar.Write(output, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 42 }, output.ToArray());
        Assert.Throws<ArchiveWriteException>(() => new ArchiveManifestSidecarInfo(ArchiveWritePolicy.MaxMetadataBytes + 1, Empty));
    }

    [Fact]
    public void Corrupt_declared_record_sizes_are_refused_before_large_allocation()
    {
        var (manifest, bytes, _) = Encoded();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(ArchiveManifestSidecar.HeaderBytes + manifest.Features.Length), int.MaxValue);
        using var input = new MemoryStream(bytes);
        using var reader = Open(input, Seal(bytes), manifest);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ArchiveWriteException>(() => reader.ReadNext());
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1 << 20);
    }

    [Fact]
    public void Cancellation_during_write_does_not_seal_or_truncate_the_partial_output_for_retry()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var output = new CancelOnWrite(stop);
        Assert.Throws<OperationCanceledException>(() => ArchiveManifestSidecar.Write(output, Nonce,
            ArchiveManifestPurpose.ExpectedOutput, Fixture(), stop.Token));
        Assert.InRange(output.Length, 1, ArchiveManifestSidecar.FixedBytes - 1);
        byte[] partial = output.ToArray(); output.Position = 0;
        Assert.Throws<ArchiveWriteException>(() => ArchiveManifestSidecar.Write(output, Nonce,
            ArchiveManifestPurpose.ExpectedOutput, Fixture(), TestContext.Current.CancellationToken));
        Assert.Equal(partial, output.ToArray());
    }

    [Fact]
    public void An_io_failure_cannot_resume_the_same_reader_or_expose_verified_digests()
    {
        var (manifest, bytes, info) = Encoded();
        using var input = new FailOnRead(bytes);
        using var reader = Open(input, info, manifest);
        input.Fail = true;
        Assert.Throws<IOException>(() => reader.ReadNext());
        input.Fail = false;
        Assert.Throws<ArchiveWriteException>(() => reader.ReadNext());
        Assert.False(reader.IsComplete); Assert.Null(reader.VerifiedManifestDigest);
    }

    private sealed class CancelOnWrite(CancellationTokenSource stop) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) { base.Write(buffer); stop.Cancel(); }
    }
    private sealed class FailOnRead(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Fail;
        public override int Read(Span<byte> buffer) => Fail ? throw new IOException("Owned sidecar IO failure") : base.Read(buffer);
    }

    [Fact]
    public void Streaming_review_does_not_retain_decoded_member_dtos_or_their_metadata_blobs()
    {
        var members = Enumerable.Range(0, 512).Select(i => new ArchiveManifestMember("f" + i, ArchiveMemberType.File, 0, 0, Empty, new byte[16384])).ToArray();
        var manifest = new ArchiveWriteManifest(new(ArchiveWriteFormat.Tar), members, []);
        using var storage = new MemoryStream();
        var info = ArchiveManifestSidecar.Write(storage, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, TestContext.Current.CancellationToken);
        storage.Position = 0;
        using var reader = Open(storage, info, manifest);
        var references = new List<WeakReference<ArchiveManifestMember>>();
        for (int i = 0; i < members.Length; i++) references.Add(ReadOne(reader));
        reader.Complete();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, reference => Assert.False(reference.TryGetTarget(out _)));
        Assert.Equal(manifest.Digest, reader.VerifiedManifestDigest);
    }

    [Fact]
    public void Aggregate_name_policy_is_independent_of_encoded_byte_and_heap_counters()
    {
        var budget = new ArchiveWriteBudget();
        budget.AddNameChars(ArchiveWritePolicy.MaxRetainedNameChars - 1); budget.AddNameChars(1);
        Assert.Equal(ArchiveWritePolicy.MaxRetainedNameChars, budget.NameChars);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => budget.AddNameChars(1)).Reason);
        Assert.Equal(ArchiveWritePolicy.MaxRetainedNameChars, budget.NameChars);
        Assert.Equal(0, budget.MetadataBytes); Assert.Equal(0, budget.RetainedHeapBytes);
    }

    [Fact]
    public void Streaming_discard_does_not_reset_aggregate_name_or_metadata_policy()
    {
        var (manifest, encoded, _) = Encoded();
        byte[] header = encoded[..(ArchiveManifestSidecar.HeaderBytes + manifest.Features.Length)];
        const int count = 4000;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(44), count);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(48), 0);
        string name = new('a', ArchiveWritePolicy.MaxNameChars);
        byte[] record = ArchiveAssessmentWire.Member(new(name, ArchiveMemberType.File, 0, 0, Empty, []));
        byte[] frame = new byte[4 + record.Length]; record.CopyTo(frame, 4);
        BinaryPrimitives.WriteInt32LittleEndian(frame, record.Length);
        using var input = new RepeatingSidecar(header, frame, count, 4 + 10 + name.Length);
        using var reader = Open(input, new(input.Length, Empty), manifest);
        int seen = 0;
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() =>
        { while (reader.ReadNext() is not null) seen++; }).Reason);
        Assert.InRange(seen, 1, count - 1);
        Assert.False(reader.IsComplete); Assert.Null(reader.VerifiedManifestDigest);
        Assert.Throws<ArchiveWriteException>(() => reader.ReadNext());
    }

    private sealed class RepeatingSidecar(byte[] header, byte[] frame, int count, int occurrenceOffset) : Stream
    {
        private long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => header.Length + (long)frame.Length * count + 4;
        public override long Position { get => position; set => position = value is >= 0 && value <= Length ? value : throw new ArgumentOutOfRangeException(nameof(value)); }
        public override int Read(byte[] buffer, int offset, int length) => Read(buffer.AsSpan(offset, length));
        public override int Read(Span<byte> buffer)
        {
            int written = 0;
            while (!buffer.IsEmpty && position < Length)
            {
                ReadOnlySpan<byte> source; int offset;
                if (position < header.Length) { source = header; offset = (int)position; }
                else if (position < Length - 4)
                {
                    long relative = position - header.Length;
                    BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(occurrenceOffset), (int)(relative / frame.Length));
                    source = frame; offset = (int)(relative % frame.Length);
                }
                else { source = "FCME"u8; offset = (int)(position - (Length - 4)); }
                int take = Math.Min(buffer.Length, source.Length - offset);
                source.Slice(offset, take).CopyTo(buffer); buffer = buffer[take..]; position += take; written += take;
            }
            return written;
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(Position + offset), SeekOrigin.End => checked(Length + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int length) => throw new NotSupportedException();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ArchiveManifestMember> ReadOne(ArchiveManifestSidecarReader reader) => new(reader.ReadNext()!);
    private static ArchiveManifestSidecarReader Open(Stream input, ArchiveManifestSidecarInfo info, ArchiveWriteManifest manifest) =>
        ArchiveManifestSidecar.OpenRead(input, info, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest.Profile, TestContext.Current.CancellationToken);
    private static ArchiveManifestSidecarInfo Seal(byte[] bytes) => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
    private static void Drain(ArchiveManifestSidecarReader reader) { while (reader.ReadNext() is not null) { } }
    private static (ArchiveWriteManifest Manifest, byte[] Bytes, ArchiveManifestSidecarInfo Info) Encoded()
    {
        var manifest = Fixture(); using var storage = new MemoryStream();
        var info = ArchiveManifestSidecar.Write(storage, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, TestContext.Current.CancellationToken);
        return (manifest, storage.ToArray(), info);
    }

    [Fact]
    public void A_fresh_file_handle_reads_persisted_evidence_and_does_not_authorize_the_current_pathname()
    {
        using var root = new TempDir();
        string path = Path.Combine(root.Path, "owned-manifest.fcm"), replacement = Path.Combine(root.Path, "replacement.fcm");
        var manifest = Fixture(); ArchiveManifestSidecarInfo info;
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            info = ArchiveManifestSidecar.Write(output, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, TestContext.Current.CancellationToken);
            output.Flush(flushToDisk: true);
        }
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = Open(input, info, manifest);
        File.WriteAllText(replacement, "a later occupant must not be cleaned up as our manifest");
        File.Move(path, Path.Combine(root.Path, "held-original.fcm"));
        File.Move(replacement, path);
        Drain(reader);
        Assert.Equal(manifest.Digest, reader.VerifiedManifestDigest); // Exact bytes consumed through the old held stream.
        Assert.Equal("a later occupant must not be cleaned up as our manifest", File.ReadAllText(path));
    }
}
