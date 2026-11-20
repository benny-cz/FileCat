using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Complete source verification, independent parent-equivalent planning, packed reuse and new input encoding.</summary>
internal static class SevenZipRewriteEngine
{
    internal static SevenZipAssemblyResult Rebuild(Stream? source, string? sourceSha256, ArchiveWriteProfile profile, Stream stage,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions,
        IReadOnlyList<ArchiveTransformInput> inputs, Func<int, Stream> openCaptured, CancellationToken ct,
        ReadOnlySpan<char> password = default)
    {
        ArgumentNullException.ThrowIfNull(stage); ArgumentNullException.ThrowIfNull(openCaptured); ct.ThrowIfCancellationRequested();
        _ = ArchiveWriterProfileAdapter.SevenZip(profile);
        if (ReferenceEquals(source, stage) || !stage.CanWrite || !stage.CanSeek || stage.Length != 0 || stage.Position != 0 ||
            (source is null) != (sourceSha256 is null) || password.IsEmpty != (profile.Encryption.Mode == ArchiveEncryptionMode.None)) throw Invalid();
        var crypto = new ArchiveCryptoWorkBudget();
        using var read = source is not null && !password.IsEmpty ? new SevenZipReadPasswordSession(password, crypto, ct) : null;
        SevenZipLogicalCatalog? catalog = null; ArchiveAssessment? assessment = null;
        if (source is not null)
        {
            ArchiveWritePolicy.ValidateDigest(sourceSha256!); catalog = StrictSevenZipCatalog.Read(source, ct, read);
            if (catalog.Physical.Revision != sourceSha256) throw Changed();
            assessment = SevenZipSemanticCatalog.Assessment(catalog, profile, ct);
        }
        var plan = ArchiveSevenZipTransformPlan.Create(profile, assessment, inputs, edits, additions, ct,
            catalog is null ? 0 : checked(catalog.RetainedHeapBytes + (128 << 10)));
        var features = ArchiveSevenZipFeatures.Decode(plan.Expected.Features);
        var headerCipher = features.HeaderGraph?.Coders.SingleOrDefault(c => c.Method == 0x06f10701);
        using var encryption = password.IsEmpty ? null : new SevenZipEncryptionSession(password, features.HiddenNames, crypto, ct,
            profile.Encryption.CountPower, headerCipher?.Properties[0], headerCipher is null ? 16 : headerCipher.Properties[1]);
        var result = SevenZipArchiveWriter.Assemble(stage, plan, catalog, openCaptured, OpenPacked, ct, encryption);
        if (source is not null && (source.Length != catalog!.Physical.Length || StrictSevenZipFraming.WholeHash(source, catalog.Physical.Length, ct) != sourceSha256)) throw Changed();
        return result;

        Stream OpenPacked(int member, int branch)
        {
            if (source is null || catalog is null || member < 0 || member >= catalog.Members.Count || branch < 0 || branch >= catalog.Members[member].Packed.Count) throw Invalid();
            var pack = catalog.Members[member].Packed[branch].Physical; return new PackedView(source, pack.Offset, pack.Length, ct);
        }
    }
    private sealed class PackedView(Stream source, long offset, long length, CancellationToken ct) : Stream
    {
        private long position; private bool disposed;
        public override int Read(Span<byte> bytes)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
            int wanted = (int)Math.Min(Math.Min(bytes.Length, ArchiveIoAuthority.MaximumReadBytes), length - position); if (wanted == 0) return 0;
            source.Position = checked(offset + position); int n = source.Read(bytes[..wanted]); if (n <= 0 || n > wanted) throw Changed(); position += n; return n;
        }
        public override int Read(byte[] bytes, int start, int count) => Read(bytes.AsSpan(start, count));
        protected override void Dispose(bool disposing) { if (disposing) disposed = true; base.Dispose(disposing); }
        public override bool CanRead => !disposed; public override bool CanSeek => !disposed; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override long Seek(long value, SeekOrigin origin)
        {
            ObjectDisposedException.ThrowIf(disposed, this); long target = origin switch
            { SeekOrigin.Begin => value, SeekOrigin.Current => checked(position + value), SeekOrigin.End => checked(length + value), _ => throw Invalid() };
            if (target < 0 || target > length) throw Invalid(); return position = target;
        }
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] bytes, int start, int count) => throw new NotSupportedException();
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The 7z rewrite's source, stage, password mode or numeric grants are invalid.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The complete reviewed 7z source changed during the rewrite.");
}
