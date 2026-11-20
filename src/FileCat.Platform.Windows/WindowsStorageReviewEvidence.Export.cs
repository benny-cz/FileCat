using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageReviewEvidence
{
    private static StorageEvidenceExport CreateBoundExport(StorageEvidenceEnvelope envelope, string forbiddenDirectory,
        string owner, string destination, CancellationToken ct, bool qualifyNativeArm64)
    {
        ct.ThrowIfCancellationRequested();
        if (OrdinaryOwner(envelope.Claim, qualifyNativeArm64) != owner)
            throw new UnauthorizedAccessException("The export's invoking identity changed.");
        if (!StoragePaths.IsOrdinaryLocalPath(destination)) throw new IOException("Choose an ordinary local output file without stream syntax.");
        string full = Path.GetFullPath(destination), leaf = Path.GetFileName(full); Leaf(leaf);
        string parent = Path.GetDirectoryName(full) ?? throw new IOException("Choose an output folder.");
        NativeHandle? directory = null, file = null;
        try
        {
            directory = WalkExport(parent, forbiddenDirectory, envelope.Claim, ct, out var volume);
            using (envelope.Claim())
            {
                int existing = NtFile.TryOpen(directory.Handle, leaf, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll,
                    NtFile.Open, NtFile.OpenReparsePoint | NoRecall, out var probe, caseSensitive: ExportCaseSensitive(directory.Handle, volume.FileSystem));
                using (probe)
                {
                    if (existing >= 0) throw new IOException("An item with this name already exists. Choose a new output filename.");
                    if (existing != NtFile.NameNotFound) throw NtFile.Error(existing, "Inspect storage export destination");
                }
            }
            string temporary = ".filecat-storage-export-" + Guid.NewGuid().ToString("N") + ".tmp";
            IDisposable? charge = envelope.Claim(); SafeFileHandle? handle = null;
            try
            {
                int status = NtFile.TryOpen(directory.Handle, temporary, NtFile.ReadData | NtFile.WriteData | NtFile.ReadAttributes |
                    NtFile.Delete | ReadControl | NtFile.Synchronize, 0, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | NoRecall,
                    out handle, (uint)FileAttributes.Normal, (volume.Flags & 8) != 0 ? Security(owner, false) : [], caseSensitive: true);
                if (status < 0) throw NtFile.Error(status, "Create new storage evidence export");
                file = new(handle, charge); handle = null; charge = null;
            }
            finally { handle?.Dispose(); charge?.Dispose(); }
            var output = new Export(directory, file, full, temporary, leaf, volume.Flags);
            directory = file = null; return output;
        }
        finally
        {
            try { if (file is not null) { try { NtFile.DeleteOpen(file.Handle); } finally { file.Dispose(); } } }
            finally { directory?.Dispose(); }
        }
    }

    // With the source already held, two rolling directory handles use the other two envelope slots. Once the final
    // directory is retained, the owned output takes the third slot. No pathname is reopened for publication/cleanup.
    private static NativeHandle WalkExport(string directory, string forbidden, Func<IDisposable> claim,
        CancellationToken ct, out WindowsArchiveVolume volume)
    {
        string raw = directory.StartsWith(@"\\?\", StringComparison.Ordinal) ? directory[4..] : directory;
        int rootLength = raw.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase) ? 45 : 3;
        string[] names = raw[rootLength..].TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length > 63) throw new IOException("The output path exceeds the 64-level directory bound.");
        NativeHandle? current = OpenExportDirectory(null, @"\??\" + raw[..rootLength], claim, names.Length == 0, true, ct);
        try
        {
            volume = WindowsArchiveNative.Volume(current.Handle, requirePrivateNtfs: false);
            string binding = WindowsStorageTransforms.ActualVolumeBinding(current.Handle, rootOnly: true, ct);
            Check(current.Handle);
            for (int i = 0; i < names.Length; i++)
            {
                bool sensitive = ExportCaseSensitive(current.Handle, volume.FileSystem);
                var next = OpenExportDirectory(current.Handle, names[i], claim, i == names.Length - 1, sensitive, ct);
                try
                {
                    Check(current.Handle);
                    if (ExportCaseSensitive(current.Handle, volume.FileSystem) != sensitive) throw new IOException("An output ancestor's name rules changed.");
                    Check(next.Handle);
                }
                catch { next.Dispose(); throw; }
                current.Dispose(); current = next;
            }
            var result = current; current = null; return result;

            void Check(SafeFileHandle held)
            {
                RequireExportDirectory(held, ct);
                if (WindowsStorageTransforms.ActualVolumeBinding(held, false, ct) != binding) throw new IOException("The output path crossed its held volume.");
                if (forbidden.StartsWith(binding + ":", StringComparison.OrdinalIgnoreCase) && Identity(held, binding) == forbidden)
                    throw new IOException("Choose an export location outside FileCat's private evidence directory.");
            }
        }
        finally { current?.Dispose(); }
    }

    private static NativeHandle OpenExportDirectory(SafeFileHandle? parent, string name, Func<IDisposable> claim,
        bool writable, bool sensitive, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); IDisposable? charge = claim(); SafeFileHandle? file = null;
        try
        {
            int status = NtFile.TryOpen(parent, name, NtFile.ReadAttributes | NtFile.Synchronize | (writable ? NtFile.WriteData : 0),
                NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint,
                out file, caseSensitive: sensitive);
            if (status < 0) throw NtFile.Error(status, "Bind storage export folder");
            RequireExportDirectory(file, ct);
            var result = new NativeHandle(file, charge); file = null; charge = null; return result;
        }
        finally { file?.Dispose(); charge?.Dispose(); }
    }

    private static void RequireExportDirectory(SafeFileHandle file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); WindowsArchiveNative.SourceDirectory(file);
        if ((NtFile.GetBasic(file).FileAttributes & (0x1000u | 0x580000u)) != 0)
            throw new IOException("The export folder is offline or managed by a recall provider.");
        ct.ThrowIfCancellationRequested();
    }

    private static bool ExportCaseSensitive(SafeFileHandle file, string fileSystem)
    {
        uint mode = 0;
        if (ExportGetInfo(file, 23 /* FileCaseSensitiveInfo */, &mode, 4)) return (mode & 1) != 0;
        int error = Marshal.GetLastPInvokeError();
        if (fileSystem is "FAT" or "FAT32" or "exFAT" && error is 1 or 50 or 87) return false;
        throw new Win32Exception(error, "The export folder's case-sensitivity rules could not be read.");
    }

    private sealed class Export : StorageEvidenceExport
    {
        private readonly NativeHandle _directory, _file;
        private readonly FileStream _stream;
        private readonly string _destination, _temporary, _leaf;
        private readonly uint _volumeFlags;
        private bool _published, _disposed;
        internal override Stream Stream => !_disposed ? _stream : throw new ObjectDisposedException(nameof(Export));
        internal Export(NativeHandle directory, NativeHandle file, string destination, string temporary, string leaf, uint volumeFlags)
        {
            _directory = directory; _file = file; _destination = destination; _temporary = temporary; _leaf = leaf; _volumeFlags = volumeFlags;
            _stream = new(file.Handle, FileAccess.ReadWrite, 64 * 1024);
        }
        internal override StorageExportResult Publish(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_published) throw new IOException("This storage evidence export was already published.");
            ct.ThrowIfCancellationRequested();
            _stream.Flush(true); ct.ThrowIfCancellationRequested();
            RequireExportDirectory(_directory.Handle, ct);
            WindowsArchiveNative.Regular(_file.Handle, long.MaxValue, artifact: true, mayRename: true);
            if ((_volumeFlags & 0x40000 /* FILE_NAMED_STREAMS */) != 0)
                WindowsArchiveNative.RequireOnlyDataStream(_file.Handle, _stream.Length);
            ct.ThrowIfCancellationRequested();
            NtFile.Rename(_file.Handle, _directory.Handle, _leaf, replace: false);
            _published = true; // Never remove a published file, even if its directory flush fails or Stop arrives now.
            try { WindowsArchiveNative.Flush(_directory.Handle); return new(_destination, null); }
            catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
            { return new(_destination, "The export was published, but its folder flush failed: " + error.Message); }
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try
            {
                if (!_published)
                {
                    try { NtFile.DeleteOpen(_file.Handle); }
                    catch (Exception error) when (error is IOException or Win32Exception or UnauthorizedAccessException)
                    { throw new IOException("The unfinished export was kept as " + Path.Join(Path.GetDirectoryName(_destination)!, _temporary) + ": " + error.Message, error); }
                }
            }
            finally { try { _stream.Dispose(); } finally { try { _file.Dispose(); } finally { _directory.Dispose(); } } }
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ExportGetInfo(SafeFileHandle file, int informationClass, void* output, uint length);
}
