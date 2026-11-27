using System.ComponentModel;
using System.Diagnostics;

namespace FileCat.App;

/// <summary>
/// I689: FileCat's own workers that write nothing belong to the FileCat that started them, not to another FileCat. A
/// Windows FileCat starts its Cloud Files query worker (<c>FileCat --storage-cloud-query</c>) with its first storage
/// check, which an ordinary window makes within a second of opening, and keeps it for as long as it runs; it starts a
/// picture worker for each picture it decodes. The recovery census counted them as other FileCat processes, so every
/// disk scan from an ordinary window was refused, and the person was asked to close FileCat windows that did not exist.
/// </summary>
public static partial class SingleInstance
{
    private const string OwnWorker = "own worker";

    /// <summary>
    /// Whether a process the census identified as FileCat is a worker that writes nothing, started by this FileCat or by
    /// the FileCat that asked for this census (<paramref name="requester"/>, the administrator helper's census). Anything
    /// Windows does not say (an unreadable parent, start time or command line) leaves the process counted.
    /// </summary>
    private static bool IsOwnReadOnlyWorker(Process process, int? requester)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var launch = Platform.Windows.ProcessIdentity.Launch(process.Id);
        if (launch.Parent is not { } parent || parent != Environment.ProcessId && parent != requester) return false;
        return OwnedReadOnlyWorker(StartedUtc(process.Id), StartedUtc(parent), Platform.Windows.ProcessIdentity.Arguments(launch.CommandLine));
    }

    private static DateTime? StartedUtc(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// A worker started after the process that now has its recorded parent's ID, with exactly one role that writes
    /// nothing. A process started before it was started by an earlier process with that ID, which has ended since.
    /// </summary>
    internal static bool OwnedReadOnlyWorker(DateTime? started, DateTime? ownerStarted, string[]? arguments) =>
        started is { } worker && ownerStarted is { } owner && worker > owner && arguments is not null && ReadOnlyWorkerRole(arguments);

    /// <summary>
    /// The roles in which FileCat writes nothing: the Cloud Files query worker, which only asks Windows whether the files
    /// it is handed belong to a sync root, and the picture worker, which decodes its input in a sandbox. The arguments are
    /// as Windows splits them: the program, for a framework-dependent FileCat its entry assembly, then the role and its own
    /// arguments, and nothing else. Archive workers write archives and the context-menu helper runs Shell commands: they
    /// still count, as does any FileCat started without exactly these arguments.
    /// </summary>
    internal static bool ReadOnlyWorkerRole(string[] arguments)
    {
        int role = arguments.Length > 1 && FileName(arguments[0]) is var host &&
                   (host.Equals("dotnet", StringComparison.OrdinalIgnoreCase) || host.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) &&
                   FileName(arguments[1]).Equals("FileCat.dll", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        return arguments.AsSpan(Math.Min(role, arguments.Length)) switch
        {
            [Platform.Windows.WindowsStorageCloudQuery.WorkerArgument] => true,
            [Services.PictureWorker.Argument, var side] => Number(side),
            [Services.PictureWorker.Argument, var side, Services.PictureWorker.PdfArgument, var page] => Number(side) && Number(page),
            _ => false,
        };

        // Windows paths, whatever this process's platform: tests check them everywhere.
        static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
        static bool Number(string text) => text.Length is > 0 and <= 9 && text.All(char.IsAsciiDigit);
    }
}
