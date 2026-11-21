using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    private static NativePath Walk(string path, bool reviewLeaf, CancellationToken ct)
    {
        if (!StoragePaths.IsOrdinaryLocalPath(path)) throw Refuse("path-grammar", "Storage changes require an ordinary local unnamed-stream path.");
        string raw = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
        int prefix = raw.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase) ? 45 : 3;
        string root = @"\??\" + raw[..prefix];
        string[] names = raw[prefix..].TrimEnd('\\').Length == 0 ? [] : raw[prefix..].TrimEnd('\\').Split('\\');
        if (names.Length + 1 > 64) throw Refuse("depth-limit", "The storage path exceeds the 64-level ancestor/depth limit.");
        if (reviewLeaf && names.Length == 0) throw Refuse("volume-root", "The volume root cannot be a storage-change target.");
        var nodes = new List<Node>(names.Length + 1);
        try
        {
            var first = Open(null, root, directory: true, pin: false, ct);
            VolumeFacts volume;
            try { volume = Volume(first.File, ct); nodes.Add(new(first, Facts(first.File, volume.Binding, ct))); }
            catch { first.Dispose(); throw; }
            for (int i = 0; i < names.Length; i++)
            {
                var parent = nodes[^1];
                if (!parent.Facts.IsDirectory || (parent.Facts.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || ManagedAttributes(parent.Facts.Attributes))
                    throw Refuse("ancestor-excluded", "A path ancestor is reparse-backed, cloud-managed or unavailable for this storage change.");
                bool last = i == names.Length - 1;
                var child = Open(parent.Handle.File, names[i], directory: !last, pin: last && reviewLeaf, ct);
                try
                {
                    var currentParent = Facts(parent.Handle.File, volume.Binding, ct);
                    if (currentParent.Identity != parent.Facts.Identity || currentParent.CaseFlags != parent.Facts.CaseFlags || currentParent.DeletePending ||
                        (currentParent.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || ManagedAttributes(currentParent.Attributes))
                        throw Changed("ancestor-changed", "A path ancestor changed during binding; inspect this selection again.");
                    nodes.Add(new(child, Facts(child.File, volume.Binding, ct)));
                }
                catch { child.Dispose(); throw; }
            }
            return new(nodes, volume);
        }
        catch { for (int i = nodes.Count - 1; i >= 0; i--) nodes[i].Handle.Dispose(); throw; }
    }

    private static Held Open(SafeFileHandle? parent, string name, bool directory, bool pin, CancellationToken ct)
    {
        if (parent is not null && !StoragePaths.IsChildName(name)) throw Refuse("child-grammar", "A relative storage open must name one exact component.");
        ct.ThrowIfCancellationRequested();
        IDisposable? charge = pin ? null : StorageNativeAllowance.RequireCurrent().ClaimTraversal();
        SafeFileHandle? file = null;
        try
        {
            int status = NtFile.TryOpen(parent, name, NtFile.ReadAttributes | NtFile.Synchronize,
                pin ? NtFile.ShareAll : NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open,
                NtFile.OpenReparsePoint | (directory ? NtFile.DirectoryFile : 0), out file, caseSensitive: parent is not null);
            if (status < 0)
            {
                var error = NtFile.Error(status, "Open the exact storage entry");
                var kind = error is UnauthorizedAccessException ? StorageReasonKind.PermissionOrPolicy : error is FileNotFoundException
                    ? StorageReasonKind.IdentityChanged : (error.HResult & 0xFFFF) is 32 or 33 ? StorageReasonKind.TransientFailure : StorageReasonKind.Unknown;
                throw new StorageInspectionException(Problem(kind, "open-status-" + ((uint)status).ToString("X8"), error.Message), error);
            }
            ct.ThrowIfCancellationRequested();
            return new(file, charge);
        }
        catch { file?.Dispose(); charge?.Dispose(); throw; }
    }

    private static NodeFacts Facts(SafeFileHandle file, string binding, CancellationToken ct)
    {
        Span<byte> basic = stackalloc byte[40], standard = stackalloc byte[24], id = stackalloc byte[24];
        Query(file, 0, basic, ct); Query(file, 1, standard, ct); Query(file, 18, id, ct);
        if (ActualVolumeBinding(file, rootOnly: false, ct) != binding)
            throw Changed("volume-changed", "The held object is on a different volume from its admitted ancestor.");
        uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(basic[32..]);
        bool folder = standard[21] != 0;
        if (folder != ((attributes & (uint)FileAttributes.Directory) != 0)) throw Changed("kind-inconsistent", "The native entry kind changed during inspection.");
        uint caseFlags = 0;
        if (folder && (attributes & (uint)FileAttributes.ReparsePoint) == 0 && !ManagedAttributes(attributes))
        {
            Span<byte> mode = stackalloc byte[4]; Query(file, 23 /* FileCaseSensitiveInfo */, mode, ct);
            caseFlags = BinaryPrimitives.ReadUInt32LittleEndian(mode);
            if ((caseFlags & ~1u) != 0) throw Refuse("case-mode-unknown", "The directory case mode is outside the qualified binding protocol.");
        }
        return new(new(BinaryPrimitives.ReadUInt64LittleEndian(id), Convert.ToHexString(id[8..])) { VolumeBinding = binding },
            attributes, BinaryPrimitives.ReadUInt32LittleEndian(standard[16..]), BinaryPrimitives.ReadInt64LittleEndian(standard[8..]), folder, standard[20] != 0, caseFlags);
    }

    private static void Query(SafeFileHandle file, int infoClass, Span<byte> output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); output.Clear();
        fixed (byte* bytes = output)
            if (!GetInfo(file, infoClass, bytes, (uint)output.Length)) throw NativeFailure(Marshal.GetLastPInvokeError(), "Read held storage metadata");
        ct.ThrowIfCancellationRequested();
    }

    private static VolumeFacts Volume(SafeFileHandle file, CancellationToken ct)
    {
        const uint removableMedia = 0x1, remoteDevice = 0x10;
        Span<byte> device = stackalloc byte[8], size = stackalloc byte[24];
        QueryVolume(file, 4 /* FileFsDeviceInformation */, device, ct);
        uint characteristics = BinaryPrimitives.ReadUInt32LittleEndian(device[4..]);
        if ((characteristics & remoteDevice) != 0)
            throw Refuse("remote-volume", "FileCat changes compression only on local drives; this drive is remote.");
        QueryVolume(file, 3 /* FileFsSizeInformation */, size, ct);
        ulong unit = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(size[16..]) * BinaryPrimitives.ReadUInt32LittleEndian(size[20..]);
        char* fs = stackalloc char[64];
        ct.ThrowIfCancellationRequested();
        if (!GetVolume(file, null, 0, out _, out _, out uint flags, fs, 64)) throw NativeFailure(Marshal.GetLastPInvokeError(), "Read the actual storage volume");
        ct.ThrowIfCancellationRequested();
        if (!new string(fs).Equals("NTFS", StringComparison.OrdinalIgnoreCase)) throw Refuse("filesystem", "This change requires a local NTFS volume.");
        if ((characteristics & removableMedia) != 0)
            throw Refuse("removable-media-unqualified", "Storage changes are unavailable on this removable NTFS media.");
        string binding = ActualVolumeBinding(file, rootOnly: true, ct);
        ct.ThrowIfCancellationRequested();
        if (AttachmentProblem(WindowsStorageDevice.Describe(binding)) is { } attachment) throw new StorageInspectionException(attachment);
        ct.ThrowIfCancellationRequested();
        return new(binding, flags, unit);
    }

    /// <summary>Hot-plug transports and unknown attachments stay outside the qualified device-loss scope.</summary>
    internal static StorageProblem? AttachmentProblem(WindowsStorageDevice.Description device) => device.Attachment switch
    {
        WindowsStorageDevice.Attachment.Fixed => null,
        WindowsStorageDevice.Attachment.HotPlug => Problem(StorageReasonKind.FileCatBoundary, "hot-plug-unqualified",
            "Storage changes are unavailable on a drive connected by USB, FireWire or a card reader, even when Windows reports it as fixed."),
        _ => Problem(StorageReasonKind.Unknown, "device-attachment-unknown",
            "The drive's connection type could not be established; storage changes are unavailable."),
    };

    internal static string ActualVolumeBinding(SafeFileHandle file, bool rootOnly, CancellationToken ct)
    {
        var binding = ActualVolumeBinding(file, ct);
        if (rootOnly && !binding.WholeVolume)
            throw Refuse("volume-binding", "The drive is an alias or its actual volume-root binding is unknown; select its ordinary volume path.");
        return binding.Identity;
    }

    internal static (string Identity, bool WholeVolume) ActualVolumeBinding(SafeHandle file, CancellationToken ct)
    {
        Span<char> buffer = stackalloc char[256];
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested(); uint chars;
            fixed (char* text = buffer) chars = FinalPath(file, text, (uint)buffer.Length, 9 /* VOLUME_NAME_GUID | FILE_NAME_OPENED */);
            ct.ThrowIfCancellationRequested();
            if (chars == 0) throw NativeFailure(Marshal.GetLastPInvokeError(), "Bind the held object's actual volume");
            if (chars >= buffer.Length)
            {
                if (chars >= 32768 || attempt >= 2) throw Refuse("volume-binding-length", "The held object's volume binding exceeded the bounded name query.");
                buffer = new char[chars + 1]; continue;
            }
            var name = buffer[..(int)chars];
            if (name.Length < 49 || !name.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) || name[48] != '\\' ||
                !Guid.TryParse(name.Slice(10, 38), out var guid))
            throw Refuse("volume-binding", "The drive is an alias or its actual volume-root binding is unknown; select its ordinary volume path.");
            return (guid.ToString("D"), name.Length == 49);
        }
    }

    private static void QueryVolume(SafeFileHandle file, int infoClass, Span<byte> output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); output.Clear(); IoStatus io;
        fixed (byte* bytes = output)
        {
            int status = NtQueryVolumeInformationFile(file, &io, bytes, (uint)output.Length, infoClass);
            if (status < 0) throw new StorageInspectionException(Problem(StorageReasonKind.Unknown, "volume-query-" + ((uint)status).ToString("X8"), NtFile.Error(status, "Read the held object's volume").Message));
        }
        if ((nuint)io.Information != (nuint)output.Length) throw Refuse("volume-query-length", "The native volume query returned incomplete metadata.");
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Strict membership for a held object without a held no-follow ancestor walk: only an explicit answer admits it.</summary>
    internal static StorageProblem? SyncRootProblem(SafeFileHandle file, CancellationToken ct) =>
        SyncRootProblem(WindowsStorageCloudQuery.Query(file, ct), volumeBinding: null, registeredRootVolumes: null, ct);

    /// <summary>Membership for an object reached by the held no-follow walk or held traversal (see the overload below).</summary>
    private static StorageProblem? SyncRootProblemAfterHeldWalk(SafeFileHandle file, string volumeBinding, CancellationToken ct) =>
        SyncRootProblem(WindowsStorageCloudQuery.Query(file, ct), volumeBinding, WindowsStorageSyncRoots.RegisteredRootVolumes, ct);

    /// <summary>
    /// Membership of the held object. Only the Cloud Files filter can answer for the object itself. When no filter
    /// instance answers on its volume (ERROR_INVALID_FUNCTION) and the caller supplies the held volume binding, every
    /// component from the volume root to the object was held without following reparse points and refused when it was
    /// reparse-backed or carried cloud attributes, and every sync root's own directory is a Cloud Files reparse point.
    /// The object is then admitted only when no registered sync root of any account is on the same volume. Without a
    /// binding (no held walk) the unanswered query keeps refusing.
    /// </summary>
    internal static StorageProblem? SyncRootProblem(int result, string? volumeBinding, Func<IReadOnlySet<string>?>? registeredRootVolumes,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (result == 0) return Problem(StorageReasonKind.FileCatBoundary, "cloud-sync-root", "This item is inside a registered cloud sync root, including hydrated or pinned content.");
        if (result == unchecked((int)0x80070186) /* ERROR_CLOUD_FILE_NOT_UNDER_SYNC_ROOT (390) */) return null;
        if (result != unchecked((int)0x80070001) /* ERROR_INVALID_FUNCTION: no filter instance answered */ ||
            volumeBinding is null || registeredRootVolumes is null)
            return Problem(StorageReasonKind.Unknown, "cloud-query-" + ((uint)result).ToString("X8"), "Cloud sync-root membership could not be ruled out; storage changes are unavailable.");
        var volumes = registeredRootVolumes();
        ct.ThrowIfCancellationRequested();
        if (volumes is null)
            return Problem(StorageReasonKind.Unknown, "cloud-registrations-unknown", "Windows Cloud Files did not answer for this drive and its registered sync roots could not be read; storage changes are unavailable.");
        return volumes.Contains(volumeBinding)
            ? Problem(StorageReasonKind.Unknown, "cloud-sync-root-volume", "A cloud sync root is registered on this drive, but Windows Cloud Files did not answer for this item; storage changes are unavailable.")
            : null;
    }

    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { public nint Status, Information; }
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetInfo(SafeFileHandle file, int infoClass, void* output, uint length);
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetVolume(SafeFileHandle file, char* label, uint labelChars, out uint serial, out uint maxComponent, out uint flags, char* fs, uint fsChars);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint FinalPath(SafeHandle file, char* text, uint chars, uint flags);
    [LibraryImport("ntdll.dll")] private static partial int NtQueryVolumeInformationFile(SafeFileHandle file, IoStatus* io, void* output, uint length, int infoClass);
}
