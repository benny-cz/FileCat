using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Recovery.Unix;

/// <summary>
/// The C library calls drive reading needs on Linux and macOS (D-47): receiving a file descriptor over a local socket
/// (UDisks2 and macOS's authopen hand one over), starting authopen with a socket as its output, and asking a device its
/// size. The structure layouts differ between the two systems and are spelled out where they are read. Apple's arm64
/// calling convention puts a C function's variadic arguments on the stack, so ioctl's third argument goes through a
/// declaration that places it there (<see cref="IoctlArgument"/>).
/// </summary>
internal static unsafe partial class UnixNative
{
    private const int SolSocketLinux = 1, SolSocketMac = 0xFFFF, ScmRights = 1, MsgCmsgCloexec = 0x40000000;
    private const nuint Fioclex = 0x20006601; // macOS: _IO('f', 1), close on exec, no argument
    private const int Eintr = 4;

    /// <summary>
    /// Receives up to <paramref name="buffer"/>.Length bytes from a connected local socket, and any file descriptors sent
    /// with them into <paramref name="fds"/> (each closed on exec). Returns the bytes received; 0 when the other end closed.
    /// </summary>
    public static int Receive(Socket socket, Span<byte> buffer, List<int> fds)
    {
        bool added = false;
        var handle = socket.SafeHandle;
        try
        {
            handle.DangerousAddRef(ref added);
            return Receive((int)handle.DangerousGetHandle(), buffer, fds);
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    public static int Receive(int socket, Span<byte> buffer, List<int> fds)
    {
        bool mac = OperatingSystem.IsMacOS();
        byte* message = stackalloc byte[64];
        byte* control = stackalloc byte[256];
        long* iov = stackalloc long[2];
        new Span<byte>(message, 64).Clear();
        new Span<byte>(control, 256).Clear();
        fixed (byte* data = buffer)
        {
            iov[0] = (long)data;
            iov[1] = buffer.Length;
            // struct msghdr: name, namelen, iov at 16, iovlen at 24 (size_t on Linux, int on macOS), control at 32,
            // controllen at 40 (size_t on Linux, socklen_t on macOS), flags after it.
            *(long*)(message + 16) = (long)iov;
            if (mac) *(int*)(message + 24) = 1;
            else *(long*)(message + 24) = 1;
            *(long*)(message + 32) = (long)control;
            if (mac) *(uint*)(message + 40) = 256;
            else *(long*)(message + 40) = 256;
            long n;
            do n = RecvMsg(socket, message, mac ? 0 : MsgCmsgCloexec);
            while (n < 0 && Marshal.GetLastPInvokeError() == Eintr);
            if (n < 0) throw new IOException($"Reading from the local socket failed (error {Marshal.GetLastPInvokeError()}).");
            long controlLength = mac ? *(uint*)(message + 40) : *(long*)(message + 40);
            // struct cmsghdr: len (size_t on Linux, socklen_t on macOS), level, type, then the data (aligned to 8 or 4).
            int header = mac ? 12 : 16, align = mac ? 4 : 8, solSocket = mac ? SolSocketMac : SolSocketLinux;
            for (long at = 0; at + header <= controlLength;)
            {
                long length = mac ? *(uint*)(control + at) : *(long*)(control + at);
                int level = *(int*)(control + at + (mac ? 4 : 8)), type = *(int*)(control + at + (mac ? 8 : 12));
                if (length < header || at + length > controlLength) break;
                if (level == solSocket && type == ScmRights)
                    for (long d = header; d + 4 <= length; d += 4)
                    {
                        int fd = *(int*)(control + at + d);
                        if (mac) _ = Ioctl(fd, Fioclex);
                        fds.Add(fd);
                    }
                at += (length + align - 1) / align * align;
            }
            return (int)n;
        }
    }

    /// <summary>Sends one byte and a file descriptor over a local socket (tests stand in for UDisks2 and authopen with it).</summary>
    public static void SendFd(int socket, int fd)
    {
        bool mac = OperatingSystem.IsMacOS();
        byte* message = stackalloc byte[64];
        byte* control = stackalloc byte[64];
        long* iov = stackalloc long[2];
        byte one = 1;
        new Span<byte>(message, 64).Clear();
        new Span<byte>(control, 64).Clear();
        iov[0] = (long)&one;
        iov[1] = 1;
        *(long*)(message + 16) = (long)iov;
        if (mac) *(int*)(message + 24) = 1;
        else *(long*)(message + 24) = 1;
        *(long*)(message + 32) = (long)control;
        int header = mac ? 12 : 16, space = mac ? 16 : 24;
        if (mac)
        {
            *(uint*)(message + 40) = (uint)space;
            *(uint*)control = (uint)(header + 4);
            *(int*)(control + 4) = SolSocketMac;
            *(int*)(control + 8) = ScmRights;
        }
        else
        {
            *(long*)(message + 40) = space;
            *(long*)control = header + 4;
            *(int*)(control + 8) = SolSocketLinux;
            *(int*)(control + 12) = ScmRights;
        }
        *(int*)(control + header) = fd;
        if (SendMsg(socket, message, 0) < 0) throw new IOException($"Sending a descriptor failed (error {Marshal.GetLastPInvokeError()}).");
    }

    /// <summary>A connected pair of local stream sockets.</summary>
    public static (int Ours, int Theirs) SocketPair()
    {
        int* pair = stackalloc int[2];
        if (SocketPairNative(1 /* AF_UNIX */, 1 /* SOCK_STREAM */, 0, pair) != 0)
            throw new IOException($"A local socket pair could not be made (error {Marshal.GetLastPInvokeError()}).");
        return (pair[0], pair[1]);
    }

    /// <summary>
    /// macOS: starts <paramref name="path"/> with <paramref name="arguments"/> and <paramref name="output"/> as its standard
    /// output, closing <paramref name="alsoClose"/> in it; returns the process id.
    /// </summary>
    public static int Spawn(string path, IReadOnlyList<string> arguments, int output, int alsoClose)
    {
        nint actions = 0;
        if (PosixSpawnFileActionsInit(&actions) != 0) throw new IOException("A process could not be prepared.");
        var strings = new List<nint>();
        try
        {
            PosixSpawnFileActionsAddDup2(&actions, output, 1);
            PosixSpawnFileActionsAddClose(&actions, alsoClose);
            var argv = stackalloc nint[arguments.Count + 1];
            for (int i = 0; i < arguments.Count; i++)
            {
                argv[i] = Marshal.StringToCoTaskMemUTF8(arguments[i]);
                strings.Add(argv[i]);
            }
            argv[arguments.Count] = 0;
            int pid;
            int error = PosixSpawn(&pid, path, &actions, null, argv, *NsGetEnviron());
            if (error != 0) throw new IOException($"{path} could not be started (error {error}).");
            return pid;
        }
        finally
        {
            PosixSpawnFileActionsDestroy(&actions);
            foreach (var s in strings) Marshal.FreeCoTaskMem(s);
        }
    }

    /// <summary>Waits for a process started with <see cref="Spawn"/> to end; its exit code (-1 when it was killed).</summary>
    public static int Wait(int pid)
    {
        int status;
        long n;
        do n = WaitPid(pid, &status, 0);
        while (n < 0 && Marshal.GetLastPInvokeError() == Eintr);
        return n < 0 ? -1 : (status & 0x7F) == 0 ? (status >> 8) & 0xFF : -1;
    }

    public static void Terminate(int pid) => _ = Kill(pid, 15 /* SIGTERM */);

    public static void Close(int fd)
    {
        if (fd >= 0) _ = CloseNative(fd);
    }

    public static uint UserId() => GetUid();

    /// <summary>A device's size: BLKGETSIZE64 on Linux; block count times block size on macOS. -1 when it does not say.</summary>
    public static long DeviceLength(SafeFileHandle device, out int sectorSize)
    {
        sectorSize = 512;
        bool added = false;
        try
        {
            device.DangerousAddRef(ref added);
            int fd = (int)device.DangerousGetHandle();
            if (OperatingSystem.IsLinux())
            {
                long bytes;
                int logical;
                if (IoctlArgument(fd, 0x1268 /* BLKSSZGET */, &logical) == 0 && logical is >= 512 and <= 65536) sectorSize = logical;
                return IoctlArgument(fd, 0x80081272 /* BLKGETSIZE64 */, &bytes) == 0 ? bytes : -1;
            }
            if (OperatingSystem.IsMacOS())
            {
                uint blockSize;
                ulong blocks;
                if (IoctlArgument(fd, 0x40046418 /* DKIOCGETBLOCKSIZE */, &blockSize) != 0 || IoctlArgument(fd, 0x40086419 /* DKIOCGETBLOCKCOUNT */, &blocks) != 0) return -1;
                if (blockSize is >= 512 and <= 65536) sectorSize = (int)blockSize;
                return (long)(blocks * blockSize);
            }
            return -1;
        }
        finally
        {
            if (added) device.DangerousRelease();
        }
    }

    [LibraryImport("libc", EntryPoint = "recvmsg", SetLastError = true)]
    private static partial long RecvMsg(int socket, byte* message, int flags);

    [LibraryImport("libc", EntryPoint = "sendmsg", SetLastError = true)]
    private static partial long SendMsg(int socket, byte* message, int flags);

    [LibraryImport("libc", EntryPoint = "socketpair", SetLastError = true)]
    private static partial int SocketPairNative(int domain, int type, int protocol, int* pair);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    private static partial int PosixSpawnFileActionsInit(nint* actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    private static partial int PosixSpawnFileActionsAddDup2(nint* actions, int fd, int newFd);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
    private static partial int PosixSpawnFileActionsAddClose(nint* actions, int fd);

    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    private static partial int PosixSpawnFileActionsDestroy(nint* actions);

    [LibraryImport("libc", EntryPoint = "posix_spawn", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PosixSpawn(int* pid, string path, nint* actions, void* attributes, nint* argv, nint envp);

    [LibraryImport("libc", EntryPoint = "_NSGetEnviron")]
    private static partial nint* NsGetEnviron();

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static partial int WaitPid(int pid, int* status, int options);

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int Kill(int pid, int signal);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseNative(int fd);

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();

    /// <summary>ioctl with its one argument where the calling convention reads a variadic argument from.</summary>
    private static int IoctlArgument(int fd, nuint request, void* argument) =>
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? IoctlStacked(fd, request, 0, 0, 0, 0, 0, 0, argument) // eight registers taken, the ninth goes on the stack
            : IoctlRegister(fd, request, argument);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int fd, nuint request);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlRegister(int fd, nuint request, void* argument);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlStacked(int fd, nuint request, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, void* argument);
}
