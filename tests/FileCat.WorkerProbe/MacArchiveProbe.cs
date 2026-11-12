using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.Archives.MacOS;

// Workloads for the macOS archive boundary tests. All but Parent run inside the sandboxed worker, after the same checks
// FileCat's own worker passes (MacArchiveIsolation.RunWorker); they report what that boundary allows.
internal static unsafe partial class MacArchiveProbe
{
    // Mach services a compromised worker could use to reach data or the network without a file or socket of its own.
    private static readonly string[] Services =
    [
        "com.apple.pasteboard.1", "com.apple.coreservices.launchservicesd", "com.apple.lsd.mapdb", "com.apple.coreservices.appleevents",
        "com.apple.windowserver.active", "com.apple.dock.server", "com.apple.tccd", "com.apple.system.notification_center",
        "com.apple.distributed_notifications@Uv3", "com.apple.cfprefsd.agent", "com.apple.cfprefsd.daemon", "com.apple.SecurityServer",
        "com.apple.securityd.xpc", "com.apple.dnssd.service", "com.apple.metadata.mds", "com.apple.FileCoordination", "com.apple.analyticsd",
        "com.apple.logd", "com.apple.diagnosticd", "com.apple.trustd.agent", "com.apple.usernoted.client", "com.apple.coreservices.quarantine-resolver",
    ];

    /// <summary>archive-mac-boundary FILE FOLDER PORT: what the sandboxed worker can reach.</summary>
    internal static int Boundary(string[] args)
    {
        if (!OperatingSystem.IsMacOS() || args.Length != 4 || !int.TryParse(args[3], out int port)) return 64;
        string file = args[1], folder = args[2];
        int reachable = 0;
        if (MacArchiveIsolation.TaskGetSpecialPort(MacArchiveIsolation.TaskSelfTrap(), 4, out uint bootstrap) != 0) return 65;
        foreach (string name in Services) if (BootstrapLookUp(bootstrap, name, out _) == 0) reachable++;
        int spawned; int child = 0;
        nint path = Marshal.StringToHGlobalAnsi("/usr/bin/true");
        try
        {
            nint* argv = stackalloc nint[2]; argv[0] = path; argv[1] = 0;
            nint* envp = stackalloc nint[1]; envp[0] = 0;
            spawned = PosixSpawn(&child, "/usr/bin/true", 0, 0, argv, envp);
            if (spawned == 0) { int status; WaitPid(child, &status, 0); }
        }
        finally { Marshal.FreeHGlobal(path); }
        // The container is the one place the sandbox lets the worker write; the launcher's zero file-size limit leaves it
        // only empty files. Ignored here, the limit's signal (which would end the worker) becomes a failed write to report.
        Signal(25 /* SIGXFSZ */, 1 /* SIG_IGN */);
        string home = Environment.GetEnvironmentVariable("HOME") ?? "/nonexistent";
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            containerWriteDenied = WriteLimited(Path.Combine(home, "probe-write-" + Environment.ProcessId + ".bin")),
            assetRead = File.ReadAllBytes(typeof(MacArchiveIsolation).Assembly.Location).Length > 0,
            readDenied = Denied(() => File.ReadAllBytes(file)),
            listDenied = Denied(() => Directory.GetFileSystemEntries(folder)),
            writeDenied = Denied(() => File.WriteAllText(Path.Combine(folder, "sandbox-write.txt"), "unexpected archive worker write")),
            metadataDenied = Denied(() => File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch)),
            tmpWriteDenied = Denied(() => File.WriteAllText("/tmp/filecat-archive-worker-" + Environment.ProcessId, "unexpected")),
            connectDenied = Denied(() => { using var c = new TcpClient(); c.Connect(IPAddress.Loopback, port); }),
            bindDenied = Denied(() => { using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); s.Bind(new IPEndPoint(IPAddress.Loopback, 0)); }),
            udpDenied = Denied(() => { using var u = new UdpClient(); u.Send(new byte[1], 1, new IPEndPoint(IPAddress.Loopback, port)); }),
            dnsDenied = Denied(() => Dns.GetHostAddresses("example.com")),
            posixSpawnErrno = spawned,
            processStartDenied = Denied(() => { using var p = Process.Start(new ProcessStartInfo("/usr/bin/true") { UseShellExecute = false })!; p.WaitForExit(); }),
            bootstrapNull = bootstrap == 0,
            machServicesReachable = reachable,
            machServicesProbed = Services.Length,
            container = Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID"),
            descriptors = MacArchiveIsolation.OpenDescriptors().Length,
        }));
        return 0;
    }

    /// <summary>archive-mac-memory MIB: touches native memory in 16 MiB steps, then waits to be stopped.</summary>
    internal static int Memory(string[] args)
    {
        if (!OperatingSystem.IsMacOS() || args.Length != 2 || !int.TryParse(args[1], out int total) || total is < 16 or > 8192) return 64;
        var blocks = new List<nint>();
        using var output = Console.OpenStandardOutput();
        for (int done = 16; done <= total; done += 16)
        {
            nint block = (nint)NativeMemory.Alloc(16u << 20);
            new Span<byte>((void*)block, 16 << 20).Fill((byte)(done | 1));
            blocks.Add(block);
            output.Write(System.Text.Encoding.ASCII.GetBytes("touched " + done + "\n")); output.Flush();
        }
        output.Write("allocated\n"u8); output.Flush();
        Thread.Sleep(30_000);
        return 9; // Reached only when nothing stopped this worker.
    }

    /// <summary>archive-mac-fd FD: whether a descriptor the parent left inheritable reached the worker.</summary>
    internal static int Descriptor(string[] args)
    {
        if (!OperatingSystem.IsMacOS() || args.Length != 2 || !int.TryParse(args[1], out int fd) || fd < 100) return 64;
        bool closed = !MacArchiveIsolation.OpenDescriptors().Contains(fd);
        bool assetRead = File.ReadAllBytes(typeof(MacArchiveIsolation).Assembly.Location).Length > 0;
        Console.WriteLine(JsonSerializer.Serialize(new { descriptorClosed = closed, assetRead }));
        return closed && assetRead ? 0 : 9;
    }

    /// <summary>
    /// archive-mac-parent (unsandboxed driver): starts an idle sandboxed worker, reports its process ID and, on "die",
    /// ends abruptly without stopping it. The worker ignores pipe EOF; only its parent watch can end it.
    /// </summary>
    internal static int Parent()
    {
        if (!OperatingSystem.IsMacOS()) return 64;
        var worker = MacArchiveProcess.Start(Environment.ProcessPath!, Guid.NewGuid(), TimeSpan.FromSeconds(60), ["archive-idle"]);
        using var reader = new StreamReader(worker.Output);
        string? ready = reader.ReadLine();
        if (ready != worker.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { worker.DisposeAsync().AsTask().GetAwaiter().GetResult(); return 65; }
        Console.WriteLine(JsonSerializer.Serialize(new { pid = worker.ProcessId })); Console.Out.Flush();
        if (Console.ReadLine() == "die") Exit(90); // Deliberately bypasses disposal.
        worker.DisposeAsync().AsTask().GetAwaiter().GetResult(); return 0;
    }

    // A write that the sandbox refuses, or that the zero file-size limit stops (EFBIG, which .NET reports as "Specified file
    // length was too large for the file system", an ArgumentOutOfRangeException); an empty file it left is removed.
    private static bool WriteLimited(string path)
    {
        try { File.WriteAllText(path, "data"); return false; }
        catch (UnauthorizedAccessException) { return true; }
        catch (ArgumentOutOfRangeException) { return true; }
        catch (IOException) { return true; }
        finally { try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }

    [LibraryImport("libc", EntryPoint = "signal")]
    private static partial nint Signal(int signal, nint handler);

    private static bool Denied(Action action)
    {
        try { action(); return false; }
        catch (UnauthorizedAccessException) { return true; }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AccessDenied or SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData) { return true; }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 35 /* EAGAIN */) { return true; }
    }

    [LibraryImport("libc", EntryPoint = "bootstrap_look_up", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int BootstrapLookUp(uint bootstrap, string name, out uint service);
    [LibraryImport("libc", EntryPoint = "posix_spawn", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PosixSpawn(int* pid, string path, nint actions, nint attributes, nint* argv, nint* envp);
    [LibraryImport("libc", EntryPoint = "waitpid")]
    private static partial int WaitPid(int pid, int* status, int options);
    [LibraryImport("libc", EntryPoint = "_exit")]
    private static partial void Exit(int status);
}
