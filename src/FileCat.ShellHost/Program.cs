using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Platform.Windows.Shell;

namespace FileCat.ShellHost;

/// <summary>
/// Answers FileCat's requests for Shell thumbnails and per-file icons, one at a time, over standard input and output.
/// Third-party handlers load here instead of in FileCat; if one hangs or crashes, FileCat ends this process and starts
/// another. The process restricts itself further at startup: no DLLs from network paths or low-integrity files, no
/// legacy extension points (AppInit DLLs, hooks), no fonts outside the system's, and no error dialogs.
/// </summary>
internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length == 0 || args[0] != ShellHostProtocol.ServeArgument) return 2;
        bool testFaults = args.Contains(ShellHostProtocol.TestFaultsArgument);
        Mitigations.Apply();
        using var input = new BinaryReader(Console.OpenStandardInput());
        using var output = new BinaryWriter(Console.OpenStandardOutput());
        // The Shell's own start (COM, its image machinery, the icon cache) is paid here, within the start's time limit,
        // not by the first picture asked for: on a slow computer that outlasted an icon's limit, and the item was then
        // never asked for again (CI, Windows ARM64). A system program's icon is the least a Shell can be asked.
        try { ShellImages.Get(Path.Combine(Environment.SystemDirectory, "cmd.exe"), ShellImageKind.Icon, 16, out _); }
        catch (Exception ex) when (ex is not IOException) { }
        ShellHostProtocol.WriteStatus(output, ShellHostProtocol.Status.Text, ShellHostProtocol.ReadyMessage);
        while (ShellHostProtocol.TryReadRequest(input, out byte kind, out int size, out string path))
        {
            if (testFaults && kind >= ShellHostProtocol.HangRequest)
            {
                TestRequest(kind, path, output);
                continue;
            }
            try
            {
                if (kind is not ((byte)ShellImageKind.Thumbnail or (byte)ShellImageKind.Icon or (byte)ShellImageKind.IconResource or (byte)ShellImageKind.OverlayIcon)) return 3;
                var image = kind == (byte)ShellImageKind.IconResource
                    ? ShellImages.GetResource(path, size, out string? problem)
                    : kind == (byte)ShellImageKind.OverlayIcon
                    ? ShellImages.GetOverlayIcon(path, size, out problem)
                    : ShellImages.Get(path, (ShellImageKind)kind, size, out problem);
                if (image is not null) ShellHostProtocol.WriteImage(output, image);
                else ShellHostProtocol.WriteStatus(output, problem is null ? ShellHostProtocol.Status.None : ShellHostProtocol.Status.Failed, problem);
            }
            catch (Exception ex) when (ex is not IOException)
            {
                ShellHostProtocol.WriteStatus(output, ShellHostProtocol.Status.Failed, ex.GetType().Name + ": " + ex.Message);
            }
        }
        return 0;
    }

    /// <summary>Faults and probes for TV-16 tests; ignored unless FileCat started the helper for tests.</summary>
    private static void TestRequest(byte kind, string argument, BinaryWriter output)
    {
        switch (kind)
        {
            case ShellHostProtocol.HangRequest:
                Thread.Sleep(Timeout.Infinite);
                break;
            case ShellHostProtocol.CrashRequest:
                Environment.Exit(99);
                break;
            case ShellHostProtocol.SpawnRequest:
                string spawned;
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit 0") { UseShellExecute = false, CreateNoWindow = true });
                    spawned = p is null ? "refused" : "started";
                }
                catch (Exception ex)
                {
                    spawned = "refused: " + ex.Message;
                }
                ShellHostProtocol.WriteStatus(output, ShellHostProtocol.Status.Text, spawned);
                break;
            case ShellHostProtocol.IntegrityRequest:
                ShellHostProtocol.WriteStatus(output, ShellHostProtocol.Status.Text, IntegrityLevel());
                break;
            case ShellHostProtocol.WriteProbeRequest:
                string written;
                try
                {
                    File.WriteAllText(argument, "probe");
                    written = "written";
                }
                catch (Exception ex)
                {
                    written = "refused: " + ex.GetType().Name;
                }
                ShellHostProtocol.WriteStatus(output, ShellHostProtocol.Status.Text, written);
                break;
        }
    }

    private static unsafe string IntegrityLevel()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x8 /* TOKEN_QUERY */, out var token)) return "unknown";
        try
        {
            var buffer = stackalloc byte[256];
            if (!GetTokenInformation(token, 25 /* TokenIntegrityLevel */, buffer, 256, out _)) return "unknown";
            nint sid = *(nint*)buffer; // TOKEN_MANDATORY_LABEL.Label.Sid
            byte count = *GetSidSubAuthorityCount(sid);
            uint rid = *GetSidSubAuthority(sid, (uint)(count - 1));
            return rid switch { 0x1000 => "low", 0x2000 => "medium", 0x3000 => "high", 0x4000 => "system", _ => $"0x{rid:X}" };
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, void* tokenInformation, uint length, out uint returnLength);

    [LibraryImport("advapi32.dll")]
    private static unsafe partial byte* GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    private static unsafe partial uint* GetSidSubAuthority(nint sid, uint subAuthority);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}

/// <summary>Process mitigations the helper applies to itself before any handler loads.</summary>
internal static unsafe partial class Mitigations
{
    private const int ProcessExtensionPointDisablePolicy = 6, ProcessFontDisablePolicy = 9, ProcessImageLoadPolicy = 10;

    public static void Apply()
    {
        SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
        uint imageLoad = 0x1 /* NoRemoteImages */ | 0x2 /* NoLowMandatoryLabelImages */ | 0x4 /* PreferSystem32Images */;
        SetProcessMitigationPolicy(ProcessImageLoadPolicy, &imageLoad, sizeof(uint));
        uint extensionPoints = 0x1; // DisableExtensionPoints
        SetProcessMitigationPolicy(ProcessExtensionPointDisablePolicy, &extensionPoints, sizeof(uint));
        uint fonts = 0x1; // DisableNonSystemFonts
        SetProcessMitigationPolicy(ProcessFontDisablePolicy, &fonts, sizeof(uint));
    }

    private const uint SEM_FAILCRITICALERRORS = 0x1, SEM_NOGPFAULTERRORBOX = 0x2, SEM_NOOPENFILEERRORBOX = 0x8000;

    [LibraryImport("kernel32.dll")]
    private static partial uint SetErrorMode(uint uMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessMitigationPolicy(int mitigationPolicy, void* lpBuffer, nuint dwLength);
}
