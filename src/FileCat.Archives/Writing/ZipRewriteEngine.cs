using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// Stream-only transform for the worker ZIP profile (ZIP_WORKER_PROFILE_DESIGN.md). The reviewed source is strictly assessed
/// again; members keep their order and an unchanged member its original compressed bytes; a new or replaced payload is
/// Deflate-compressed behind a data descriptor, so no header is rewritten. Headers are written canonically from each
/// member's metadata. Input IDs resolve only through parent grants; there is no path, publication or deletion authority.
/// </summary>
internal static class ZipRewriteEngine
{
    private sealed record Planned(string Name, ArchiveMemberType Type, byte[] Metadata, ZipWriteMember? Source, ArchiveTransformInput? Input);
    private sealed record Written(byte[] Name, ushort Needed, ushort Flags, ushort Method, uint Crc, long Compressed, long Uncompressed,
        long Offset, ArchiveZipMetadata Metadata, byte[] Comment);

    internal static ArchiveWriteManifest Rebuild(Stream? original, string? expectedRevision, ArchiveWriteProfile profile, Stream stage,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions, IReadOnlyList<ArchiveTransformInput> inputs,
        Func<int, Stream> openInput, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(stage); ArgumentNullException.ThrowIfNull(openInput);
        var level = ArchiveZipFeatures.Level(profile);
        if (profile.Encryption.Mode != ArchiveEncryptionMode.None || !stage.CanWrite || stage.CanSeek && stage.Length != 0 || ReferenceEquals(stage, original) ||
            edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers || inputs.Count > ArchiveWritePolicy.MaxMembers)
            throw Invalid("The ZIP transformation request is invalid or too large.");
        ZipWriteCatalog? catalog = null;
        if (original is not null)
        {
            if (!original.CanSeek || !original.CanRead || expectedRevision is null) throw Invalid("An update needs a seekable source and its reviewed revision.");
            ArchiveWritePolicy.ValidateDigest(expectedRevision);
            catalog = StrictZipCatalog.Read(original, ct);
            if (catalog.Revision != expectedRevision || catalog.Length != original.Length) throw Changed();
        }
        else if (expectedRevision is not null || edits.Count != 0) throw Invalid("A new ZIP cannot target existing members.");

        var grants = new Dictionary<int, ArchiveTransformInput>();
        foreach (var input in inputs) if (input is null || !grants.TryAdd(input.Id, input)) throw Invalid("A captured input is repeated.");
        var targets = new Dictionary<(string, ArchiveMemberType, int), ArchiveTransformEdit>();
        // One set of the catalog's member keys keeps matching linear in members plus edits.
        var keys = catalog is null ? null : catalog.Members.Select(m => (m.Name, m.Type, m.Occurrence)).ToHashSet();
        foreach (var edit in edits)
        {
            ct.ThrowIfCancellationRequested();
            if (edit is null || catalog is null || edit.Key.CatalogRevision != catalog.Revision ||
                !keys!.Contains((edit.Key.RawPath, edit.Key.Type, edit.Key.Occurrence)) ||
                !targets.TryAdd((edit.Key.RawPath, edit.Key.Type, edit.Key.Occurrence), edit)) throw Changed();
            if (edit.Kind == ArchiveChangeKind.Replace && (edit.InputId is not { } id || !grants.ContainsKey(id)))
                throw Invalid("A replacement needs one captured regular file.");
        }

        // Freeze the complete ordered output before writing, exactly as the parent's plan derives its expectation.
        var planned = new List<Planned>();
        var budget = new ArchiveWriteBudget();
        foreach (var old in catalog?.Members ?? [])
        {
            ct.ThrowIfCancellationRequested();
            targets.TryGetValue((old.Name, old.Type, old.Occurrence), out var edit);
            if (edit?.Kind == ArchiveChangeKind.Delete) continue;
            string name = edit?.Kind == ArchiveChangeKind.Rename ? ArchiveZipTransformPlan.Name(edit.NewName!, old.Type) : old.Name;
            var input = edit?.Kind == ArchiveChangeKind.Replace ? grants[edit.InputId!.Value] : null;
            planned.Add(new(name, old.Type, old.Metadata, input is null ? old : null, input));
        }
        foreach (var addition in additions)
        {
            ct.ThrowIfCancellationRequested();
            if (addition is null) throw Invalid("An addition is missing.");
            ArchiveTransformInput? input = null; ArchiveZipMetadata metadata;
            if (addition.Type == ArchiveMemberType.File)
            {
                if (addition.InputId is not { } id || !grants.TryGetValue(id, out input)) throw Invalid("A file addition needs a captured input.");
                metadata = ArchiveZipMetadata.ForCaptured(ArchiveMemberType.File, input.Mode, input.ModifiedTimestamp, input.LocalOffsetMinutes);
            }
            else
            {
                var folder = addition.DirectoryMetadata ?? throw Invalid("A folder addition needs its metadata.");
                metadata = ArchiveZipMetadata.ForCaptured(ArchiveMemberType.Directory, folder.Mode, folder.Modified, folder.LocalOffsetMinutes);
            }
            planned.Add(new(ArchiveZipTransformPlan.Name(addition.Name, addition.Type), addition.Type, metadata.Encode(), null, input));
        }
        var members = new List<ArchiveManifestMember>(planned.Count);
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        foreach (var item in planned)
        {
            ct.ThrowIfCancellationRequested();
            ArchiveWritePolicy.ValidateName(item.Name, item.Type);
            if ((item.Type == ArchiveMemberType.Directory) != item.Name.EndsWith('/')) throw Invalid("A ZIP folder name must end in a slash.");
            var key = (item.Name, item.Type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
            long length = item.Input?.Length ?? item.Source?.Length ?? 0;
            string sha = item.Input?.Sha256 ?? item.Source?.Sha256 ?? "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";
            ArchiveWriteManifest.Reserve(budget, item.Name.Length, ArchiveWritePolicy.Utf8.GetByteCount(item.Name), item.Metadata.Length, length);
            members.Add(new(item.Name, item.Type, occurrence, length, sha, item.Metadata));
        }
        byte[] comment = catalog?.Comment ?? [];
        var manifest = new ArchiveWriteManifest(profile, members, ArchiveZipFeatures.Create(comment));

        var output = new Output(stage);
        var written = new List<Written>(planned.Count);
        byte[] buffer = new byte[ArchiveIoAuthority.MaximumReadBytes];
        try
        {
            foreach (var item in planned)
            {
                ct.ThrowIfCancellationRequested();
                var metadata = ArchiveZipMetadata.Decode(item.Metadata);
                byte[] name = ArchiveWritePolicy.Utf8.GetBytes(item.Name), note = ArchiveWritePolicy.Utf8.GetBytes(metadata.Comment);
                // Without the UTF-8 flag the strict catalog admits only printable ASCII (and TAB, LF, CR in comments), so any
                // other byte, even below 0x80, keeps the flag; a name or comment too long for its 16-bit field is refused.
                ushort utf8 = (ushort)(Plain(name, comment: false) && Plain(note, comment: true) ? 0 : 0x800);
                if (name.Length > ushort.MaxValue || note.Length > ushort.MaxValue)
                    throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "A ZIP archive stores names and comments of at most 65,535 bytes.");
                long offset = output.Written;
                if (item.Source is { } source)
                {
                    bool zip64 = source.Length >= uint.MaxValue || source.CompressedLength >= uint.MaxValue;
                    ushort needed = (ushort)(zip64 ? 45 : source.Method == 8 || item.Type == ArchiveMemberType.Directory ? 20 : 10);
                    ushort flags = (ushort)(source.Flags | utf8);
                    WriteLocal(output, needed, flags, source.Method, metadata, source.Crc, source.CompressedLength, source.Length, zip64, name);
                    original!.Position = source.DataOffset;
                    for (long left = source.CompressedLength; left > 0;)
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = original.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (n <= 0) throw Changed(); output.Write(buffer, 0, n); left -= n;
                    }
                    written.Add(new(name, needed, flags, source.Method, source.Crc, source.CompressedLength, source.Length, offset, metadata, note));
                }
                else if (item.Input is not { Length: > 0 } input)
                {
                    if (item.Input is { } empty) Confirm(openInput(empty.Id), empty, buffer, ct); // the reviewed empty file is still empty
                    ushort needed = (ushort)(item.Type == ArchiveMemberType.Directory ? 20 : 10);
                    WriteLocal(output, needed, utf8, 0, metadata, 0, 0, 0, zip64: false, name);
                    written.Add(new(name, needed, utf8, 0, 0, 0, 0, offset, metadata, note));
                }
                else
                {
                    // A payload whose Deflate ceiling could pass 4 GiB records ZIP64 sizes in its descriptor.
                    bool zip64 = ArchiveZipTransformPlan.DeflateBound(input.Length) >= uint.MaxValue;
                    ushort method = (ushort)(level is null ? 0 : 8);
                    ushort needed = (ushort)(zip64 ? 45 : method == 8 ? 20 : 10), flags = (ushort)(0x8 | utf8);
                    WriteLocal(output, needed, flags, method, metadata, 0, 0, 0, zip64, name);
                    long start = output.Written; uint crc;
                    using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        var check = new ArchiveCrc32(); long consumed = 0;
                        using (Stream encoder = level is { } deflateLevel ? new DeflateStream(output, deflateLevel, leaveOpen: true) : new Unclosed(output))
                        using (var from = openInput(input.Id))
                        {
                            for (int n; (n = from.Read(buffer, 0, buffer.Length)) != 0;)
                            {
                                ct.ThrowIfCancellationRequested(); consumed = checked(consumed + n);
                                if (consumed > input.Length) throw Changed();
                                hash.AppendData(buffer, 0, n); check.Append(buffer.AsSpan(0, n)); encoder.Write(buffer, 0, n);
                            }
                        }
                        if (consumed != input.Length || Convert.ToHexString(hash.GetHashAndReset()) != input.Sha256) throw Changed();
                        crc = check.Value;
                    }
                    long compressed = output.Written - start;
                    if (compressed > ArchiveZipTransformPlan.DeflateBound(input.Length)) throw ArchiveWriteBudget.Limit();
                    Span<byte> descriptor = stackalloc byte[24]; int size = zip64 ? 24 : 16;
                    BinaryPrimitives.WriteUInt32LittleEndian(descriptor, StrictZipCatalog.DescriptorSignature);
                    BinaryPrimitives.WriteUInt32LittleEndian(descriptor[4..], crc);
                    if (zip64) { BinaryPrimitives.WriteInt64LittleEndian(descriptor[8..], compressed); BinaryPrimitives.WriteInt64LittleEndian(descriptor[16..], input.Length); }
                    else { BinaryPrimitives.WriteUInt32LittleEndian(descriptor[8..], (uint)compressed); BinaryPrimitives.WriteUInt32LittleEndian(descriptor[12..], (uint)input.Length); }
                    output.Write(descriptor[..size]);
                    written.Add(new(name, needed, flags, method, crc, compressed, input.Length, offset, metadata, note));
                }
            }

            long directoryOffset = output.Written;
            foreach (var entry in written) { ct.ThrowIfCancellationRequested(); WriteCentral(output, entry); }
            long directorySize = output.Written - directoryOffset;
            bool zip64End = written.Count >= 0xffff || directorySize >= uint.MaxValue || directoryOffset >= uint.MaxValue;
            if (zip64End)
            {
                long record = output.Written;
                Span<byte> end64 = stackalloc byte[76];
                BinaryPrimitives.WriteUInt32LittleEndian(end64, StrictZipCatalog.Zip64EndSignature);
                BinaryPrimitives.WriteUInt64LittleEndian(end64[4..], 44);
                BinaryPrimitives.WriteUInt16LittleEndian(end64[12..], ArchiveZipMetadata.UnixHost << 8 | 63);
                BinaryPrimitives.WriteUInt16LittleEndian(end64[14..], 45);
                BinaryPrimitives.WriteInt64LittleEndian(end64[24..], written.Count); BinaryPrimitives.WriteInt64LittleEndian(end64[32..], written.Count);
                BinaryPrimitives.WriteInt64LittleEndian(end64[40..], directorySize); BinaryPrimitives.WriteInt64LittleEndian(end64[48..], directoryOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(end64[56..], StrictZipCatalog.Zip64LocatorSignature);
                BinaryPrimitives.WriteInt64LittleEndian(end64[64..], record); BinaryPrimitives.WriteUInt32LittleEndian(end64[72..], 1);
                output.Write(end64);
            }
            Span<byte> end = stackalloc byte[22];
            BinaryPrimitives.WriteUInt32LittleEndian(end, StrictZipCatalog.EndSignature);
            ushort count = (ushort)Math.Min(written.Count, 0xffff);
            BinaryPrimitives.WriteUInt16LittleEndian(end[8..], count); BinaryPrimitives.WriteUInt16LittleEndian(end[10..], count);
            BinaryPrimitives.WriteUInt32LittleEndian(end[12..], (uint)Math.Min(directorySize, uint.MaxValue));
            BinaryPrimitives.WriteUInt32LittleEndian(end[16..], (uint)Math.Min(directoryOffset, uint.MaxValue));
            BinaryPrimitives.WriteUInt16LittleEndian(end[20..], (ushort)comment.Length);
            output.Write(end); output.Write(comment);
            output.Flush();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); output.Clear(); }
        return manifest;
    }

    private static void WriteLocal(Output output, ushort needed, ushort flags, ushort method, ArchiveZipMetadata metadata, uint crc, long compressed,
        long uncompressed, bool zip64, byte[] name)
    {
        byte[] extra = Extras(metadata, central: false, zip64 ? [uncompressed, compressed] : []);
        Span<byte> header = stackalloc byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(header, StrictZipCatalog.LocalSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], needed); BinaryPrimitives.WriteUInt16LittleEndian(header[6..], flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], method); BinaryPrimitives.WriteUInt16LittleEndian(header[10..], metadata.DosTime);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], metadata.DosDate); BinaryPrimitives.WriteUInt32LittleEndian(header[14..], crc);
        BinaryPrimitives.WriteUInt32LittleEndian(header[18..], zip64 ? uint.MaxValue : (uint)compressed);
        BinaryPrimitives.WriteUInt32LittleEndian(header[22..], zip64 ? uint.MaxValue : (uint)uncompressed);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], checked((ushort)name.Length)); BinaryPrimitives.WriteUInt16LittleEndian(header[28..], checked((ushort)extra.Length));
        output.Write(header); output.Write(name); output.Write(extra);
    }

    private static void WriteCentral(Output output, Written entry)
    {
        var saturated = new List<long>(3);
        if (entry.Uncompressed >= uint.MaxValue) saturated.Add(entry.Uncompressed);
        if (entry.Compressed >= uint.MaxValue) saturated.Add(entry.Compressed);
        if (entry.Offset >= uint.MaxValue) saturated.Add(entry.Offset);
        byte[] extra = Extras(entry.Metadata, central: true, saturated.ToArray());
        Span<byte> header = stackalloc byte[46];
        BinaryPrimitives.WriteUInt32LittleEndian(header, StrictZipCatalog.CentralSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], entry.Metadata.VersionMadeBy); BinaryPrimitives.WriteUInt16LittleEndian(header[6..], entry.Needed);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], entry.Flags); BinaryPrimitives.WriteUInt16LittleEndian(header[10..], entry.Method);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], entry.Metadata.DosTime); BinaryPrimitives.WriteUInt16LittleEndian(header[14..], entry.Metadata.DosDate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], entry.Crc);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)Math.Min(entry.Compressed, uint.MaxValue));
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)Math.Min(entry.Uncompressed, uint.MaxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], checked((ushort)entry.Name.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header[30..], checked((ushort)extra.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], checked((ushort)entry.Comment.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(header[36..], entry.Metadata.InternalAttributes);
        BinaryPrimitives.WriteUInt32LittleEndian(header[38..], entry.Metadata.ExternalAttributes);
        BinaryPrimitives.WriteUInt32LittleEndian(header[42..], (uint)Math.Min(entry.Offset, uint.MaxValue));
        output.Write(header); output.Write(entry.Name); output.Write(extra); output.Write(entry.Comment);
    }

    /// <summary>Canonical admitted extra fields: ZIP64 values first, then the extended timestamp, NTFS times and owner IDs.</summary>
    private static byte[] Extras(ArchiveZipMetadata metadata, bool central, long[] zip64)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (zip64.Length != 0) { writer.Write(StrictZipCatalog.Zip64Extra); writer.Write((ushort)(8 * zip64.Length)); foreach (long value in zip64) writer.Write(value); }
        if (metadata.TimeFlags != 0)
        {
            int times = central ? ((metadata.TimeFlags & 1) != 0 ? 1 : 0) : System.Numerics.BitOperations.PopCount(metadata.TimeFlags);
            writer.Write(StrictZipCatalog.TimeExtra); writer.Write((ushort)(1 + 4 * times)); writer.Write(metadata.TimeFlags);
            if ((metadata.TimeFlags & 1) != 0) writer.Write(metadata.Modified);
            if (!central && (metadata.TimeFlags & 2) != 0) writer.Write(metadata.Accessed);
            if (!central && (metadata.TimeFlags & 4) != 0) writer.Write(metadata.Created);
        }
        if (metadata.Ntfs is { } ntfs)
        {
            writer.Write(StrictZipCatalog.NtfsExtra); writer.Write((ushort)32); writer.Write(0u); writer.Write((ushort)1); writer.Write((ushort)24);
            writer.Write(ntfs.Modified); writer.Write(ntfs.Accessed); writer.Write(ntfs.Created);
        }
        if (metadata.Owner is { } owner)
        {
            // Info-ZIP's convention: the IDs in the local header, an empty field in the central record.
            writer.Write(StrictZipCatalog.OwnerExtra);
            if (central) writer.Write((ushort)0);
            else
            {
                writer.Write((ushort)(3 + owner.UidSize + owner.GidSize)); writer.Write((byte)1);
                writer.Write(owner.UidSize); for (int i = 0; i < owner.UidSize; i++) writer.Write((byte)(owner.Uid >> (8 * i)));
                writer.Write(owner.GidSize); for (int i = 0; i < owner.GidSize; i++) writer.Write((byte)(owner.Gid >> (8 * i)));
            }
        }
        writer.Flush();
        if (stream.Length > ushort.MaxValue) throw ArchiveZipMetadata.Unsupported();
        return stream.ToArray();
    }

    private static void Confirm(Stream input, ArchiveTransformInput expected, byte[] buffer, CancellationToken ct)
    {
        using (input)
        {
            ct.ThrowIfCancellationRequested();
            if (expected.Length != 0 || input.Read(buffer, 0, 1) != 0) throw Changed();
        }
    }

    private static bool Plain(ReadOnlySpan<byte> bytes, bool comment)
    {
        foreach (byte b in bytes) if (b is < 0x20 or > 0x7e && !(comment && b is 0x09 or 0x0a or 0x0d)) return false;
        return true;
    }
    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidRequest, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The reviewed ZIP or a captured input changed during the rebuild.");

    /// <summary>Stored payload bytes written straight through; disposing it leaves the shared output open.</summary>
    private sealed class Unclosed(Stream output) : Stream
    {
        public override void Write(ReadOnlySpan<byte> bytes) => output.Write(bytes);
        public override void Write(byte[] bytes, int offset, int count) => output.Write(bytes, offset, count);
        public override void Flush() { }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>Coalesces the many small header writes into large stage writes and counts every byte.</summary>
    private sealed class Output(Stream stage) : Stream
    {
        private readonly byte[] buffer = new byte[1 << 20];
        private int used;
        internal long Written { get; private set; }
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            while (!bytes.IsEmpty)
            {
                int n = Math.Min(bytes.Length, buffer.Length - used);
                bytes[..n].CopyTo(buffer.AsSpan(used)); used += n; Written += n; bytes = bytes[n..];
                if (used == buffer.Length) Drain();
            }
        }
        public override void Write(byte[] bytes, int offset, int count) => Write(bytes.AsSpan(offset, count));
        public override void Flush() { Drain(); stage.Flush(); }
        private void Drain() { if (used == 0) return; stage.Write(buffer, 0, used); buffer.AsSpan(0, used).Clear(); used = 0; }
        internal void Clear() => buffer.AsSpan().Clear();
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => Written; public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override int Read(byte[] bytes, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
