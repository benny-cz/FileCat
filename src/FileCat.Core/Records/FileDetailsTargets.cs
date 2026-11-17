using FileCat.Core.FileSystem;

namespace FileCat.Core.Records;

/// <summary>Pure filesystem-namespace validation. Stored/display names are never passed to a shell.</summary>
public static class FileDetailsTargets
{
    public static bool TryResolve(FileDetailsSnapshot snapshot, bool windows, out string path, out bool network)
    {
        path = ""; network = false;
        if (snapshot.Target is not { State: FactState.Known, Value: { } target }) return false;
        if (windows && snapshot.ReparseTag.Value is not (ReparseLinks.SymbolicLinkTag or ReparseLinks.MountPointTag)) return false;
        if (!windows && snapshot.Kind != FileObjectKind.SymbolicLink) return false;
        string stored = target.SubstituteName;
        if (stored.Length == 0 || stored.Contains('\0')) return false;
        if (windows)
        {
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
        }
        try
        {
            path = Path.GetFullPath(stored, Path.GetDirectoryName(snapshot.Path) ?? snapshot.Path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
