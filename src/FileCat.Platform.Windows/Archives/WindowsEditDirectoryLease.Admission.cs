using System.Runtime.InteropServices;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed unsafe partial class WindowsEditDirectoryLease
{
    internal override FileStream OpenStorageGate(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();ReviewCore(ct);if(!isPrivate)throw new IOException("Edit admission needs a private root.");
            var handle=OpenOrThrow(directory,EditStorageAdmission.GateName,GenericRead|GenericWrite|0x20000|Synchronize,ShareAll,3 /* OPEN_IF */,
                NonDirectoryFile|OpenReparsePoint,"Open native edit storage gate",(uint)FileAttributes.Normal,security);
            try
            {
                WindowsArchiveNative.Regular(handle,0,true,false);WindowsArchiveNative.RequirePrivateSecurity(handle,owner);
                WindowsArchiveNative.RequireOnlyDataStream(handle,0);ReviewCore(ct);return new(handle,FileAccess.ReadWrite);
            }
            catch{handle.Dispose();throw;}
        }
    }
    internal override bool TryLockStorageGate(FileStream file,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();GateOffset offset=default;
        if(LockAdmissionFile(file.SafeFileHandle,1|2,0,1,0,&offset))return true;
        int error=Marshal.GetLastPInvokeError();if(error==33)return false;
        throw new IOException("The native edit storage gate could not be locked.",new System.ComponentModel.Win32Exception(error));
    }
    internal override FileSystemCapacitySnapshot? StorageCapacity(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();ReviewCore(ct);var identity=ProtectedHexFile.Identity(directory);
            var capacity=FileSystemCapacity.QueryHeldDirectory(directory,identity.VolumeSerial);ReviewCore(ct);return capacity;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct GateOffset {public nuint Internal,InternalHigh;public uint Offset,OffsetHigh;public nint Event;}
    [LibraryImport("kernel32.dll",EntryPoint="LockFileEx",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockAdmissionFile(SafeFileHandle file,uint flags,uint reserved,uint low,uint high,GateOffset* offset);
}
