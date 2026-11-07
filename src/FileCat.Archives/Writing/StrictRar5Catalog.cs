using System.Security.Cryptography;
using FileCat.Archives.Rar;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record Rar5LogicalMember(Rar5PhysicalMember Physical, string Sha256, Rar5FilterFeatures Filters)
{
    internal string PackedSha256 { get; init; } = string.Empty;
}

/// <summary>Complete observed content, not a writable-profile assessment or publication authority.</summary>
internal sealed record Rar5LogicalCatalog(Rar5PhysicalCatalog Physical, IReadOnlyList<Rar5LogicalMember> Members);

/// <summary>
/// Stream-only, non-solid RAR5 verification. Both passes hash every physical byte; every member is decoded,
/// including members a later operation might delete. Exact metadata stays in the physical observations.
/// Preservation admission must still qualify every observed codec, filter, metadata and password feature.
/// </summary>
internal static class StrictRar5Catalog
{
    // Profile policy, not a RAR format maximum. The complete worker/job memory qualification remains separate.
    internal const long MaximumDictionaryBytes = 64L << 20;

    internal static Rar5LogicalCatalog Read(Stream input, CancellationToken ct, Rar5ReadPasswordSession? password = null)
    {
        var physical = StrictRar5Framing.Read(input, ct, password);
        var budget = new ArchiveWriteBudget();
        // The framing objects remain alive alongside this list, its digest strings and bounded read buffers.
        budget.AddMetadata(0, checked(physical.RetainedHeapBytes + physical.Members.Count * 576L + (128 << 10)));
        foreach (var member in physical.Members)
        {
            ct.ThrowIfCancellationRequested();
            if (member.DictionaryBytes > MaximumDictionaryBytes)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "This RAR requires a dictionary above the strict write profile's 64 MiB limit.");
            if (member.Encryption is not null && password is null)
                throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "A password is required to verify the RAR member data.");
        }
        if (input.Length != physical.Length) throw Changed();
        input.Position = 0;
        using var source = new StrictRar5Framing.Forward(input, ct);
        var members = new List<Rar5LogicalMember>(physical.Members.Count);
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        try
        {
            foreach (var member in physical.Members)
            {
                ct.ThrowIfCancellationRequested();
                source.Drain(member.DataOffset - source.Position);
                using var packed = new Region(source, member.PackedLength, ct);
                using var cipher = member.Encryption is null ? null : new Rar5MemberDecryption(packed,
                    member.PackedLength, member.Encryption, password!, _ => { });
                Stream data = cipher ?? (Stream)packed;
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var blake = member.Blake2sp is null ? null : new Blake2sp();
                var crc = new ArchiveCrc32(); long produced = 0;
                Rar5FilterFeatures filters = Rar5FilterFeatures.None;
                if (member.Method == 0 || member.Type == ArchiveMemberType.Directory)
                {
                    while (produced < member.Length)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = data.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, member.Length - produced)));
                        if (n <= 0) throw Invalid("The stored RAR member ends early.");
                        Append(buffer.AsSpan(0, n));
                    }
                    for (int padding = 0; ; padding++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (data.ReadByte() == -1) break;
                        if (padding == (cipher is null ? 0 : 15)) throw Invalid("The stored RAR member has trailing data.");
                    }
                }
                else
                {
                    using var bits = new RarBitReader(); bits.Reset(data);
                    using var window = new RarWindow(member.DictionaryBytes, member.Length);
                    var decoder = new Rar5Decoder(rar7: member.AlgorithmVersion == 1 && (member.Compression & 0x100000) == 0,
                        strict: true, ct);
                    decoder.StartMember(bits, solid: false);
                    bool done = false;
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = window.ReadOutput(buffer, member.Length, 0, ct);
                        if (n > 0) { Append(buffer.AsSpan(0, n)); continue; }
                        if (done) break;
                        long before = window.Written;
                        done = decoder.Decode(window);
                        if (window.Written > member.Length) throw Invalid("The RAR decoder produced more than the declared member size.");
                        if (!done && window.Written == before) throw Invalid("The RAR decoder cannot complete its pending output.");
                    }
                    if (window.HasPendingFilters || window.HasFilteredOutput)
                        throw Invalid("A RAR filter extends beyond the member's data.");
                    bits.VerifyPackedEnd(cipher is null ? 0 : 15, ct);
                    filters = decoder.Filters;
                }
                if (produced != member.Length || packed.Remaining != 0) throw Invalid("The RAR member's complete extents disagree with its headers.");
                if (member.Crc32 is { } expected && (cipher?.ProtectCrc(crc.Value) ?? crc.Value) != expected)
                    throw Invalid("The RAR member checksum does not match its data or password.");
                if (blake is not null)
                {
                    byte[] hash = blake.Finish(); byte[]? final = null;
                    try
                    {
                        final = cipher?.ProtectHash(hash) ?? hash;
                        if (!final.AsSpan().SequenceEqual(member.Blake2sp)) throw Invalid("The RAR member BLAKE2sp hash does not match its data or password.");
                    }
                    finally { CryptographicOperations.ZeroMemory(hash); if (final is not null) CryptographicOperations.ZeroMemory(final); }
                }
                members.Add(new(member, Convert.ToHexString(sha.GetHashAndReset()), filters) { PackedSha256 = packed.Digest() });

                void Append(ReadOnlySpan<byte> bytes)
                {
                    if (bytes.Length > member.Length - produced) throw Invalid("The RAR decoder returned excess member content.");
                    produced += bytes.Length; sha.AppendData(bytes); crc.Append(bytes); blake?.Append(bytes);
                }
            }
            source.Drain(source.Length - source.Position); source.End();
            if (source.Digest() != physical.Revision) throw Changed();
            return new(physical, members.AsReadOnly());
        }
        catch (ArchiveWriteException) { throw; }
        catch (InvalidDataException ex) when (ex.InnerException is ArchiveWriteException) { throw (ArchiveWriteException)ex.InnerException; }
        catch (InvalidDataException ex) when (RarData.IsUnsupported(ex))
        { throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The RAR payload uses an unsupported feature.", ex); }
        catch (InvalidDataException ex) { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The RAR payload or password check is invalid.", ex); }
        catch (EndOfStreamException ex) { throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The RAR payload ends unexpectedly.", ex); }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private sealed class Region(Stream source, long length, CancellationToken ct) : Stream
    {
        private readonly long _length = length;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        internal long Remaining { get; private set; } = length;
        public override int Read(Span<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            int wanted = (int)Math.Min(Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes), Remaining);
            int n = source.Read(buffer[..wanted]);
            if (n < 0 || n > wanted) throw Invalid("The packed RAR source violated its read grant.");
            if (n == 0 && Remaining > 0 && !buffer.IsEmpty) throw Invalid("The packed RAR member ends early.");
            _hash.AppendData(buffer[..n]); Remaining -= n; return n;
        }
        internal string Digest()
        { if (Remaining != 0) throw Invalid("The packed RAR member has not been verified completely."); return Convert.ToHexString(_hash.GetCurrentHash()); }
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - Remaining; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The RAR source changed between its framing and complete content passes.");
}
