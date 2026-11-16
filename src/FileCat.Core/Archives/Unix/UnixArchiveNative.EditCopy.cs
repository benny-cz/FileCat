using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal static unsafe partial class UnixArchiveNative
{
    internal static SafeFileHandle? ChildOrAbsent(SafeFileHandle directory,string leaf)
    {
        Leaf(leaf);int fd=OpenAt(Fd(directory),leaf,SafeOpen,0);
        if(fd<0&&Marshal.GetLastPInvokeError()==2)return null;
        return Handle(fd);
    }
}
