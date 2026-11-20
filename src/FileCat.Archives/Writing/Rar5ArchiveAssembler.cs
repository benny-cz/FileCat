using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Archives.Rar;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Worker-local payload witness from the complete decoder pass. Crypto material never enters a manifest.</summary>
internal sealed class Rar5RetainedPayload
{
    internal int Id { get; }
    internal string OriginalName { get; }
    internal ArchiveRar5Metadata Metadata { get; }
    internal long Length { get; }
    internal long PackedLength { get; }
    internal string Sha256 { get; }
    internal string PackedSha256 { get; }
    internal uint? Crc32 { get; }
    private readonly byte[]? blake;
    private readonly Rar5CryptoParameters? crypto;
    internal ReadOnlySpan<byte> Blake2sp => blake;
    private Rar5RetainedPayload(Rar5LogicalMember member, int id)
    {
        if (id < 0) throw Rar5ArchiveAssembler.Invalid();
        ArchiveWritePolicy.ValidateDigest(member.Sha256); ArchiveWritePolicy.ValidateDigest(member.PackedSha256);
        var physical = member.Physical;
        Id = id; OriginalName = physical.Name; Metadata = Rar5SemanticCatalog.Metadata(member); Length = physical.Length;
        PackedLength = physical.PackedLength; Sha256 = member.Sha256; PackedSha256 = member.PackedSha256;
        Crc32 = physical.Crc32; blake = physical.Blake2sp?.ToArray();
        if (physical.Encryption is { } c) crypto = new(c.CountPower, c.Salt.ToArray(), c.Iv.ToArray(), c.Check.ToArray(), c.UseMac);
    }
    internal static Rar5RetainedPayload FromVerified(Rar5LogicalMember member, int id) => new(member, id);
    internal void EncryptionBody(ref Rar5AssemblyRecord body)
    {
        if (crypto is null) throw Rar5ArchiveAssembler.Invalid();
        body.VarInt(0); body.VarInt((ulong)Metadata.EncryptionFlags!.Value); body.Byte((byte)crypto.CountPower);
        body.Bytes(crypto.Salt); body.Bytes(crypto.Iv); body.Bytes(crypto.Check);
    }
}

internal sealed record Rar5CapturedPayload(Rar5InputDescriptor Input, Rar5Compression Compression);
internal sealed record Rar5AssemblyEntry(string Name, ArchiveRar5Metadata Metadata,
    Rar5CapturedPayload? Captured = null, Rar5RetainedPayload? Retained = null);
internal sealed record Rar5AssemblyResult(ArchiveWriteManifest Manifest, long PhysicalBytes);

/// <summary>
/// Complete-metadata non-solid assembly: retained packed bytes have exact witnesses; only captured payloads encode.
/// The parent/worker still owns baseline, namespace review, private grants, fresh output verification and publication.
/// </summary>
internal static class Rar5ArchiveAssembler
{
    private static readonly System.Text.UTF8Encoding Utf8 = new(false, true);
    internal static Rar5AssemblyResult Create(Stream stage, IReadOnlyList<Rar5AssemblyEntry> entries,
        Func<int, Stream> openCaptured, Func<int, Stream> openPacked, ArchiveWriteProfile profile, ArchiveRar5Features features,
        CancellationToken ct, Rar5EncryptionSession? encryption = null)
    {
        ArgumentNullException.ThrowIfNull(stage); ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(openCaptured); ArgumentNullException.ThrowIfNull(openPacked);
        if (profile.Format != ArchiveWriteFormat.Rar5 || profile.Version != 2 || !stage.CanWrite || !stage.CanSeek || stage.Length != 0 || stage.Position != 0 ||
            entries.Count > ArchiveWritePolicy.MaxMembers || (encryption?.HideNames == true) != features.HiddenNames ||
            features.HiddenNames && encryption!.CountPower != features.HeaderCountPower) throw Invalid();
        ct.ThrowIfCancellationRequested(); var budget = new ArchiveWriteBudget();
        budget.AddMetadata(0, checked(entries.Count * 384L + ArchiveWritePolicy.MaxEntryMetadataBytes + (128 << 10)));
        var members = entries.ToArray(); var seenRetained = new HashSet<int>();
        var names = new Dictionary<string, (ArchiveMemberType Type, bool Changed)>(StringComparer.Ordinal);
        var files = new List<string>();
        foreach (var entry in members)
        {
            ct.ThrowIfCancellationRequested(); if (entry is null || entry.Metadata is null) throw Invalid();
            var m = entry.Metadata; ArchiveWritePolicy.ValidateName(entry.Name, m.Type);
            int nameBytes = Utf8.GetByteCount(entry.Name); if (nameBytes > RarLimits.MaxNameBytes) throw Invalid();
            long native = (m.Owner?.UserName.Length ?? 0L) + (m.Owner?.GroupName.Length ?? 0L);
            budget.AddMember(); budget.AddNameChars(entry.Name.Length);
            budget.AddMetadata(0, checked(entry.Name.Length * 4L + native * 4 + 1024));
            if (entry.Retained is { } retained)
            {
                if (entry.Captured is not null || !seenRetained.Add(retained.Id) || !m.Encode().AsSpan().SequenceEqual(retained.Metadata.Encode())) throw Invalid();
                budget.AddLogicalBytes(retained.Length);
            }
            else if (m.Type == ArchiveMemberType.File)
            {
                if (entry.Captured is not { } captured || captured.Input is null || captured.Input.Id < 0 || !Enum.IsDefined(captured.Compression) ||
                    captured.Input.Length is < 0 or > ArchiveWritePolicy.MaxPayloadBytes ||
                    (m.Compression & 63) == 1 && (m.Compression & 0x100000) == 0 || m.FilterFeatures != 0 ||
                    ((m.Compression >> 7) & 7) != (ulong)(captured.Input.Length == 0 ? 0 : (int)captured.Compression) ||
                    m.EncryptionFlags is not null && (encryption is null || m.CountPower != encryption.CountPower)) throw Invalid();
                ArchiveWritePolicy.ValidateDigest(captured.Input.Sha256); budget.AddLogicalBytes(captured.Input.Length);
            }
            else if (entry.Captured is not null) throw Invalid();
            long packed = entry.Retained?.PackedLength ?? entry.Captured?.Input.Length ?? 0;
            if ((m.HeaderFlags & 2) == 0 && packed != 0) throw Invalid();
            string name = m.Type == ArchiveMemberType.Directory ? entry.Name.TrimEnd('/') : entry.Name;
            bool changed = entry.Retained is null || entry.Name != entry.Retained.OriginalName;
            if (names.TryGetValue(name, out var previous) && (changed || previous.Changed || previous.Type != m.Type)) throw Invalid();
            names[name] = (m.Type, changed); if (m.Type == ArchiveMemberType.File) files.Add(name);
        }
        string[] ordered = names.Keys.ToArray(); Array.Sort(ordered, StringComparer.Ordinal);
        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested(); string prefix = file + "/"; int index = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal);
            if (index < 0) index = ~index; if (index < ordered.Length && ordered[index].StartsWith(prefix, StringComparison.Ordinal)) throw Invalid();
        }
        var observedMode = features.HiddenNames ? ArchiveEncryptionMode.DataAndNames : members.Any(entry => entry.Metadata.EncryptionFlags is not null)
            ? ArchiveEncryptionMode.Data : ArchiveEncryptionMode.None;
        if (profile.Encryption.Mode != observedMode || (encryption is null) != (observedMode == ArchiveEncryptionMode.None) ||
            encryption is not null && encryption.CountPower != profile.Encryption.CountPower) throw Invalid();
        encryption?.ClaimArchive(); byte[] buffer = new byte[ArchiveWritePolicy.MaxEntryMetadataBytes];
        var sink = new Sink(stage, ct); var output = new List<ArchiveManifestMember>(members.Length);
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        try
        {
            sink.Write("Rar!\x1a\x07\x01\x00"u8);
            if (features.HiddenNames)
            {
                var root = new Rar5AssemblyRecord(buffer); root.VarInt(0); root.VarInt(features.HeaderPasswordCheckPresent == true ? 1UL : 0UL);
                root.Byte((byte)encryption!.CountPower); root.Bytes(encryption.HeaderSalt);
                if (features.HeaderPasswordCheckPresent == true) root.Bytes(encryption.HeaderCheck);
                Rar5WireHeaderWriter.Header(sink, root.BytesWritten, 4, null, 0, features.EncryptionHeaderFlags);
            }
            var main = new Rar5AssemblyRecord(buffer); main.VarInt(0);
            Rar5WireHeaderWriter.Record(sink, main.BytesWritten, 1, null, 0, encryption, ct, features.MainHeaderFlags);
            foreach (var entry in members)
            {
                ct.ThrowIfCancellationRequested(); var m = entry.Metadata;
                Rar5EncodedContent? counted = null; byte[]? countedBlake = null, encodedHash = null;
                try
                {
                    if (entry.Captured is { } captured)
                    {
                        using var source = Open(openCaptured, captured.Input.Id, captured.Input.Length);
                        using var hashed = new BlakeSource(source, m.HasBlake2sp);
                        counted = Rar5Encoder.Encode(hashed, captured.Input.Length, Stream.Null, captured.Compression, ct);
                        if (counted.Sha256 != captured.Input.Sha256) throw Changed(); countedBlake = hashed.Finish();
                    }
                    long length = entry.Retained?.Length ?? counted?.Length ?? 0;
                    long plainPacked = counted?.PackedLength ?? 0;
                    bool newCipher = entry.Retained is null && m.EncryptionFlags is not null;
                    long packed = entry.Retained?.PackedLength ?? (newCipher ? checked((plainPacked + 15) & ~15L) : plainPacked);
                    var target = newCipher ? encryption!.CreateCipher(sink, false, plainPacked, ct) : ((ArchiveAesCbcWriteStream Cipher, byte[] Iv)?)null;
                    using ArchiveAesCbcWriteStream? cipher = target?.Cipher;
                    uint crc = entry.Retained?.Crc32 ?? counted?.Crc32 ?? 0;
                    if (newCipher && (m.EncryptionFlags!.Value & 2) != 0) crc = encryption!.ProtectCrc32(crc);
                    var body = new Rar5AssemblyRecord(buffer);
                    body.VarInt((ulong)((m.Type == ArchiveMemberType.Directory ? 1 : 0) | (m.BasicModifiedSeconds is null ? 0 : 2) | (m.HasCrc32 ? 4 : 0)));
                    body.VarInt((ulong)length); body.VarInt(m.Attributes);
                    if (m.BasicModifiedSeconds is { } basic) body.UInt32(basic); if (m.HasCrc32) body.UInt32(crc);
                    body.VarInt(m.Compression); body.VarInt((ulong)m.Host); byte[] name = Utf8.GetBytes(entry.Name); body.VarInt((ulong)name.Length); body.Bytes(name);
                    int extraStart = body.Length;
                    if (m.HasBlake2sp)
                    {
                        encodedHash = entry.Retained is { } old ? old.Blake2sp.ToArray() : countedBlake?.ToArray() ?? EmptyBlake();
                        if (newCipher && (m.EncryptionFlags!.Value & 2) != 0) encodedHash = Protect(encryption!, encodedHash);
                        int record = body.BeginExtra(2); body.VarInt(0); body.Bytes(encodedHash); body.EndExtra(record);
                    }
                    if (m.Times is { } times)
                    {
                        int record = body.BeginExtra(3); body.VarInt(times.Flags); bool unix = (times.Flags & 1) != 0;
                        Time(ref body, times.Modified, unix); Time(ref body, times.Created, unix); Time(ref body, times.Accessed, unix);
                        if ((times.Flags & 16) != 0)
                        { Fraction(ref body, times.Modified); Fraction(ref body, times.Created); Fraction(ref body, times.Accessed); }
                        body.EndExtra(record);
                    }
                    if (m.Owner is { } owner)
                    {
                        int record = body.BeginExtra(6); body.VarInt(owner.Flags);
                        if ((owner.Flags & 1) != 0) { body.VarInt((ulong)owner.UserName.Length); body.Bytes(owner.UserName); }
                        if ((owner.Flags & 2) != 0) { body.VarInt((ulong)owner.GroupName.Length); body.Bytes(owner.GroupName); }
                        if (owner.UserId is { } uid) body.VarInt(uid); if (owner.GroupId is { } gid) body.VarInt(gid); body.EndExtra(record);
                    }
                    if (m.EncryptionFlags is not null)
                    {
                        int record = body.BeginExtra(1);
                        if (entry.Retained is { } old) old.EncryptionBody(ref body);
                        else
                        {
                            body.VarInt(0); body.VarInt((ulong)m.EncryptionFlags.Value); body.Byte((byte)encryption!.CountPower);
                            body.Bytes(encryption.DataSalt); body.Bytes(target!.Value.Iv);
                            if ((m.EncryptionFlags.Value & 1) != 0) body.Bytes(encryption.DataCheck);
                        }
                        body.EndExtra(record);
                    }
                    Rar5WireHeaderWriter.Record(sink, body.BytesWritten, 2, packed, body.Length - extraStart, encryption, ct, m.HeaderFlags);
                    if (entry.Retained is { } retained)
                    {
                        using var source = Open(openPacked, retained.Id, retained.PackedLength);
                        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long left = retained.PackedLength;
                        byte[] bytes = new byte[ArchiveIoAuthority.MaximumReadBytes];
                        try
                        {
                            while (left > 0)
                            {
                                ct.ThrowIfCancellationRequested(); int wanted = (int)Math.Min(bytes.Length, left); int n = source.Read(bytes.AsSpan(0, wanted));
                                if (n <= 0 || n > wanted) throw Changed(); sha.AppendData(bytes, 0, n); sink.Write(bytes.AsSpan(0, n)); left -= n;
                            }
                            if (source.ReadByte() != -1 || Convert.ToHexString(sha.GetHashAndReset()) != retained.PackedSha256) throw Changed();
                        }
                        finally { CryptographicOperations.ZeroMemory(bytes); }
                    }
                    else if (entry.Captured is { } input)
                    {
                        using var source = Open(openCaptured, input.Input.Id, input.Input.Length); using var hashed = new BlakeSource(source, m.HasBlake2sp);
                        var emitted = Rar5Encoder.Encode(hashed, input.Input.Length, cipher is null ? sink : cipher, input.Compression, ct);
                        byte[]? emittedBlake = hashed.Finish();
                        try { if (emitted != counted || m.HasBlake2sp && !emittedBlake!.AsSpan().SequenceEqual(countedBlake)) throw Changed(); }
                        finally { if (emittedBlake is not null) CryptographicOperations.ZeroMemory(emittedBlake); }
                    }
                    if (cipher is not null) { cipher.Finish(); if (cipher.PlaintextBytes != plainPacked || cipher.CiphertextBytes != packed) throw Changed(); }
                    var key = (entry.Name, m.Type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
                    output.Add(new(entry.Name, m.Type, occurrence, length, entry.Retained?.Sha256 ?? counted?.Sha256 ?? Convert.ToHexString(SHA256.HashData([])), m.Encode()));
                }
                finally
                {
                    if (countedBlake is not null) CryptographicOperations.ZeroMemory(countedBlake);
                    if (encodedHash is not null) CryptographicOperations.ZeroMemory(encodedHash);
                }
            }
            var end = new Rar5AssemblyRecord(buffer); end.VarInt(0);
            Rar5WireHeaderWriter.Record(sink, end.BytesWritten, 5, null, 0, encryption, ct, features.EndHeaderFlags);
            if (stage.Position != sink.BytesWritten || stage.Length != sink.BytesWritten)
                throw new ArchiveWriteException(ArchiveWriteReason.VerificationFailed, "The RAR stage extent disagrees with its complete assembly.");
            return new(new(profile, output, features.Encode()), sink.BytesWritten);
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }

        Stream Open(Func<int, Stream> opener, int id, long length)
        {
            ct.ThrowIfCancellationRequested(); Stream source = opener(id) ?? throw Invalid();
            if (ReferenceEquals(source, stage)) throw Invalid();
            try { if (!source.CanRead || !source.CanSeek || source.Position != 0 || source.Length != length) throw Changed(); return source; }
            catch { source.Dispose(); throw; }
        }
    }
    private static byte[] EmptyBlake() { using var hash = new Blake2sp(); return hash.Finish(); }
    private static byte[] Protect(Rar5EncryptionSession session, byte[] hash)
    { try { return session.ProtectHash(hash); } finally { CryptographicOperations.ZeroMemory(hash); } }
    private static void Time(ref Rar5AssemblyRecord body, ArchiveTimestamp? value, bool unix)
    {
        if (value is not { } time) return;
        if (unix) body.UInt32(checked((uint)time.Seconds));
        else body.UInt64(checked((ulong)((time.Seconds + 11644473600) * 10000000 + time.Nanoseconds / 100)));
    }
    private static void Fraction(ref Rar5AssemblyRecord body, ArchiveTimestamp? value) { if (value is { } time) body.UInt32((uint)time.Nanoseconds); }
    internal static ArchiveWriteException Invalid() => new(ArchiveWriteReason.UnsupportedFeature, "The RAR assembly metadata, payload references or namespace cannot be preserved.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The reviewed RAR payload changed during assembly.");
    private sealed class BlakeSource(Stream source, bool enabled) : Stream
    {
        private readonly Blake2sp? hash = enabled ? new() : null;
        internal byte[]? Finish() => hash?.Finish();
        public override int Read(Span<byte> bytes)
        { int n = source.Read(bytes); if (n < 0 || n > bytes.Length) throw Changed(); hash?.Append(bytes[..n]); return n; }
        public override int Read(byte[] bytes, int offset, int count) => Read(bytes.AsSpan(offset, count));
        protected override void Dispose(bool disposing) { if (disposing) hash?.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => source.Length; public override long Position { get => source.Position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Sink(Stream source, CancellationToken ct) : Stream
    {
        internal long BytesWritten;
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            ct.ThrowIfCancellationRequested(); if (bytes.Length > ArchiveWritePolicy.MaxPhysicalBytes - BytesWritten) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The RAR stage exceeds its physical-byte budget.");
            while (!bytes.IsEmpty) { ct.ThrowIfCancellationRequested(); int n = Math.Min(bytes.Length, ArchiveIoAuthority.MaximumReadBytes); source.Write(bytes[..n]); BytesWritten += n; bytes = bytes[n..]; }
        }
        public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => BytesWritten; public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() => source.Flush(); public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
}

internal ref struct Rar5AssemblyRecord(byte[] buffer)
{
    private readonly Span<byte> bytes = buffer;
    internal int Length { get; private set; }
    internal ReadOnlySpan<byte> BytesWritten => bytes[..Length];
    private Span<byte> Take(int length)
    { if (length < 0 || length > bytes.Length - Length) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The RAR header exceeds its metadata budget."); var result = bytes.Slice(Length, length); Length += length; return result; }
    internal void Byte(byte value) => Take(1)[0] = value;
    internal void Bytes(ReadOnlySpan<byte> value) => value.CopyTo(Take(value.Length));
    internal void VarInt(ulong value) => Length += Rar5WireHeaderWriter.VarInt(bytes[Length..], value);
    internal void UInt32(uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Take(4), value);
    internal void UInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), value);
    internal int BeginExtra(int kind) { int offset = Length; Take(3); VarInt((ulong)kind); return offset; }
    internal void EndExtra(int offset)
    {
        int payload = Length - offset - 3; Span<byte> size = stackalloc byte[3]; int n = Rar5WireHeaderWriter.VarInt(size, (ulong)payload);
        bytes.Slice(offset + 3, payload).CopyTo(bytes[(offset + n)..]); size[..n].CopyTo(bytes[offset..]); Length -= 3 - n;
    }
}
