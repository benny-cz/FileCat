using FileCat.Core.FileSystem;

namespace FileCat.Core.Records;

/// <summary>Pure filesystem-namespace validation. Stored/display names are never passed to a shell.</summary>
public static class FileDetailsTargets
{
    public static bool IsOrdinaryNavigationTarget(FileDetailsSnapshot snapshot, bool windows) =>
        (snapshot.Kind is FileObjectKind.File or FileObjectKind.Directory) &&
        (!windows || snapshot.ReparseTag.State == FactState.NotApplicable);

    public static bool TryResolve(FileDetailsSnapshot snapshot, bool windows, out string path, out bool network)
    {
        path = ""; network = false;
        if (snapshot.Target is not { State: FactState.Known, Value: { } target }) return false;
        if (windows && snapshot.ReparseTag.Value is not (ReparseLinks.SymbolicLinkTag or ReparseLinks.MountPointTag)) return false;
        if (!windows && snapshot.Kind != FileObjectKind.SymbolicLink) return false;
        string stored = target.SubstituteName;
        if (stored.Length == 0 || stored.Contains('\0')) return false;
        if (!windows)
        {
            if (PathUtil.HasAmbiguousUnixName(stored) || target.Relative == Path.IsPathRooted(stored)) return false;
            // Keep dot components until the kernel resolves the target's parent. Lexical normalization
            // here would give .. the wrong meaning if an earlier component is a symlink.
            path = target.Relative ? Path.Combine(Path.GetDirectoryName(snapshot.Path) ?? snapshot.Path, stored) : stored;
            return Path.IsPathFullyQualified(path);
        }
        if (stored.StartsWith(@"\??\UNC\", StringComparison.OrdinalIgnoreCase)) stored = @"\\" + stored[8..];
        else if (stored.StartsWith(@"\??\", StringComparison.Ordinal)) stored = stored[4..];
        if (stored.StartsWith(@"\\?\", StringComparison.Ordinal)) stored = stored[4..];
        bool drive = stored.Length >= 3 && char.IsAsciiLetter(stored[0]) && stored[1] == ':' && stored[2] == '\\';
        bool unc = stored.StartsWith(@"\\", StringComparison.Ordinal) && !stored.StartsWith(@"\\.\", StringComparison.Ordinal) && !stored.StartsWith(@"\\?\", StringComparison.Ordinal);
        bool volume = stored.Length >= 45 && stored.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)
            && stored[43] == '}' && stored[44] == '\\' && Guid.TryParse(stored.AsSpan(6, 38), out _);
        if (target.Relative)
        {
            if (stored.StartsWith('\\') || stored.StartsWith('/') || stored.Contains(':')) return false;
        }
        else if (!drive && !unc && !volume) return false;
        if (stored.AsSpan(drive ? 2 : 0).Contains(':')) return false;
        if (unc)
        {
            var parts = stored[2..].Split('\\');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
            if (parts[1].Equals("pipe", StringComparison.OrdinalIgnoreCase)) return false;
            network = true;
        }
        if (volume) stored = @"\\?\" + stored;
        try
        {
            path = Path.GetFullPath(stored, Path.GetDirectoryName(snapshot.Path) ?? snapshot.Path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Canonicalize only the directories needed for an explicit Unix navigation, keeping the final object unopened.</summary>
    public static (string Path, bool RequiresDirectory) ResolveUnixNavigationPath(string path, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows() || !path.StartsWith('/')) throw new NotSupportedException("This action needs an absolute Unix path.");
        ct.ThrowIfCancellationRequested();
        string trimmed = path.TrimEnd('/');
        if (trimmed.Length == 0) return ("/", true);
        int separator = trimmed.LastIndexOf('/');
        string leaf = trimmed[(separator + 1)..];
        bool directoryOperand = leaf is "." or "..";
        string directory = directoryOperand ? trimmed : separator == 0 ? "/" : trimmed[..separator];
        string? canonical = UnixFiles.RealPath(directory, out int error);
        ct.ThrowIfCancellationRequested();
        if (canonical is null) throw error switch
        {
            2 or 20 => new DirectoryNotFoundException("A component of the target folder does not exist or is not a directory."),
            1 or 13 => new UnauthorizedAccessException("The target folder could not be reached with this access."),
            _ => new IOException("The target folder could not be resolved."),
        };
        return (directoryOperand ? canonical : Path.Join(canonical, leaf), directoryOperand || path.EndsWith('/'));
    }
}
