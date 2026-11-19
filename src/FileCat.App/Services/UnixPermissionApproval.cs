using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.App.Services;

/// <summary>A one-shot helper for mode changes on explicitly reviewed Unix files and directories.</summary>
internal static partial class UnixPermissionApproval
{
    internal const string Argument = "--filecat-apply-unix-permissions";
    private const int MaximumBytes = 32 * 1024, MaximumItems = 128;
    private const uint Editable = 0x3ff; // rwx and sticky; set-ID may only be preserved
    internal sealed record Entry(string Path, UnixStat Before, uint Mode);
    internal sealed record Result(int Changed, string? Problem);

    internal static bool CanAsk => (OperatingSystem.IsLinux() && File.Exists("/usr/bin/pkexec") ||
        OperatingSystem.IsMacOS() && File.Exists("/usr/bin/osascript")) && !Environment.IsPrivilegedProcess;

    internal static Entry[] Capture(IEnumerable<string> paths, UnixFileMode set, UnixFileMode clear)
    {
        var selected = paths.Distinct(StringComparer.Ordinal).Take(MaximumItems + 1).ToArray();
        if (selected.Length > MaximumItems)
            throw new NotSupportedException("Select at most 128 files or folders for administrator approval.");
        var reviewed = new List<Entry>();
        foreach (string original in selected)
        {
            if (UnixFiles.Stat(original) is not { HasChangeTime: true } stat || !Ordinary(stat) ||
                UnixFiles.RealPath(Path.GetDirectoryName(original) ?? "") is not { } parent)
                throw new IOException($"The identity of {original} could not be verified as a regular file or directory.");
            string path = Path.Combine(parent, Path.GetFileName(original));
            if (!Matches(UnixFiles.Stat(path), stat)) throw new IOException($"The selected item changed: {original}.");
            uint mode = (uint)UnixPermissions.Apply((UnixFileMode)stat.Mode, set, clear, stat.IsDirectory, inside: false);
            if (((mode ^ stat.Mode) & ~Editable) != 0)
                throw new NotSupportedException("Administrator approval covers read, write, execute and sticky permissions.");
            if (mode != stat.Mode) reviewed.Add(new(path, stat, mode));
        }
        // Validate the request before bringing up an operating-system dialog.
        _ = Encode(reviewed.ToArray());
        return reviewed.ToArray();
    }

    internal static string Encode(Entry[] entries)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(entries);
        if (entries.Length > MaximumItems || bytes.Length > MaximumBytes)
            throw new NotSupportedException("Select a smaller group of files or folders for administrator approval.");
        return Convert.ToBase64String(bytes);
    }

    internal static int RunHelper(string[] args)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || GetEffectiveUser() != 0 ||
            args is not [Argument, var encoded] || encoded.Length > (MaximumBytes + 2) / 3 * 4) return 2;
        try
        {
            byte[] data = Convert.FromBase64String(encoded);
            if (data.Length > MaximumBytes || JsonSerializer.Deserialize<Entry[]>(data) is not { Length: > 0 and <= MaximumItems } entries)
                return 2;
            Console.WriteLine(JsonSerializer.Serialize(Apply(entries)));
            return 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException) { return 2; }
    }

    private static Result Apply(Entry[] entries)
    {
        int changed = 0;
        var handles = new List<SafeFileHandle>(entries.Length);
        try
        {
            var seen = new HashSet<(ulong Device, ulong Inode)>();
            foreach (var entry in entries)
            {
                if (entry is null || !Ordinary(entry.Before) || !entry.Before.HasChangeTime || entry.Mode > 0xfff ||
                    ((entry.Mode ^ entry.Before.Mode) & ~Editable) != 0 ||
                    !seen.Add((entry.Before.Device, entry.Before.Inode)))
                    throw new IOException("The administrator permission plan is invalid or names the same item twice.");
                var handle = OpenVerifiedPath(entry.Path);
                handles.Add(handle);
                if (!Matches(UnixFiles.Stat(handle), entry.Before))
                    throw new IOException($"The selected item changed: {entry.Path}.");
            }
            for (int i = 0; i < handles.Count; i++)
            {
                if (!Matches(UnixFiles.Stat(handles[i]), entries[i].Before))
                    throw new IOException($"The selected item changed: {entries[i].Path}.");
                if (ChangeMode(handles[i], entries[i].Mode) != 0)
                    throw new IOException($"Could not change {entries[i].Path}: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())}");
                changed++;
                if (UnixFiles.Stat(handles[i])?.Mode != entries[i].Mode)
                    throw new IOException($"The file system did not retain the requested mode on {entries[i].Path}.");
            }
            return new(changed, null);
        }
        catch (IOException ex) { return new(changed, ex.Message); }
        finally { foreach (var handle in handles) handle.Dispose(); }
    }

    private static bool Ordinary(UnixStat item) => item.Inode != 0 && !item.IsLink && (item.IsDirectory || item.IsRegular);

    private static bool Matches(UnixStat? current, UnixStat before) => current is { } stat && Ordinary(stat) &&
        stat.Device == before.Device && stat.Inode == before.Inode && stat.Owner == before.Owner &&
        stat.Mode == before.Mode && stat.IsDirectory == before.IsDirectory &&
        stat.HasChangeTime && stat.ChangeSeconds == before.ChangeSeconds && stat.ChangeNanoseconds == before.ChangeNanoseconds;

    private static SafeFileHandle OpenVerifiedPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || PathUtil.HasAmbiguousUnixName(path) || path.Contains('\0'))
            throw new IOException("The path is not an unambiguous Unix path.");
        var parts = path[1..].Split('/');
        if (parts.Any(part => part is "" or "." or ".."))
            throw new IOException("The path is not a canonical absolute path.");
        bool mac = OperatingSystem.IsMacOS();
        int noFollow = mac ? 0x100 : 0x20000, nonBlock = mac ? 4 : 0x800;
        int directory = mac ? 0x100000 : 0x10000, closeOnExec = mac ? 0x1000000 : 0x80000;
        int flags = noFollow | nonBlock | closeOnExec;
        var cursor = new SafeFileHandle((nint)Open("/", flags | directory), ownsHandle: true);
        try
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (cursor.IsInvalid) throw new IOException($"Could not open {path} without following links.");
                var next = new SafeFileHandle((nint)OpenAt(cursor, parts[i], flags | (i < parts.Length - 1 ? directory : 0)),
                    ownsHandle: true);
                cursor.Dispose();
                cursor = next;
            }
            if (cursor.IsInvalid) throw new IOException($"Could not open {path} without following links.");
            return cursor;
        }
        catch { cursor.Dispose(); throw; }
    }

    internal static ProcessStartInfo StartInfo(string request)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("The FileCat executable could not be identified.");
        // AppImage's running executable is on the ordinary user's FUSE mount. Start a new mount as root.
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPDIR") is { Length: > 0 } appDir &&
            Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } image &&
            Path.IsPathFullyQualified(image) && PathUtil.IsSameOrUnder(executable, appDir)) executable = image;
        var arguments = new List<string> { executable };
        if (Path.GetFileName(executable) == "dotnet")
            arguments.Add(typeof(Program).Assembly.Location);
        arguments.Add(Argument);
        arguments.Add(request);
        var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/osascript" : "/usr/bin/pkexec")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = "/",
        };
        if (OperatingSystem.IsMacOS())
        {
            foreach (string line in new[] { "on run argv", "set invocation to \"exec\"",
                "repeat with arg in argv", "set invocation to invocation & \" \" & quoted form of (contents of arg)",
                "end repeat", "return do shell script invocation with administrator privileges", "end run" })
            {
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add(line);
            }
            start.ArgumentList.Add("--");
        }
        else start.ArgumentList.Add("--disable-internal-agent");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task<Result> RequestAsync(Entry[] entries)
    {
        using var process = Process.Start(StartInfo(Encode(entries))) ?? throw new IOException("The system approval process could not start.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode == 126 || (await errors.ConfigureAwait(false)).Contains("(-128)", StringComparison.Ordinal))
            throw new OperationCanceledException();
        if (process.ExitCode != 0)
            throw new IOException("The system did not authorize the permission change, or the FileCat helper could not start.");
        return JsonSerializer.Deserialize<Result>(await output.ConfigureAwait(false))
            ?? throw new IOException("The permission helper returned no result.");
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUser();
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string path, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenAt(SafeFileHandle directory, string leaf, int flags);
    [LibraryImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static partial int ChangeMode(SafeFileHandle file, uint mode);
}
