using System.Globalization;
using FileCat.Core.Selection;

namespace FileCat.Core.Search;

public enum SizeUnit
{
    Bytes,
    KB,
    MB,
    GB,
}

public enum TimeUnit
{
    Seconds,
    Minutes,
    Hours,
    Days,
    Weeks,
    Months,
    Years,
}

public enum TimeFilterMode
{
    /// <summary>Any time.</summary>
    Any,
    /// <summary>Within the last N units before the search starts.</summary>
    Within,
    /// <summary>From one moment to another; either end may be open.</summary>
    Between,
}

/// <summary>A time criterion of Find (plan §11): any time, the last N units, or a range whose ends are optional.</summary>
public sealed class TimeCriterion
{
    public TimeFilterMode Mode { get; set; }
    public int Amount { get; set; } = 7;
    public TimeUnit Unit { get; set; } = TimeUnit.Days;
    /// <summary>Local time the range starts (inclusive), or null for no start.</summary>
    public DateTime? From { get; set; }
    /// <summary>Local time the range ends (inclusive), or null for no end.</summary>
    public DateTime? To { get; set; }

    public bool IsActive => Mode == TimeFilterMode.Within || Mode == TimeFilterMode.Between && (From is not null || To is not null);

    /// <summary>The criterion as UTC bounds, the last N units counted back from <paramref name="nowUtc"/>.</summary>
    public (DateTime? AfterUtc, DateTime? BeforeUtc) Resolve(DateTime nowUtc) => Mode switch
    {
        TimeFilterMode.Within => (Back(nowUtc), null),
        TimeFilterMode.Between => (From is { } from ? DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime() : null,
            To is { } to ? DateTime.SpecifyKind(to, DateTimeKind.Local).ToUniversalTime() : null),
        _ => (null, null),
    };

    private DateTime Back(DateTime nowUtc)
    {
        int n = Math.Max(0, Amount);
        return Unit switch
        {
            TimeUnit.Seconds => nowUtc.AddSeconds(-n),
            TimeUnit.Minutes => nowUtc.AddMinutes(-n),
            TimeUnit.Hours => nowUtc.AddHours(-n),
            TimeUnit.Weeks => nowUtc.AddDays(-7.0 * n),
            TimeUnit.Months => nowUtc.AddMonths(-Math.Min(n, 12 * 9000)),
            TimeUnit.Years => nowUtc.AddYears(-Math.Min(n, 9000)),
            _ => nowUtc.AddDays(-n),
        };
    }

    /// <summary>"in the last 7 days", "from 1. 3. 2026", "1. 3. 2026 – 31. 3. 2026", or null when inactive.</summary>
    public string? Describe()
    {
        static string When(DateTime t) => t.TimeOfDay == TimeSpan.Zero ? t.ToString("d", CultureInfo.CurrentCulture) : t.ToString("g", CultureInfo.CurrentCulture);
        return Mode switch
        {
            TimeFilterMode.Within => $"in the last {Amount} {(Amount == 1 ? Unit.ToString().ToLowerInvariant().TrimEnd('s') : Unit.ToString().ToLowerInvariant())}",
            TimeFilterMode.Between when From is { } f && To is { } t => $"{When(f)} – {When(t)}",
            TimeFilterMode.Between when From is { } f => $"from {When(f)}",
            TimeFilterMode.Between when To is { } t => $"until {When(t)}",
            _ => null,
        };
    }

    public TimeCriterion Clone() => (TimeCriterion)MemberwiseClone();
}

/// <summary>Find's advanced criteria (plan §11): attributes that must be set or clear, size bounds, and times.</summary>
public sealed class AdvancedSearchCriteria
{
    /// <summary>The attributes the advanced criteria offer, in the order they are shown.</summary>
    public static readonly (FileAttributes Attribute, string Name)[] Attributes =
    [
        (FileAttributes.Archive, "archive"), (FileAttributes.ReadOnly, "read-only"), (FileAttributes.Hidden, "hidden"),
        (FileAttributes.System, "system"), (FileAttributes.Compressed, "compressed"), (FileAttributes.Encrypted, "encrypted"),
        (FileAttributes.Directory, "folder"),
    ];

    public FileAttributes AttributesSet { get; set; }
    public FileAttributes AttributesClear { get; set; }
    public double? SizeAtLeast { get; set; }
    public SizeUnit SizeAtLeastUnit { get; set; } = SizeUnit.KB;
    public double? SizeAtMost { get; set; }
    public SizeUnit SizeAtMostUnit { get; set; } = SizeUnit.KB;
    public TimeCriterion Modified { get; set; } = new();
    public TimeCriterion Created { get; set; } = new();

    /// <summary>Only items carrying a stream or attribute besides their download mark (D-55).</summary>
    public bool CarriesHiddenData { get; set; }

    public bool IsEmpty => (AttributesSet | AttributesClear) == 0 && SizeAtLeast is null && SizeAtMost is null && !Modified.IsActive && !Created.IsActive && !CarriesHiddenData;

    public static long Bytes(double value, SizeUnit unit) => (long)Math.Round(value * unit switch
    {
        SizeUnit.KB => 1024d,
        SizeUnit.MB => 1024d * 1024,
        SizeUnit.GB => 1024d * 1024 * 1024,
        _ => 1d,
    });

    private static string Unit(SizeUnit unit) => unit == SizeUnit.Bytes ? "bytes" : unit.ToString();

    /// <summary>
    /// The active criteria on one line ("files only · not hidden · at least 10 KB · modified in the last 7 days"), or an
    /// empty string when none is active.
    /// </summary>
    public string Summary()
    {
        var parts = new List<string>();
        if ((AttributesSet & FileAttributes.Directory) != 0) parts.Add("folders only");
        if ((AttributesClear & FileAttributes.Directory) != 0) parts.Add("files only");
        foreach (var (attribute, name) in Attributes)
        {
            if (attribute == FileAttributes.Directory) continue;
            if ((AttributesSet & attribute) != 0) parts.Add(name);
            if ((AttributesClear & attribute) != 0) parts.Add("not " + name);
        }
        if (SizeAtLeast is { } least) parts.Add($"at least {least.ToString("0.##", CultureInfo.CurrentCulture)} {Unit(SizeAtLeastUnit)}");
        if (SizeAtMost is { } most) parts.Add($"at most {most.ToString("0.##", CultureInfo.CurrentCulture)} {Unit(SizeAtMostUnit)}");
        if (Modified.Describe() is { } modified) parts.Add("modified " + modified);
        if (Created.Describe() is { } created) parts.Add("created " + created);
        if (CarriesHiddenData) parts.Add("carrying streams or attributes");
        return string.Join(" · ", parts);
    }

    public AdvancedSearchCriteria Clone()
    {
        var copy = (AdvancedSearchCriteria)MemberwiseClone();
        copy.Modified = Modified.Clone();
        copy.Created = Created.Clone();
        return copy;
    }
}

/// <summary>Everything Find's dialog holds, as saved searches keep it (plan §11).</summary>
public sealed class SearchCriteria
{
    public string Names { get; set; } = "*";
    /// <summary>Where to search: folders separated by semicolons.</summary>
    public string LookIn { get; set; } = string.Empty;
    public bool Subfolders { get; set; } = true;
    public bool IncludeHidden { get; set; } = true;
    /// <summary>Also find names inside the archives met (their contents are not searched).</summary>
    public bool InsideArchives { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool MatchCase { get; set; }
    public bool WholeWords { get; set; }
    public bool Regex { get; set; }
    /// <summary>The text is bytes (<see cref="HexPattern"/>).</summary>
    public bool Hex { get; set; }
    /// <summary>The text is also found as UTF-16 and UTF-8 in any file (<see cref="SearchQuery.Unicode"/>).</summary>
    public bool Unicode { get; set; }
    public AdvancedSearchCriteria Advanced { get; set; } = new();

    public SearchCriteria Clone()
    {
        var copy = (SearchCriteria)MemberwiseClone();
        copy.Advanced = Advanced.Clone();
        return copy;
    }

    /// <summary>The folders in <see cref="LookIn"/>.</summary>
    public IReadOnlyList<string> Roots() =>
        LookIn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(r => r.Trim('"')).Where(r => r.Length > 0).ToList();

    /// <summary>
    /// The engine's query for these criteria, times counted back from <paramref name="nowUtc"/>. Fails with the reason
    /// when a mask, hex pattern, or range cannot work, or a folder to search is missing.
    /// </summary>
    public bool TryBuildQuery(DateTime nowUtc, IReadOnlyList<string> ignoredFolders, IReadOnlyList<(Resources.ItemRef Item, string Relative)>? within,
        out SearchQuery? query, out string? error, IArchiveMembers? archives = null, HiddenData.IHiddenData? hiddenData = null)
    {
        query = null;
        if (!Mask.TryParse(string.IsNullOrWhiteSpace(Names) ? "*" : Names, plainMeansContains: true, out var mask, out var maskError))
        {
            error = "Names: " + maskError;
            return false;
        }
        byte[]? bytes = null;
        if (Hex && !string.IsNullOrWhiteSpace(Text) && !HexPattern.TryParse(Text, out bytes, out var hexError))
        {
            error = "Containing (hex): " + hexError;
            return false;
        }
        var roots = within is null ? Roots() : [];
        if (within is null)
        {
            if (roots.Count == 0)
            {
                error = "Enter a folder to search.";
                return false;
            }
            if (roots.FirstOrDefault(r => !Directory.Exists(r)) is { } missing)
            {
                error = $"\"{missing}\" is not a folder.";
                return false;
            }
        }
        var (modifiedAfter, modifiedBefore) = Advanced.Modified.Resolve(nowUtc);
        var (createdAfter, createdBefore) = Advanced.Created.Resolve(nowUtc);
        query = new SearchQuery
        {
            Roots = roots,
            Names = mask,
            Text = Hex || string.IsNullOrEmpty(Text) ? null : Text,
            Bytes = bytes,
            MatchCase = MatchCase,
            WholeWords = WholeWords && !Hex,
            Regex = Regex && !Hex,
            Unicode = Unicode && !Hex,
            Recursive = Subfolders,
            IncludeHidden = IncludeHidden,
            MinSize = Advanced.SizeAtLeast is { } least ? AdvancedSearchCriteria.Bytes(least, Advanced.SizeAtLeastUnit) : null,
            MaxSize = Advanced.SizeAtMost is { } most ? AdvancedSearchCriteria.Bytes(most, Advanced.SizeAtMostUnit) : null,
            ModifiedAfterUtc = modifiedAfter,
            ModifiedBeforeUtc = modifiedBefore,
            CreatedAfterUtc = createdAfter,
            CreatedBeforeUtc = createdBefore,
            AttributesSet = Advanced.AttributesSet,
            AttributesClear = Advanced.AttributesClear,
            IgnoredFolders = ignoredFolders,
            WithinResults = within,
            Archives = InsideArchives ? archives : null,
            ResultArchives = archives as IArchiveResultLookup,
            CarriesHiddenData = Advanced.CarriesHiddenData ? hiddenData : null,
        };
        if (Advanced.CarriesHiddenData && hiddenData is not { IsSupported: true })
        {
            query = null;
            error = "This system keeps no streams or attributes beside files: clear that criterion.";
            return false;
        }
        return SearchSession.TryValidate(query, out error);
    }
}

/// <summary>A set of Find criteria saved under a name (plan §11).</summary>
public sealed class SavedSearch
{
    public string Name { get; set; } = string.Empty;
    public SearchCriteria Criteria { get; set; } = new();
    /// <summary>Loaded whenever Find opens (at most one saved search has it).</summary>
    public bool LoadOnOpen { get; set; }
}

/// <summary>A folder Find skips, switchable without deleting it (<see cref="SearchQuery.IgnoredFolders"/>).</summary>
public sealed class IgnoredFolderEntry
{
    public string Folder { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
