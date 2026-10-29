namespace FileCat.Archives.Rar;

/// <summary>
/// The volumes of a RAR set, found beside the path the user opened: FileCat's RAR reader opens them all, and the archive
/// provider checks that the set is still the same before it reuses a catalog.
/// </summary>
internal static class RarVolumes
{
    /// <summary>"x.part1.rar" (or any part) → all parts in order; "x.rar" with "x.r00"… → the old naming.</summary>
    public static List<string> Of(string path) => Of(path, out _);

    public static List<string> Of(string path, out bool numberedVolumeGap)
    {
        numberedVolumeGap = false;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileName(path);
        var modern = System.Text.RegularExpressions.Regex.Match(name, @"^(.*)\.part(\d+)\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (modern.Success)
        {
            var stem = modern.Groups[1].Value;
            var parts = Directory.EnumerateFiles(dir, stem + ".part*.rar")
                .Select(p => (Path: p, M: System.Text.RegularExpressions.Regex.Match(Path.GetFileName(p), @"\.part(\d+)\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                .Where(x => x.M.Success && Path.GetFileName(x.Path).StartsWith(stem + ".part", StringComparison.OrdinalIgnoreCase))
                .Select(x => (x.Path, Number: int.Parse(x.M.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
                .OrderBy(x => x.Number).ToList();
            // Split-before/after flags describe each member's ends, so the dependency can call a member complete
            // even when an interior numbered volume is absent. Keep that discovery warning independently.
            for (int i = 1; i < parts.Count; i++)
                if (parts[i].Number - parts[i - 1].Number > 1) numberedVolumeGap = true;
            return parts.Count > 0 ? parts.Select(x => x.Path).ToList() : [path];
        }
        var extension = Path.GetExtension(name);
        bool secondary = System.Text.RegularExpressions.Regex.IsMatch(extension, @"^\.[r-z]\d{2}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var baseName = Path.GetFileNameWithoutExtension(name);
        var old = new List<(string Path, int Number)>();
        if (!secondary) old.Add((path, 0));
        foreach (string candidate in Directory.EnumerateFiles(dir))
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(candidate), baseName, StringComparison.OrdinalIgnoreCase)) continue;
            string suffix = Path.GetExtension(candidate).ToLowerInvariant();
            if (secondary && suffix == ".rar") old.Add((candidate, 0));
            else if (System.Text.RegularExpressions.Regex.IsMatch(suffix, @"^\.[r-z]\d{2}$"))
                old.Add((candidate, 1 + (suffix[1] - 'r') * 100 + (suffix[2] - '0') * 10 + suffix[3] - '0'));
        }
        old.Sort((a, b) => a.Number.CompareTo(b.Number));
        // A secondary entry point needs the primary volume first, once. Retain later volumes after a gap,
        // with the same explicit discovery warning as numbered sets; stopping at a gap loses intact members.
        if (old.Count > 0 && old[0].Number != 0) numberedVolumeGap = true;
        for (int i = 1; i < old.Count; i++)
            if (old[i].Number - old[i - 1].Number > 1) numberedVolumeGap = true;
        return old.Count > 0 ? old.Select(x => x.Path).ToList() : [path];
    }
}
