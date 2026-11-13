using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

/// <summary>
/// The removal of one file under the private cleanup rule (UNIX_PRIVATE_CLEANUP_DECISION.md), shared by edit discard,
/// commit-capture cleanup and old-folder retirement.
/// <list type="bullet">
/// <item>Linux: the caller takes a write lease before it reviews the file's content. The lease is granted only while no
/// other descriptor or mapping holds the file, and while it is held another program's open is held back and starts a
/// lease break, which <c>F_GETLEASE</c> shows; so no save can land unseen between the review and the removal. SIGIO, the
/// break signal, is ignored: its default action ends the process and .NET installs no handler for it.</item>
/// <item>The name is then set aside (an exclusive rename to a private name) and removed only if that name binds the held,
/// reviewed object and the lease is unbroken. Otherwise the name is restored, so an editor's atomic save that took the
/// name just before, or an open that arrived meanwhile, keeps its file.</item>
/// <item>macOS has no lease for an ordinary process: an editor that still holds the file can write to it after the
/// review. The callers say so and ask for it to be closed first.</item>
/// </list>
/// </summary>
internal static partial class UnixExactRemoval
{
    private const int SetLease=1024,GetLease=1025,WriteLease=1,Unlock=2,SigIo=29;
    // Every reason that another program holds the file for now starts so (IsInUse): a later attempt may succeed.
    private const string InUse="another program ";
    private static int signalIgnored;

    /// <summary>Test seam: runs right before a reviewed name is set aside (an editor's save can be staged there).</summary>
    internal static readonly AsyncLocal<Action<string>?> BeforeSetAside=new();

    private static void IgnoreLeaseBreakSignal()
    {
        if(Interlocked.Exchange(ref signalIgnored,1)!=0)return;
        nint previous=Signal(SigIo,1 /* SIG_IGN */);
        // Only the default (terminating) action is replaced; a handler someone installed stays.
        if(previous!=0&&previous!=1&&previous!=-1)_=Signal(SigIo,previous);
    }

    /// <summary>Takes the write lease on Linux (elsewhere nothing). Null when held, otherwise why not.</summary>
    internal static string? TakeLease(SafeFileHandle file,string what)
    {
        if(!OperatingSystem.IsLinux())return null;
        IgnoreLeaseBreakSignal();
        if(Fcntl(UnixArchiveNative.Fd(file),SetLease,WriteLease)==0)return null;
        int errno=Marshal.GetLastPInvokeError();
        return errno==11 /* EAGAIN */?InUse+"has "+what+" open; close it, then try again"
            :"FileCat could not check whether another program has "+what+" open (errno "+errno.ToString(CultureInfo.InvariantCulture)+")";
    }
    /// <summary>Whether a reason from this class says that another program holds the file for now.</summary>
    internal static bool IsInUse(string reason)=>reason.StartsWith(InUse,StringComparison.Ordinal);
    internal static void ReleaseLease(SafeFileHandle file){if(OperatingSystem.IsLinux())_=Fcntl(UnixArchiveNative.Fd(file),SetLease,Unlock);}
    // During a break F_GETLEASE answers the type the lease is being broken to, so only an unbroken lease answers F_WRLCK.
    private static bool Unbroken(SafeFileHandle file)=>!OperatingSystem.IsLinux()||Fcntl(UnixArchiveNative.Fd(file),GetLease,0)==WriteLease;

    /// <summary>
    /// Removes <paramref name="name"/> from <paramref name="directory"/> only while it binds <paramref name="held"/>, whose
    /// content the caller reviewed under the lease. Null when removed; otherwise why it was kept (it keeps its name, or
    /// the reason names the private name it has).
    /// </summary>
    internal static string? Unlink(SafeFileHandle directory,string name,SafeFileHandle held,string identity,string what,CancellationToken ct)
    {
        if(!Unbroken(held))return InUse+"opened "+what+" while it was checked";
        if(UnixFiles.StatChild(directory,name)?.Identity.ToString()!=identity)return what+" was replaced";
        ct.ThrowIfCancellationRequested();
        BeforeSetAside.Value?.Invoke(name);
        string aside=".filecat-removing-"+Guid.NewGuid().ToString("N");
        if(UnixArchiveNative.RenameExclusive(directory,name,aside)!=0)return Errno(what+" could not be set aside for removal");
        if(UnixFiles.StatChild(directory,aside)?.Identity.ToString()!=identity||!Unbroken(held))
            return UnixArchiveNative.RenameExclusive(directory,aside,name)==0?what+" changed during its removal and is kept"
                :what+" changed during its removal and is kept as "+aside;
        if(UnlinkAt(UnixArchiveNative.Fd(directory),aside,0)!=0)return Errno(what+" could not be removed")+"; it is kept as "+aside;
        ulong left=ArchiveUnixOuterMetadata.Capture(held).Native.LinkCount;
        UnixArchiveNative.SyncDirectory(directory);
        return left==0?null:what+" has another link, which is kept";
    }

    /// <summary>Removes an empty folder's name from its held parent while the name binds the folder. Null when removed.</summary>
    internal static string? RemoveFolder(SafeFileHandle parent,string name,string identity,string what)
    {
        if(UnixFiles.StatChild(parent,name)?.Identity.ToString()!=identity)return what+" was replaced";
        if(UnlinkAt(UnixArchiveNative.Fd(parent),name,OperatingSystem.IsMacOS()?0x80:0x200 /* AT_REMOVEDIR */)!=0)
            return Errno(what+" could not be removed");
        if(UnixFiles.StatChild(parent,name)?.Identity.ToString()==identity)return what+" is still present";
        UnixArchiveNative.SyncDirectory(parent);
        return null;
    }

    /// <summary>A child that is not there (ENOENT) is null; any other failure to open it is an error, never "removed".</summary>
    internal static SafeFileHandle? TryOpen(SafeFileHandle parent,string name,bool directory)
    {
        try{return directory?UnixArchiveNative.ChildDirectory(parent,name):UnixArchiveNative.Child(parent,name);}
        catch(IOException error)when(error.HResult==2 /* ENOENT */){return null;}
    }

    private static string Errno(string what)=>what+" (errno "+Marshal.GetLastPInvokeError().ToString(CultureInfo.InvariantCulture)+")";

    [LibraryImport("libc",EntryPoint="fcntl",SetLastError=true)]
    private static partial int Fcntl(int fd,int command,int argument);
    [LibraryImport("libc",EntryPoint="unlinkat",StringMarshalling=StringMarshalling.Utf8,SetLastError=true)]
    private static partial int UnlinkAt(int directory,string leaf,int flags);
    [LibraryImport("libc",EntryPoint="signal",SetLastError=true)]
    private static partial nint Signal(int signal,nint handler);
}
