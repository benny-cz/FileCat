// A worker stand-in; the Linux route establishes all native controls before dispatching its controlled workload.
if (args.Length > 0 && args[0] == FileCat.Core.Archives.Linux.LinuxArchiveIsolation.LauncherArgument)
    return FileCat.Core.Archives.Linux.LinuxArchiveIsolation.RunLauncher(args);
if (args.Length > 0 && args[0] == FileCat.Core.Archives.Linux.LinuxArchiveIsolation.WorkerArgument)
{
    try
    {
        return FileCat.Core.Archives.Linux.LinuxArchiveIsolation.RunWorker(args, startup =>
        {
            string[] requests = args[4..];
            if (requests.Length == 0 || requests[0] is "archive-assess" or "archive-verify" && (requests.Length != 2 || requests[1] != startup.Nonce.ToString("D"))) return 64;
            return RunProbe(requests);
        });
    }
    catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
    { Console.Error.WriteLine(ex.Message); return 65; }
}
return RunProbe(args);

static int RunProbe(string[] requests)
{
    if (requests.Length == 0)
    {
        Console.Error.WriteLine("usage: env | read <path>... | exit0 | dropprivs [path]");
        return 2;
    }

    switch (requests[0])
    {
        case "archive-linux-authority":
            return LinuxArchiveProbe.Authority(requests);
        case "archive-linux-memory":
            return LinuxArchiveProbe.Memory();
        case "archive-linux-fd":
            return LinuxArchiveProbe.Descriptor(requests);
        case "archive-linux-main-exit":
            return LinuxArchiveProbe.MainExitGuard();
        case "archive-linux-parent":
            return LinuxArchiveProbe.Parent();
        case "archive-assess":
        case "archive-verify":
            if (requests.Length != 2 || !Guid.TryParse(requests[1], out Guid taskNonce) || taskNonce == Guid.Empty) return 2;
            using (var input = Console.OpenStandardInput())
            using (var output = Console.OpenStandardOutput())
                return requests[0] == "archive-verify"
                    ? FileCat.Archives.Writing.ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, taskNonce)
                    : FileCat.Archives.Writing.ArchiveAssessmentWorker.RunAfterIsolation(input, output, taskNonce);

        case "archive-boundary":
            {
                if (!OperatingSystem.IsWindows() || requests.Length != 6 || !int.TryParse(requests[4], out int archivePort)) return 2;
                var token = ArchiveProbeNative.TokenFacts();
                bool assetRead = File.ReadAllBytes(typeof(FileCat.Core.Archives.ArchivePlan).Assembly.Location).Length > 0;
                bool readDenied = false, writeDenied = false, profileWriteDenied = false, childDenied = false, registryDenied = false;
                try { File.ReadAllText(requests[1]); }
                catch (UnauthorizedAccessException) { readDenied = true; }
                try { File.WriteAllText(requests[2], "unwanted worker write"); }
                catch (UnauthorizedAccessException) { writeDenied = true; }
                profileWriteDenied = true;
                foreach (string suffix in new[] { "", "LocalState", "TempState", "LocalCache", "AC\\Temp" })
                {
                    string path = Path.Combine(Path.GetDirectoryName(requests[3])!, suffix, Path.GetFileName(requests[3]));
                    try { File.WriteAllText(path, "unwanted profile write"); profileWriteDenied = false; }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException ex) when (ex.HResult == unchecked((int)0x80070522)) { } // ERROR_PRIVILEGE_NOT_HELD: MIC
                }
                string network = ArchiveProbeNative.Network(archivePort);
                try
                {
                    var command = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    command.ArgumentList.Add("exit0");
                    using var child = System.Diagnostics.Process.Start(command);
                    if (child is not null && !child.WaitForExit(2000)) { child.Kill(); child.WaitForExit(); }
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 5 or 367 or 1260 or 50) { childDenied = true; }
                if (OperatingSystem.IsWindows())
                {
                    try { using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(requests[5]); }
                    catch (UnauthorizedAccessException) { registryDenied = true; }
                    catch (System.Security.SecurityException) { registryDenied = true; }
                }
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    appContainer = token.AppContainer,
                    capabilityCount = token.Capabilities,
                    assetRead,
                    readDenied,
                    writeDenied,
                    profileWriteDenied,
                    network,
                    childDenied,
                    registryDenied,
                    runtime = Environment.Version.ToString(),
                }));
                return 0;
            }

        case "archive-memory":
            {
                if (!OperatingSystem.IsWindows()) return 2;
                nint small = ArchiveProbeNative.VirtualAlloc(0, 64u << 20, 0x3000, 4);
                bool healthy = small != 0;
                if (small != 0) ArchiveProbeNative.VirtualFree(small, 0, 0x8000);
                nint excess = ArchiveProbeNative.VirtualAlloc(0, (nuint)1 << 30, 0x3000, 4);
                bool refused = excess == 0;
                if (excess != 0) ArchiveProbeNative.VirtualFree(excess, 0, 0x8000);
                Console.WriteLine($"healthy={healthy} refused={refused}");
                return healthy && refused ? 0 : 8;
            }

        case "archive-cpu":
            {
                Console.WriteLine("CPU-PROBE-STARTED");
                Console.Out.Flush();
                var time = System.Diagnostics.Stopwatch.StartNew();
                while (time.Elapsed < TimeSpan.FromSeconds(30)) Thread.SpinWait(10_000);
                Console.WriteLine("CPU LIMIT DID NOT STOP THE PROBE");
                return 9;
            }

        case "archive-stderr":
            Console.WriteLine("STDERR-PROBE-STARTED");
            Console.Out.Flush();
            for (int i = 0; i < 1024; i++) Console.Error.Write(new string('x', 4096));
            Console.WriteLine("STDERR LIMIT DID NOT STOP THE PROBE");
            return 9;

        case "archive-idle":
            Console.WriteLine(Environment.ProcessId);
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite); // ignore pipe EOF: parent-death qualification must prove job termination
            return 0;

        case "archive-handle":
            {
                if (!OperatingSystem.IsWindows() || requests.Length != 2 || !long.TryParse(requests[1], out long inheritedHandle)) return 2;
                using var ownHandle = File.OpenHandle(typeof(FileCat.Core.Archives.ArchivePlan).Assembly.Location, FileMode.Open, FileAccess.Read, FileShare.Read);
                Console.WriteLine("HANDLE-PROBE-CONTROL:" + ArchiveProbeNative.FileIdentity(ownHandle.DangerousGetHandle()));
                Console.WriteLine("HANDLE-PROBE-QUERY");
                Console.Out.Flush();
                Console.WriteLine("HANDLE-PROBE:" + ArchiveProbeNative.FileIdentity(new nint(inheritedHandle)));
                return 0;
            }

        case "archive-proxy":
            {
                if (requests.Length != 3 || !Guid.TryParse(requests[1], out Guid nonce) ||
                    !long.TryParse(requests[2], out long length) || length is < 0 or > 1L << 30) return 2;
                using var input = Console.OpenStandardInput();
                using var output = Console.OpenStandardOutput();
                var receive = new FileCat.Core.Archives.ArchiveWorkerProtocol(nonce, 4 * length + (16 << 20));
                var send = new FileCat.Core.Archives.ArchiveWorkerProtocol(nonce, 4 * length + (16 << 20));
                var connection = new FileCat.Core.Archives.ArchiveProxyConnection(request =>
                {
                    send.Write(output, request.Kind, request.Payload);
                    return receive.Read(input);
                });
                using var source = new FileCat.Core.Archives.ArchiveProxyStream(connection, 0, length, length);
                using var stage = new FileCat.Core.Archives.ArchiveProxyStream(connection, FileCat.Core.Archives.ArchiveIoAuthority.StageId,
                    0, length, writable: true);
                source.CopyTo(stage, 64 << 10);
                stage.Position = 0;
                byte[] digest = System.Security.Cryptography.SHA256.HashData(stage);
                send.Write(output, FileCat.Core.Archives.ArchiveWorkerMessage.Completed, digest);
                return 0;
            }

        case "exit0":
            return 0;

        case "env":
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                Console.WriteLine($"{entry.Key}={entry.Value}");
            return 0;

        case "read":
            foreach (var path in requests.Skip(1))
            {
                try { Console.WriteLine(File.ReadAllText(path)); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    Console.WriteLine($"refused {Path.GetFileName(path)}: {ex.GetType().Name}");
                }
            }
            return 0;

        case "dropprivs":
            {
                // Reports the worker identity change the picture decoder performs, and then whether a root-only file
                // is still readable, so a test can see the drop took effect rather than take the report on trust.
                var result = FileCat.Core.FileSystem.UnixWorkerIdentity.Drop();
                Console.WriteLine($"before={result.UserBefore}:{result.GroupBefore} after={result.UserAfter}:{result.GroupAfter} " +
                                  $"regainRefused={result.RegainRefused} startedPrivileged={result.StartedPrivileged} " +
                                  $"unprivileged={result.Unprivileged} refusal={result.Refusal ?? "<none>"}");
                if (requests.Length > 1)
                {
                    try { Console.WriteLine("read=" + File.ReadAllText(requests[1]).Trim()); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        Console.WriteLine("read=refused " + ex.GetType().Name);
                    }
                }
                return 0;
            }

        default:
            Console.Error.WriteLine("unknown request");
            return 2;
    }

}

internal static class ArchiveProbeNative
{
    internal static string FileIdentity(nint handle)
    {
        byte[] info = new byte[52]; // BY_HANDLE_FILE_INFORMATION: volume serial + file index identify the object
        return GetFileInformationByHandle(handle, info)
            ? Convert.ToHexString(info.AsSpan(28, 4)) + Convert.ToHexString(info.AsSpan(44, 8)) : "unavailable";
    }
    internal static string Network(int port)
    {
        // Probe native Winsock directly: LPAC can also refuse Winsock's catalog initialization, which the CLR
        // otherwise wraps in a TypeInitializationException before Connect is reached. The parent has a live,
        // positively tested loopback listener at this exact port; connection refusal is not a passing result.
        nint data = System.Runtime.InteropServices.Marshal.AllocHGlobal(512);
        int startup;
        try { startup = WSAStartup(0x202, data); }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(data); }
        if (startup != 0) return $"StartupRefused:{startup}";
        try
        {
            nint socketHandle = socket(2, 1, 6);
            if (socketHandle == new nint(-1)) return $"SocketRefused:{WSAGetLastError()}";
            try
            {
                byte[] address = new byte[16];
                address[0] = 2; // AF_INET, little endian; port/address are network byte order.
                address[2] = (byte)(port >> 8); address[3] = (byte)port;
                address[4] = 127; address[7] = 1;
                return connect(socketHandle, address, address.Length) == 0 ? "Connected" : $"ConnectRefused:{WSAGetLastError()}";
            }
            finally { closesocket(socketHandle); }
        }
        finally { WSACleanup(); }
    }

    internal static (bool AppContainer, int Capabilities) TokenFacts()
    {
        if (!OpenProcessToken(new nint(-1), 8, out nint token)) throw new System.ComponentModel.Win32Exception();
        try
        {
            nint value = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
            try
            {
                if (!GetTokenInformation(token, 29, value, 4, out _)) throw new System.ComponentModel.Win32Exception();
                bool appContainer = System.Runtime.InteropServices.Marshal.ReadInt32(value) == 1;
                GetTokenInformation(token, 30, 0, 0, out int bytes);
                if (bytes < 4 || bytes > 1 << 20) throw new IOException("Invalid token capability data.");
                nint capabilities = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes);
                try
                {
                    if (!GetTokenInformation(token, 30, capabilities, bytes, out _)) throw new System.ComponentModel.Win32Exception();
                    return (appContainer, System.Runtime.InteropServices.Marshal.ReadInt32(capabilities));
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(capabilities); }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(value); }
        }
        finally { CloseHandle(token); }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int kind, nint data, int size, out int needed);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint VirtualAlloc(nint address, nuint size, uint allocation, uint protection);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool VirtualFree(nint address, nuint size, uint kind);
    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int WSAStartup(ushort version, nint data);
    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int WSACleanup();
    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern nint socket(int addressFamily, int type, int protocol);
    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int connect(nint handle, byte[] address, int length);
    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int closesocket(nint handle);
    [System.Runtime.InteropServices.DllImport("ws2_32.dll")]
    private static extern int WSAGetLastError();
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(nint handle, byte[] information);
}
