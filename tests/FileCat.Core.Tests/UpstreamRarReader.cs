extern alias UpstreamSharpCompress;

using System.Runtime.ExceptionServices;
using FileCat.Archives;
using FileCat.Archives.Rar;
using UpstreamSharpCompress::SharpCompress.Archives;
using UpstreamRarArchive = UpstreamSharpCompress::SharpCompress.Archives.Rar.RarArchive;
using UpstreamSharpCompress::SharpCompress.Readers;

namespace FileCat.Core.Tests;

/// <summary>
/// The differential oracle of FileCat's RAR reader: upstream SharpCompress's RAR reader (test-only; it is ported from
/// UnRAR and FileCat does not ship it), opened over FileCat's own volume discovery and behaving exactly as
/// <c>SharpArchiveReader.Open(path, rar: true)</c> did before RAR left SharpArchiveReader: the same listing and
/// warnings, direct member streams for non-solid archives and one forward cursor for solid ones, and the same errors.
/// </summary>
internal sealed class UpstreamRarReader : IMemberReader
{
    private readonly IArchive _archive;
    private readonly List<IArchiveEntry> _entries;
    private readonly List<FileStream> _volumes;
    private readonly bool _sequential;
    private readonly bool _numberedVolumeGap;
    // The reader visits entries in its own order, so members are matched by name and occurrence, never by position.
    private readonly int[] _ordinals;
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
    private IReader? _reader;
    private bool _disposed;

    private UpstreamRarReader(IArchive archive, List<FileStream> volumes, bool numberedVolumeGap)
    {
        _archive = archive;
        _volumes = volumes;
        _numberedVolumeGap = numberedVolumeGap;
        _sequential = archive.IsSolid;
        Format = volumes.Count > 1 ? $"RAR ({volumes.Count} volumes)" : "RAR";
        _entries = archive.Entries.ToList();
        _ordinals = new int[_entries.Count];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < _entries.Count; i++)
        {
            var key = _entries[i].Key ?? string.Empty;
            _ordinals[i] = counts.TryGetValue(key, out var n) ? n : 0;
            counts[key] = _ordinals[i] + 1;
        }
    }

    public string Format { get; }

    public bool SharesCursor => _sequential;

    public static UpstreamRarReader Open(string path)
    {
        var volumes = new List<FileStream>();
        bool numberedVolumeGap;
        try
        {
            foreach (var volume in RarVolumes.Of(path, out numberedVolumeGap))
                volumes.Add(ArchiveFormats.OpenShared(volume));
        }
        catch
        {
            foreach (var volume in volumes) volume.Dispose();
            throw;
        }
        try
        {
            var options = new ReaderOptions { LeaveStreamOpen = true, LookForHeader = false };
            IArchive archive = volumes.Count > 1
                ? UpstreamRarArchive.OpenArchive(volumes.Cast<Stream>().ToList(), options)
                : UpstreamRarArchive.OpenArchive(volumes[0], options);
            return new UpstreamRarReader(archive, volumes, numberedVolumeGap);
        }
        catch (Exception ex)
        {
            foreach (var v in volumes) v.Dispose();
            if (ex is IOException or UnauthorizedAccessException) throw;
            throw new InvalidDataException(Describe(ex), ex);
        }
    }

    private static string Describe(Exception ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex is System.Security.Cryptography.CryptographicException
            ? "The archive's file list is encrypted; FileCat does not ask for archive passwords."
            : "The archive cannot be read: " + ex.Message;

    public IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_numberedVolumeGap || !_archive.IsComplete)
            warn("Some volumes of this archive are missing; members that continue in them cannot be extracted.");
        for (int i = 0; i < _entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = _entries[i];
            var kind = e.IsDirectory ? MemberKind.Directory : e.LinkTarget is { Length: > 0 } ? MemberKind.SymbolicLink : MemberKind.File;
            yield return new MemberInfo(i, e.Key ?? string.Empty, kind, e.IsDirectory ? 0 : e.Size, e.CompressedSize, ArchiveFormats.Utc(e.LastModifiedTime),
                e.IsEncrypted, e.LinkTarget);
        }
    }

    public Stream Open(int index, CancellationToken ct)
    {
        try
        {
            if (!_sequential) return _entries[index].OpenEntryStream();
            string key = _entries[index].Key ?? string.Empty;
            int ordinal = _ordinals[index];
            // Already passed (or current, its stream consumed): start over; otherwise continue forward.
            if (_reader is null || _seen.GetValueOrDefault(key) > ordinal)
            {
                var previous = _reader;
                _reader = null;
                previous?.Dispose();
                _reader = _archive.ExtractAllEntries();
                _seen.Clear();
            }
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!_reader.MoveToNextEntry()) throw new FileNotFoundException("The member is no longer in the archive.");
                var current = _reader.Entry.Key ?? string.Empty;
                int seen = _seen[current] = _seen.GetValueOrDefault(current) + 1;
                if (current == key && seen - 1 == ordinal) return new BorrowedStream(_reader.OpenEntryStream());
            }
        }
        catch (Exception ex) when (ex is not (IOException or OperationCanceledException or InvalidDataException))
        {
            throw new InvalidDataException(Describe(ex), ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var reader = _reader;
        _reader = null;
        Exception? failure = null;
        void Close(IDisposable? resource)
        {
            try { resource?.Dispose(); }
            catch (Exception ex) { failure ??= ex; }
        }
        Close(reader);
        Close(_archive);
        foreach (var volume in _volumes) Close(volume);
        _volumes.Clear();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
