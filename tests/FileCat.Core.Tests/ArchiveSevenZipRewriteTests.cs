using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveSevenZipRewriteTests
{
    private const string Password = "Public-7z-rewrite-暗号-42829324";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));
    private static byte[] Data(int seed) { byte[] data = new byte[8197]; new Random(seed).NextBytes(data); for (int i = 0; i + 5 < data.Length; i += 32) { data[i] = 0xe8; data[i + 4] = 0; } return data; }
    private static ArchiveWriteProfile Profile(int codec, int filter, int protection) => ArchiveWriteProfile.ForSevenZip(
        new((ArchiveSevenZipCodec)codec, (ArchiveSevenZipFilter)filter, codec == 6 ? 4 : 0, codec == 6 ? 1 << 20 : 0, filter == 1 ? 7 : 1),
        codec == 0 ? 0 : 1, codec is 1 or 2 ? 1 << 20 : 8 << 20,
        protection == 0 ? null : new(protection == 2 ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data));
    private static ArchiveTransformInput Input(int id, byte[] data) => new(id, data.Length, Sha(data), new ArchiveTimestamp(1700000000 + id, 123456700), 0x1a4);
    private static byte[] Create(ArchiveWriteProfile profile, byte[] data)
    {
        using var stage = new MemoryStream();
        var result = SevenZipRewriteEngine.Rebuild(null, null, profile, stage, [],
            [new("folder/original", ArchiveMemberType.File, 1), new("folder", new ArchiveTransformDirectoryMetadata(new(1, 100), 0x1ed)), new("empty", ArchiveMemberType.File, 2)],
            [Input(1, data), Input(2, [])], id => new MemoryStream(id == 1 ? data : [], false), Ct,
            profile.Encryption.Mode == ArchiveEncryptionMode.None ? default : Password);
        Assert.Equal(stage.Length, result.PhysicalBytes); Assert.True(stage.CanWrite); return stage.ToArray();
    }
    private static SevenZipLogicalCatalog Read(byte[] bytes, ArchiveWriteProfile profile)
    {
        using var source = new MemoryStream(bytes, false); using var password = profile.Encryption.Mode == ArchiveEncryptionMode.None ? null : new SevenZipReadPasswordSession(Password, new(), Ct);
        return StrictSevenZipCatalog.Read(source, Ct, password);
    }

    [Theory] [MemberData(nameof(StrictSevenZipCatalogTests.Graphs), MemberType = typeof(StrictSevenZipCatalogTests))]
    public void Creation_and_occurrence_updates_preserve_retained_packs_and_complete_metadata(int codec, int filter, int protection)
    {
        var profile = Profile(codec, filter, protection); byte[] original = Data(42829324), replacement = Data(42829325), added = Data(42829326);
        byte[] archive = Create(profile, original); var before = Read(archive, profile); var assessment = SevenZipSemanticCatalog.Assessment(before, profile, Ct);
        var edits = new ArchiveTransformEdit[] { new(ArchiveChangeKind.Rename, new(assessment.CatalogRevision, "folder/original", ArchiveMemberType.File, 0), "folder/renamed"),
            new(ArchiveChangeKind.Replace, new(assessment.CatalogRevision, "empty", ArchiveMemberType.File, 0), inputId: 2) };
        var additions = new ArchiveTransformAddition[] { new("folder/added", ArchiveMemberType.File, 3) };
        var inputs = new[] { Input(2, replacement), Input(3, added) };
        var expected = ArchiveSevenZipTransformPlan.Create(profile, assessment, inputs, edits, additions, Ct);
        using var source = new MemoryStream(archive, false); using var stage = new MemoryStream();
        var result = SevenZipRewriteEngine.Rebuild(source, assessment.SourceSha256, profile, stage, edits, additions, inputs,
            id => new MemoryStream(id == 2 ? replacement : added, false), Ct, protection == 0 ? default : Password);
        Assert.Equal(expected.Expected.Digest, result.Manifest.Digest); Assert.True(source.CanRead); Assert.True(stage.CanWrite);
        var after = Read(stage.ToArray(), profile); var observed = SevenZipSemanticCatalog.Assessment(after, profile, Ct);
        Assert.Equal(expected.Expected.Digest, observed.Manifest.Digest); Assert.Equal(4, after.Members.Count);
        Assert.Equal(before.Members[0].Packed.Select(p => p.Sha256), after.Members[0].Packed.Select(p => p.Sha256));
        Assert.Equal(before.Members[0].Physical.Length, after.Members[0].Physical.Length);
        Assert.Equal(new[] { Sha(original), Sha([]), Sha(replacement), Sha(added) }, after.Members.Select(m => m.Sha256));
        var oldMetadata = ArchiveSevenZipMetadata.Decode(assessment.Manifest.Members[2].Metadata);
        var newMetadata = ArchiveSevenZipMetadata.Decode(observed.Manifest.Members[2].Metadata);
        Assert.Equal(oldMetadata.Attributes, newMetadata.Attributes); Assert.Equal(oldMetadata.CreatedFileTime, newMetadata.CreatedFileTime);
        Assert.Equal(oldMetadata.AccessedFileTime, newMetadata.AccessedFileTime); Assert.Equal(oldMetadata.ModifiedFileTime, newMetadata.ModifiedFileTime);
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Windows_preflight_covers_encrypted_sevenzip_rewrite_and_BCJ2_empty_Deflate_branches(int protection)
        => Creation_and_occurrence_updates_preserve_retained_packs_and_complete_metadata(4, 12, protection);

    [Fact]
    public void Deleted_member_corruption_is_still_verified_before_any_stage_write()
    {
        var profile = Profile(0, 0, 0); byte[] archive = Create(profile, Data(42829324)); archive[32] ^= 1;
        using var source = new MemoryStream(archive, false); using var stage = new MemoryStream(); string revision = Sha(archive);
        var edit = new ArchiveTransformEdit(ArchiveChangeKind.Delete, new(revision, "folder/original", ArchiveMemberType.File, 0));
        Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() =>
            SevenZipRewriteEngine.Rebuild(source, revision, profile, stage, [edit], [], [], _ => throw new InvalidOperationException(), Ct)).Reason);
        Assert.Equal(0, stage.Length); Assert.True(source.CanRead);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Mutation_during_packed_copy_or_after_assembly_never_returns_an_output(bool finalScan)
    {
        var profile = Profile(0, 0, 0); byte[] archive = Create(profile, Data(42829324)); string revision = Sha(archive);
        using var source = new Mutating(archive, finalScan); using var stage = new MemoryStream();
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => SevenZipRewriteEngine.Rebuild(source, revision,
            profile, stage, [], [], [], _ => throw new InvalidOperationException(), Ct)).Reason);
        Assert.True(stage.Length > 0); Assert.True(source.Mutated); Assert.True(source.CanRead);
    }

    private sealed class Mutating(byte[] bytes, bool finalScan) : Stream
    {
        private readonly MemoryStream inner = new(bytes, false); private int zero, packed; internal bool Mutated;
        public override long Position
        {
            get => inner.Position;
            set
            {
                if (value == 0) zero++; if (value == 32) packed++;
                if (!Mutated && (finalScan ? zero == 5 && value == 0 : packed == 2 && value == 32))
                { bytes[finalScan ? 7 : 32] ^= 1; Mutated = true; }
                inner.Position = value;
            }
        }
        public override int Read(Span<byte> value) => inner.Read(value[..Math.Min(value.Length, 97)]);
        public override int Read(byte[] value, int offset, int count) => Read(value.AsSpan(offset, count));
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false; public override long Length => inner.Length;
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, _ => Length + offset };
        public override void Flush() { } public override void SetLength(long length) => throw new NotSupportedException(); public override void Write(byte[] value, int offset, int count) => throw new NotSupportedException();
    }
}
