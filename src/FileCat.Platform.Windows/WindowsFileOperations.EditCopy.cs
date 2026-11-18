using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public partial class WindowsFileOperations : IEditCopyPublisher
{
    private const string EditCopyMarkLost = "Its download mark was not kept: the destination cannot store it, so the system may not warn when the copy is opened.";

    IDisposable? IEditCopyPublisher.HoldClearEditInput(SafeFileHandle source, string refusal) => HoldClearEditInput(source, refusal);

    internal static WindowsFileTransaction? HoldClearEditInput(SafeFileHandle source, string refusal)
    {
        ArchiveOuterEfs.RequireClear(source, refusal);
        try { return WindowsFileTransaction.PinSource(source, refusal); }
        catch (Win32Exception error)
        {
            ArchiveOuterEfs.RequireClear(source, refusal);
            throw new IOException("FileCat could not hold the edit input's EFS state. " + error.Message, error);
        }
    }

    ISealedEditSnapshot? IEditCopyPublisher.SealSnapshot(EditDirectoryLease directory, string leaf, FileStream writer, CancellationToken ct)
    {
        const string refusal = ArchiveOuterEfs.EditTemporaryOutputUnavailable;
        ArchiveOuterEfs.RequireClear(writer.SafeFileHandle, refusal);
        if (!WindowsFileTransaction.CanEncrypt(writer.SafeFileHandle)) return null;

        string identity = directory.Identify(writer.SafeFileHandle);
        // Keep the actual file alive across the writer close. TxF cannot open an ordinary file with a live writer.
        using var anchor = directory.OpenSharedRecord(leaf, ct);
        if (directory.Identify(anchor.SafeFileHandle) != identity) throw ChangedSnapshot();
        writer.Dispose();
        ct.ThrowIfCancellationRequested();

        WindowsFileTransaction? transaction = null;
        FileStream? payload = null;
        try
        {
            transaction = new WindowsFileTransaction("FileCat retained edit snapshot");
            SafeFileHandle handle = transaction.OpenHeldChild(directory.Handle, leaf, GenericRead | Delete | Synchronize);
            try { payload = new FileStream(handle, FileAccess.Read); }
            catch { handle.Dispose(); throw; }
            ArchiveOuterEfs.RequireClear(payload.SafeFileHandle, refusal);
            if (directory.Identify(payload.SafeFileHandle) != identity) throw ChangedSnapshot();
            return new SealedWindowsEditSnapshot(directory, leaf, payload, transaction);
        }
        catch (Exception error)
        {
            try { payload?.Dispose(); } finally { transaction?.Dispose(); }
            ArchiveOuterEfs.RequireClear(anchor.SafeFileHandle, refusal);
            if (error is Win32Exception) throw new IOException("FileCat could not hold the snapshot's EFS state. " + error.Message, error);
            throw;
        }

        static IOException ChangedSnapshot() => new("The private snapshot changed while FileCat sealed it. Its files are kept.");
    }

    private sealed class SealedWindowsEditSnapshot(EditDirectoryLease directory, string leaf, FileStream payload,
        WindowsFileTransaction transaction) : ISealedEditSnapshot
    {
        public FileStream Payload => payload;

        public void Retire(ArchiveFileObservation expected, CancellationToken ct)
        {
            directory.ReviewRemoval(payload, leaf, expected, ct);
            ct.ThrowIfCancellationRequested();
            try
            {
                WindowsFileTransaction.RemoveOnClose(payload.SafeFileHandle);
                payload.Dispose();
                transaction.Commit("Windows could not confirm removal of the private edit snapshot.");
            }
            catch (Win32Exception error)
            {
                throw new IOException("The private edit snapshot could not be removed: " + directory.Path, error);
            }
        }

        public void Dispose()
        {
            try { payload.Dispose(); } finally { transaction.Dispose(); }
        }
    }

    string? IEditCopyPublisher.PublishEditCopy(SafeFileHandle capture, string sha256, long length, string destination, Action checkpoint) =>
        PublishEditCopyCore(capture, sha256, length, destination, checkpoint, beforeCommit: null);

    internal string? PublishEditCopyCore(SafeFileHandle capture, string sha256, long length, string destination,
        Action checkpoint, Action? beforeCommit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        string target = Path.GetFullPath(destination);
        string folder = Path.GetDirectoryName(target) ?? throw new IOException("Choose a destination with a containing folder.");
        string name = Path.GetFileName(target);
        EditSaveCopyNames.Validate(name);
        bool commitStarted = false;
        try
        {
            checkpoint();
            CheckEditCapture(capture);
            WindowsArchiveNative.Regular(capture, length, artifact: false, mayRename: false);
            using var heldSource = HoldClearEditInput(capture, ArchiveOuterEfs.EditSnapshotUnavailable);
            CheckEditCapture(capture);

            using var local = LocalIconFileLease.TryOpenPlainDirectory(folder);
            var advertised = local is null ? GetVolumeInfo(folder) : null;
            using var network = local is null && advertised?.IsRemote == true ? OpenEditCopyNetworkFolder(folder) : null;
            var directory = local?.LastHandle ?? network ?? throw new IOException("The saved copy's destination folder cannot be held safely.");
            RequireOrdinaryEditCopyFolder(directory);

            var localVolume = local is null ? null : WindowsArchiveNative.Volume(directory, requirePrivateNtfs: false);
            using var tx = localVolume is not null && SupportsLocalNtfsTransactions(localVolume)
                ? new WindowsFileTransaction("FileCat save edit copy") : null;
            uint outputFlags = localVolume?.Flags ?? WindowsFileTransaction.VolumeFlags(directory);
            bool canEncrypt = (outputFlags & 0x00020000 /* FILE_SUPPORTS_ENCRYPTION */) != 0;
            using var original = OpenReviewedEditDestination(directory, name, tx, canReplaceWithoutTransaction: localVolume is not null && !canEncrypt);

            if (tx is null && canEncrypt)
                throw new IOException("FileCat cannot hold the saved copy's EFS state on this volume. Choose another destination folder.");

            const uint namedStreams = 0x00040000;
            bool inputHasStreams = (WindowsFileTransaction.VolumeFlags(capture) & namedStreams) != 0;
            bool outputHasStreams = (outputFlags & namedStreams) != 0;
            var mark = inputHasStreams ? ReadHeldFileOrigin(capture, CheckEditCapture)
                : ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
            bool marked = mark.State == ArchiveOriginState.Present;
            bool markKept = !marked || outputHasStreams;
            long modified = GetBasic(capture).LastWriteTime;
            string stage = $".filecat-edit-copy-{Guid.NewGuid():N}.tmp";
            string? transactedPath = tx is null ? null : Path.Combine(WindowsFileTransaction.HeldPath(directory), stage);
            FileIdentity savedIdentity;
            const uint writeAccess = GenericRead | GenericWrite | Delete | Synchronize;
            using (var output = tx is null
                ? OpenOrThrow(directory, stage, writeAccess, ShareRead, Create, NonDirectoryFile | OpenReparsePoint,
                    "Create the temporary saved edit", (uint)FileAttributes.Normal)
                : tx.CreateFile(transactedPath!, writeAccess, ShareRead, "Create the private saved edit"))
            {
                bool renamed = false;
                try
                {
                    CheckEditCopyOutput(output);
                    if (transactedPath is not null && !string.Equals(WindowsFileTransaction.HeldPath(output), transactedPath, StringComparison.Ordinal))
                        throw new IOException("The temporary saved edit was opened outside its held destination.");
                    RequireOrdinaryEditCopyFolder(directory);
                    savedIdentity = EditCopyIdentity(output, tx);
                    CopyCapturedEdit(capture, output, sha256, length, checkpoint);
                    if (marked && outputHasStreams) WriteEditCopyMark(output, tx, transactedPath, mark);
                    if (inputHasStreams && ReadHeldFileOrigin(capture, CheckEditCapture).Digest != mark.Digest)
                        throw new IOException("The retained edit's download mark changed during Save Copy.");
                    SetBasic(output, new BasicInfo { LastWriteTime = modified });
                    CheckEditCopyOutput(output);
                    WindowsArchiveNative.Flush(output);
                    if (!DigestMatches(output, sha256, length, CancellationToken.None, () => { checkpoint(); CheckEditCopyOutput(output); }))
                        throw new IOException("The temporary saved edit does not match the captured bytes.");
                    checkpoint();
                    RequireOrdinaryEditCopyFolder(directory);
                    CheckEditCopyOutput(output);
                    bool replace = original is not null;
                    if (replace)
                    {
                        RequireReviewedEditDestination(directory, name, original!, tx);
                        if (tx is not null)
                        {
                            NtFile.Rename(original!, directory, $".filecat-edit-copy-{Guid.NewGuid():N}.old", replace: false);
                            WindowsFileTransaction.RemoveOnClose(original!);
                        }
                        original!.Dispose();
                    }
                    NtFile.Rename(output, tx is null ? null : directory, name, replace: tx is null && replace);
                    renamed = true;
                    commitStarted = tx is null;
                    // FAT can change the ID when the directory entry changes. Query the renamed open item.
                    if (tx is null) savedIdentity = EditCopyIdentity(output, tx);
                }
                finally
                {
                    if (!renamed && tx is null)
                    {
                        try { CheckEditCopyOutput(output); DeleteOpen(output); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
                        { Core.Diagnostics.AppLog.Warn($"FileCat kept the temporary saved edit at {Path.Combine(folder, stage)}: {error.Message}"); }
                    }
                }
            }
            beforeCommit?.Invoke();
            if (tx is not null)
            {
                checkpoint();
                commitStarted = true;
                tx.Commit("Windows could not confirm publication of the saved edit.");
            }
            else WriteThrough(folder);
            using var saved = OpenOrThrow(directory, name, GenericRead | Synchronize, ShareRead, Open,
                NonDirectoryFile | OpenReparsePoint, "Verify the saved edit");
            CheckEditCopyOutput(saved);
            if (EditCopyIdentity(saved, tx) != savedIdentity || RandomAccess.GetLength(saved) != length)
                throw new IOException("The saved edit's identity or length changed during publication.");
            if (outputHasStreams && ReadHeldFileOrigin(saved, CheckEditCopyOutput).Digest != mark.Digest)
                throw new IOException("The saved edit's download mark changed during publication.");
            return markKept ? null : EditCopyMarkLost;
        }
        catch (Exception error) when (commitStarted && error is IOException or UnauthorizedAccessException or Win32Exception)
        { throw new IOException("FileCat could not confirm the final saved copy. Check the destination before retrying. " + error.Message, error); }
        catch (Win32Exception error)
        { throw new IOException(error.Message, error); }
    }

    private static SafeFileHandle OpenEditCopyNetworkFolder(string path)
    {
        var file = CreateFileForIdentity(Long(path), Traverse | ReadAttributes | Synchronize, ShareRead | ShareWrite, 0,
            3 /* OPEN_EXISTING */, 0x02000000 /* BACKUP_SEMANTICS */ | OpenReparsePoint, 0);
        if (!file.IsInvalid) return file;
        var error = new Win32Exception(Marshal.GetLastPInvokeError());
        file.Dispose();
        throw error;
    }

    private static void RequireOrdinaryEditCopyFolder(SafeFileHandle directory)
    {
        var attributes = GetBasic(directory).FileAttributes;
        ArchiveOuterEfs.RequireClear((FileAttributes)attributes, ArchiveOuterEfs.EditTemporaryOutputUnavailable);
        if (!IsDirectory(attributes) || IsReparsePoint(attributes))
            throw new IOException("The saved copy's destination folder changed or is a reparse point.");
    }

    private static SafeFileHandle? OpenReviewedEditDestination(SafeFileHandle directory, string name,
        WindowsFileTransaction? tx, bool canReplaceWithoutTransaction)
    {
        int status = TryOpen(directory, name, ReadAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, out var reviewed);
        using (reviewed)
        {
            if (status == NameNotFound) return null;
            if (status < 0) throw Error(status, "Review the saved copy's destination");
            ArchiveOuterEfs.RequireClear(reviewed, EfsReplacementUnavailable);
            WindowsArchiveNative.Regular(reviewed, long.MaxValue, artifact: false, mayRename: true);
            if (tx is null && !canReplaceWithoutTransaction)
                throw new IOException("FileCat cannot safely replace an existing saved-copy destination on this volume. Choose a different destination.");

            var actual = tx is null
                ? OpenOrThrow(directory, name, ReadAttributes | Synchronize, ShareAll, Open, NonDirectoryFile | OpenReparsePoint,
                    "Hold the existing saved-copy destination")
                : tx.OpenHeldChild(directory, name, GenericRead | GenericWrite | Delete | Synchronize);
            try
            {
                ArchiveOuterEfs.RequireClear(actual, EfsReplacementUnavailable);
                if (EditCopyIdentity(actual, tx) != EditCopyIdentity(reviewed, tx))
                    throw new IOException("The saved copy's destination changed during review.");
                return actual;
            }
            catch { actual.Dispose(); throw; }
        }
    }

    private static void RequireReviewedEditDestination(SafeFileHandle directory, string name, SafeFileHandle reviewed,
        WindowsFileTransaction? tx)
    {
        using var named = OpenOrThrow(directory, name, ReadAttributes | Synchronize, ShareAll, Open,
            NonDirectoryFile | OpenReparsePoint, "Check the reviewed saved-copy destination");
        ArchiveOuterEfs.RequireClear(named, EfsReplacementUnavailable);
        if (EditCopyIdentity(named, tx) != EditCopyIdentity(reviewed, tx))
            throw new IOException("The saved copy's destination changed during preparation.");
    }

    private static FileIdentity EditCopyIdentity(SafeFileHandle file, WindowsFileTransaction? tx)
    {
        try { return ProtectedHexFile.Identity(file); }
        catch (Win32Exception error) when (tx is null && error.NativeErrorCode is 1 or 50 or 87)
        {
            // Some FAT and SMB providers expose only the legacy file index. Compare current live objects only.
            if (!GetEditCopyFileInformation(file, out var information))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if ((information.IndexHigh | information.IndexLow) == 0)
                throw new IOException("The destination does not provide a usable saved-copy file identity.");
            return new(information.VolumeSerial, $"{information.IndexHigh:X8}{information.IndexLow:X8}");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EditCopyFileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetEditCopyFileInformation(SafeFileHandle file, out EditCopyFileInformation information);

    private static void CheckEditCapture(SafeFileHandle file) => ArchiveOuterEfs.RequireClear(file, ArchiveOuterEfs.EditSnapshotUnavailable);
    private static void CheckEditCopyOutput(SafeFileHandle file) => ArchiveOuterEfs.RequireClear(file, ArchiveOuterEfs.EditTemporaryOutputUnavailable);

    private static void CopyCapturedEdit(SafeFileHandle source, SafeFileHandle output, string expectedSha256, long length, Action checkpoint)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[(int)Math.Min(1024 * 1024, length)];
        for (long offset = 0; offset < length;)
        {
            checkpoint(); CheckEditCapture(source);
            int count = RandomAccess.Read(source, buffer.AsSpan(0, (int)Math.Min(length - offset, buffer.Length)), offset);
            CheckEditCapture(source);
            if (count == 0) throw new IOException("The retained edit ended before its captured length.");
            CheckEditCopyOutput(output);
            RandomAccess.Write(output, buffer.AsSpan(0, count), offset);
            hash.AppendData(buffer.AsSpan(0, count));
            offset += count;
        }
        CheckEditCapture(source);
        if (RandomAccess.GetLength(source) != length || Convert.ToHexString(hash.GetHashAndReset()) != expectedSha256)
            throw new IOException("The retained edit no longer matches the captured bytes.");
    }

    private static void WriteEditCopyMark(SafeFileHandle output, WindowsFileTransaction? tx, string? path, ArchiveOriginSnapshot mark)
    {
        CheckEditCopyOutput(output);
        const uint access = GenericRead | GenericWrite | Synchronize, share = ShareRead | ShareDelete;
        using var zone = tx is null
            ? OpenOrThrow(output, ZoneStream, access, share, Create, NonDirectoryFile | OpenReparsePoint, "Save the edit's download mark")
            : tx.CreateFile(path! + ZoneStream, access, share, "Save the edit's download mark");
        if (ProtectedHexFile.Identity(zone) != ProtectedHexFile.Identity(output))
            throw new IOException("The saved edit's download mark belongs to a different file.");
        CheckEditCopyOutput(zone);
        RandomAccess.Write(zone, mark.Bytes, 0);
        WindowsArchiveNative.Flush(zone);
        if (ReadHeldFileOrigin(output, zone, CheckEditCopyOutput).Digest != mark.Digest)
            throw new IOException("The saved edit's download mark could not be verified.");
    }
}
