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
    /// <summary>
    /// The lock a native edit creation holds on its session folder while it runs (exclusive), and that a reviewer takes to
    /// see whether one still runs (shared); false when another open description holds a conflicting lock. A process that
    /// ends releases its lock, so a folder nobody holds belongs to a creation that was interrupted.
    /// </summary>
    internal static bool TryLockEditCreation(SafeFileHandle file,bool exclusive)
    {
        // Only EWOULDBLOCK answers the question: another open of this folder holds the lock, which is what a creation's
        // own loop waits out and what a listing reads as "another FileCat process is still making this edit". A signal
        // (EINTR) answers nothing - it would have said that edit is in use right now, of a folder nobody holds - so the
        // call is made again, bounded, and anything else is still a failure.
        for(int attempt=0;;attempt++)
        {
            if(EditFlock(Fd(file),(exclusive?2:1)|4)==0)return true; // LOCK_EX or LOCK_SH, | LOCK_NB.
            int error=Marshal.GetLastPInvokeError();
            if(error==(Mac?35:11))return false;
            if(error!=4||attempt>=1024)throw new IOException("The native edit creation folder could not be locked.",error);
        }
    }
    internal static void UnlockEditCreation(SafeFileHandle file)=>_=EditFlock(Fd(file),8); // LOCK_UN
    [LibraryImport("libc",EntryPoint="flock",SetLastError=true)]
    private static partial int EditFlock(int fd,int operation);
}
