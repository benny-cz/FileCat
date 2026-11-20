using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsAnalysisPrivateDirectory
{
    internal override AnalysisPrivateFile OpenMaintenance(string name)
        => OpenMaintenanceCore(name, replacement: false);
    internal override AnalysisPrivateFile OpenReplacementFile(string name)
        => OpenMaintenanceCore(name, replacement: true);
    private AnalysisPrivateFile OpenMaintenanceCore(string name, bool replacement)
    {
        if (replacement) AnalysisPrivateNames.ReplacementData(name); else AnalysisPrivateNames.Maintenance(name);
        Verify();
        var owned = OpenOwned(Root, name, GenericRead | GenericWrite | Delete | ReadControl | Synchronize,
            replacement ? ShareRead | ShareDelete : ShareRead, Open, NonDirectoryFile | OpenReparsePoint);
        try
        {
            Private(owned.Handle, false);
            byte* overlapped = stackalloc byte[32]; new Span<byte>(overlapped, 32).Clear();
            *(uint*)(overlapped + 2 * IntPtr.Size) = 64 * 1024 * 1024;
            if (!LockFileEx(owned.Handle, 3, 0, 1, 0, overlapped)) throw new IOException("The saved analysis is in use. Close its active readers and try again.");
            return new MaintenanceFile(this, owned, name, replacement);
        }
        catch { owned.Dispose(); throw; }
    }
    private static string FileIdentity(SafeHandle handle)
    {
        byte* data = stackalloc byte[24];
        if (!GetFileInformationByHandleEx(handle, 18, data, 24)) throw new IOException("Private file identity is unavailable.");
        return "ntfs:" + (*(ulong*)data).ToString("X16", System.Globalization.CultureInfo.InvariantCulture) + ":" + Convert.ToHexStringLower(new ReadOnlySpan<byte>(data + 8, 16));
    }
    private sealed class MaintenanceFile : AnalysisPrivateFile
    {
        private readonly WindowsAnalysisPrivateDirectory _directory;
        private readonly OwnedHandle _owned;
        private readonly FileStream _stream;
        private string _name;
        private readonly bool _replacement;
        private bool _removed, _disposed;
        internal MaintenanceFile(WindowsAnalysisPrivateDirectory directory, OwnedHandle owned, string name, bool replacement)
        { _directory = directory; _owned = owned; _name = name; _replacement = replacement; _stream = owned.Handle.OpenStream(FileAccess.Read); }
        public override Stream Stream => _stream;
        internal override string Identity { get { Verify(); return FileIdentity(_owned.Handle); } }
        internal override string MaintenanceName => _name;
        internal override bool RemovalRequested => _removed;
        public override void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this); _directory.Verify();
            if (!_removed) _directory.Private(_owned.Handle, false);
        }
        public override void Flush()
        { Verify(); if (!FlushMaintenance(_owned.Handle)) throw new IOException("Private maintenance durability could not be confirmed."); }
        public override void PublishNew(string name) => throw new NotSupportedException();
        private void VerifyName()
        {
            Verify(); if (_removed) throw new InvalidOperationException("The private entry was already removed.");
            using var probe = OpenOwned(_directory.Root, _name, GenericRead | ReadControl | Synchronize,
                ShareRead | ShareWrite | ShareDelete, Open, NonDirectoryFile | OpenReparsePoint);
            _directory.Private(probe.Handle, false);
            if (FileIdentity(probe.Handle) != FileIdentity(_owned.Handle)) throw new IOException("The private replacement name no longer denotes the held object.");
        }
        internal override void ReplaceOwned(AnalysisPrivateFile destination)
        {
            if (!_replacement || destination is not MaintenanceFile target || !target._replacement || !ReferenceEquals(_directory, target._directory))
                throw new ArgumentException("Both replacement leases must belong to this exact private directory capability.");
            AnalysisPrivateNames.ReplacementTarget(_name, target._name); VerifyName(); target.VerifyName();
            RequireReplacementWritable(_owned.Handle); RequireReplacementWritable(target._owned.Handle);
            string name = target._name;
            RenameReplacement(_owned.Handle, _directory.Root, name);
            _name = name; target._removed = true;
            try { Flush(); VerifyName(); target.Verify(); }
            catch (Exception error) { throw new AnalysisPublicationUncertainException(name, error); }
        }
        internal override void MoveOwnedNew(string name)
        {
            Verify(); if (_removed) throw new InvalidOperationException("The private entry was already removed.");
            AnalysisPrivateNames.MaintenanceMove(_name, name);
            Rename(_owned.Handle, _directory.Root, name, replace: false); _name = name;
            Flush(); Verify();
        }
        internal override void RemoveOwned()
        {
            Verify(); if (_removed) throw new InvalidOperationException("The private entry was already removed.");
            DeleteOpen(_owned.Handle); _removed = true; _directory.Verify();
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { _stream.Dispose(); } finally { _owned.Dispose(); }
        }
    }
    [LibraryImport("kernel32.dll", EntryPoint = "FlushFileBuffers", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool FlushMaintenance(SafeHandle handle);
}
