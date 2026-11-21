using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService(bool qualifySmbReads = false, bool qualifyRefsReads = false,
    bool qualifyAccessEstimates = false, bool qualifyNativePropagation = false, bool qualifyInheritanceChanges = false,
    bool qualifyFileTrees = false, bool qualifyFolderOnly = false, bool qualifyAclResets = false,
    bool qualifyFolderTrees = false) : IPermissionService
{
    private const string Source = "Windows GetSecurityInfo";
    private const int ReviewedTreeMemberLimit = 4096;
    private readonly bool _qualifyAccessEstimates = qualifyAccessEstimates;
    private readonly bool _qualifyNativePropagation = qualifyNativePropagation;
    private readonly bool _qualifyInheritanceChanges = qualifyInheritanceChanges;
    private readonly bool _qualifyFileTrees = qualifyFileTrees;
    private readonly bool _qualifyFolderTrees = qualifyFolderTrees;
    private readonly bool _qualifyFolderOnly = qualifyFolderOnly;
    private readonly bool _qualifyAclResets = qualifyAclResets;
    private const uint ReadControl = 0x20000, OpenNoRecall = 0x400000;
    private const uint UnsafeAttributes = (uint)(FileAttributes.ReparsePoint | FileAttributes.Offline) | 0x00440000;
    internal const PermissionSections DiscretionarySections = PermissionSections.Owner | PermissionSections.Group |
        PermissionSections.Access | PermissionSections.Inheritance;

    public PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "This reader requires Windows x64.");
        if (qualifySmbReads && PermissionSnapshots.IsFileSystemSubject(subject) &&
            subject.Authority.Kind is PermissionAuthorityKind.Local or PermissionAuthorityKind.MountedServer)
        {
            if (TrySmbTarget(subject.RawLocator, out var mounted))
                return InspectSmb(subject, sections, mounted, cancellationToken);
            if (subject.Authority.Kind == PermissionAuthorityKind.MountedServer)
                return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Gone,
                    "The original Windows SMB mapping is no longer available at this location.", Source);
        }
        if (!PermissionSnapshots.IsLocal(subject))
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "This reader requires a local Windows file or directory.");
        if (!StoragePaths.IsOrdinaryLocalPath(subject.RawLocator))
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "The original must be an ordinary local filesystem path.");

        var (root, names) = SplitLocalPath(subject.RawLocator);
        if (names.Length > PermissionSnapshots.MaxPathDepth)
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Incomplete, "The original exceeds the path-depth limit.");

        SafeFileHandle? file = null;
        SafeFileHandle? parent = null;
        try
        {
            file = Walk(root, names, subject.Authority, cancellationToken, out parent, out var chain, out var binding,
                allowRefs: qualifyRefsReads);
            if (!PermissionSnapshots.Matches(subject, binding.Identity))
                throw new ReadFailure(PermissionReadStatus.Stale, "The original type, volume or object identity changed.");
            if (!PermissionSnapshots.MatchesListing(subject, chain[^1].Size, chain[^1].ModifiedUtcTicks))
                throw new ReadFailure(PermissionReadStatus.Stale, "The file differs from the original listing observation. Refresh the listing and select it again.");

            var snapshot = PermissionSnapshots.Create(subject, PermissionModel.WindowsDescriptor, binding, sections, DateTimeOffset.UtcNow, Source);
            if ((sections & DiscretionarySections) != 0)
                snapshot = ReadDescriptor(snapshot, sections, parent, names.Length == 0 ? @"\??\" + root : names[^1], chain[^1], cancellationToken);
            if ((sections & PermissionSections.Audit) != 0)
                snapshot = snapshot with { Audit = new(PermissionReadStatus.Unsupported, Source, DateTimeOffset.UtcNow,
                    Reason: "Reading the Windows audit ACL requires an authorized FileCat session with SeSecurityPrivilege.") };

            using var current = Walk(root, names, subject.Authority, cancellationToken, out var currentParent, out var refreshed, out var currentBinding,
                allowRefs: qualifyRefsReads);
            using (currentParent)
            {
                if (currentBinding != binding || !chain.Select(s => s.Identity).SequenceEqual(refreshed.Select(s => s.Identity)) || chain[^1] != refreshed[^1])
                    throw new ReadFailure(PermissionReadStatus.Stale, "The original namespace or security metadata changed during inspection.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = PermissionSnapshots.WithCapabilities(snapshot, sections);
            if (binding.FileSystem == "ReFS")
                return new PermissionInspection(snapshot);
            var result = new PermissionInspection(snapshot, new WindowsInspectionPin(file, root, names, chain));
            file = null;
            return result;
        }
        catch (ReadFailure ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PermissionSnapshots.Unavailable(subject, sections, ex.Status, ex.Message, Source, ex.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int error = ex is Win32Exception native ? native.NativeErrorCode : ex.HResult & 0xffff;
            return PermissionSnapshots.Unavailable(subject, sections, ErrorStatus(error), ex.Message, Source, error);
        }
        finally { parent?.Dispose(); file?.Dispose(); }
    }

    private static (string Root, string[] Names) SplitLocalPath(string locator)
    {
        string path = locator.StartsWith(@"\\?\", StringComparison.Ordinal) ? locator[4..] : locator;
        int rootLength = path[1] == ':' ? 3 : 45;
        return (path[..rootLength], path[rootLength..].TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries));
    }

    private static SafeFileHandle Walk(string root, string[] names, PermissionAuthority authority, CancellationToken ct, out SafeFileHandle? parent,
        out FileStamp[] chain, out PermissionBinding binding, uint leafReadAccess = 0, bool backupIntent = false, bool allowRefs = false)
    {
        string winRoot = root.Length == 3 ? root : @"\\?\" + root;
        if (GetDriveType(winRoot) != 3 /* DRIVE_FIXED */)
            throw new ReadFailure(PermissionReadStatus.Unsupported, "This reader supports fixed local NTFS volumes.");
        SafeFileHandle? current = Open(null, @"\??\" + root, security: false, ct,
            readAccess: names.Length == 0 ? leafReadAccess : 0, backupIntent: backupIntent);
        SafeFileHandle? previous = null;
        chain = new FileStamp[names.Length + 1];
        try
        {
            string volume = WindowsStorageTransforms.ActualVolumeBinding(current, rootOnly: true, ct);
            Span<char> fileSystem = stackalloc char[32];
            uint flags;
            fixed (char* text = fileSystem)
                if (!GetVolumeInformation(current, null, 0, out _, out _, out flags, text, (uint)fileSystem.Length))
                    throw NativeFailure("Read the held volume", Marshal.GetLastPInvokeError());
            bool isNtfs = fileSystem.StartsWith("NTFS\0", StringComparison.OrdinalIgnoreCase);
            if (!isNtfs && !(allowRefs && fileSystem.StartsWith("ReFS\0", StringComparison.OrdinalIgnoreCase)))
                throw new ReadFailure(PermissionReadStatus.Unsupported, "This reader supports NTFS and separately qualified ReFS volumes.");
            chain[0] = Screen(current, volume, root: true, authority, ct, checkCloud: isNtfs);
            for (int i = 0; i < names.Length; i++)
            {
                previous?.Dispose();
                previous = current;
                current = Open(previous, names[i], security: false, ct,
                    readAccess: i == names.Length - 1 ? leafReadAccess : 0, backupIntent: backupIntent);
                chain[i + 1] = Screen(current, volume, root: false, authority, ct, checkCloud: isNtfs);
                if (i < names.Length - 1 && chain[i + 1].Identity.Kind != PermissionObjectKind.Directory)
                    throw new ReadFailure(PermissionReadStatus.Stale, "An original ancestor is no longer a directory.");
            }
            var last = chain[^1];
            binding = new(last.Identity, isNtfs ? "NTFS" : "ReFS", @"\\?\Volume{" + volume + @"}\", last.Links, (flags & 0x80000) != 0);
            parent = previous;
            previous = null;
            var result = current;
            current = null;
            return result;
        }
        finally { previous?.Dispose(); current?.Dispose(); }
    }

    private static FileStamp Screen(SafeFileHandle handle, string volume, bool root, PermissionAuthority authority, CancellationToken ct,
        bool checkCloud = true)
    {
        ct.ThrowIfCancellationRequested();
        var basic = NtFile.GetBasic(handle);
        if ((basic.FileAttributes & UnsafeAttributes) != 0)
            throw new ReadFailure(PermissionReadStatus.Unsupported, "The original or an ancestor is a reparse point, offline, or requires recall.");
        if (WindowsStorageTransforms.ActualVolumeBinding(handle, rootOnly: root, ct) != volume)
            throw new ReadFailure(PermissionReadStatus.Stale, "The held entry is on a different volume from its ancestor.");
        // CldFlt supports NTFS only. ReFS is already corroborated by the volume handle and screened for recall and reparse attributes.
        if (checkCloud)
        {
            int cloud = WindowsStorageCloudQuery.Query(handle, ct);
            if (cloud != unchecked((int)0x80070186)) // ERROR_CLOUD_FILE_NOT_UNDER_SYNC_ROOT
                throw new ReadFailure(PermissionReadStatus.Unsupported, cloud == 0
                    ? "The original is within a cloud sync root." : "Cloud sync-root membership could not be ruled out.", cloud);
        }
        return ReadFileStamp(handle, basic, volume, root, authority, ct);
    }

    private static FileStamp ReadFileStamp(SafeFileHandle handle, NtFile.BasicInfo basic, string volume, bool root, PermissionAuthority authority,
        CancellationToken ct)
    {
        StandardInfo standard;
        if (!GetInformation(handle, 1 /* FileStandardInfo */, &standard, (uint)sizeof(StandardInfo)))
            throw NativeFailure("Read the held entry type", Marshal.GetLastPInvokeError());
        if (standard.DeletePending != 0 || standard.Links == 0)
            throw new ReadFailure(PermissionReadStatus.Gone, "The held entry is being removed.");
        if ((standard.Directory != 0) != NtFile.IsDirectory(basic.FileAttributes) || (root && standard.Directory == 0))
            throw new ReadFailure(PermissionReadStatus.Stale, "The original entry type changed during inspection.");
        var kind = standard.Directory == 0 ? PermissionObjectKind.File : root ? PermissionObjectKind.VolumeRoot : PermissionObjectKind.Directory;
        var identity = new PermissionIdentity(ProtectedHexFile.Identity(handle), volume, kind, authority);
        ct.ThrowIfCancellationRequested();
        long? modified = basic.LastWriteTime is >= 0 and <= 2650467743999999999
            ? DateTime.FromFileTimeUtc(basic.LastWriteTime).Ticks : null;
        return new(identity, basic.FileAttributes, basic.ChangeTime, standard.Links, standard.EndOfFile, modified);
    }

    private static SafeFileHandle Open(SafeFileHandle? parent, string name, bool security, CancellationToken ct, uint writeAccess = 0,
        uint readAccess = 0, bool backupIntent = false, bool allowDirectoryWrite = false)
    {
        ct.ThrowIfCancellationRequested();
        // FILE_DIRECTORY_FILE rejects NO_RECALL. Verify directory type from the opened object before using it as a parent.
        const uint OpenForBackupIntent = 0x4000;
        int status = NtFile.TryOpen(parent, name, NtFile.Synchronize | NtFile.ReadAttributes | (security ? ReadControl : 0) | writeAccess | readAccess,
            NtFile.ShareAll, NtFile.Open, OpenNoRecall | NtFile.OpenReparsePoint | (writeAccess != 0 && !allowDirectoryWrite ? NtFile.NonDirectoryFile : 0) |
                (backupIntent ? OpenForBackupIntent : 0),
            out var handle, caseSensitive: parent is not null);
        if (status >= 0) return handle;
        handle.Dispose();
        int error = StatusToDosError(status);
        throw NativeFailure("Open the exact filesystem entry", error);
    }

    private static PermissionSnapshot ReadDescriptor(PermissionSnapshot snapshot, PermissionSections sections, SafeFileHandle? parent, string name,
        FileStamp metadata, CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PermissionSections requested = sections & DiscretionarySections;
        try
        {
            using var security = Open(parent, name, security: true, ct);
            bool root = metadata.Identity.Kind == PermissionObjectKind.VolumeRoot;
            bool checkCloud = snapshot.Binding?.FileSystem != "ReFS";
            if (Screen(security, metadata.Identity.MountInstance, root, snapshot.Subject.Authority, ct, checkCloud) != metadata)
                throw new ReadFailure(PermissionReadStatus.Stale, "The security handle did not match the screened object.");
            var result = ReadHeldDescriptor(snapshot, requested, security, ct);
            if (Screen(security, metadata.Identity.MountInstance, root, snapshot.Subject.Authority, ct, checkCloud) != metadata)
                throw new ReadFailure(PermissionReadStatus.Stale, "The object changed while reading its security descriptor.");
            return result;
        }
        catch (ReadFailure ex) when (ex.Status != PermissionReadStatus.Stale)
        {
            var failure = PermissionSnapshots.Create(snapshot.Subject, snapshot.Model, snapshot.Binding, requested, now, Source,
                ex.Status, ex.Message, ex.Error);
            return snapshot with
            {
                Owner = requested.HasFlag(PermissionSections.Owner) ? failure.Owner : snapshot.Owner,
                Group = requested.HasFlag(PermissionSections.Group) ? failure.Group : snapshot.Group,
                Access = requested.HasFlag(PermissionSections.Access) ? failure.Access : snapshot.Access,
                Inheritance = requested.HasFlag(PermissionSections.Inheritance) ? failure.Inheritance : snapshot.Inheritance
            };
        }
    }

    private static PermissionSnapshot ReadHeldDescriptor(PermissionSnapshot snapshot, PermissionSections requested, SafeFileHandle security,
        CancellationToken ct)
    {
        return ReadSecurityDescriptor(security, DiscretionaryFlags(requested), ct,
            bytes => Decode(snapshot, requested, bytes, DateTimeOffset.UtcNow));
    }

    private static uint DiscretionaryFlags(PermissionSections requested) =>
        (requested.HasFlag(PermissionSections.Owner) ? 1u : 0) | (requested.HasFlag(PermissionSections.Group) ? 2u : 0) |
        ((requested & (PermissionSections.Access | PermissionSections.Inheritance)) != 0 ? 4u : 0);

    private delegate T ReadSecurityBytes<T>(ReadOnlySpan<byte> bytes);

    private static T ReadSecurityDescriptor<T>(SafeFileHandle security, uint flags, CancellationToken ct, ReadSecurityBytes<T> decode)
    {
        ct.ThrowIfCancellationRequested();
        int error = GetSecurityInfo(security, 1 /* SE_FILE_OBJECT */, flags, 0, 0, 0, 0, out nint descriptor);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (error != 0) throw NativeFailure("Read the held object's security descriptor", error);
            if (descriptor == 0 || !IsValidSecurityDescriptor(descriptor))
                throw new ReadFailure(PermissionReadStatus.Failed, "Windows returned an invalid security descriptor.");
            uint length = GetSecurityDescriptorLength(descriptor);
            if (length is < 20 or > PermissionSnapshots.MaxSectionBytes)
                throw new ReadFailure(PermissionReadStatus.Incomplete, "The security descriptor is outside the byte limit.");
            return decode(new ReadOnlySpan<byte>((void*)descriptor, (int)length));
        }
        finally { if (descriptor != 0) _ = LocalFree(descriptor); }
    }

    private sealed record WindowsInspectionPin(SafeFileHandle File, string Root, string[] Names, FileStamp[] Chain) : IDisposable
    {
        public void Dispose() => File.Dispose();
    }

    private static PermissionSnapshot Decode(PermissionSnapshot snapshot, PermissionSections sections, ReadOnlySpan<byte> descriptor, DateTimeOffset now)
    {
        string authority = snapshot.Subject.Authority.Id;
        if (sections.HasFlag(PermissionSections.Owner))
            snapshot = snapshot with { Owner = ObservePrincipal(descriptor, 4, authority, now) };
        if (sections.HasFlag(PermissionSections.Group))
            snapshot = snapshot with { Group = ObservePrincipal(descriptor, 8, authority, now) };
        if (sections.HasFlag(PermissionSections.Inheritance))
        {
            try { snapshot = snapshot with { Inheritance = new(PermissionReadStatus.Present, Source, now, WindowsPermissionDescriptor.Control(descriptor)) }; }
            catch (InvalidDataException ex) { snapshot = snapshot with { Inheritance = new(PermissionReadStatus.Failed, Source, now, Reason: ex.Message) }; }
        }
        if (sections.HasFlag(PermissionSections.Access))
        {
            try
            {
                var dacl = WindowsPermissionDescriptor.Dacl(descriptor, authority);
                snapshot = snapshot with { Access = new(dacl.Form == WindowsDaclForm.Absent ? PermissionReadStatus.Absent : PermissionReadStatus.Present,
                    Source, now, new PermissionAclValue(dacl)) };
            }
            catch (InvalidDataException ex) { snapshot = snapshot with { Access = new(PermissionReadStatus.Failed, Source, now, Reason: ex.Message) }; }
            catch (OverflowException ex) { snapshot = snapshot with { Access = new(PermissionReadStatus.Incomplete, Source, now, Reason: ex.Message) }; }
        }
        return snapshot;
    }

    private static PermissionObservation<Principal> ObservePrincipal(ReadOnlySpan<byte> descriptor, int fieldOffset, string authority, DateTimeOffset now)
    {
        try
        {
            Principal? principal = WindowsPermissionDescriptor.PrincipalAt(descriptor, fieldOffset, authority);
            return new(principal.HasValue ? PermissionReadStatus.Present : PermissionReadStatus.Absent, Source, now, principal);
        }
        catch (InvalidDataException ex) { return new(PermissionReadStatus.Failed, Source, now, Reason: ex.Message); }
    }

    private static PermissionReadStatus ErrorStatus(int error) => error switch
    {
        2 or 3 or 303 => PermissionReadStatus.Gone,
        5 or 1314 => PermissionReadStatus.Denied,
        1 or 50 => PermissionReadStatus.Unsupported,
        _ => PermissionReadStatus.Failed
    };
    private static ReadFailure NativeFailure(string action, int error) => new(ErrorStatus(error), $"{action}: {new Win32Exception(error).Message}", error);
    private sealed class ReadFailure(PermissionReadStatus status, string message, int? error = null) : Exception(message)
    {
        internal PermissionReadStatus Status { get; } = status;
        internal int? Error { get; } = error;
    }
    private readonly record struct FileStamp(PermissionIdentity Identity, uint Attributes, long ChangeTime, uint Links, long Size, long? ModifiedUtcTicks);
    [StructLayout(LayoutKind.Sequential)] private struct StandardInfo { public long Allocation, EndOfFile; public uint Links; public byte DeletePending, Directory; }

    [LibraryImport("ntdll.dll", EntryPoint = "RtlNtStatusToDosError")]
    private static partial int StatusToDosError(int status);
    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetInformation(SafeFileHandle file, int infoClass, void* output, uint length);
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetVolumeInformation(SafeFileHandle file, char* label, uint labelLength,
        out uint serial, out uint maximumComponent, out uint flags, char* fileSystem, uint fileSystemLength);
    [LibraryImport("advapi32.dll")]
    private static partial int GetSecurityInfo(SafeFileHandle file, int type, uint flags, nint owner, nint group, nint dacl, nint sacl, out nint descriptor);
    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsValidSecurityDescriptor(nint descriptor);
    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityDescriptorLength(nint descriptor);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint pointer);
}
