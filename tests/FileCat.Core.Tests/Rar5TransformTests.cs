using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class Rar5TransformTests
{
    private const string Password = "Public-RAR-Transform-暗号-42829316";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Data(int id) => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"RAR transform {id} \0 and binary 界.\n", id + 47)));
    private static ArchiveTransformInput Input(int id, byte[] data) => new(id, data.Length, Sha(data), new ArchiveTimestamp(1700000000 + id, 123456789), 0x180 | id & 0x3f);
    private static Rar5EncryptionSession? Key(ArchiveWriteProfile profile) => ArchiveWriterProfileAdapter.Rar5Encryption(profile,
        profile.Encryption.Mode == ArchiveEncryptionMode.None ? null : new(Password), new(), Ct);
    private static Rar5ReadPasswordSession? ReadKey(ArchiveWriteProfile profile) => profile.Encryption.Mode == ArchiveEncryptionMode.None ? null : new(Password, new(), Ct);
    public static IEnumerable<object[]> Modes() => from method in Enumerable.Range(0, 6) from mode in Enumerable.Range(0, 3) select new object[] { method, mode };

    [Theory] [MemberData(nameof(Modes))]
    public void Parent_planning_and_complete_rewrite_preserve_untouched_bytes_and_exact_metadata(int method, int mode)
    {
        var profile = ArchiveWriteProfile.ForRar5((ArchiveRar5Compression)method, mode == 0 ? null : new((ArchiveEncryptionMode)mode));
        var bytes = new Dictionary<int, byte[]> { [1] = Data(1), [2] = Data(2), [3] = Data(3) };
        ArchiveTransformInput[] originalInputs = bytes.Select(pair => Input(pair.Key, pair.Value)).ToArray();
        ArchiveTransformAddition[] originalAdditions = [new("keep.bin", ArchiveMemberType.File, 1), new("replace.bin", ArchiveMemberType.File, 2),
            new("delete.bin", ArchiveMemberType.File, 3), new("folder/", new ArchiveTransformDirectoryMetadata(new(1700000010, 999999999), 0x1c0))];
        var create = ArchiveRar5TransformPlan.Create(profile, null, originalInputs, [], originalAdditions, Ct);
        using var source = new MemoryStream(); using (var key = Key(profile))
        {
            var written = Rar5RewriteEngine.Rebuild(null, null, profile, source, [], originalAdditions, originalInputs, id => new MemoryStream(bytes[id], false), Ct, encryption: key);
            Assert.Equal(create.Expected.Digest, written.Manifest.Digest);
        }
        source.Position = 0; using var sourceKey = ReadKey(profile); var original = StrictRar5Catalog.Read(source, Ct, sourceKey);
        var assessed = Rar5SemanticCatalog.Assessment(original, profile, Ct); var before = original.Members[0];
        ArchiveMemberKey Member(int index) { var member = assessed.Manifest.Members[index]; return new(assessed.CatalogRevision, member.Name, member.Type, member.Occurrence); }
        var newBytes = Data(10); ArchiveTransformInput[] inputs = [Input(10, newBytes)];
        ArchiveTransformEdit[] edits = [new(ArchiveChangeKind.Rename, Member(0), "renamed-界.bin"), new(ArchiveChangeKind.Replace, Member(1), inputId: 10), new(ArchiveChangeKind.Delete, Member(2))];
        ArchiveTransformAddition[] additions = [new("new-folder/", new ArchiveTransformDirectoryMetadata(new(0, 1), 0x1ed)), new("new-界.bin", ArchiveMemberType.File, 10)];
        var plan = ArchiveRar5TransformPlan.Create(profile, assessed, inputs, edits, additions, Ct);
        Assert.Equal(2, plan.Members.Count(member => member.SourceIndex is not null)); Assert.Equal(2, plan.Members.Count(member => member.InputId == 10));
        using var stage = new MemoryStream(); source.Position = 0; int opened = 0;
        using (var key = Key(profile)) using (var read = ReadKey(profile))
        {
            var result = Rar5RewriteEngine.Rebuild(source, assessed.SourceSha256, profile, stage, edits, additions, inputs,
                id => { Assert.Equal(10, id); opened++; return new MemoryStream(newBytes, false); }, Ct, read, key);
            Assert.Equal(plan.Expected.Digest, result.Manifest.Digest); Assert.Equal(stage.Length, result.PhysicalBytes);
        }
        Assert.Equal(4, opened); Assert.True(source.CanRead); Assert.True(stage.CanWrite);
        stage.Position = 0; using var freshKey = ReadKey(profile); var fresh = StrictRar5Catalog.Read(stage, Ct, freshKey);
        var output = Rar5SemanticCatalog.Assessment(fresh, profile, Ct); Assert.Equal(plan.Expected.Digest, output.Manifest.Digest);
        Assert.Equal(before.PackedSha256, fresh.Members[0].PackedSha256); Assert.Equal(before.Sha256, fresh.Members[0].Sha256);
        Assert.Equal(assessed.Manifest.Members[0].Metadata.ToArray(), output.Manifest.Members[0].Metadata.ToArray());
        Assert.Equal(assessed.Manifest.Members[1].Metadata.ToArray(), output.Manifest.Members[1].Metadata.ToArray());
        Export($"method-{method}-mode-{mode}", source.ToArray(), stage.ToArray(), assessed, output);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Invalid_occurrence_namespace_and_capture_requests_refuse_before_any_payload_open(int kind)
    {
        var profile = ArchiveWriteProfile.ForRar5(); var input = Input(1, Data(1)); var source = Assessment(profile);
        var member = source.Manifest.Members[0]; var key = new ArchiveMemberKey(source.CatalogRevision, member.Name, member.Type, 0);
        ArchiveTransformInput[] inputs = []; ArchiveTransformAddition[] additions = []; ArchiveTransformEdit[] edits = [];
        if (kind == 0) edits = [new(ArchiveChangeKind.Delete, key with { }) , new(ArchiveChangeKind.Delete, key)];
        if (kind == 1) edits = [new(ArchiveChangeKind.Delete, new(Sha([1]), member.Name, member.Type, 0))];
        if (kind == 2) edits = [new(ArchiveChangeKind.Delete, new(source.CatalogRevision, member.Name, member.Type, 1))];
        if (kind == 3) inputs = [input];
        if (kind == 4) { inputs = [input, input]; additions = [new("added", ArchiveMemberType.File, 1)]; }
        if (kind == 5) { inputs = [input]; additions = [new("missing", ArchiveMemberType.File, 2)]; }
        if (kind == 6) { inputs = [input]; additions = [new("f", ArchiveMemberType.File, 1)]; }
        if (kind == 7) additions = [new("f/child", ArchiveMemberType.Directory)];
        Assert.Throws<ArchiveWriteException>(() => ArchiveRar5TransformPlan.Create(profile, source, inputs, edits, additions, Ct));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Whole_source_admission_precedes_deleting_an_unrepresentable_member(int damage)
    {
        var profile = ArchiveWriteProfile.ForRar5(); var original = Assessment(profile); var old = original.Manifest.Members[0];
        var metadata = new ArchiveRar5Metadata(ArchiveMemberType.File, 2, 0x81a4, 1, 3UL << 7);
        if (damage == 0) metadata = new(ArchiveMemberType.File, 2, 0x81a4, 1, (3UL << 7) | (10UL << 10));
        if (damage == 1) { profile = ArchiveWriteProfile.ForRar5(encryption: new(ArchiveEncryptionMode.Data)); metadata = new(ArchiveMemberType.File, 3, 0x81a4, 1, 3UL << 7, encryptionFlags: 0, countPower: 19); }
        if (damage == 2) metadata = new(ArchiveMemberType.Directory, 2, 0x41ed, 1, 0);
        byte[] features = damage == 3 ? [255] : new ArchiveRar5Features().Encode();
        var source = new ArchiveAssessment(new(profile, [new(old.Name, old.Type, 0, old.Length, old.Sha256, metadata.Encode())], features), original.CatalogRevision, original.SourceLength, original.SourceSha256);
        var key = new ArchiveMemberKey(source.CatalogRevision, old.Name, old.Type, 0);
        var refusal = Assert.Throws<ArchiveWriteException>(() => ArchiveRar5TransformPlan.Create(profile, source, [], [new(ArchiveChangeKind.Delete, key)], [], Ct));
        Assert.Equal(damage == 0 ? ArchiveWriteReason.BudgetExceeded : ArchiveWriteReason.UnsupportedFeature, refusal.Reason);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Every_source_payload_is_checked_even_when_deleted_or_replaced(bool replace)
    {
        var profile = ArchiveWriteProfile.ForRar5(ArchiveRar5Compression.Store); using var source = ActualSource(profile);
        source.Position = 0; var catalog = StrictRar5Catalog.Read(source, Ct); var member = catalog.Members[0].Physical;
        byte[] damaged = source.ToArray(); damaged[(int)member.DataOffset] ^= 1; using var corrupt = new MemoryStream(damaged, false); using var stage = new MemoryStream();
        var key = new ArchiveMemberKey(catalog.Physical.Revision, member.Name, member.Type, member.Occurrence); int opens = 0;
        ArchiveTransformInput[] inputs = replace ? [Input(2, Data(2))] : [];
        Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(corrupt, catalog.Physical.Revision, profile, stage,
            [new(replace ? ArchiveChangeKind.Replace : ArchiveChangeKind.Delete, key, inputId: replace ? 2 : null)], [], inputs,
            _ => { opens++; return new MemoryStream(Data(2), false); }, Ct));
        Assert.Equal(0, stage.Length); Assert.Equal(0, opens); Assert.True(corrupt.CanRead);
    }

    [Fact]
    public void Header_changes_after_source_verification_are_detected_even_with_unchanged_retained_payloads()
    {
        var profile = ArchiveWriteProfile.ForRar5(); using var original = ActualSource(profile); byte[] data = original.ToArray();
        using var source = new MemoryStream(data, true); using var output = new MemoryStream(); bool changed = false;
        using var stage = new WriteObserver(output, () => { if (!changed) { data[8] ^= 1; changed = true; } });
        var refusal = Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(source, Sha(original.ToArray()), profile, stage, [], [], [], _ => throw new InvalidOperationException(), Ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, refusal.Reason); Assert.True(changed); Assert.True(source.CanRead); Assert.True(output.Length > 0);
    }

    [Fact]
    public void A_captured_input_change_between_counting_and_writing_refuses()
    {
        var profile = ArchiveWriteProfile.ForRar5(); byte[] data = Data(1), changed = (byte[])data.Clone(); changed[0] ^= 1;
        using var stage = new MemoryStream(); int opened = 0;
        var refusal = Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(null, null, profile, stage, [],
            [new("f", ArchiveMemberType.File, 1)], [Input(1, data)], _ => new MemoryStream(++opened == 1 ? data : changed, false), Ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, refusal.Reason); Assert.Equal(2, opened);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Payload_only_password_cannot_silently_disappear_when_no_files_remain(bool hidden)
    {
        var profile = ArchiveWriteProfile.ForRar5(encryption: new(hidden ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data));
        using var stage = new MemoryStream(); using var key = Key(profile);
        if (hidden)
        {
            var result = Rar5RewriteEngine.Rebuild(null, null, profile, stage, [], [new("d/", ArchiveMemberType.Directory)], [], _ => throw new InvalidOperationException(), Ct, encryption: key);
            stage.Position = 0; using var read = ReadKey(profile); Assert.Equal(result.Manifest.Digest, Rar5SemanticCatalog.Assessment(StrictRar5Catalog.Read(stage, Ct, read), profile, Ct).Manifest.Digest);
        }
        else { Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(null, null, profile, stage, [], [new("d/", ArchiveMemberType.Directory)], [], _ => throw new InvalidOperationException(), Ct, encryption: key)); Assert.Equal(0, stage.Length); }
    }

    [Theory] [InlineData(-1, 123456700, true)] [InlineData(-1, 123456789, false)] [InlineData(4294967296, 100, true)]
    [InlineData(4294967296, 1, false)] [InlineData(0, 1, true)] [InlineData(-11644473601, 0, false)]
    public void New_times_are_exact_or_refused(long seconds, int nanos, bool accepted)
    {
        var profile = ArchiveWriteProfile.ForRar5(); using var stage = new MemoryStream();
        var addition = new ArchiveTransformAddition("d/", new ArchiveTransformDirectoryMetadata(new(seconds, nanos)));
        if (!accepted) { Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(null, null, profile, stage, [], [addition], [], _ => throw new InvalidOperationException(), Ct)); Assert.Equal(0, stage.Length); return; }
        Rar5RewriteEngine.Rebuild(null, null, profile, stage, [], [addition], [], _ => throw new InvalidOperationException(), Ct);
        stage.Position = 0; var member = Assert.Single(StrictRar5Catalog.Read(stage, Ct).Members); Assert.Equal(new ArchiveTimestamp(seconds, nanos), member.Physical.Modified);
    }

    [Fact]
    public void Existing_memory_and_cancellation_refuse_before_work_or_stage_changes()
    {
        var profile = ArchiveWriteProfile.ForRar5();
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => ArchiveRar5TransformPlan.CreateObserved(profile, null, [], [], [], ArchiveWritePolicy.MaxRetainedHeapBytes + 1, Ct)).Reason);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct); stop.Cancel(); using var stage = new MemoryStream();
        Assert.ThrowsAny<OperationCanceledException>(() => Rar5RewriteEngine.Rebuild(null, null, profile, stage, [], [], [], _ => throw new InvalidOperationException(), stop.Token)); Assert.Equal(0, stage.Length);
    }

    [Fact]
    public void The_final_source_hash_probes_one_extra_byte_instead_of_draining_an_unbounded_tail()
    {
        var profile = ArchiveWriteProfile.ForRar5(); using var original = ActualSource(profile); byte[] data = original.ToArray();
        using var source = new ExtraTail(data); using var output = new MemoryStream(); using var stage = new WriteObserver(output, () => source.AppendTail = true);
        var error = Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(source, Sha(data), profile, stage, [], [], [], _ => throw new InvalidOperationException(), Ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, error.Reason); Assert.Equal(1, source.TailReads); Assert.True(source.CanRead);
    }

    [Theory] [InlineData(1)] [InlineData(2)]
    public void Wrong_read_passwords_cannot_open_captures_or_start_stage_writes(int mode)
    {
        var profile = ArchiveWriteProfile.ForRar5(encryption: new((ArchiveEncryptionMode)mode)); byte[] data = Data(1);
        using var source = new MemoryStream(); using (var key = Key(profile))
            Rar5RewriteEngine.Rebuild(null, null, profile, source, [], [new("f", ArchiveMemberType.File, 1)], [Input(1, data)], _ => new MemoryStream(data, false), Ct, encryption: key);
        string sha = Sha(source.ToArray()); source.Position = 0; using var stage = new MemoryStream(); using var wrong = new Rar5ReadPasswordSession("Wrong synthetic password", new(), Ct);
        using var outputKey = Key(profile); int opened = 0;
        Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(source, sha, profile, stage, [], [new("g", ArchiveMemberType.File, 2)], [Input(2, data)],
            _ => { opened++; return new MemoryStream(data, false); }, Ct, wrong, outputKey));
        Assert.Equal(0, opened); Assert.Equal(0, stage.Length); Assert.True(source.CanRead);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Remove_all_preserves_the_explicit_password_mode_or_refuses_before_writing(int mode)
    {
        var profile = ArchiveWriteProfile.ForRar5(encryption: mode == 0 ? null : new((ArchiveEncryptionMode)mode)); byte[] data = Data(1);
        using var source = new MemoryStream(); using (var key = Key(profile))
            Rar5RewriteEngine.Rebuild(null, null, profile, source, [], [new("f", ArchiveMemberType.File, 1)], [Input(1, data)], _ => new MemoryStream(data, false), Ct, encryption: key);
        source.Position = 0; using var reader = ReadKey(profile); var catalog = StrictRar5Catalog.Read(source, Ct, reader); var member = catalog.Members[0].Physical;
        ArchiveTransformEdit[] edits = [new(ArchiveChangeKind.Delete, new(catalog.Physical.Revision, member.Name, member.Type, 0))];
        using var stage = new MemoryStream(); source.Position = 0; using var read = ReadKey(profile); using var outputKey = Key(profile);
        if (mode == 1)
        { Assert.Throws<ArchiveWriteException>(() => Rar5RewriteEngine.Rebuild(source, catalog.Physical.Revision, profile, stage, edits, [], [], _ => throw new InvalidOperationException(), Ct, read, outputKey)); Assert.Equal(0, stage.Length); }
        else
        {
            var result = Rar5RewriteEngine.Rebuild(source, catalog.Physical.Revision, profile, stage, edits, [], [], _ => throw new InvalidOperationException(), Ct, read, outputKey);
            stage.Position = 0; using var freshKey = ReadKey(profile); var fresh = StrictRar5Catalog.Read(stage, Ct, freshKey); Assert.Empty(fresh.Members);
            Assert.Equal(mode == 2, fresh.Physical.HiddenNames); Assert.Equal(result.Manifest.Digest, Rar5SemanticCatalog.Assessment(fresh, profile, Ct).Manifest.Digest);
        }
    }

    private sealed class ExtraTail(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes, false);
        internal bool AppendTail; internal int TailReads;
        public override int Read(Span<byte> output)
        {
            if (output.IsEmpty) return 0;
            if (AppendTail && inner.Position == inner.Length)
            { if (++TailReads > 2) throw new InvalidOperationException("The source-hash end probe exceeded its one-byte grant."); output[0] = 0; return 1; }
            return inner.Read(output);
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => inner.CanRead; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin); public override void Flush() { }
        public override void SetLength(long length) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static ArchiveAssessment Assessment(ArchiveWriteProfile profile)
    {
        var metadata = new ArchiveRar5Metadata(ArchiveMemberType.File, 2, 0x81a4, 1, 3UL << 7, hasCrc32: true);
        return new(new(profile, [new("f", ArchiveMemberType.File, 0, Data(1).Length, Sha(Data(1)), metadata.Encode())], new ArchiveRar5Features().Encode()), Sha([0]), 123, Sha([0]));
    }
    private static MemoryStream ActualSource(ArchiveWriteProfile profile)
    {
        var stream = new MemoryStream(); byte[] data = Data(1); Rar5RewriteEngine.Rebuild(null, null, profile, stream, [], [new("f", ArchiveMemberType.File, 1)], [Input(1, data)], _ => new MemoryStream(data, false), Ct); return stream;
    }
    private static void Export(string name, byte[] source, byte[] rebuilt, ArchiveAssessment before, ArchiveAssessment after)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_RAR_TRANSFORM_EXPORT_ROOT") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "source.rar"), source); File.WriteAllBytes(Path.Combine(directory, "rebuilt.rar"), rebuilt);
        void Manifest(string which, ArchiveAssessment assessment)
        {
            File.WriteAllText(Path.Combine(directory, which + ".json"), JsonSerializer.Serialize(new { password = Password, synthetic_public_vector = true,
                digest = assessment.Manifest.Digest, features = Convert.ToHexString(assessment.Manifest.Features), members = assessment.Manifest.Members.Select(member => new
                { name = member.Name, type = (int)member.Type, length = member.Length, sha256 = member.Sha256, metadata = Convert.ToHexString(member.Metadata) }) }));
        }
        Manifest("source", before); Manifest("rebuilt", after);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(root)!, "runtime-rar-transform.json"), JsonSerializer.Serialize(new
        { runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() }));
    }
    private sealed class WriteObserver(Stream inner, Action observe) : Stream
    {
        public override void Write(ReadOnlySpan<byte> bytes) { observe(); inner.Write(bytes); }
        public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
        public override bool CanRead => false; public override bool CanSeek => true; public override bool CanWrite => true;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value); public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
    }
}
