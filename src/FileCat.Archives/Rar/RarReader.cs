namespace FileCat.Archives.Rar;

/// <summary>
/// FileCat's own RAR reader (RAR 1.5–4.x and RAR 5/7 archives, single or multi-volume). Non-solid members open
/// independently; a solid run keeps one decoder and window and is read forward, restarting at the run's first member
/// when an earlier member is asked for.
/// </summary>
internal sealed class RarReader : IMemberReader
{
    private readonly List<Stream> _volumes;
    private readonly RarArchive _archive;
    private readonly bool _numberedVolumeGap;
    private RarBitReader _bits = new();
    private RarMemberStream? _decoded;   // the member the decoder is in (solid runs continue from it)
    private int _decodedIndex = -1;
    private RarDecoder? _decoder;
    private RarWindow? _window;
    private bool _disposed;

    private RarReader(List<Stream> volumes, RarArchive archive, bool numberedVolumeGap)
    {
        _volumes = volumes;
        _archive = archive;
        _numberedVolumeGap = numberedVolumeGap;
    }

    public string Format => _volumes.Count > 1 ? $"RAR ({_volumes.Count} volumes)" : "RAR";

    public bool SharesCursor => _archive.Solid;

    internal IReadOnlyList<FileStream> VolumeStreams => _volumes.OfType<FileStream>().ToList();

    /// <summary>Reads the headers of <paramref name="volumes"/> (in order); the reader owns the streams from here on.</summary>
    public static RarReader Open(List<FileStream> volumes, bool numberedVolumeGap, CancellationToken ct = default) => Open(volumes.Cast<Stream>().ToList(), numberedVolumeGap, ct);

    /// <summary>The same over any seekable streams (in-memory volumes for fuzzing).</summary>
    internal static RarReader Open(List<Stream> volumes, bool numberedVolumeGap, CancellationToken ct = default)
    {
        try
        {
            var archive = RarArchive.Read(volumes, ct);
            return new RarReader(volumes, archive, numberedVolumeGap);
        }
        catch
        {
            foreach (var volume in volumes) volume.Dispose();
            throw;
        }
    }

    public IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var warnings = new List<string>(_archive.Warnings);
        if (_numberedVolumeGap && !warnings.Contains(RarArchiveWarnings.MissingVolumes)) warnings.Add(RarArchiveWarnings.MissingVolumes);
        foreach (string warning in warnings) warn(warning);
        for (int i = 0; i < _archive.Members.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = _archive.Members[i];
            long size = m.Kind == MemberKind.Directory ? 0 : Math.Max(m.UnpackedSize, 0);
            yield return new MemberInfo(i, m.Name, m.Kind, size, m.PackedSize, ArchiveFormats.Utc(m.ModifiedUtc), m.Encrypted, m.LinkTarget);
        }
    }

    public Stream Open(int index, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_archive.Members.Count) throw new FileNotFoundException("The member is no longer in the archive.");
        var member = _archive.Members[index];
        if (member.Kind != MemberKind.File) return Stream.Null;
        if (member.Encrypted) throw new InvalidDataException("This member is encrypted; FileCat does not ask for archive passwords.");
        if (!member.Complete) throw new InvalidDataException("This member continues in a volume that is missing.");
        try
        {
            var stream = member.Method == 0
                ? RarMemberStream.Stored(member, new RarPackedStream(_volumes, member.Segments), ct)
                : OpenDecoded(index, ct);
            return new BorrowedStream(stream);
        }
        catch (Exception ex) when (ex is not (IOException or OperationCanceledException or InvalidDataException))
        {
            throw new InvalidDataException("The archive cannot be read: " + ex.Message, ex);
        }
    }

    private RarMemberStream OpenDecoded(int index, CancellationToken ct)
    {
        var member = _archive.Members[index];
        if (!member.SolidContinuation)
        {
            // Independent of every other stream: its own decoder, window and bit reader.
            NewRun(member.Version, member.Dictionary, member.UnpackedSize);
            return Start(index, solid: false, ct);
        }
        // A solid member needs every compressed member before it in its run decoded, in order.
        int runStart = index;
        while (runStart > 0 && (_archive.Members[runStart].SolidContinuation || !IsCompressedFile(_archive.Members[runStart])))
            runStart--;
        bool continues = _decoder is not null && _decoded is { Broken: false } && _decodedIndex >= runStart && _decodedIndex < index;
        if (continues)
        {
            _decoded!.Drain();
        }
        else
        {
            long dictionary = 0;
            for (int i = runStart; i <= index; i++) dictionary = Math.Max(dictionary, _archive.Members[i].Dictionary);
            NewRun(member.Version, dictionary, member.UnpackedSize);
        }
        for (int i = continues ? _decodedIndex + 1 : runStart; i < index; i++)
        {
            var earlier = _archive.Members[i];
            if (!IsCompressedFile(earlier)) continue;
            if (earlier.Encrypted || !earlier.Complete) throw new InvalidDataException("An earlier member of this solid archive cannot be decoded, so this one cannot either.");
            if (DecoderFamily(earlier.Version) != DecoderFamily(member.Version)) throw RarData.Unsupported("a solid run that changes compression version");
            Start(i, solid: i != runStart || earlier.SolidContinuation, ct).Drain();
        }
        return Start(index, solid: true, ct);
    }

    private static bool IsCompressedFile(RarMember member) => member.Kind == MemberKind.File && member.Method != 0;

    private void NewRun(int version, long dictionary, long expectedBytes)
    {
        _decoder = CreateDecoder(version);
        _window = new RarWindow(dictionary, expectedBytes);
        _bits = new RarBitReader();
        _decoded = null;
        _decodedIndex = -1;
    }

    private RarMemberStream Start(int index, bool solid, CancellationToken ct)
    {
        var member = _archive.Members[index];
        _bits.Reset(new RarPackedStream(_volumes, member.Segments));
        _decoder!.StartMember(_bits, solid);
        _decoded = RarMemberStream.Decoded(member, _decoder, _window!, ct);
        _decodedIndex = index;
        return _decoded;
    }

    private static int DecoderFamily(int version) => version switch
    {
        50 or 70 => 5,
        29 or 36 => 3,
        20 or 26 => 2,
        15 => 1,
        _ => 0,
    };

    private static RarDecoder CreateDecoder(int version) => version switch
    {
        50 => new Rar5Decoder(rar7: false),
        70 => new Rar5Decoder(rar7: true),
        _ => throw RarData.Unsupported($"compression version {version}"),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _decoder = null;
        _window = null;
        _decoded = null;
        foreach (var volume in _volumes) volume.Dispose();
        _volumes.Clear();
    }
}

internal static class RarArchiveWarnings
{
    public const string MissingVolumes = "Some volumes of this archive are missing; members that continue in them cannot be extracted.";
}
