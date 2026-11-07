using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Analysis;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

/// <summary>Private NTFS inventory storage. Relative opens retain every ancestor without delete sharing.</summary>
public sealed unsafe partial class WindowsAnalysisPrivateDirectory : AnalysisPrivateDirectory
{
    private const uint ReadControl = 0x20000;
    private readonly List<OwnedHandle> _chain = [];
    private readonly SecurityIdentifier _owner;
    private readonly byte[] _directorySecurity, _fileSecurity;
    private bool _disposed;
    private SafeHandle Root => _chain[^1].Handle;

    public WindowsAnalysisPrivateDirectory(string parent, string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        if (!Path.IsPathFullyQualified(parent) || parent.Contains('/') || parent.Split('\\').Any(p => p is "." or "..") ||
            !AnalysisNames.IsOrdinaryChild(name, windows: true))
            throw new ArgumentException("An absolute local parent and a single private directory name are required.");
        string full = Path.GetFullPath(parent), drive = Path.GetPathRoot(full) ?? "";
        if (drive.Length != 3 || drive[1] != ':' || drive[2] != '\\' || GetDriveType(drive) != 3)
            throw new IOException("Private analysis storage requires a fixed local drive.");
        using var identity = WindowsIdentity.GetCurrent();
        _owner = identity.User ?? throw new IOException("The analysis profile owner could not be resolved.");
        _directorySecurity = Descriptor(directory: true); _fileSecurity = Descriptor(directory: false);
        try
        {
            _chain.Add(OpenOwned(null, @"\??\" + drive, Traverse | ReadAttributes | ReadControl | Synchronize,
                ShareRead | ShareWrite, Open, DirectoryFile | OpenReparsePoint));
            foreach (string component in full[drive.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!AnalysisNames.IsOrdinaryChild(component, windows: true)) throw new IOException("An analysis storage ancestor has an unsupported name.");
                RejectReparse(Root);
                _chain.Add(OpenOwned(Root, component, Traverse | ReadAttributes | ReadControl | Synchronize,
                    ShareRead | ShareWrite, Open, DirectoryFile | OpenReparsePoint));
            }
            RejectReparse(Root);
            const uint rootAccess = GenericRead | GenericWrite | ReadControl | Synchronize;
            var reservation = AnalysisNativeHandles.Acquire();
            try
            {
                int status = TryOpen(Root, name, rootAccess, ShareRead | ShareWrite, Create,
                    DirectoryFile | OpenReparsePoint, out var created, securityDescriptor: _directorySecurity);
                if (status == NameCollision)
                {
                    created.Dispose();
                    status = TryOpen(Root, name, rootAccess, ShareRead | ShareWrite, Open,
                        DirectoryFile | OpenReparsePoint, out created);
                }
                if (status < 0) { created.Dispose(); throw Error(status, "Open private analysis directory"); }
                _chain.Add(new(created, reservation)); reservation = null!;
            }
            finally { reservation?.Dispose(); }
            Verify(); _ = Space();
        }
        catch { Dispose(); throw; }
    }

    private byte[] Descriptor(bool directory)
    {
        string inherit = directory ? "OICI" : "";
        var descriptor = new RawSecurityDescriptor($"O:{_owner.Value}D:P(A;{inherit};FA;;;SY)(A;{inherit};FA;;;{_owner.Value})");
        var bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0); return bytes;
    }

    internal override void Verify()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var handle in _chain) RejectReparse(handle.Handle);
        Private(Root, directory: true);
    }

    public override bool MatchesRoot(IAnalysisBackend root)
    {
        Verify();
        return root is Analysis.WindowsAnalysisBackend && root.Root.Assurance == IdentityAssurance.Verified && root.RootEntry.Kind == AnalysisKind.Directory &&
            root.RootEntry.Identity is { } identity && identity == Analysis.WindowsAnalysisBackend.CaptureDirectoryIdentity(Root);
    }

    internal override (UInt128 Capacity, UInt128 Available) Space()
    {
        char* fs = stackalloc char[64];
        if (!GetVolumeInformationByHandle(Root, null, 0, out _, out _, out uint flags, fs, 64) ||
            new string(fs) != "NTFS" || (flags & 8) == 0) // FILE_PERSISTENT_ACLS
            throw new IOException("Private analysis publication requires a qualified local NTFS volume with persistent ACLs.");
        byte* buffer = stackalloc byte[32]; IoStatus io;
        int status = NtQueryVolumeInformationFile(Root, &io, buffer, 32, 7); // FileFsFullSizeInformation
        if (status < 0) throw Error(status, "Query private destination capacity");
        long total = *(long*)buffer, available = *(long*)(buffer + 8);
        ulong unit = checked((ulong)*(uint*)(buffer + 24) * *(uint*)(buffer + 28));
        if (total <= 0 || available < 0 || available > total || unit == 0)
            throw new IOException("Private destination capacity is unknown.");
        return (checked((UInt128)(ulong)total * unit), checked((UInt128)(ulong)available * unit));
    }

    internal override AnalysisPrivateFile CreateStaging()
    {
        Verify(); string name = ".staging-" + Guid.NewGuid().ToString("N");
        var owned = OpenOwned(Root, name, GenericRead | GenericWrite | Delete | ReadControl | Synchronize,
            ShareRead, Create, NonDirectoryFile | OpenReparsePoint, _fileSecurity);
        try { Private(owned.Handle, directory: false); return new PrivateFile(this, owned, writable: true); }
        catch
        {
            try { DeleteOpen(owned.Handle); }
            finally { owned.Dispose(); }
            throw;
        }
    }

    internal override AnalysisPrivateFile OpenRead(string name)
    {
        AnalysisPrivateNames.Entry(name); Verify();
        OwnedHandle owned;
        try { owned = OpenOwned(Root, name, GenericRead | ReadControl | Synchronize, ShareRead, Open, NonDirectoryFile | OpenReparsePoint); }
        catch (IOException error) when ((name == AnalysisPrivateNames.PrivateState || AnalysisPrivateNames.IsCapacityToken(name)) && error.InnerException is Win32Exception { NativeErrorCode: 2 })
        { throw new FileNotFoundException("The private analysis entry is missing.", error); }
        try { Private(owned.Handle, directory: false); return new PrivateFile(this, owned, writable: false); }
        catch { owned.Dispose(); throw; }
    }

    internal override AnalysisPrivateScratchFile CreateScratch()
    {
        Verify();
        // FILE_DELETE_ON_CLOSE is set in the create itself, including the crash-before-first-write case.
        // Exclusive sharing also prevents another opener from retaining the temporary data after our exit.
        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-zwcreatefile
        var owned = OpenOwned(Root, ".scratch-" + Guid.NewGuid().ToString("N"),
            GenericRead | GenericWrite | Delete | ReadControl | Synchronize, 0, Create,
            NonDirectoryFile | OpenReparsePoint | 0x1000, _fileSecurity);
        try
        {
            Private(owned.Handle, directory: false, allowDeletePending: true);
            return new AnalysisHandleScratchFile(owned.Handle, () => Private(owned.Handle, false, allowDeletePending: true), owned.Dispose);
        }
        catch { owned.Dispose(); throw; }
    }

    internal override string[] Entries(CancellationToken cancellation)
    {
        Verify(); var names = new List<string>(); byte[] buffer = new byte[64 * 1024]; long bytes = 0; bool restart = true;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested(); int status; IoStatus io;
            fixed (byte* data = buffer) status = NtQueryDirectoryFile(Root, 0, 0, 0, out io, data, (uint)buffer.Length, 1, false, 0, restart);
            restart = false;
            if (status == unchecked((int)0x80000006)) break; // STATUS_NO_MORE_FILES
            if (status < 0) throw Error(status, "Enumerate private analysis catalog");
            long used = (long)io.Information;
            if (used is < 64 or > 65536) throw new InvalidDataException("Invalid private directory response.");
            int offset = 0;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested(); var record = buffer.AsSpan(offset, (int)used - offset);
                if (record.Length < 64) throw new InvalidDataException("Invalid private directory record.");
                uint next = MemoryMarshal.Read<uint>(record), length = MemoryMarshal.Read<uint>(record[60..]);
                if ((length & 1) != 0 || length == 0 || length > record.Length - 64 ||
                    next != 0 && (next < 64 + length || next >= record.Length || (next & 7) != 0))
                    throw new InvalidDataException("Invalid private directory name extent.");
                string name = new(MemoryMarshal.Cast<byte, char>(record.Slice(64, (int)length)));
                if (name.Contains('\0') || name.Contains('/') || name.Contains('\\') || !AnalysisPrivateInventoryStore.WellFormedText(name))
                    throw new InvalidDataException("Invalid private catalog entry name.");
                if (name is not ("." or ".."))
                {
                    if (names.Count >= AnalysisPrivateNames.DirectoryEntries || (bytes += name.Length * 4L + 128) > AnalysisPrivateNames.DirectoryNameBytes)
                        throw new InvalidDataException("The private catalog directory exceeds its enumeration limit.");
                    names.Add(name);
                }
                if (next == 0) break;
                offset += (int)next;
            }
            Verify();
        }
        Verify(); return names.ToArray();
    }

    private void Private(SafeHandle handle, bool directory, bool allowDeletePending = false)
    {
        RejectReparse(handle);
        byte* standard = stackalloc byte[24];
        if (!GetFileInformationByHandleEx(handle, 1, standard, 24) || (standard[21] != 0) != directory || !allowDeletePending && standard[20] != 0 ||
            !directory && (*(uint*)(standard + 16) != 1 || *(long*)(standard + 8) is < 0 or > AnalysisLimits.InventoryBytes))
            throw new IOException("Analysis storage is not an ordinary private directory or single-link file.");
        int error = GetSecurityInfo(handle, 1, 1 | 4, out _, out _, out _, out _, out nint raw);
        if (error != 0) throw new IOException("Private analysis ownership and ACLs could not be checked.", new Win32Exception(error));
        try
        {
            uint length = GetSecurityDescriptorLength(raw);
            if (length is < 20 or > 65536) throw new IOException("The private analysis security descriptor is invalid.");
            byte[] bytes = new byte[length]; Marshal.Copy(raw, bytes, 0, bytes.Length);
            var descriptor = new RawSecurityDescriptor(bytes, 0);
            if (!Equals(descriptor.Owner, _owner) || (descriptor.ControlFlags & (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected)) !=
                (ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected) || descriptor.DiscretionaryAcl is not { Count: 2 } acl)
                throw new IOException("The private analysis owner or protected ACL does not match this profile.");
            bool user = false, system = false;
            var expectedFlags = directory ? AceFlags.ObjectInherit | AceFlags.ContainerInherit : AceFlags.None;
            foreach (GenericAce entry in acl)
            {
                if (entry is not CommonAce ace || ace.IsCallback || ace.AceQualifier != AceQualifier.AccessAllowed ||
                    ace.AceFlags != expectedFlags || ace.AccessMask != 0x1f01ff)
                    throw new IOException("The private analysis ACL has an unsupported grant.");
                if (ace.SecurityIdentifier.Equals(_owner) && !user) user = true;
                else if (ace.SecurityIdentifier.IsWellKnown(WellKnownSidType.LocalSystemSid) && !system) system = true;
                else throw new IOException("The private analysis ACL grants an unexpected identity.");
            }
            if (!user || !system) throw new IOException("The private analysis ACL is incomplete.");
        }
        finally { LocalFree(raw); }
    }

    private static void RejectReparse(SafeHandle handle)
    {
        if (GetReparseTag(handle) != 0) throw new IOException("An analysis storage ancestor or file is a reparse point.");
    }

    private sealed class PrivateFile : AnalysisPrivateFile
    {
        private readonly WindowsAnalysisPrivateDirectory _directory;
        private readonly OwnedHandle _owned;
        private readonly FileStream _stream;
        private readonly bool _writable;
        private bool _published, _disposed;
        public PrivateFile(WindowsAnalysisPrivateDirectory directory, OwnedHandle owned, bool writable)
        {
            _directory = directory; _owned = owned; _writable = writable;
            _stream = owned.Handle.OpenStream(writable ? FileAccess.ReadWrite : FileAccess.Read);
        }
        public override Stream Stream => _stream;
        internal override bool TryLockCapacity()
        {
            if (_writable) throw new InvalidOperationException("Capacity requires an immutable published file.");
            _directory.Verify(); Verify();
            // Keep the lock beyond both the marker and FileStream's 64-KiB read-ahead window.
            // NTFS can check the requested buffered read range before trimming it to EOF.
            byte* overlapped = stackalloc byte[32]; new Span<byte>(overlapped, 32).Clear(); *(uint*)(overlapped + 2 * IntPtr.Size) = 64 * 1024 * 1024;
            if (LockFileEx(_owned.Handle, 3, 0, 1, 0, overlapped)) return true; // EXCLUSIVE | FAIL_IMMEDIATELY
            if (Marshal.GetLastPInvokeError() == 33) return false; // ERROR_LOCK_VIOLATION
            throw new IOException("Private profile capacity locking is unavailable.");
        }
        public override void Verify() => _directory.Private(_owned.Handle, directory: false);
        public override void Flush() => _stream.Flush(flushToDisk: true);
        public override void PublishNew(string name)
        {
            if (!_writable || _published) throw new InvalidOperationException("This private file cannot be published.");
            AnalysisPrivateNames.Entry(name); _directory.Verify(); Verify();
            Rename(_owned.Handle, _directory.Root, name, replace: false);
            _published = true;
            try { Flush(); _directory.Verify(); Verify(); }
            catch (Exception ex) { throw new AnalysisPublicationUncertainException(name, ex); }
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { if (_writable && !_published) DeleteOpen(_owned.Handle); }
            finally { try { _stream.Dispose(); } finally { _owned.Dispose(); } }
        }
    }

    public override void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { AnalysisNativeHandle.CloseAll(_chain.AsEnumerable().Reverse()); }
        finally { _chain.Clear(); }
    }

    private sealed class OwnedHandle(SafeFileHandle handle, IDisposable reservation) : IDisposable
    {
        public AnalysisNativeHandle Handle { get; } = AnalysisNativeHandle.Adopt(handle, reservation);
        public void Dispose() => Handle.CloseChecked();
    }
    private static OwnedHandle OpenOwned(SafeHandle? parent, string name, uint access, uint share, uint disposition,
        uint options, ReadOnlySpan<byte> security = default)
    {
        var reservation = AnalysisNativeHandles.Acquire();
        try
        {
            int status = TryOpen(parent, name, access, share, disposition, options, out var handle, securityDescriptor: security);
            if (status < 0) { handle.Dispose(); throw Error(status, "Open private analysis storage handle"); }
            return new(handle, reservation);
        }
        catch { reservation.Dispose(); throw; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { public nint Status, Information; }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool LockFileEx(SafeHandle handle, uint flags, uint reserved, uint low, uint high, byte* overlapped);
    [LibraryImport("ntdll.dll")] private static partial int NtQueryDirectoryFile(SafeHandle handle, nint evt, nint apc, nint context,
        out IoStatus io, byte* buffer, uint length, int kind, [MarshalAs(UnmanagedType.U1)] bool single, nint pattern, [MarshalAs(UnmanagedType.U1)] bool restart);
    [LibraryImport("ntdll.dll")] private static partial int NtQueryVolumeInformationFile(SafeHandle handle, IoStatus* io, byte* buffer, uint length, int informationClass);
    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)] private static partial uint GetDriveType(string root);
    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetVolumeInformationByHandle(SafeHandle handle, char* label, uint labelSize,
        out uint serial, out uint maxComponent, out uint flags, char* fileSystem, uint fileSystemSize);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetFileInformationByHandleEx(SafeHandle handle, int kind, byte* buffer, uint length);
    [LibraryImport("advapi32.dll")] private static partial int GetSecurityInfo(SafeHandle handle, int type, uint information,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
    [LibraryImport("advapi32.dll")] private static partial uint GetSecurityDescriptorLength(nint descriptor);
    [LibraryImport("kernel32.dll")] private static partial nint LocalFree(nint memory);
}
