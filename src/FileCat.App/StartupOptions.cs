namespace FileCat.App;

/// <summary>
/// Command-line arguments (plan §19.1, adapting Total Commander's /O /L /R /S): locations open in new tabs
/// of the active panel (or with --left/--right in a given panel), --profile selects a profile, --data keeps
/// everything FileCat writes in one folder, and --new-instance bypasses forwarding to a running instance.
/// </summary>
public sealed class StartupOptions
{
    public List<string> Locations { get; } = [];
    public string? LeftLocation { get; set; }
    public string? RightLocation { get; set; }
    public string? Profile { get; set; }
    /// <summary>
    /// --data: a folder that holds everything FileCat writes (settings, logs, journals, caches, scratch, hex originals),
    /// for recovering deleted files from the disk that holds FileCat's usual places (release plan V09, I09).
    /// </summary>
    public string? DataRoot { get; set; }
    public string? Workspace { get; set; }
    public string? ListFile { get; set; }
    public bool NewInstance { get; set; }
    public bool ResetLayout { get; set; }

    /// <summary>TV-01 native benchmark: synthetic entries per panel (0 = off). Implies a new, isolated instance.</summary>
    public int BenchmarkCount { get; set; }
    public int BenchmarkPanels { get; set; } = 4;
    public string? BenchmarkOut { get; set; }
    /// <summary>Optional real directory whose first-rows time is measured three times.</summary>
    public string? BenchmarkDirectory { get; set; }

    public static StartupOptions Parse(string[] args)
    {
        var o = new StartupOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (a.ToLowerInvariant())
            {
                case "--left" or "/l": o.LeftLocation = Next(); break;
                case "--right" or "/r": o.RightLocation = Next(); break;
                case "--profile": o.Profile = Next(); break;
                case "--data": o.DataRoot = Next() is { Length: > 0 } data ? Path.GetFullPath(data) : null; break;
                case "--workspace": o.Workspace = Next(); break;
                case "--list" or "--loadlist": o.ListFile = Next(); break;
                case "--new-instance" or "/n": o.NewInstance = true; break;
                case "--reset-layout": o.ResetLayout = true; break;
                case "--benchmark":
                    o.BenchmarkCount = int.TryParse(Next(), out int count) ? Math.Clamp(count, 1, 5_000_000) : 1_000_000;
                    o.NewInstance = true;
                    o.ResetLayout = true;
                    break;
                case "--benchmark-panels": o.BenchmarkPanels = int.TryParse(Next(), out int panels) ? panels : 4; break;
                case "--benchmark-out": o.BenchmarkOut = Next(); break;
                case "--benchmark-dir": o.BenchmarkDirectory = Next(); break;
                case "/o": break; // forwarding is the default, accepted for Total Commander familiarity
                default:
                    if (!a.StartsWith("--", StringComparison.Ordinal)) o.Locations.Add(a);
                    break;
            }
        }
        return o;
    }

    public string[] ToForwardArgs()
    {
        var list = new List<string>();
        if (LeftLocation is not null) list.AddRange(["--left", LeftLocation]);
        if (RightLocation is not null) list.AddRange(["--right", RightLocation]);
        if (ListFile is not null) list.AddRange(["--list", ListFile]);
        if (Workspace is not null) list.AddRange(["--workspace", Workspace]);
        list.AddRange(Locations.Select(l => Path.IsPathRooted(l) ? l : Path.GetFullPath(l)));
        return list.ToArray();
    }
}
