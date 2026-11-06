using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

internal static class GuestPolicyProbe
{
    internal static int Run(Stream output)
    {
        bool Denied(long call, nint a = 0, nint b = 0, nint c = 0)
        {
            long result = LinuxArchiveNative.Syscall(call, a, b, c, 0, 0, 0);
            int error = Marshal.GetLastPInvokeError();
            if (result == 0 && call == 220) Environment.Exit(91); // A revealed clone defect cannot leave a stray child.
            if (result >= 0 && call == 198) Close((int)result);
            return result == -1 && error == 1;
        }
        bool create = false, control = false, sealedRead = false, hostRead = false, directoryLink = false, fileLink = false;
        bool archiveDirectoryLink = false, archiveFileLink = false, aclDirectoryLink = false, aclFileAsDirectory = false;
        using (var runtime = UnixArchiveNative.PinDirectory("/worker"))
        using (var file = UnixArchiveNative.Child(runtime, "FileCat.Core.dll"))
            if (UnixFiles.Stat(file) is not { IsRegular: true }) return 90;
        try { using var link = UnixArchiveNative.PinDirectory("/sealed/linked-runtime"); } catch (IOException) { archiveDirectoryLink = true; }
        using (var directory = UnixArchiveNative.PinDirectory("/sealed"))
            try { using var link = UnixArchiveNative.Child(directory, "control-link"); } catch (IOException) { archiveFileLink = true; }
        using (var runtime = LinuxPosixAcl.OpenDirectory("/worker")) { }
        try { using var link = LinuxPosixAcl.OpenDirectory("/sealed/linked-runtime"); } catch (IOException) { aclDirectoryLink = true; }
        try { using var file = LinuxPosixAcl.OpenDirectory("/worker/FileCat.Core.dll"); } catch (IOException) { aclFileAsDirectory = true; }
        using (var runtime = LinuxArchiveNative.OpenDirectory("/worker")) { }
        try { using var link = LinuxArchiveNative.OpenDirectory("/sealed/linked-runtime"); } catch (ArchiveWriteException) { directoryLink = true; }
        using (var directory = LinuxArchiveNative.OpenDirectory("/sealed"))
            try { using var link = LinuxArchiveNative.OpenAt(directory, "control-link"); } catch (ArchiveWriteException) { fileLink = true; }
        try { File.WriteAllText("/tmp/guest-should-not-exist", "unexpected"); } catch (UnauthorizedAccessException) { create = true; }
        try { using var file = File.OpenWrite("/sys/fs/cgroup/filecat/memory.max"); } catch (UnauthorizedAccessException) { control = true; }
        try { File.ReadAllBytes("/sealed/not-runtime"); } catch (UnauthorizedAccessException) { sealedRead = true; }
        try { File.ReadAllBytes("/Users/benny/FileCatValidation/archive-writing-42829297/guest-host-read-sentinel"); }
        catch (DirectoryNotFoundException) { hostRead = true; } catch (FileNotFoundException) { hostRead = true; }
        var fields = new Dictionary<string, bool>
        {
            ["runtimeRead"] = File.ReadAllBytes(typeof(ArchivePlan).Assembly.Location).Length > 0,
            ["archiveDirectoryAndFileOpened"] = true, ["archiveDirectoryLinkDenied"] = archiveDirectoryLink,
            ["archiveFileLinkDenied"] = archiveFileLink, ["aclDirectoryOpened"] = true,
            ["aclDirectoryLinkDenied"] = aclDirectoryLink, ["aclFileAsDirectoryDenied"] = aclFileAsDirectory,
            ["directoryLinkDenied"] = directoryLink, ["fileLinkDenied"] = fileLink,
            ["createDenied"] = create, ["controlWriteDenied"] = control, ["sealedReadDenied"] = sealedRead, ["hostReadDenied"] = hostRead,
            ["tcpDenied"] = Denied(198, 2, 1), ["udpDenied"] = Denied(198, 2, 2), ["unixSocketDenied"] = Denied(198, 1, 1),
            ["processCloneDenied"] = Denied(220, 17), ["execDenied"] = Denied(221), ["execAtDenied"] = Denied(281),
            ["openAt2Denied"] = Denied(437), ["limitRaiseDenied"] = Denied(261, 0, 0, 1), ["parentSignalDenied"] = Denied(129, 1, 0),
            ["namespaceMutationDenied"] = Denied(34),
            ["ownThreadSignalProbeAllowed"] = LinuxArchiveNative.Syscall(130, (nint)LinuxArchiveNative.Syscall(178, 0, 0, 0, 0, 0, 0), 0, 0, 0, 0, 0) == 0,
            ["supervisorThreadSignalDenied"] = Denied(130, 1, 35),
            ["unapprovedThreadSignalDenied"] = Denied(130, (nint)LinuxArchiveNative.Syscall(178, 0, 0, 0, 0, 0, 0), 9),
            ["tracingDenied"] = Denied(117), ["newNamespaceDenied"] = Denied(97),
            ["parentDeathClearDenied"] = LinuxArchiveProcessPolicy.Prctl(1, 0, 0, 0, 0) == -1 && Marshal.GetLastPInvokeError() == 1,
        };
        output.Write("FCVMPOL1"u8);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(fields); byte[] size = BitConverter.GetBytes(json.Length);
        output.Write(size); output.Write(json); output.Flush(); return fields.Values.All(value => value) ? 0 : 9;
    }
    [DllImport("libc", EntryPoint = "close")] private static extern int Close(int descriptor);
}
