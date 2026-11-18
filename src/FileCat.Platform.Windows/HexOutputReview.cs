using System.ComponentModel;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal enum HexOutputKind { SaveAs, Patch }

/// <summary>Owns one frozen overlay and, for EFS-derived content, the exact destination folder through publication.</summary>
internal sealed class HexOutputReview : IDisposable
{
    private readonly HexContentContributor _origin;
    private readonly LocalIconFileLease? _folder;
    private readonly string _destinationLeaf, _temporaryLeaf, _operation;
    private readonly HexOutputKind _kind;
    private bool _approved, _attempted, _disposed;

    internal ProtectedHexFile Source { get; }
    internal HexPatchOverlay Overlay { get; }
    internal IReadOnlyList<HexPatchRange> Ranges { get; }
    internal string Destination { get; }
    internal string Temporary { get; }
    internal bool NeedsEfsConsent { get; }

    internal static HexOutputReview Prepare(ProtectedHexFile source, HexPatchOverlay overlay, string destination, HexOutputKind kind)
    {
        string full = Path.GetFullPath(destination);
        string operation = kind == HexOutputKind.SaveAs ? "Save As" : "Patch export";
        if (File.Exists(full) || Directory.Exists(full)) throw DestinationExists(operation);
        var origin = source.CaptureHexOrigin();
        overlay.IncludeContributor(origin);
        var snapshot = overlay.FreezeForPersistence();
        try { return new(source, overlay, full, origin, snapshot, kind, operation); }
        catch { overlay.FinishSave(false); throw; }
    }

    private HexOutputReview(ProtectedHexFile source, HexPatchOverlay overlay, string destination, HexContentContributor origin,
        HexSaveSnapshot snapshot, HexOutputKind kind, string operation)
    {
        Source = source;
        Overlay = overlay;
        _origin = origin;
        _kind = kind;
        _operation = operation;
        Ranges = snapshot.Ranges;
        Destination = destination;
        _destinationLeaf = Path.GetFileName(destination);
        string directory = Path.GetDirectoryName(destination) ?? throw new IOException("Choose a destination inside a folder.");
        string prefix = kind == HexOutputKind.SaveAs ? ".filecat-hex-" : ".filecat-patch-";
        _temporaryLeaf = prefix + Guid.NewGuid().ToString("N") + ".tmp";
        Temporary = Path.Join(directory, _temporaryLeaf);
        if (kind == HexOutputKind.Patch && Ranges.Count == 0) throw new IOException("There are no modified bytes to export.");
        if (snapshot.Contributors.IsDefaultOrEmpty || snapshot.Contributors.Any(contributor =>
            contributor.Protection == HexEfsOriginState.Unknown || string.IsNullOrEmpty(contributor.Identity)))
            throw new IOException($"{operation} is unavailable because the protection of an input could not be established.");
        NeedsEfsConsent = snapshot.Contributors.Any(contributor => contributor.Protection == HexEfsOriginState.Efs);
        if (!NeedsEfsConsent) return;
        if (!StoragePaths.IsOrdinaryLocalPath(destination) || SafeNames.Validate(_destinationLeaf) is not null)
            throw new IOException("Choose an ordinary local destination for the EFS-derived output.");
        _folder = LocalIconFileLease.TryOpenPlainDirectory(directory) ??
            throw new IOException("The EFS-derived output folder could not be held without following links. Choose another local folder.");
    }

    internal void ApproveEfsExport()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NeedsEfsConsent || _attempted) throw new InvalidOperationException("Open a new EFS export review.");
        _approved = true;
    }

    internal void Begin(HexOutputKind kind, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_attempted || _kind != kind) throw new InvalidOperationException("Open a new review for this output action.");
        _attempted = true;
        if (NeedsEfsConsent && !_approved) throw new IOException($"{_operation} requires approval to write its contents without EFS protection.");
        ct.ThrowIfCancellationRequested();
        Source.ValidateForSave(Ranges);
        RequireSourceProtection();
    }

    internal TResult PublishBound<TResult>(Func<FileStream, TResult> write, CancellationToken ct)
    {
        if (_folder is null || !_attempted || !_approved) throw new InvalidOperationException("The EFS output is not approved.");
        SafeFileHandle folder = _folder.LastHandle;
        WindowsArchiveNative.SourceDirectory(folder);
        int existing = NtFile.TryOpen(folder, _destinationLeaf, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll,
            NtFile.Open, NtFile.OpenReparsePoint, out var probe);
        using (probe)
        {
            if (existing >= 0) throw DestinationExists(_operation);
            if (existing != NtFile.NameNotFound) throw NtFile.Error(existing, "Inspect the reviewed output name");
        }
        ct.ThrowIfCancellationRequested();
        using var handle = NtFile.OpenOrThrow(folder, _temporaryLeaf, NtFile.GenericRead | NtFile.GenericWrite | NtFile.Delete | NtFile.Synchronize,
            0, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, "Create the reviewed temporary output", (uint)FileAttributes.Normal);
        bool published = false;
        FileStream? stream = null;
        try
        {
            WindowsArchiveNative.Regular(handle, long.MaxValue, artifact: true, mayRename: true);
            stream = new(handle, FileAccess.ReadWrite, bufferSize: 1);
            TResult result = write(stream);
            stream.Flush(flushToDisk: true);
            RequireSourceProtection();
            WindowsArchiveNative.SourceDirectory(folder);
            WindowsArchiveNative.Regular(handle, long.MaxValue, artifact: true, mayRename: true);
            ct.ThrowIfCancellationRequested();
            NtFile.Rename(handle, folder, _destinationLeaf, replace: false);
            published = true;
            return result;
        }
        finally
        {
            try { if (!published) NtFile.DeleteOpen(handle); }
            catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
            { throw new IOException($"The unfinished output was kept at {Temporary}. {error.Message}", error); }
            finally { stream?.Dispose(); }
        }
    }

    internal void CopyDownloadMark(SafeFileHandle output, ReadOnlySpan<byte> bytes)
    {
        if (_folder is null || !_attempted || !_approved) throw new InvalidOperationException("The EFS output is not approved.");
        using var mark = NtFile.OpenOrThrow(_folder.LastHandle, _temporaryLeaf + ":Zone.Identifier", NtFile.WriteData | NtFile.ReadAttributes | NtFile.Synchronize,
            NtFile.ShareAll, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, "Create the download mark on the held temporary output");
        if (ProtectedHexFile.Identity(mark) != ProtectedHexFile.Identity(output))
            throw new IOException("The download mark does not belong to the reviewed temporary output.");
        RandomAccess.Write(mark, bytes, 0);
        RandomAccess.FlushToDisk(mark);
    }

    internal void RequireSourceProtection()
    {
        if (Source.CaptureHexOrigin() != _origin)
            throw new IOException($"The source's EFS protection or identity changed during {_operation}. Review the output again.");
    }

    private static IOException DestinationExists(string operation) => new($"{operation} requires a new file name. An item with this name already exists.");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _folder?.Dispose(); }
        finally { Overlay.FinishSave(false); }
    }
}
