using System.Runtime.InteropServices;

namespace FileCat.Core.Analysis;

public sealed unsafe partial class UnixAnalysisPrivateDirectory
{
    internal override AnalysisPrivateFile OpenMaintenance(string name)
    {
        AnalysisPrivateNames.Maintenance(name); Verify();
        var owned = OpenOwned(Root, name, FileFlags, 0);
        try
        {
            Private(owned.Handle, false, 1);
            bool locked = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (Flock(owned.Handle, 2 | 4) == 0) { locked = true; break; }
                if (Marshal.GetLastPInvokeError() != 4) break;
            }
            if (!locked) throw new IOException("The saved analysis is in use or exclusive maintenance locking is unavailable. Try again when its readers finish.");
            return new MaintenanceFile(this, owned, name);
        }
        catch { owned.Dispose(); throw; }
    }
    private static string FileIdentity(SafeHandle handle)
    {
        var value = Stat(handle);
        return (OperatingSystem.IsMacOS() ? "darwin:" : "linux:") + value.Device.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + ":" + value.Inode.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
    }
    private sealed class MaintenanceFile : AnalysisPrivateFile
    {
        private readonly UnixAnalysisPrivateDirectory _directory;
        private readonly OwnedHandle _owned;
        private readonly FileStream _stream;
        private string _name;
        private bool _removed, _disposed;
        internal MaintenanceFile(UnixAnalysisPrivateDirectory directory, OwnedHandle owned, string name)
        { _directory = directory; _owned = owned; _name = name; _stream = owned.Handle.OpenStream(FileAccess.Read); }
        public override Stream Stream => _stream;
        internal override string Identity { get { Verify(); return FileIdentity(_owned.Handle); } }
        internal override string MaintenanceName => _name;
        internal override bool RemovalRequested => _removed;
        public override void Verify()
        {
            ObjectDisposedException.ThrowIf(_disposed, this); _directory.Verify();
            _directory.Private(_owned.Handle, false, _removed ? 0u : 1u);
        }
        private void VerifyName()
        {
            Verify(); using var probe = OpenOwned(_directory.Root, _name, _directory.FileFlags, 0);
            _directory.Private(probe.Handle, false, 1);
            if (!Same(Stat(probe.Handle), Stat(_owned.Handle))) throw new IOException("The private maintenance entry was replaced. No mutation was authorized for its replacement.");
        }
        public override void Flush()
        {
            Verify();
            if (Fsync(_directory.Root) != 0) throw Error("Private maintenance directory durability could not be confirmed.");
            if (_directory._mac && Fcntl(_owned.Handle, 51) != 0) throw Error("Private maintenance durability could not be confirmed.");
        }
        public override void PublishNew(string name) => throw new NotSupportedException();
        internal override void MoveOwnedNew(string name)
        {
            AnalysisPrivateNames.MaintenanceMove(_name, name); VerifyName();
            int result;
            try
            {
                result = _directory._mac ? RenameMaintenanceMac(_directory.Root, _name, _directory.Root, name, 4)
                    : RenameMaintenanceLinux(_directory.Root, _name, _directory.Root, name, 1);
            }
            catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException)
            { throw new NotSupportedException("Atomic create-new private metadata renaming is unavailable.", error); }
            if (result != 0) throw Error("Private metadata could not move without replacing an existing entry.");
            _name = name; VerifyName(); Flush();
        }
        internal override void RemoveOwned()
        {
            VerifyName();
            if (UnlinkAt(_directory.Root, _name, 0) != 0) throw Error("The private entry could not be removed.");
            _removed = true; Verify(); Flush();
        }
        public override void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { _stream.Dispose(); } finally { _owned.Dispose(); }
        }
    }
    [LibraryImport("libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameMaintenanceLinux(SafeHandle sourceDirectory, string source, SafeHandle destinationDirectory, string destination, uint flags);
    [LibraryImport("libc", EntryPoint = "renameatx_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameMaintenanceMac(SafeHandle sourceDirectory, string source, SafeHandle destinationDirectory, string destination, uint flags);
}
