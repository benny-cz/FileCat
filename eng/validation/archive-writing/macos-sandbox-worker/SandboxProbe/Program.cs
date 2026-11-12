// macOS App Sandbox worker spike (route decision of 12 November 2026). A self-contained .NET probe that runs either
// unsandboxed (control) or as a copy signed with only the App Sandbox entitlement, and reports what the sandboxed .NET
// runtime can and cannot do. It is validation tooling, never shipped, and reads no archive data.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static unsafe class Program
{
    [DllImport("libc", SetLastError = true)] private static extern int getppid();
    [DllImport("libc", SetLastError = true)] private static extern int kqueue();
    [DllImport("libc", SetLastError = true)] private static extern int kevent(int kq, KEvent* changes, int nchanges, KEvent* events, int nevents, TimeSpec* timeout);
    [DllImport("libc", SetLastError = true)] private static extern int getrlimit(int resource, RLimit* limit);
    [DllImport("libc", SetLastError = true)] private static extern int posix_spawn(int* pid, string path, nint fileActions, nint attributes, nint* argv, nint* envp);
    [DllImport("libc", SetLastError = true)] private static extern int waitpid(int pid, int* status, int options);
    [DllImport("libc")] private static extern void _exit(int status);

    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern uint task_self_trap();
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int task_get_special_port(uint task, int which, out uint port);
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int bootstrap_look_up(uint bootstrap, string name, out uint service);
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/Frameworks/HIServices.framework/HIServices")]
    private static extern int PasteboardCreate(nint name, out nint pasteboard);
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/Frameworks/HIServices.framework/HIServices")]
    private static extern int PasteboardGetItemCount(nint pasteboard, out nuint count);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFStringCreateWithCString(nint allocator, string text, uint encoding);

    // Mach services a compromised worker might use to reach data or the network without a file or socket of its own.
    private static readonly string[] Services =
    [
        "com.apple.pasteboard.1", "com.apple.coreservices.launchservicesd", "com.apple.lsd.mapdb", "com.apple.coreservices.appleevents",
        "com.apple.windowserver.active", "com.apple.dock.server", "com.apple.tccd", "com.apple.system.notification_center",
        "com.apple.distributed_notifications@Uv3", "com.apple.cfprefsd.agent", "com.apple.cfprefsd.daemon", "com.apple.SecurityServer",
        "com.apple.securityd.xpc", "com.apple.dnssd.service", "com.apple.mDNSResponder", "com.apple.metadata.mds", "com.apple.FileCoordination",
        "com.apple.analyticsd", "com.apple.logd", "com.apple.diagnosticd", "com.apple.trustd.agent", "com.apple.nsurlsessiond",
        "com.apple.networkd", "com.apple.usernoted.client", "com.apple.coreservices.quarantine-resolver",
    ];

    [StructLayout(LayoutKind.Sequential)] private struct KEvent { public nuint Ident; public short Filter; public ushort Flags; public uint FilterFlags; public nint Data; public nint UserData; }
    [StructLayout(LayoutKind.Sequential)] private struct TimeSpec { public long Seconds; public long Nanoseconds; }
    [StructLayout(LayoutKind.Sequential)] private struct RLimit { public ulong Current, Maximum; }

    private const short ProcFilter = -5; // EVFILT_PROC
    private const ushort Add = 0x0001, OneShot = 0x0010; // EV_ADD, EV_ONESHOT
    private const uint NoteExit = 0x80000000;

    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex) { Console.Error.WriteLine("probe failure: " + ex); return 70; }
    }

    private static int Run(string[] args)
    {
        switch (args.Length == 0 ? "" : args[0])
        {
            case "info":
                Report(new()
                {
                    ["pid"] = Environment.ProcessId, ["ppid"] = getppid(), ["home"] = Environment.GetEnvironmentVariable("HOME"),
                    ["tmpdir"] = Environment.GetEnvironmentVariable("TMPDIR"), ["container"] = Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID"),
                    ["cwd"] = Environment.CurrentDirectory, ["runtime"] = Environment.Version.ToString(), ["processPath"] = Environment.ProcessPath,
                    ["rlimitNproc"] = Limit(7), ["rlimitCpu"] = Limit(0), ["rlimitCore"] = Limit(4),
                    ["ownAsset"] = Attempt(() => File.ReadAllBytes(typeof(object).Assembly.Location).Length.ToString()),
                    ["sha256"] = Attempt(() => Convert.ToHexString(SHA256.HashData("abc"u8))),
                    ["zlib"] = Attempt(() => { using var m = new MemoryStream(); using (var d = new System.IO.Compression.DeflateStream(m, System.IO.Compression.CompressionLevel.Fastest, true)) d.WriteByte(1); return m.Length.ToString(); }),
                    ["timeZone"] = Attempt(() => TimeZoneInfo.Local.Id),
                });
                return 0;

            case "deny": // deny <file to read> <folder to write in> <tcp port on 127.0.0.1> <real home>
                {
                    string read = args[1], folder = args[2], home = args[4]; int port = int.Parse(args[3]);
                    Report(new()
                    {
                        ["readFixture"] = Attempt(() => File.ReadAllText(read)),
                        ["listHome"] = Attempt(() => Directory.GetFileSystemEntries(home).Length.ToString()),
                        ["listDocuments"] = Attempt(() => Directory.GetFileSystemEntries(Path.Combine(home, "Documents")).Length.ToString()),
                        ["writeFixture"] = Attempt(() => { File.WriteAllText(Path.Combine(folder, "sandbox-write.txt"), "unwanted"); return "written"; }),
                        ["writeTmp"] = Attempt(() => { string p = "/tmp/filecat-sandbox-probe-" + Environment.ProcessId; File.WriteAllText(p, "x"); File.Delete(p); return "written"; }),
                        ["writeContainerTmp"] = Attempt(() => { string p = Path.Combine(Path.GetTempPath(), "probe-" + Environment.ProcessId); File.WriteAllText(p, "x"); File.Delete(p); return "written " + p; }),
                        ["readEtcHosts"] = Attempt(() => File.ReadAllText("/etc/hosts").Length.ToString()),
                        ["connectLoopback"] = Attempt(() => { using var c = new TcpClient(); c.Connect(IPAddress.Loopback, port); return "connected"; }),
                        ["listenLoopback"] = Attempt(() => { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return "listening " + p; }),
                        ["udpSend"] = Attempt(() => { using var u = new UdpClient(); return u.Send(new byte[1], 1, new IPEndPoint(IPAddress.Loopback, port)).ToString(); }),
                        ["dns"] = Attempt(() => Dns.GetHostAddresses("localhost").Length.ToString()),
                    });
                    return 0;
                }

            case "spawn": // whether this process can start another one (posix_spawn and Process.Start)
                {
                    int pid = 0, status = 0;
                    nint path = Marshal.StringToHGlobalAnsi("/usr/bin/true");
                    nint* argv = stackalloc nint[2]; argv[0] = path; argv[1] = 0;
                    nint* envp = stackalloc nint[1]; envp[0] = 0;
                    int spawned = posix_spawn(&pid, "/usr/bin/true", 0, 0, argv, envp);
                    if (spawned == 0) waitpid(pid, &status, 0);
                    Report(new()
                    {
                        ["posixSpawn"] = spawned == 0 ? "started, status " + status : "errno " + spawned,
                        ["processStart"] = Attempt(() => { using var p = Process.Start(new ProcessStartInfo("/usr/bin/true") { UseShellExecute = false })!; p.WaitForExit(); return "started, exit " + p.ExitCode; }),
                        ["rlimitNproc"] = Limit(7),
                    });
                    return 0;
                }

            case "fd": // fd <inherited descriptor>: read it to its end
                {
                    using var stream = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(int.Parse(args[1]), true), FileAccess.Read, 1);
                    Report(new() { ["inheritedBytes"] = Attempt(() => { using var m = new MemoryStream(); stream.CopyTo(m); return m.Length.ToString(); }) });
                    return 0;
                }

            case "echo": // standard input to its SHA-256 on standard output
                {
                    using var input = Console.OpenStandardInput();
                    byte[] digest = SHA256.HashData(input);
                    Console.WriteLine(Convert.ToHexString(digest));
                    return 0;
                }

            case "parent-watch": // register for the parent's exit, report, then exit as soon as it ends
                {
                    int parent = getppid();
                    int kq = kqueue();
                    if (kq < 0) { Console.WriteLine("kqueue errno " + Marshal.GetLastPInvokeError()); return 3; }
                    var change = new KEvent { Ident = (nuint)parent, Filter = ProcFilter, Flags = Add | OneShot, FilterFlags = NoteExit };
                    int registered = kevent(kq, &change, 1, null, 0, null);
                    int error = registered < 0 ? Marshal.GetLastPInvokeError() : 0;
                    Console.WriteLine($"watching parent {parent}: kevent {registered} errno {error} ppid-now {getppid()}");
                    Console.Out.Flush();
                    if (registered < 0) return 4;
                    var fired = new KEvent();
                    int count = kevent(kq, null, 0, &fired, 1, null);
                    // stdout may be gone with the parent; the exit status is what the test observes.
                    _exit(count == 1 && fired.Ident == (nuint)parent ? 42 : 43);
                    return 0;
                }

            case "memory": // memory <MiB>: touch native memory in 16 MiB steps, then wait to be stopped
                {
                    int total = int.Parse(args[1]);
                    var blocks = new List<nint>();
                    for (int done = 0; done < total; done += 16)
                    {
                        nint block = (nint)NativeMemory.Alloc(16u << 20);
                        new Span<byte>((void*)block, 16 << 20).Fill((byte)(done & 0xff | 1));
                        blocks.Add(block);
                        Console.WriteLine("touched " + (done + 16));
                        Console.Out.Flush();
                        Thread.Sleep(20);
                    }
                    Console.WriteLine("allocated"); Console.Out.Flush();
                    Thread.Sleep(30_000);
                    return 9; // never reached when the monitor works
                }

            case "mach": // which mach services this process can look up, the pasteboard and name resolution
                {
                    var facts = new Dictionary<string, object?>();
                    int got = task_get_special_port(task_self_trap(), 4 /* TASK_BOOTSTRAP_PORT */, out uint bootstrap);
                    facts["bootstrapPort"] = got == 0 ? (bootstrap == 0 ? "null" : "present") : "kr " + got;
                    foreach (string name in Services)
                    {
                        int kr = bootstrap_look_up(bootstrap, name, out _);
                        facts[name] = kr == 0 ? "reachable" : "kr " + kr;
                    }
                    facts["pasteboard"] = Attempt(() =>
                    {
                        int created = PasteboardCreate(CFStringCreateWithCString(0, "com.apple.pasteboard.clipboard", 0x08000100), out nint board);
                        if (created != 0) return "create status " + created;
                        int counted = PasteboardGetItemCount(board, out nuint items);
                        return counted == 0 ? "items " + items : "count status " + counted;
                    });
                    facts["resolveExternal"] = Attempt(() => string.Join(",", Dns.GetHostAddresses("example.com").Select(a => a.ToString())));
                    facts["resolveLocalhost"] = Attempt(() => Dns.GetHostAddresses("localhost").Length.ToString());
                    Report(facts);
                    return 0;
                }

            case "exit0":
                return 0;

            default:
                Console.Error.WriteLine("usage: info | deny FILE FOLDER PORT HOME | spawn | fd N | echo | parent-watch | memory MIB | exit0");
                return 2;
        }
    }

    private static string Limit(int resource)
    {
        RLimit limit;
        return getrlimit(resource, &limit) == 0 ? $"{limit.Current}/{limit.Maximum}" : "errno " + Marshal.GetLastPInvokeError();
    }

    private static string Attempt(Func<string> action)
    {
        try { string value = action(); return "ok: " + (value.Length > 80 ? value[..80] : value); }
        catch (Exception ex) { return "refused: " + ex.GetType().Name + ": " + ex.Message; }
    }

    private static void Report(Dictionary<string, object?> facts) =>
        Console.WriteLine(JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));
}
