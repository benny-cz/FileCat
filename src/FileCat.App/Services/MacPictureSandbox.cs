using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FileCat.App.Services;

/// <summary>
/// Starts the macOS picture worker under the OS Seatbelt facility. Hostile decoder input cannot create a descendant,
/// communicate over the network, write to the filesystem, or read outside the application/runtime/system allowlist.
/// </summary>
internal static class MacPictureSandbox
{
    internal const string Executable = "/usr/bin/sandbox-exec";

    private static readonly string[] SystemReadRoots =
    [
        "/System",
        "/Library",
        "/usr",
        "/bin",
        "/sbin",
        "/dev",
        "/private/etc",
        "/private/var/db/dyld",
        "/private/var/db/timezone",
    ];

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
        profile.Append("(deny file-write*)(deny process-fork)(deny network*)");
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
}
