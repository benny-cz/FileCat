using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FileCat.App.Services;

/// <summary>
/// Starts the macOS picture worker under the OS Seatbelt facility. Hostile decoder input cannot create a descendant or
/// run another program, have launchd or a system service act for it, communicate over the network, write to the
/// filesystem, or read outside the application/runtime/system allowlist.
/// </summary>
internal static partial class MacPictureSandbox
{
    internal const string Executable = "/usr/bin/sandbox-exec";

    private static readonly string[] SystemReadRoots =
    [
        "/System",
        "/Library",
        "/usr",
        "/bin",
        "/sbin",
        "/private/etc",
        "/private/var/db/dyld",
        "/private/var/db/timezone",
    ];

    /// <summary>The devices it reads; never the user's terminals.</summary>
    private static readonly string[] Devices = ["/dev/null", "/dev/zero", "/dev/random", "/dev/urandom"];

    internal static ProcessStartInfo StartInfo(string executable, IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("The macOS picture sandbox is available only on macOS.");
        if (!File.Exists(Executable))
            throw new InvalidOperationException("The macOS picture sandbox is unavailable; FileCat will not decode the picture outside it.");
        if (!Path.IsPathFullyQualified(executable))
            throw new InvalidOperationException("The picture worker executable must have a full path before entering the macOS sandbox.");
        var start = new ProcessStartInfo(Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(ProfileFor(executable));
        start.ArgumentList.Add(executable);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal static string ProfileFor(string executable)
    {
        SortedSet<string> roots = new(StringComparer.Ordinal);
        SortedSet<string> metadataPaths = new(StringComparer.Ordinal);
        foreach (string root in SystemReadRoots) Add(root);
        Add(Path.GetDirectoryName(Path.GetFullPath(executable)));
        Add(AppContext.BaseDirectory);
        Add(Path.GetDirectoryName(typeof(PictureWorker).Assembly.Location));
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        Add(runtime.FullName);
        if (runtime.Parent?.Name == "Microsoft.NETCore.App" && runtime.Parent.Parent?.Name == "shared")
            Add(runtime.Parent.Parent.Parent?.FullName);

        // Seatbelt evaluates the last matching rule. Remove content reads first, then import Apple's installed
        // system baseline so dyld and other OS runtime machinery retain their non-path-filtered read grants. Restore
        // application/runtime paths explicitly, and put the process, network, and write denials last so the imported
        // baseline cannot widen those boundaries.
        StringBuilder profile = new("(version 1)(allow default)(deny file-read*)(import \"system.sb\")");
        foreach (string root in roots)
            profile.Append("(allow file-read* (subpath ").Append(Quote(root)).Append("))");
        foreach (string path in metadataPaths)
            profile.Append("(allow file-read-metadata (literal ").Append(Quote(path)).Append("))");
        foreach (string device in Devices)
            profile.Append("(allow file-read* (literal ").Append(Quote(device)).Append("))");
        profile.Append("(deny file-write*)(deny process-fork)(deny network*)");
        // Nothing another process could do for it (review finding R-H4): no program but the worker itself, which
        // sandbox-exec starts under this profile; no launchd job (launchd asks the sandbox before creating one, however
        // the request is made), no system service lookup, Apple event, signal to another process, I/O Kit device or
        // terminal.
        profile.Append("(deny process-exec*)(allow process-exec (literal ").Append(Quote(RealPath(executable))).Append("))");
        // "(target others)" is not enforced (a probe still signals another process); a signal to itself stays allowed.
        profile.Append("(deny job-creation)(deny mach-lookup)(deny appleevent-send)(deny signal)(allow signal (target self))");
        profile.Append("(deny iokit-open)(deny pseudo-tty)(deny file-read* (regex #\"^/dev/(tty|pty|ptmx)\"))");
        return profile.ToString();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            roots.Add(root);
            string? parent = Path.GetDirectoryName(root);
            while (!string.IsNullOrWhiteSpace(parent))
            {
                metadataPaths.Add(Path.TrimEndingDirectorySeparator(parent));
                parent = Path.GetDirectoryName(parent);
            }
        }
    }

    private static string Quote(string path) => '"' + path.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    /// <summary>The path Seatbelt sees, with every link resolved (/var is /private/var); the full path where it cannot.</summary>
    private static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        if (!OperatingSystem.IsMacOS()) return full;
        nint resolved = RealPathNative(full, 0);
        if (resolved == 0) return full;
        try { return Marshal.PtrToStringUTF8(resolved) ?? full; }
        finally { Free(resolved); }
    }

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RealPathNative(string path, nint resolved);

    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(nint pointer);
}
