using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;

/// <summary>Test-only VMware/CI launcher: verify medium integrity and launch a hidden child with explicit redirected IO.</summary>
internal static class ArchiveTestMedium
{
    internal static async Task<int> Run(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length < 3 || !Path.IsPathFullyQualified(args[0]) || !Path.IsPathFullyQualified(args[1])) return 70;
        string logPrefix = args[0];
        Directory.CreateDirectory(Path.GetDirectoryName(logPrefix)!);
        using var facts = new FileStream(logPrefix + ".facts.json", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var report = new Dictionary<string, object?> { ["parentPid"] = Environment.ProcessId, ["runtime"] = Environment.Version.ToString() };
        int code = 71;
        try
        {
            report["before"] = Integrity();
            Lower();
            report["after"] = Integrity();
            if ((int)report["after"]! != 8192) throw new IOException("The test process did not reach medium integrity.");
            var start = new ProcessStartInfo(args[1])
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string arg in args[2..]) start.ArgumentList.Add(arg);
            await using var output = new FileStream(logPrefix + ".out.log", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using var error = new FileStream(logPrefix + ".err.log", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var child = Process.Start(start) ?? throw new IOException("The native test child did not start.");
            report["childPid"] = child.Id;
            try
            {
                Task stdout = child.StandardOutput.BaseStream.CopyToAsync(output);
                Task stderr = child.StandardError.BaseStream.CopyToAsync(error);
                await child.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                code = child.ExitCode;
                report["exitCode"] = code;
            }
            finally
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            report["error"] = ex.ToString();
            report["exitCode"] = code;
        }
        finally
        {
            JsonSerializer.Serialize(facts, report);
            facts.Flush(flushToDisk: true);
        }
        return code;
    }

    [SupportedOSPlatform("windows")]
    private static int Integrity()
    {
        if (!OpenProcessToken(new nint(-1), 8, out nint token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            GetTokenInformation(token, 25, 0, 0, out int length);
            if (length < IntPtr.Size || length > 4096) throw new IOException("Invalid token integrity data.");
            nint buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, length, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                string sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
                return int.Parse(sid["S-1-16-".Length..], System.Globalization.CultureInfo.InvariantCulture);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }

    [SupportedOSPlatform("windows")]
    private static void Lower()
    {
        if (Integrity() == 8192) return;
        if (!OpenProcessToken(new nint(-1), 0x88, out nint token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var sid = new SecurityIdentifier("S-1-16-8192");
            byte[] bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            int header = Marshal.SizeOf<SidAndAttributes>();
            nint label = Marshal.AllocHGlobal(header + bytes.Length);
            try
            {
                nint sidData = label + header;
                Marshal.Copy(bytes, 0, sidData, bytes.Length);
                Marshal.StructureToPtr(new SidAndAttributes { Sid = sidData, Attributes = 0x20 }, label, false);
                if (!SetTokenInformation(token, 25, label, header + bytes.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { Marshal.FreeHGlobal(label); }
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes { public nint Sid; public uint Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int kind, nint data, int length, out int needed);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(nint token, int kind, nint data, int length);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
