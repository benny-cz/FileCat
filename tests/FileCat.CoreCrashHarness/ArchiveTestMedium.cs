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
            using var identity = WindowsIdentity.GetCurrent();
            report["userSid"] = identity.User?.Value ?? throw new IOException("The test identity has no user SID.");
            report["before"] = Integrity();
            report["beforeAdministratorsGroup"] = AdministratorsGroupPresent();
            Lower();
            report["after"] = Integrity();
            report["afterAdministratorsGroup"] = AdministratorsGroupPresent();
            if ((int)report["after"]! != 8192) throw new IOException("The test process did not reach medium integrity.");
            if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TEST_REQUIRE_STANDARD_USER") == "1" && (bool)report["afterAdministratorsGroup"]!)
                throw new IOException("Standard-user qualification refuses any Administrators SID, including deny-only membership.");
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

    internal static int ProbeTokenGroups(string output)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(output)) return 70;
        if (!OpenProcessToken(new nint(-1), 0xa /* QUERY | DUPLICATE */, out nint token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint sid = 0, restricted = 0;
        try
        {
            if (!AdministratorsGroupPresentInToken(token)) throw new IOException("The deny-only control requires an actual administrator token.");
            var administrator = new SecurityIdentifier("S-1-5-32-544"); byte[] bytes = new byte[administrator.BinaryLength]; administrator.GetBinaryForm(bytes, 0);
            sid = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, sid, bytes.Length);
            var disabled = new SidAndAttributes { Sid = sid };
            if (!CreateRestrictedToken(token, 0, 1, ref disabled, 0, 0, 0, 0, out restricted)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using var identity = new WindowsIdentity(restricted);
            bool raw = AdministratorsGroupPresentInToken(restricted);
            bool enabled = (identity.Groups ?? throw new IOException("No enabled-group observation.")).Contains(administrator);
            bool role = new WindowsPrincipal(identity).IsInRole(administrator);
            if (!raw || enabled || role) throw new IOException("The actual deny-only control did not distinguish raw membership from enabled membership.");
            using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new { userSid = identity.User!.Value, rawAdministrators = raw, enabledAdministrators = enabled, administratorRole = role });
            stream.Flush(flushToDisk: true); return 0;
        }
        finally { if (restricted != 0) CloseHandle(restricted); if (sid != 0) Marshal.FreeHGlobal(sid); CloseHandle(token); }
    }

    [SupportedOSPlatform("windows")]
    private static bool AdministratorsGroupPresent()
    {
        if (!OpenProcessToken(new nint(-1), 8, out nint token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { return AdministratorsGroupPresentInToken(token); }
        finally { CloseHandle(token); }
    }

    [SupportedOSPlatform("windows")]
    private static bool AdministratorsGroupPresentInToken(nint token)
    {
        // WindowsIdentity.Groups and IsInRole omit deny-only groups. Inspect every native entry without filtering
        // attributes, so a filtered administrator is never accepted as an ordinary standard account.
        GetTokenInformation(token, 2 /* TokenGroups */, 0, 0, out int length);
        if (length < sizeof(uint) || length > 1024 * 1024) throw new IOException("Invalid token group data.");
        nint buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, 2, buffer, length, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            uint count = unchecked((uint)Marshal.ReadInt32(buffer));
            int offset = Marshal.OffsetOf<TokenGroupsHeader>(nameof(TokenGroupsHeader.First)).ToInt32();
            int stride = Marshal.SizeOf<SidAndAttributes>();
            if (count > 4096 || count != 0 && offset + (long)count * stride > length) throw new IOException("Invalid token group count.");
            for (int i = 0; i < count; i++)
            {
                var group = Marshal.PtrToStructure<SidAndAttributes>(buffer + offset + i * stride);
                long displacement = group.Sid.ToInt64() - buffer.ToInt64();
                if (displacement < 0 || displacement > length - 8 || Marshal.ReadByte(group.Sid) != 1)
                    throw new IOException("Invalid token group SID.");
                int subAuthorities = Marshal.ReadByte(group.Sid, 1);
                if (subAuthorities > 15 || 8 + 4 * subAuthorities > length - displacement) throw new IOException("Invalid token group SID length.");
                if (new SecurityIdentifier(group.Sid).Value == "S-1-5-32-544") return true;
            }
            return false;
        }
        finally { Marshal.FreeHGlobal(buffer); }
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
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenGroupsHeader { public uint Count; public SidAndAttributes First; }
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateRestrictedToken(nint token, uint flags, uint disableCount, ref SidAndAttributes disable,
        uint deletePrivilegeCount, nint deletePrivileges, uint restrictCount, nint restrict, out nint restricted);
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
