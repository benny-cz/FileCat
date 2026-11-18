using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Analysis;

/// <summary>One local pipe for an authenticated metadata request. Client access omits the instance-creation bit.</summary>
internal static partial class NtfsMetadataPipe
{
    private const uint ClientRights = 0x00100003; // SYNCHRONIZE | FILE_READ_DATA | FILE_WRITE_DATA
    private const uint FirstInstance = 0x00080000, Overlapped = 0x40000000, RejectRemote = 0x00000008;
    private const ulong ConnectionMilliseconds = 120_000;
    private const string PipePrefix = @"\\.\pipe\";

    internal static string LocalName(string nonce)
    {
        if (!NtfsMetadataProtocol.ValidNonce(nonce)) throw new ArgumentException("Invalid NTFS metadata session identifier.", nameof(nonce));
        return PipePrefix + "FileCat-ntfs-metadata-" + nonce;
    }

    internal static NamedPipeServerStream CreateServer(string nonce, SecurityIdentifier logonSid)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!NtfsMetadataPeer.ValidLogonSid(logonSid))
            throw new ArgumentException("The requester must have a local logon identity.", nameof(logonSid));
        // OWNER RIGHTS suppresses implicit owner DACL access, including when UAC uses the same user.
        // A filtered administrator token has the Administrators SID only for deny checks.
        string acl = $"D:P(D;;0x000C0004;;;{logonSid.Value})(A;;0x00100000;;;OW)(A;;GA;;;SY)(A;;GA;;;BA)" +
            $"(A;;0x{ClientRights:X8};;;{logonSid.Value})";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(acl, 1, out nint descriptor, out _))
            throw NativeFailure("Create metadata pipe security");
        try
        {
            var security = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipe(LocalName(nonce), 3 | FirstInstance | Overlapped, RejectRemote, 1, 64 * 1024, 64 * 1024, 0, ref security);
            if (handle.IsInvalid)
            {
                var error = NativeFailure("Create the first local metadata pipe");
                handle.Dispose();
                throw error;
            }
            try { return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { _ = LocalFree(descriptor); }
    }

    internal static async Task<NtfsMetadataChannel> AcceptAsync(NamedPipeServerStream server, string nonce,
        NtfsMetadataPeer requester, CancellationToken cancellation)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var timer = StartConnectionWatchdog(wait);
        try
        {
            await server.WaitForConnectionAsync(wait.Token).ConfigureAwait(false);
            wait.Token.ThrowIfCancellationRequested();
            VerifyPeer(server.SafePipeHandle, requester, serverEnd: true);
            return new NtfsMetadataChannel(server, nonce, helper: true);
        }
        catch { server.Dispose(); throw; }
    }

    internal static async Task<NtfsMetadataChannel> ConnectAsync(string nonce, NtfsMetadataPeer helper, CancellationToken cancellation)
    {
        string pipe = @"\??\pipe\" + LocalName(nonce)[PipePrefix.Length..];
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var timer = StartConnectionWatchdog(wait);
        while (true)
        {
            wait.Token.ThrowIfCancellationRequested();
            helper.VerifyAlive();
            int status = OpenClient(pipe, out var handle);
            if (!handle.IsInvalid)
            {
                try
                {
                    wait.Token.ThrowIfCancellationRequested();
                    if (GrantedAccess(handle) != ClientRights)
                        throw new IOException("The local metadata pipe did not grant precisely the requested client rights.");
                    VerifyPeer(handle, helper, serverEnd: false);
                    var stream = new NamedPipeClientStream(PipeDirection.InOut, isAsync: true, isConnected: true, handle);
                    return new NtfsMetadataChannel(stream, nonce, helper: false);
                }
                catch { handle.Dispose(); throw; }
            }
            int error = (int)RtlNtStatusToDosError(status);
            handle.Dispose();
            if (error is not (2 or 231)) throw new IOException("The local NTFS metadata pipe could not be opened.", new Win32Exception(error));
            await Task.Delay(50, wait.Token).ConfigureAwait(false);
        }
    }

    internal static unsafe uint GrantedAccess(SafeHandle handle)
    {
        const int objectBasicInformation = 0, publicBasicInfoWords = 14;
        uint* info = stackalloc uint[publicBasicInfoWords];
        if (NtQueryObject(handle, objectBasicInformation, info, publicBasicInfoWords * sizeof(uint), out uint bytes) < 0 ||
            bytes != publicBasicInfoWords * sizeof(uint))
            throw new IOException("The local metadata pipe granted rights could not be checked.");
        return info[1];
    }

    private static unsafe int OpenClient(string pipe, out SafePipeHandle handle)
    {
        // The Win32 open failed under the exact data-only ACL. NtOpenFile sends precisely the
        // requested rights and explicit identification-level QoS to the local pipe driver.
        fixed (char* chars = pipe)
        {
            ushort bytes = checked((ushort)(pipe.Length * 2));
            var name = new UnicodeString { Length = bytes, MaximumLength = bytes, Buffer = chars };
            var qos = new SecurityQualityOfService
            {
                Length = sizeof(SecurityQualityOfService),
                ImpersonationLevel = 1, // SecurityIdentification
                EffectiveOnly = 1,
            };
            var attributes = new ObjectAttributes { Length = sizeof(ObjectAttributes), Name = &name, Flags = 0x40, Quality = &qos };
            int status = NtOpenFile(out nint native, ClientRights, &attributes, out _, 0, 0);
            handle = new SafePipeHandle(status >= 0 ? native : -1, ownsHandle: true);
            return status;
        }
    }

    private static void VerifyPeer(SafePipeHandle pipe, NtfsMetadataPeer expected, bool serverEnd)
    {
        bool process = serverEnd ? GetNamedPipeClientProcessId(pipe, out uint pid) : GetNamedPipeServerProcessId(pipe, out pid);
        bool session = serverEnd ? GetNamedPipeClientSessionId(pipe, out uint id) : GetNamedPipeServerSessionId(pipe, out id);
        if (!process || !session) throw new IOException("The local metadata pipe peer could not be identified.");
        expected.VerifyPipePeer(pid, id);
    }

    private static Timer StartConnectionWatchdog(CancellationTokenSource wait)
    {
        ulong started = NtfsMetadataChannel.MonotonicMilliseconds();
        return new Timer(_ =>
        {
            if (NtfsMetadataChannel.MonotonicMilliseconds() - started >= ConnectionMilliseconds)
                try { wait.Cancel(); } catch (ObjectDisposedException) { }
        }, null, 100, 100);
    }

    private static IOException NativeFailure(string action) =>
        new(action + " failed.", new Win32Exception(Marshal.GetLastPInvokeError()));

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Size;
        internal nint Descriptor;
        internal int Inherit;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct UnicodeString { internal ushort Length, MaximumLength; internal char* Buffer; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityQualityOfService { internal int Length, ImpersonationLevel; internal byte DynamicTracking, EffectiveOnly; }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ObjectAttributes
    {
        internal int Length;
        internal nint Root;
        internal UnicodeString* Name;
        internal uint Flags;
        internal nint Descriptor;
        internal SecurityQualityOfService* Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { internal nint Status, Information; }

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint descriptor, out uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances, uint outSize, uint inSize, uint timeout,
        ref SecurityAttributes security);

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtOpenFile(out nint file, uint access, ObjectAttributes* attributes, out IoStatusBlock result,
        uint sharing, uint options);

    [LibraryImport("ntdll.dll")]
    private static partial uint RtlNtStatusToDosError(int status);

    [LibraryImport("ntdll.dll")]
    private static unsafe partial int NtQueryObject(SafeHandle handle, int informationClass, uint* buffer, uint capacity, out uint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint id);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientSessionId(SafePipeHandle pipe, out uint id);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint id);
}
