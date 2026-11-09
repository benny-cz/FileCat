using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal static unsafe partial class UnixArchiveNative
{
    internal static SafeFileHandle OpenEditAdmissionFile(SafeFileHandle parent,string leaf)
    {
        Leaf(leaf);int flags=SafeOpen|2|(Mac?0x200:0x40); // O_RDWR | O_CREAT, no-follow, no truncation.
        return Handle(Mac&&RuntimeInformation.ProcessArchitecture==Architecture.Arm64
            ?EditOpenAtMacArm64(Fd(parent),leaf,flags,0,0,0,0,0,0x180):OpenAt(Fd(parent),leaf,flags,0x180));
    }
    internal static bool TryLockEditAdmission(SafeFileHandle file)
    {
        if(EditFlock(Fd(file),2|4)==0)return true; // LOCK_EX | LOCK_NB; close releases this open-description lock.
        int error=Marshal.GetLastPInvokeError();
        if(error==4||error==(Mac?35:11))return false;
        throw new IOException("The native edit storage gate could not be locked.",error);
    }
    [LibraryImport("libc",EntryPoint="flock",SetLastError=true)]
    private static partial int EditFlock(int fd,int operation);
}
