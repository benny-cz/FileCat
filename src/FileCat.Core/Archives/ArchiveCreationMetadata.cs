using System.Security.Cryptography;

namespace FileCat.Core.Archives;

internal enum ArchiveCreationBackend { WindowsNtfs = 1, LinuxExt4 = 2, MacApfs = 3 }

/// <summary>Live native creation witness, issued by one fresh stage owner. It grants no update or cleanup authority.</summary>
internal abstract class ArchiveCreatePublicationReview
{
    private readonly object issuer;
    private readonly byte[] witness;
    internal ArchiveFileObservation Stage { get; }
    internal ArchiveCreationBackend Backend { get; }
    internal string TargetLeaf { get; }
    internal ReadOnlySpan<byte> Witness => witness;
    protected ArchiveCreatePublicationReview(object issuer, ArchiveFileObservation stage, string targetLeaf, ArchiveCreationBackend backend, byte[] witness)
    {
        ArgumentNullException.ThrowIfNull(issuer); ArgumentNullException.ThrowIfNull(stage);
        if (!Enum.IsDefined(backend) || witness.Length > ArchiveCreationMetadata.MaximumWitnessBytes ||
            stage.Origin.State != ArchiveOriginState.Absent && backend != ArchiveCreationBackend.WindowsNtfs)
            throw new ArgumentException("Invalid private creation witness.");
        ArchiveCreationOrigin.RequireSupported(stage.Origin);
        ArchiveTransactionFacts.ValidateText(targetLeaf, 1024);
        this.issuer = issuer; Stage = stage; TargetLeaf = targetLeaf; Backend = backend; this.witness = witness.ToArray();
    }
    internal void RequireIssuer(object expected)
    { if (!ReferenceEquals(issuer, expected)) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The creation review belongs to another native stage."); }
}

internal interface IArchiveCreatePublicationStage : IArchiveRetainedStage
{
    ArchiveFileObservation ApplyCreationOrigin(ArchiveFileObservation sealedStage, ArchiveOriginSnapshot required, CancellationToken ct);
    ArchiveCreatePublicationReview ReviewCreate(ArchiveFileObservation verified, string targetLeaf, CancellationToken ct);
    void ClaimNewName(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct);
    ArchiveFileObservation ReviewPublished(string leaf, ArchiveCreatePublicationReview review, CancellationToken ct);
}

/// <summary>Bounded parent evidence for creator-private new files, not an update or backup descriptor.</summary>
internal sealed class ArchiveCreationMetadata
{
    internal const int MaximumBytes = 512 * 1024, MaximumWitnessBytes = MaximumBytes - 64 * 1024;
    private readonly byte[] bytes;
    private readonly Guid nonce;
    private readonly ArchiveWriteProfile profile;
    private readonly ArchiveObjectVersion stage;
    private readonly string parent, archive;
    internal long Length => bytes.Length;
    internal string Sha256 { get; }
    internal ArchiveCreationMetadata(ArchiveTransactionFacts facts, ArchiveCreatePublicationReview review)
    {
        ArgumentNullException.ThrowIfNull(facts); ArgumentNullException.ThrowIfNull(review);
        if (facts.RecordVersion != ArchiveTransactionFacts.SchemaVersion || facts.Original is not null || facts.Phase != ArchiveTransactionPhase.Verifying ||
            facts.VerifiedStage != review.Stage.Version || facts.ParentIdentity != review.Stage.ParentIdentity ||
            Path.GetFileName(facts.StagePath) != review.Stage.LeafName || Path.GetFileName(facts.ArchivePath) != review.TargetLeaf) throw new ArgumentException("Creation evidence differs from its verified stage scope.");
        nonce = facts.Nonce; profile = facts.Profile; stage = review.Stage.Version; parent = facts.ParentIdentity; archive = facts.ArchivePath;
        using var output = new BoundedBuffer(MaximumBytes); using var writer = new BinaryWriter(output, ArchiveWritePolicy.Utf8, leaveOpen: true);
        bool marked = review.Stage.Origin.State == ArchiveOriginState.Present;
        writer.Write("FCCMETA1"u8); writer.Write((ushort)(marked ? 2 : 1)); writer.Write((ushort)0);
        writer.Write(facts.Nonce.ToByteArray(bigEndian: true)); writer.Write((int)facts.Profile.Format); writer.Write(facts.Profile.Version);
        writer.Write(facts.Profile.CompressionLevel); writer.Write(facts.Profile.DictionaryBytes);
        writer.Write(marked ? 2 : 1); // 1: private unmarked; 2: private Windows zone-only. Never an update/backup policy.
        writer.Write((int)review.Backend); Text(review.Stage.ParentIdentity); Text(review.Stage.LeafName); Text(review.TargetLeaf); Text(review.Stage.Version.Identity);
        writer.Write(review.Stage.Version.Length); writer.Write(Convert.FromHexString(review.Stage.Version.Sha256));
        writer.Write(Convert.FromHexString(review.Stage.MetadataDigest)); writer.Write(review.Witness.Length); writer.Write(review.Witness);
        writer.Write("FCCMEND1"u8); writer.Flush(); bytes = output.ToArray(); Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        void Text(string value) { int length = ArchiveWritePolicy.Utf8.GetByteCount(value); if (length > 16 * 1024) throw ArchiveWriteBudget.Limit(); writer.Write(length); writer.Write(ArchiveWritePolicy.Utf8.GetBytes(value)); }
    }
    internal void RequireScope(ArchiveTransactionFacts facts)
    {
        if (facts.Nonce != nonce || facts.Profile != profile || facts.VerifiedStage != stage || facts.ParentIdentity != parent || facts.ArchivePath != archive || facts.Original is not null)
            throw new ArgumentException("The creation metadata belongs to another transaction scope.");
    }
    internal void Write(Stream stream, CancellationToken ct) { ct.ThrowIfCancellationRequested(); stream.Write(bytes); ct.ThrowIfCancellationRequested(); }
    internal void Readback(Stream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024]; int offset = 0;
        while (offset < bytes.Length)
        {
            ct.ThrowIfCancellationRequested(); int count = Math.Min(buffer.Length, bytes.Length - offset); stream.ReadExactly(buffer.AsSpan(0, count));
            if (!buffer.AsSpan(0, count).SequenceEqual(bytes.AsSpan(offset, count))) throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The native creation metadata evidence changed."); offset += count;
        }
        if (stream.ReadByte() != -1) throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview, "The creation metadata has trailing evidence.");
    }
    internal static byte[] EncodeWitness(Action<BinaryWriter> encode)
    {
        using var stream = new BoundedBuffer(MaximumWitnessBytes); using var writer = new BinaryWriter(stream, ArchiveWritePolicy.Utf8, leaveOpen: true);
        encode(writer); writer.Flush(); return stream.ToArray();
    }
    private sealed class BoundedBuffer(int maximum) : MemoryStream
    {
        private void Check(long count) { if (count < 0 || Position > maximum - count) throw ArchiveWriteBudget.Limit(); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void SetLength(long value) { if (value < 0 || value > maximum) throw ArchiveWriteBudget.Limit(); base.SetLength(value); }
    }
}
