using System.ComponentModel;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public static partial class HexPatchExport
{
    internal static ExportReview Prepare(ProtectedHexFile source, HexPatchOverlay overlay, string destination)
    {
        string full = Path.GetFullPath(destination);
        if (File.Exists(full) || Directory.Exists(full)) throw DestinationExists();
        var origin = source.CaptureHexOrigin();
        overlay.IncludeContributor(origin);
        var snapshot = overlay.FreezeForPersistence();
        try { return new(source, overlay, full, origin, snapshot); }
        catch { overlay.FinishSave(false); throw; }
    }

    private static IOException DestinationExists() => new("Patch export requires a new file name. An item with this name already exists.");

    /// <summary>Owns one frozen overlay and, for EFS-derived content, the exact destination folder through publication.</summary>
    internal sealed class ExportReview : IDisposable
    {
        private readonly ProtectedHexFile _source;
        private readonly HexPatchOverlay _overlay;
        private readonly HexSaveSnapshot _snapshot;
        private readonly HexContentContributor _origin;
        private readonly LocalIconFileLease? _folder;
        private readonly string _destinationLeaf, _temporaryLeaf;
        private bool _approved, _attempted, _disposed;

        internal string Destination { get; }
        internal string Temporary { get; }
        internal int RangeCount => _snapshot.Ranges.Count;
        internal bool NeedsEfsConsent { get; }

        internal ExportReview(ProtectedHexFile source, HexPatchOverlay overlay, string destination,
            HexContentContributor origin, HexSaveSnapshot snapshot)
        {
            _source = source;
            _overlay = overlay;
            _origin = origin;
            _snapshot = snapshot;
            Destination = destination;
            _destinationLeaf = Path.GetFileName(destination);
            string directory = Path.GetDirectoryName(destination) ?? throw new IOException("Choose a patch destination inside a folder.");
            _temporaryLeaf = ".filecat-patch-" + Guid.NewGuid().ToString("N") + ".tmp";
            Temporary = Path.Join(directory, _temporaryLeaf);
            if (RangeCount == 0) throw new IOException("There are no modified bytes to export.");
            if (snapshot.Contributors.IsDefaultOrEmpty || snapshot.Contributors.Any(contributor =>
                contributor.Protection == HexEfsOriginState.Unknown || string.IsNullOrEmpty(contributor.Identity)))
                throw new IOException("Patch export is unavailable because the protection of an input could not be established.");
            NeedsEfsConsent = snapshot.Contributors.Any(contributor => contributor.Protection == HexEfsOriginState.Efs);
            if (!NeedsEfsConsent) return;
            if (!StoragePaths.IsOrdinaryLocalPath(destination) || SafeNames.Validate(_destinationLeaf) is not null)
                throw new IOException("Choose an ordinary local destination for the EFS-derived patch.");
            _folder = LocalIconFileLease.TryOpenPlainDirectory(directory) ??
                throw new IOException("The EFS-derived patch destination could not be held without following links. Choose another local folder.");
        }

        internal void ApproveEfsExport()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!NeedsEfsConsent || _attempted) throw new InvalidOperationException("Open a new EFS patch export review.");
            _approved = true;
        }

        internal void Export(CancellationToken ct = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_attempted) throw new InvalidOperationException("Open a new patch export review.");
            _attempted = true;
            if (NeedsEfsConsent && !_approved) throw new IOException("Patch export requires approval to write its contents without EFS protection.");
            ct.ThrowIfCancellationRequested();
            _source.ValidateForSave(_snapshot.Ranges);
            RequireSourceProtection();
            if (_folder is not null) ExportBound(_folder.LastHandle, ct);
            else ExportOrdinary(ct);
        }

        private void ExportOrdinary(CancellationToken ct)
        {
            bool created = false;
            try
            {
                using (var stream = new FileStream(Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
                {
                    created = true;
                    Write(stream, ct);
                }
                RequireSourceProtection();
                ct.ThrowIfCancellationRequested();
                File.Move(Temporary, Destination, overwrite: false);
                created = false;
            }
            finally { if (created && File.Exists(Temporary)) File.Delete(Temporary); }
        }

        private void ExportBound(SafeFileHandle folder, CancellationToken ct)
        {
            WindowsArchiveNative.SourceDirectory(folder);
            int existing = NtFile.TryOpen(folder, _destinationLeaf, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll,
                NtFile.Open, NtFile.OpenReparsePoint, out var probe);
            using (probe)
            {
                if (existing >= 0) throw DestinationExists();
                if (existing != NtFile.NameNotFound) throw NtFile.Error(existing, "Inspect the reviewed patch destination");
            }
            ct.ThrowIfCancellationRequested();
            using var handle = NtFile.OpenOrThrow(folder, _temporaryLeaf, NtFile.WriteData | NtFile.ReadAttributes | NtFile.Delete | NtFile.Synchronize,
                0, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, "Create the reviewed temporary patch", (uint)FileAttributes.Normal);
            bool published = false;
            FileStream? stream = null;
            try
            {
                WindowsArchiveNative.Regular(handle, long.MaxValue, artifact: true, mayRename: true);
                stream = new(handle, FileAccess.Write, bufferSize: 1);
                Write(stream, ct);
                RequireSourceProtection();
                WindowsArchiveNative.SourceDirectory(folder);
                WindowsArchiveNative.Regular(handle, long.MaxValue, artifact: true, mayRename: true);
                ct.ThrowIfCancellationRequested();
                NtFile.Rename(handle, folder, _destinationLeaf, replace: false);
                published = true;
            }
            finally
            {
                try
                {
                    if (!published) NtFile.DeleteOpen(handle);
                }
                catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
                { throw new IOException($"The unfinished patch was kept at {Temporary}. {error.Message}", error); }
                finally { stream?.Dispose(); }
            }
        }

        private void Write(FileStream stream, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RequireSourceProtection();
            JsonSerializer.Serialize(stream, new
            {
                format = Format,
                sourcePath = _source.LocalPath,
                sourceIdentity = _source.FileIdentity.ToString(),
                sourceLength = _source.Length,
                ranges = _snapshot.Ranges.Select(r => new { offset = r.Offset, originalBase64 = Convert.ToBase64String(r.Original),
                    replacementBase64 = Convert.ToBase64String(r.Replacement) }).ToArray(),
            }, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(flushToDisk: true);
        }

        private void RequireSourceProtection()
        {
            if (_source.CaptureHexOrigin() != _origin)
                throw new IOException("The source's EFS protection or identity changed during patch export. Review the export again.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _folder?.Dispose(); }
            finally { _overlay.FinishSave(false); }
        }
    }
}
