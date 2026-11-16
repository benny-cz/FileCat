using System.Security.Cryptography;
using FileCat.Archives.Rar;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// Complete source verification, parent-equivalent occurrence planning and retained/new payload assembly.
/// Only borrowed streams and numeric captured grants. Role-bound workers, fresh verification and publication remain
/// the caller's separate responsibilities; this engine is not registered for application writes.
/// </summary>
internal static class Rar5RewriteEngine
{
    internal static Rar5AssemblyResult Rebuild(Stream? source, string? sourceSha256, ArchiveWriteProfile profile,
        Stream stage, IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions,
        IReadOnlyList<ArchiveTransformInput> inputs, Func<int, Stream> openCaptured, CancellationToken ct,
        Rar5ReadPasswordSession? readPassword = null, Rar5EncryptionSession? encryption = null)
    {
        ArgumentNullException.ThrowIfNull(stage); ArgumentNullException.ThrowIfNull(openCaptured); ct.ThrowIfCancellationRequested();
        _ = ArchiveWriterProfileAdapter.Rar5(profile);
        if (ReferenceEquals(source, stage) || !stage.CanWrite || !stage.CanSeek || stage.Length != 0 || stage.Position != 0 ||
            (source is null) != (sourceSha256 is null)) throw Invalid();
        Rar5LogicalCatalog? catalog = null; ArchiveAssessment? assessment = null;
        if (source is not null)
        {
            ArchiveWritePolicy.ValidateDigest(sourceSha256!);
            // Read every source member, including deleted/replaced members, before touching the stage.
            catalog = StrictRar5Catalog.Read(source, ct, readPassword);
            if (catalog.Physical.Revision != sourceSha256) throw Changed();
            assessment = Rar5SemanticCatalog.Assessment(catalog, profile, ct);
        }
        long retained = catalog is null ? 0 : checked(catalog.Physical.RetainedHeapBytes + catalog.Members.Count * 1088L + (128 << 10));
        var plan = ArchiveRar5TransformPlan.CreateObserved(profile, assessment, inputs, edits, additions, retained, ct);
        var entries = new Rar5AssemblyEntry[plan.Members.Count];
        for (int index = 0; index < entries.Length; index++)
        {
            ct.ThrowIfCancellationRequested(); var member = plan.Members[index]; var expected = member.Expected;
            var metadata = ArchiveRar5Metadata.Decode(expected.Metadata);
            entries[index] = member.SourceIndex is { } original
                ? new(expected.Name, metadata, Retained: Rar5RetainedPayload.FromVerified(catalog!.Members[original], original))
                : member.InputId is { } id ? new(expected.Name, metadata,
                    new(new(id, plan.Inputs[id].Length, plan.Inputs[id].Sha256), ArchiveWriterProfileAdapter.Rar5(profile)))
                : new(expected.Name, metadata);
        }
        var result = Rar5ArchiveAssembler.Create(stage, entries, openCaptured, OpenPacked, profile,
            ArchiveRar5Features.Decode(plan.Expected.Features), ct, encryption);
        if (result.Manifest.Digest != plan.Expected.Digest)
            throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "RAR assembly disagrees with the independently planned semantic output.");
        if (source is not null)
        {
            if (source.Length != catalog!.Physical.Length) throw Changed();
            source.Position = 0;
            if (SourceDigest(source, catalog.Physical.Length, ct) != sourceSha256) throw Changed();
        }
        return result;

        Stream OpenPacked(int id)
        {
            if (source is null || catalog is null || id < 0 || id >= catalog.Members.Count) throw Invalid();
            var member = catalog.Members[id].Physical;
            return new PackedView(source, member.DataOffset, member.PackedLength, ct);
        }
    }
    // The final pass is finite even if an untrusted stream reports its old length but keeps returning extra bytes.
    private static string SourceDigest(Stream source, long length, CancellationToken ct)
    {
        byte[] bytes = new byte[ArchiveIoAuthority.MaximumReadBytes]; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            long left = length;
            while (left > 0)
            {
                ct.ThrowIfCancellationRequested(); int wanted = (int)Math.Min(bytes.Length, left), n = source.Read(bytes.AsSpan(0, wanted));
                if (n <= 0 || n > wanted) throw Changed(); hash.AppendData(bytes, 0, n); left -= n;
            }
            ct.ThrowIfCancellationRequested(); if (source.ReadByte() != -1 || source.Length != length) throw Changed();
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private sealed class PackedView(Stream source, long offset, long length, CancellationToken ct) : Stream
    {
        private long position; private bool disposed;
        public override int Read(Span<byte> bytes)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
            int wanted = (int)Math.Min(Math.Min(bytes.Length, ArchiveIoAuthority.MaximumReadBytes), length - position);
            if (wanted == 0) return 0;
            source.Position = checked(offset + position); int n = source.Read(bytes[..wanted]);
            if (n <= 0 || n > wanted) throw Changed(); position += n; return n;
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
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The RAR rewrite's source, stage or numeric grants are invalid.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The complete reviewed RAR source changed during the rewrite.");
}
