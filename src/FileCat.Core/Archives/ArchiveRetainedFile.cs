namespace FileCat.Core.Archives;

/// <summary>Parent-only native observations. This is neither a complete metadata admission nor publication authority.</summary>
internal sealed record ArchiveFileObservation
{
    internal ArchiveObjectVersion Version { get; }
    internal string ParentIdentity { get; }
    internal string LeafName { get; }
    internal string MetadataDigest { get; }
    internal ArchiveOriginSnapshot Origin { get; }
    internal ArchiveTimestamp? MemberModified { get; }
    internal int? MemberMode { get; }
    internal ArchiveFileObservation(ArchiveObjectVersion version, string parentIdentity, string leafName, string metadataDigest,
        ArchiveOriginSnapshot origin, ArchiveTimestamp? memberModified, int? memberMode)
    {
        ArgumentNullException.ThrowIfNull(version); ArgumentNullException.ThrowIfNull(origin);
        ArchiveTransactionFacts.ValidateText(parentIdentity, 4096); ArchiveTransactionFacts.ValidateText(leafName, 1024);
        ArchiveWritePolicy.ValidateDigest(metadataDigest);
        if (origin.State == ArchiveOriginState.Unavailable || memberMode is < 0 or > 0x1ff) throw new ArgumentException("Incomplete retained-file observation.");
        Version = version; ParentIdentity = parentIdentity; LeafName = leafName; MetadataDigest = metadataDigest; Origin = origin;
        MemberModified = memberModified; MemberMode = memberMode;
    }
    internal ArchiveTransformInput Input(int id)
    {
        if (MemberModified is null || MemberMode is null)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The captured file time or mode cannot be represented exactly by this archive input profile.");
        return new(id, Version.Length, Version.Sha256, MemberModified.Value, MemberMode.Value);
    }
    internal bool Matches(ArchiveFileObservation other) => Version == other.Version && ParentIdentity == other.ParentIdentity &&
        LeafName == other.LeafName && MetadataDigest == other.MetadataDigest && Origin.Digest == other.Origin.Digest &&
        MemberModified == other.MemberModified && MemberMode == other.MemberMode;
}

/// <summary>No arbitrary pathname reopening; implementations bind observations/views to retained native authority.</summary>
internal interface IArchiveRetainedFile
{
    string ObjectIdentity { get; }
    ArchiveFileObservation Observe(CancellationToken ct);
    void Review(ArchiveFileObservation expected, CancellationToken ct);
    Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct);
    /// <summary>
    /// The object is still the reviewed one with the same length and outer metadata (identity, parent, name, times,
    /// attributes, security and provenance), without hashing its bytes again. Use it only where a worker receipt and the
    /// fresh verification already bind the content (plan §7.1 ReadyToPublish rechecks provenance and outer metadata);
    /// capture and source removal keep the complete <see cref="Review"/>. The default performs the complete review.
    /// </summary>
    void ReviewMetadata(ArchiveFileObservation expected, CancellationToken ct) => Review(expected, ct);
}

internal interface IArchiveRetainedArtifact : IArchiveRetainedFile, IDisposable
{
    string ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct);
    Stream BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct);
    ArchiveFileObservation SealEvidence(CancellationToken ct);
    void StopWrites();
}

internal interface IArchiveRetainedStage : IArchiveRetainedArtifact
{
    Stream BeginRebuild(long exactMaximumLength, CancellationToken ct);
    ArchiveFileObservation SealForVerification(CancellationToken ct);
}

internal sealed record ArchiveRetainedBinding(IArchiveRetainedFile File, ArchiveFileObservation Reviewed);

internal delegate int ArchiveRetainedReadAt(long offset, Span<byte> buffer);

/// <summary>Independent bounded cursor. Disposal closes only this read view, never the parent's retained object.</summary>
internal sealed class ArchiveRetainedReadStream(long length, ArchiveRetainedReadAt readAt, Func<bool> ownerIsReadable) : Stream
{
    private readonly object gate = new();
    private bool disposed;
    private long position;
    public override bool CanRead { get { lock (gate) return !disposed && ownerIsReadable(); } }
    public override bool CanSeek => CanRead;
    public override bool CanWrite => false;
    public override long Length { get { lock (gate) { Live(); return length; } } }
    public override long Position { get { lock (gate) { Live(); return position; } } set => Seek(value, SeekOrigin.Begin); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        lock (gate)
        {
            Live(); int count = (int)Math.Min(buffer.Length, length - position); int read = readAt(position, buffer[..count]);
            if (read < 0 || read > count || read == 0 && count != 0) throw Changed(); position += read; return read;
        }
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (gate)
        {
            Live(); long start = origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => position, SeekOrigin.End => length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (offset < -start || offset > length - start) throw new IOException("The retained archive read exceeds its reviewed length.");
            return position = start + offset;
        }
    }
    private void Live() { ObjectDisposedException.ThrowIf(disposed, this); if (!ownerIsReadable()) throw Changed(); }
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The retained archive read view is no longer available.");
    public override void Flush() { lock (gate) Live(); }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { lock (gate) disposed = true; base.Dispose(disposing); }
}
