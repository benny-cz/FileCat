using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record TarInputDescriptor(int Id, long Length, string Sha256, ArchiveTimestamp Modified, int Mode = 0x1a4);
internal sealed record TarWriteAddition(string Name, ArchiveMemberType Type, int? InputId = null);
internal sealed record TarWriteEdit(ArchiveChangeKind Kind, ArchiveMemberKey Key, string? NewName = null, int? InputId = null);
internal sealed record TarRewriteManifest(TarEntryFormat Format, IReadOnlyList<TarWriteMember> Members);

/// <summary>
/// Stream-only TAR transform. Input IDs resolve only through parent-granted leases; this engine has no pathname,
/// publication, journal or deletion authority. Production registration requires a qualified archive worker.
/// </summary>
internal static class TarRewriteEngine
{
    internal static TarRewriteManifest Rebuild(Stream? original, string? expectedRevision, Stream stage,
        IReadOnlyList<TarWriteEdit> edits, IReadOnlyList<TarWriteAddition> additions,
        IReadOnlyDictionary<int, TarInputDescriptor> inputs, Func<int, Stream> openInput, CancellationToken ct)
    {
        if (!stage.CanWrite || stage.CanSeek && stage.Length != 0 || ReferenceEquals(stage, original) ||
            edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers ||
            inputs.Count > ArchiveWritePolicy.MaxMembers)
            throw Invalid("The TAR transformation request is invalid or too large.");
        TarWriteCatalog? catalog = null;
        if (original is not null)
        {
            if (!original.CanSeek || !original.CanRead || original.Length > ArchiveWritePolicy.MaxPhysicalBytes || expectedRevision is null)
                throw Invalid("An update needs a bounded, seekable source and its reviewed revision.");
            ArchiveWritePolicy.ValidateDigest(expectedRevision);
            original.Position = 0;
            catalog = StrictTarCatalog.Read(original, ct);
            if (catalog.Revision != expectedRevision) throw Changed();
            original.Position = 0;
            StrictTarCatalog.CompareDecoder(original, catalog, ct);
            ValidateNamespace(catalog.Members.Select(m => (m, (int?)null, false)).ToList());
        }
        else if (expectedRevision is not null || edits.Count != 0) throw Invalid("A new archive cannot target existing members.");

        long inputBytes = 0;
        foreach (var member in catalog?.Members ?? []) inputBytes = checked(inputBytes + member.Length);

        foreach (var (id, input) in inputs)
        {
            if (id < 0 || id != input.Id || input.Length < 0 || input.Length > ArchiveWritePolicy.MaxPayloadBytes ||
                (input.Mode & ~0x1ff) != 0) throw Invalid("A captured input has invalid size, mode or identity.");
            ArchiveWritePolicy.ValidateDigest(input.Sha256);
        }
        TarEntryFormat format = catalog?.Format ?? TarEntryFormat.Pax;
        var targets = new Dictionary<(string, ArchiveMemberType, int), TarWriteEdit>();
        var sourceKeys = new HashSet<(string, ArchiveMemberType, int)>();
        foreach (var member in catalog?.Members ?? []) sourceKeys.Add((member.Name, member.Type, member.Occurrence));
        foreach (var edit in edits)
        {
            var key = (edit.Key.RawPath, edit.Key.Type, edit.Key.Occurrence);
            if (catalog is null || edit.Key.CatalogRevision != catalog.Revision || !sourceKeys.Contains(key) || !targets.TryAdd(key, edit))
                throw Changed();
            if (edit.Kind is not (ArchiveChangeKind.Delete or ArchiveChangeKind.Rename or ArchiveChangeKind.Replace))
                throw Invalid("This TAR member operation is not supported.");
            if (edit.Kind == ArchiveChangeKind.Rename)
                ArchiveWritePolicy.ValidateName(edit.NewName ?? "", edit.Key.Type);
            if (edit.Kind == ArchiveChangeKind.Replace && (edit.Key.Type != ArchiveMemberType.File ||
                edit.InputId is not { } replaceId || !inputs.ContainsKey(replaceId))) throw Invalid("A replacement needs one captured regular file.");
            if (edit.Kind == ArchiveChangeKind.Replace) ChargeInput(inputs[edit.InputId!.Value].Length);
            if (edit.Kind != ArchiveChangeKind.Replace && edit.InputId is not null ||
                edit.Kind != ArchiveChangeKind.Rename && edit.NewName is not null)
                throw Invalid("The TAR operation contains unrelated options.");
        }

        // Freeze the complete ordered output before touching the stage. Recursive folder operations arrive expanded
        // into exact physical keys; a name-only delete/rename can never widen their scope inside this engine.
        var planned = new List<(TarWriteMember Member, int? InputId, bool RenamedOrAdded)>();
        var budget = new ArchiveWriteBudget();
        foreach (var old in catalog?.Members ?? [])
        {
            ct.ThrowIfCancellationRequested();
            targets.TryGetValue((old.Name, old.Type, old.Occurrence), out var edit);
            if (edit?.Kind == ArchiveChangeKind.Delete) continue;
            TarWriteMember member = old;
            int? inputId = null;
            if (edit?.Kind == ArchiveChangeKind.Rename) member = old with { Name = edit.NewName! };
            if (edit?.Kind == ArchiveChangeKind.Replace)
            {
                inputId = edit.InputId;
                var input = inputs[inputId!.Value];
                member = member with { Length = input.Length, Sha256 = input.Sha256 };
            }
            ReserveMember(budget, member, format);
            planned.Add((member, inputId, member.Name != old.Name));
        }
        foreach (var addition in additions)
        {
            ct.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(addition.Type)) throw Invalid("An addition has an unsupported entry type.");
            ArchiveWritePolicy.ValidateName(addition.Name, addition.Type);
            TarInputDescriptor? input = null;
            if (addition.Type == ArchiveMemberType.File)
            {
                if (addition.InputId is not { } id || !inputs.TryGetValue(id, out input)) throw Invalid("A file addition needs a captured input.");
                ChargeInput(input.Length);
            }
            else if (addition.InputId is not null) throw Invalid("A directory cannot carry a file payload.");
            decimal modified = input?.Modified.UnixSeconds ?? 0;
            if (format == TarEntryFormat.Ustar) modified = decimal.Truncate(modified);
            var member = new TarWriteMember(addition.Name, addition.Type, 0, input?.Length ?? 0,
                input?.Mode ?? 0x1ed, 0, 0, "", "", modified, null, null,
                input?.Sha256 ?? Convert.ToHexString(SHA256.HashData([])), 0);
            ReserveMember(budget, member, format);
            planned.Add((member, addition.InputId, true));
        }
        ValidateNamespace(planned);

        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        var manifest = new List<TarWriteMember>(planned.Count);
        using var exactPax = format == TarEntryFormat.Pax ? new PaxTimestampWriteStream(stage, ct) : null;
        var writer = new TarWriter(exactPax ?? stage, format, leaveOpen: true);
        try
        {
            foreach (var (member, inputId, _) in planned)
            {
                ct.ThrowIfCancellationRequested();
                exactPax?.BeginMember(member.Modified, member.Length);
                var kind = member.Type == ArchiveMemberType.File ? TarEntryType.RegularFile : TarEntryType.Directory;
                PosixTarEntry entry;
                if (format == TarEntryFormat.Pax)
                {
                    var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (member.Accessed is { } accessed) attrs.Add("atime", accessed.ToString(CultureInfo.InvariantCulture));
                    if (member.Changed is { } changed) attrs.Add("ctime", changed.ToString(CultureInfo.InvariantCulture));
                    entry = new PaxTarEntry(kind, member.Name, attrs);
                }
                else entry = new UstarTarEntry(kind, member.Name);
                entry.Mode = (UnixFileMode)member.Mode;
                entry.Uid = member.Uid;
                entry.Gid = member.Gid;
                entry.UserName = member.UserName;
                entry.GroupName = member.GroupName;
                entry.ModificationTime = StrictTarCatalog.ToTime(member.Modified);
                if (member.Type == ArchiveMemberType.File)
                {
                    using var lease = inputId is { } id ? openInput(id) : null;
                    Stream source = lease ?? original ?? throw Invalid("A preserved member has no source stream.");
                    if (!source.CanSeek) throw Invalid("A TAR payload needs a seekable captured input to avoid whole-file buffering.");
                    if (lease is null) source.Position = member.Offset;
                    else if (source.CanSeek && (source.Position != 0 || source.Length != member.Length)) throw Changed();
                    using var content = new CheckedContent(source, member.Length, member.Sha256, ct);
                    entry.DataStream = content;
                    writer.WriteEntry(entry);
                    content.Complete();
                }
                else writer.WriteEntry(entry);
                exactPax?.CompleteMember();
                var key = (member.Name, member.Type);
                int occurrence = occurrences.GetValueOrDefault(key);
                occurrences[key] = occurrence + 1;
                manifest.Add(member with { Offset = 0, Occurrence = occurrence });
            }
            exactPax?.BeginEndMarkers(planned.Count != 0);
        }
        catch
        {
            // Finalizer writes must neither extend a failed PAX entry nor replace the original source/cancel error.
            exactPax?.Abort(); try { writer.Dispose(); } catch { }
            throw;
        }
        writer.Dispose();
        exactPax?.CompleteArchive();
        // TarWriter emits no bytes if no entry was written. Removing the last member still requires a real TAR
        // with both end markers; an empty byte stream is not admitted by the strict verifier.
        if (planned.Count == 0) stage.Write(new byte[1024]);
        stage.Flush(); // the parent flushes the real stage descriptor to disk after transform exit
        return new TarRewriteManifest(format, manifest.AsReadOnly());

        void ChargeInput(long bytes)
        {
            if (bytes > ArchiveWritePolicy.MaxLogicalBytes - inputBytes) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The TAR source and requested payloads exceed the job's logical input allowance.");
            inputBytes += bytes;
        }
    }

    internal static void Verify(Stream stage, TarRewriteManifest expected, CancellationToken ct)
    {
        if (!stage.CanSeek || !stage.CanRead || stage.Length > ArchiveWritePolicy.MaxPhysicalBytes)
            throw Invalid("A verifier needs a bounded seekable staged archive.");
        stage.Position = 0;
        var observed = StrictTarCatalog.Read(stage, ct);
        CompareManifest(observed, expected);
        stage.Position = 0;
        StrictTarCatalog.CompareDecoder(stage, observed, ct);
    }

    internal static void VerifyGzip(Stream stage, TarRewriteManifest expected, CancellationToken ct)
    {
        stage.Position = 0;
        TarWriteCatalog observed;
        using (var gzip = new StrictGzipReadStream(stage, ct)) observed = StrictTarCatalog.Read(gzip, ct);
        CompareManifest(observed, expected);
        stage.Position = 0;
        using var independentPass = new StrictGzipReadStream(stage, ct);
        StrictTarCatalog.CompareDecoder(independentPass, observed, ct);
        independentPass.CopyTo(Stream.Null); // validate the outer trailer even if TarReader stopped at its end marker
    }

    private static void CompareManifest(TarWriteCatalog observed, TarRewriteManifest expected)
    {
        if (observed.Members.Count != expected.Members.Count || observed.Members.Count > 0 && observed.Format != expected.Format)
            throw Verification();
        for (int i = 0; i < expected.Members.Count; i++)
            if (observed.Members[i] with { Offset = 0 } != expected.Members[i] with { Offset = 0 }) throw Verification();
    }

    private static void ReserveMember(ArchiveWriteBudget budget, TarWriteMember member, TarEntryFormat format)
    {
        ArchiveWritePolicy.ValidateName(member.Name, member.Type);
        budget.AddMember();
        budget.AddNameChars(member.Name.Length);
        budget.AddLogicalBytes(member.Length);
        long chars = (long)member.Name.Length + member.UserName.Length + member.GroupName.Length;
        budget.AddMetadata(checked(chars * 4 + 256), checked(chars * 2 + 1024));
        if (format == TarEntryFormat.Ustar &&
            (member.Modified != decimal.Truncate(member.Modified) || member.Modified < 0 || member.Modified > 0x1ffffffffL ||
             member.Length > 0x1ffffffffL || member.Uid > 0x1fffff || member.Gid > 0x1fffff ||
             System.Text.Encoding.UTF8.GetByteCount(member.UserName) > 32 || System.Text.Encoding.UTF8.GetByteCount(member.GroupName) > 32 ||
             !FitsUstarName(member.Name)))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This change cannot preserve the USTAR archive's format and metadata.");
    }

    private static bool FitsUstarName(string name)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(name) <= 100) return true;
        for (int slash = name.LastIndexOf('/'); slash > 0; slash = name.LastIndexOf('/', slash - 1))
            if (slash < name.Length - 1 && System.Text.Encoding.UTF8.GetByteCount(name.AsSpan(0, slash)) <= 155 &&
                System.Text.Encoding.UTF8.GetByteCount(name.AsSpan(slash + 1)) <= 100) return true;
        return false;
    }

    private static void ValidateNamespace(List<(TarWriteMember Member, int? InputId, bool RenamedOrAdded)> planned)
    {
        var names = new Dictionary<string, (ArchiveMemberType Type, bool Changed)>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (member, _, changed) in planned)
        {
            string name = member.Type == ArchiveMemberType.Directory ? member.Name.TrimEnd('/') : member.Name;
            if (names.TryGetValue(name, out var previous) && (changed || previous.Changed || previous.Type != member.Type))
                throw Invalid("The TAR change would create a member name or file/folder collision.");
            names[name] = (member.Type, changed);
            if (member.Type == ArchiveMemberType.File) files.Add(name);
        }
        foreach (string name in names.Keys)
            for (int slash = name.LastIndexOf('/'); slash >= 0; slash = name.LastIndexOf('/', slash - 1))
            {
                if (files.Contains(name[..slash])) throw Invalid("A regular TAR file would also be used as a folder.");
                if (slash == 0) break;
            }
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidRequest, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The archive or captured input changed after review. Review the change again.");
    private static ArchiveWriteException Verification() => new(ArchiveWriteReason.VerificationFailed, "The rebuilt TAR does not match every planned member and metadata field.");

    private sealed class CheckedContent(Stream source, long length, string expectedDigest, CancellationToken ct) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly long _start = source.Position;
        private long _position;
        private long _readBytes;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            if (_position == length) return 0;
            buffer = buffer[..(int)Math.Min(buffer.Length, length - _position)];
            int read = source.Read(buffer);
            if (read == 0 && buffer.Length > 0) throw Changed();
            if (read > length - _readBytes) throw Changed();
            _hash.AppendData(buffer[..read]);
            _position += read;
            _readBytes += read;
            return read;
        }
        public void Complete()
        {
            if (_position != length || _readBytes != length || Convert.ToHexString(_hash.GetHashAndReset()) != expectedDigest) throw Changed();
        }
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
        {
            long basis = origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => _position, SeekOrigin.End => length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (offset < -basis || offset > length - basis) throw Changed();
            _position = basis + offset;
            source.Position = checked(_start + _position);
            return _position;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
