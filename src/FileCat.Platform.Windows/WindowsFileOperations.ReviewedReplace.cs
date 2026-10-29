using System.ComponentModel;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public partial class WindowsFileOperations
{
    /// <summary>
    /// Fixed and removable drives (a USB stick, an SD card): their items can be held, checked and renamed by handle. A
    /// network location cannot bind replacement to the reviewed object, and by the owner's policy is not replaced.
    /// </summary>
    internal static bool IsReplaceVolume(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        try { return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram; }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return false; }
    }

    public override bool CanReplaceReviewed(string destination) => OperatingSystem.IsWindows() && IsReplaceVolume(destination);

    public override BoundTreeDeletion? DeleteReviewedTree(string path, IReadOnlyList<BoundDeletionEntry> reviewed,
        Action checkpoint, CancellationToken ct)
        => DeleteReviewedTreeCore(path, reviewed, checkpoint, ct, null);

    internal BoundTreeDeletion? DeleteReviewedTreeCore(string path, IReadOnlyList<BoundDeletionEntry> reviewed,
        Action checkpoint, CancellationToken ct, Action? beforeRemove)
    {
        if (!OperatingSystem.IsWindows() || !IsReplaceVolume(path)) return null;
        string root = Path.GetFullPath(path);
        if (reviewed.Count is < 1 or > 1000 ||
            reviewed.Any(e => !PathUtil.IsSameOrUnder(e.Path, root) || e.Identity is null || e.IsLink ||
                !e.IsDirectory && (e.Sha256?.Length != 64 || e.Length < 0)))
            return new BoundTreeDeletion(0, 1, ["The compared tree has no complete, link-free native review."]);
        var expected = reviewed.ToDictionary(e => Path.GetFullPath(e.Path), PathUtil.SafetyComparer);
        if (expected.Count != reviewed.Count || !expected.TryGetValue(root, out var top) || !top.IsDirectory)
            return new BoundTreeDeletion(0, 1, ["The compared tree membership is incomplete."]);
        using var parent = LocalIconFileLease.TryOpenDirectory(Path.GetDirectoryName(root)!, LeaseLinks.Refuse, removable: true);
        if (parent is null) return new BoundTreeDeletion(0, 1, ["The compared tree's parent cannot be held without following a link."]);
        try
        {
            bool Approve(SafeFileHandle handle, string volumePath)
            {
                checkpoint();
                ct.ThrowIfCancellationRequested();
                string display = Path.GetFullPath(ElevationPaths.ToDisplayPath(volumePath));
                if (!expected.TryGetValue(display, out var item)) return false;
                uint attributes = GetBasic(handle).FileAttributes;
                if (IsLink(handle) || IsDirectory(attributes) != item.IsDirectory) return false;
                var identity = ProtectedHexFile.Identity(handle);
                string actual = $"{identity.VolumeSerial:X16}:{identity.FileId}";
                if (!string.Equals(actual, item.Identity, StringComparison.OrdinalIgnoreCase)) return false;
                if (item.IsDirectory)
                {
                    var actualNames = List(handle).Select(e => e.Name).ToHashSet(PathUtil.SafetyComparer);
                    var expectedNames = expected.Values.Where(e => PathUtil.SafetyComparer.Equals(Path.GetDirectoryName(e.Path), display))
                        .Select(e => Path.GetFileName(e.Path)).ToHashSet(PathUtil.SafetyComparer);
                    return actualNames.SetEquals(expectedNames);
                }
                return RandomAccess.GetLength(handle) == item.Length &&
                    DigestMatches(handle, item.Sha256!, item.Length, ct);
            }

            var report = new SecureFileOps(checkpoint, ElevationPaths.ToDisplayPath)
                .DeleteTree(ElevationPaths.ToVolumePathWithoutFollowing(root), Approve, beforeRemove);
            return new BoundTreeDeletion(report.Done, report.Failed, report.Problems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new BoundTreeDeletion(0, 1, ["The compared tree could not be deleted through reviewed handles: " + ex.Message]);
        }
    }

    public override BoundDeletion? DeleteReviewedFile(string path, string? expectedIdentity, string expectedSha256,
        long expectedLength, CancellationToken ct)
        => DeleteReviewedFileCore(path, expectedIdentity, expectedSha256, expectedLength, ct, null);

    internal BoundDeletion? DeleteReviewedFileCore(string path, string? expectedIdentity, string expectedSha256,
        long expectedLength, CancellationToken ct, Action? beforeDelete)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (expectedIdentity is null || expectedLength < 0 || expectedSha256.Length != 64 || !IsReplaceVolume(path))
            return new BoundDeletion(false, true, "The compared file has no usable local identity and complete byte review.");
        string full = Path.GetFullPath(path);
        using var parent = LocalIconFileLease.TryOpenDirectory(Path.GetDirectoryName(full)!, LeaseLinks.Refuse, removable: true);
        if (parent is null) return new BoundDeletion(false, true, "The compared file's parent is linked or cannot be held.");
        try
        {
            using var file = OpenOrThrow(parent.LastHandle, Path.GetFileName(full), ReadData | ReadAttributes | Delete | Synchronize,
                ShareRead, Open, NonDirectoryFile | OpenReparsePoint, "Open compared file for deletion");
            if (IsLink(file))
                return new BoundDeletion(false, true, "The compared file is now a link.");
            var identity = ProtectedHexFile.Identity(file);
            string actual = $"{identity.VolumeSerial:X16}:{identity.FileId}";
            if (!string.Equals(actual, expectedIdentity, StringComparison.OrdinalIgnoreCase) ||
                RandomAccess.GetLength(file) != expectedLength || !DigestMatches(file, expectedSha256, expectedLength, ct))
                return new BoundDeletion(false, true, "The compared file's identity or complete bytes changed.");
            ct.ThrowIfCancellationRequested();
            beforeDelete?.Invoke();
            DeleteOpen(file);
            return new BoundDeletion(true, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new BoundDeletion(false, false, "The compared file could not be deleted through its handle: " + ex.Message);
        }
    }

    /// <summary>
    /// Move the reviewed object aside by its handle, then publish the incoming file without replacing any name.
    /// A different occupant at the destination can never be destroyed by the publication. The old target is retained
    /// under a named recovery path if an interrupted or failed publication cannot put it back.
    /// </summary>
    public override string? PrepareReviewedReplacement(string source, string destination)
    {
        if (!OperatingSystem.IsWindows() || !IsReplaceVolume(source) || !IsReplaceVolume(destination)) return null;
        try
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
            return Path.Combine(parent, $".filecat-replaced-{Guid.NewGuid():N}.bak");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public override BoundReplacement? ReplaceReviewed(string source, string destination, string? expectedIdentity,
        string expectedSha256, long expectedLength, CancellationToken ct, string? recoveryPath = null,
        DateTime? expectedModifiedUtc = null)
        => ReplaceReviewedCore(source, destination, expectedIdentity, expectedSha256, expectedLength, ct, null, recoveryPath, expectedModifiedUtc);

    internal BoundReplacement? ReplaceReviewedCore(string source, string destination, string? expectedIdentity,
        string expectedSha256, long expectedLength, CancellationToken ct, Action? afterRetire,
        string? recoveryPath = null, DateTime? expectedModifiedUtc = null)
    {
        if (!OperatingSystem.IsWindows()) return null;
        // Bound by its complete content, or (beyond the content budget, or kept by a cloud provider) by its identity,
        // length and last write time, all checked through the handle that keeps writers out.
        bool byContent = expectedSha256.Length == 64;
        if (expectedIdentity is null || expectedLength < 0 || (!byContent && expectedModifiedUtc is null) ||
            !IsReplaceVolume(source) || !IsReplaceVolume(destination))
            return new BoundReplacement(false, true, Reason: "This destination cannot be replaced with an object-bound local operation.");
        string target = Path.GetFullPath(destination);
        string parentPath = Path.GetDirectoryName(target)!;
        string name = Path.GetFileName(target);
        if (name.Length == 0) return new BoundReplacement(false, true, Reason: "The destination has no file name.");
        string? backupPath = recoveryPath ?? PrepareReviewedReplacement(source, destination);
        if (backupPath is null)
            return new BoundReplacement(false, true, Reason: "No safe retained-target recovery name is available.");
        string backupName = Path.GetFileName(backupPath);
        if (!PathUtil.SafetyComparer.Equals(Path.GetDirectoryName(Path.GetFullPath(backupPath)), parentPath) ||
            !backupName.StartsWith(".filecat-replaced-", StringComparison.Ordinal) ||
            !backupName.EndsWith(".bak", StringComparison.Ordinal))
            return new BoundReplacement(false, true, Reason: "The retained-target recovery name is invalid.");
        using var parent = LocalIconFileLease.TryOpenDirectory(parentPath, LeaseLinks.Refuse, removable: true);
        if (parent is null)
            return new BoundReplacement(false, true, Reason: "The destination's parent is linked or cannot be held.");
        SafeFileHandle opened;
        try
        {
            opened = OpenOrThrow(parent.LastHandle, name, ReadData | ReadAttributes | Delete | Synchronize,
                ShareRead | ShareDelete, Open, NonDirectoryFile | OpenReparsePoint, "Open reviewed destination");
        }
        catch (IOException ex) when (Core.Jobs.ErrorText.Classify(ex) == "sharing")
        {
            // An editor has it open without letting it be deleted: nothing changed. The question says it is in use, and
            // Retry replaces it once the editor lets go (I22).
            return new BoundReplacement(false, false, Reason: "The destination is in use by another program.", InUse: ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new BoundReplacement(false, true, Reason: "The reviewed destination cannot be opened exclusively for its final check: " + ex.Message);
        }
        using var old = opened;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (IsLink(old))
                return new BoundReplacement(false, true, Reason: "A linked destination is not eligible for bound replacement.");
            var identity = ProtectedHexFile.Identity(old);
            string actualIdentity = $"{identity.VolumeSerial:X16}:{identity.FileId}";
            if (!string.Equals(actualIdentity, expectedIdentity, StringComparison.OrdinalIgnoreCase) ||
                RandomAccess.GetLength(old) != expectedLength ||
                (byContent ? !DigestMatches(old, expectedSha256, expectedLength, ct)
                    : DateTime.FromFileTimeUtc(GetBasic(old).LastWriteTime) != expectedModifiedUtc!.Value.ToUniversalTime()))
                return new BoundReplacement(false, true, Reason: "The destination's identity or complete bytes changed after review.");

            // A different file can enter the old name between this check and the handle rename. The later incoming
            // rename uses no-replace, so that new file survives and the reviewed object remains recoverable.
            using (var atName = OpenOrThrow(parent.LastHandle, name, ReadAttributes | Synchronize, ShareAll, Open,
                       OpenReparsePoint, "Check reviewed destination name"))
            {
                if (ProtectedHexFile.Identity(atName) != identity)
                    return new BoundReplacement(false, true, Reason: "Another file now has the reviewed destination name.");
            }
            NtFile.Rename(old, parent.LastHandle, backupName, replace: false);
            try
            {
                ct.ThrowIfCancellationRequested();
                afterRetire?.Invoke();
                new SecureFileOps(() => ct.ThrowIfCancellationRequested()).MoveItem(
                    ElevationPaths.ToVolumePathWithoutFollowing(source),
                    ElevationPaths.ToVolumePathWithoutFollowing(parentPath), name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or OperationCanceledException)
            {
                try
                {
                    NtFile.Rename(old, parent.LastHandle, name, replace: false);
                    if (ex is OperationCanceledException) throw;
                    return new BoundReplacement(false, false, Reason: "The incoming file could not be published; the original destination was restored: " + ex.Message);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    // Also when canceled: the original is reported at its recovery name, and the step is not journaled as
                    // canceled before any change, which left that name only in a message (review finding W9 of f134ba37).
                    return new BoundReplacement(false, false, backupPath, (ex is OperationCanceledException
                        ? "Publication was canceled, and the original destination could not return to its name: "
                        : "The incoming file was not published and the original destination could not return to its name: ") + restore.Message);
                }
            }
            WriteThrough(parentPath);
            try { DeleteOpen(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
            {
                return new BoundReplacement(true, false, backupPath,
                    "The incoming file was published, but the replaced file remains at a recovery name: " + ex.Message);
            }
            return new BoundReplacement(true, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return new BoundReplacement(false, true, Reason: "Bound replacement refused: " + ex.Message);
        }
    }

    /// <summary>
    /// Makes the renames that published a replacement durable before its caller goes on, as a replacement by name is
    /// written through (review finding C8 of f134ba37). Otherwise, after a power loss a Move's only copy could survive
    /// under its staged name, which recovery offers to delete, its source on another volume already gone. Flushing the
    /// folder that holds both names commits them; where a file system cannot flush a folder, its own commit follows.
    /// </summary>
    private static void WriteThrough(string folder)
    {
        const uint AddFile = 0x2, Synchronize = 0x00100000, ShareAll = 7, OpenExisting = 3, BackupSemantics = 0x02000000,
            OpenReparsePoint = 0x00200000;
        using var handle = CreateFileForRename(Verbatim(folder), AddFile | Synchronize, ShareAll, 0, OpenExisting,
            BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid || !FlushFileBuffers(handle.DangerousGetHandle()))
            Core.Diagnostics.AppLog.Warn($"A replacement in a folder could not be written through (Win32 error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
    }

    private static bool DigestMatches(SafeFileHandle handle, string expected, long length, CancellationToken ct)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        for (long at = 0; at < length;)
        {
            ct.ThrowIfCancellationRequested();
            int read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - at)), at);
            if (read <= 0) return false;
            digest.AppendData(buffer.AsSpan(0, read));
            at += read;
        }
        return RandomAccess.GetLength(handle) == length &&
            string.Equals(Convert.ToHexString(digest.GetHashAndReset()), expected, StringComparison.OrdinalIgnoreCase);
    }
}
