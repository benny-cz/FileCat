using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.State;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.Services;

/// <summary>Runtime FileCat artifact boundaries. Captured inventories and recipes never supply these locations.</summary>
internal sealed class AnalysisProtectedObjects(AppPaths paths, DeviceIoScheduler io)
{
    private const int MaximumDirectories = 64;

    internal static bool IsSystemManagedLocation(string location)
    {
        if (OperatingSystem.IsWindows())
        {
            string drive = Path.GetPathRoot(location)!;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return windows.Length != 0 && PathUtil.IsSameOrUnder(location, windows) ||
                PathUtil.IsSameOrUnder(location, Path.Combine(drive, "$Recycle.Bin")) ||
                PathUtil.IsSameOrUnder(location, Path.Combine(drive, "System Volume Information"));
        }
        if (OperatingSystem.IsMacOS())
            return PathUtil.IsSameOrUnder(location, "/System") && !PathUtil.IsSameOrUnder(location, "/System/Volumes/Data") ||
                PathUtil.IsSameOrUnder(location, "/private/var/db");
        return new[] { "/boot", "/dev", "/etc", "/proc", "/sys", "/run" }.Any(root => PathUtil.IsSameOrUnder(location, root));
    }

    internal async Task<IReadOnlySet<string>> CaptureDirectoryIdentitiesAsync(CancellationToken cancellation)
    {
        var protectedPaths = paths.WriteFolders.Select(item => item.Folder).Append(AppContext.BaseDirectory)
            .Concat(paths.DataRoot is { } dataRoot ? [dataRoot] : Array.Empty<string>())
            .Select(Path.GetFullPath).Distinct(PathUtil.SafetyComparer).ToArray();
        if (protectedPaths.Length > MaximumDirectories)
            throw new NotSupportedException("The FileCat artifact boundary exceeds its limit.");

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (string protectedPath in protectedPaths)
        {
            bool optionalScratch = paths.AnalysisScratchDirectory != paths.DefaultAnalysisScratchDirectory &&
                PathUtil.SafetyComparer.Equals(protectedPath, paths.AnalysisScratchDirectory);
            string? identity = await AnalysisIo.Run(io, PathUtil.GetDeviceKey(protectedPath),
                token => Capture(protectedPath, token, optionalScratch), cancellation).ConfigureAwait(false);
            if (identity is not null) identities.Add(identity);
        }
        return identities;
    }

    private static string? Capture(string path, CancellationToken cancellation, bool optionalScratch)
    {
        for (int depth = 0; depth <= AnalysisLimits.Depth; depth++)
        {
            cancellation.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            {
                if (optionalScratch) return null;
                path = Parent(path); continue;
            }

            // A FileCat store may deliberately redirect outside its enclosing data root.
            // Missing stores can use an existing parent. A present redirection needs its own binding.
            bool redirected = attributes.HasFlag(FileAttributes.ReparsePoint);
            if (!redirected && !attributes.HasFlag(FileAttributes.Directory))
            {
                path = Parent(path);
                continue;
            }

            string target = redirected ? LinkTarget(path) : path;
            IAnalysisBackend directory = OperatingSystem.IsWindows() ? new WindowsAnalysisBackend(target) : new UnixAnalysisBackend(target);
            try
            {
                if (directory.Root.Assurance != IdentityAssurance.Verified ||
                    directory.RootEntry is not { Kind: AnalysisKind.Directory, Cloud: not true, Identity: { } identity })
                    throw new IOException("A protected FileCat directory cannot be verified.");
                directory.ValidateReadScope(directory.RootEntry);
                if (redirected && !PathUtil.SafetyComparer.Equals(target, LinkTarget(path)))
                    throw new AnalysisSourceChangedException();
                return identity;
            }
            finally { AnalysisIo.CloseOnWorker(directory); }
        }
        throw new IOException("A protected FileCat directory exceeds the ancestry limit.");
    }

    private static string Parent(string path) =>
        Path.GetDirectoryName(path) ?? throw new IOException("A protected FileCat directory is unavailable.");

    private static string LinkTarget(string path)
    {
        string? target = Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
        if (target is null || !Path.IsPathFullyQualified(target))
            throw new IOException("A redirected FileCat directory cannot be verified.");
        return target;
    }
}
