using System.Collections.Immutable;
using System.Globalization;

namespace FileCat.Core.Analysis;

public enum AnalysisMetadataRecipe
{
    LargestFiles, OldLargeFiles, DownloadsInstallers, GeneratedBuildOutput, ApplicationCache, CustomFilter, GrowthSinceAnalysis,
    AllocationDifference, ManySmallFiles, UnmeasuredLocations, LargeExtraData, LargestFolders
}

public sealed record AnalysisRecipeDefinition(AnalysisMetadataRecipe Recipe, string Id, int Version, string Title,
    string Rule, string Limitations);

public sealed record AnalysisRecipeRequest(AnalysisMetadataRecipe Recipe, ImmutableArray<string> RootIds,
    UInt128? MinimumBytes = null, int? MinimumAgeDays = null);

public enum AnalysisRecipeMatch { OutsideScope, Excluded, NoMatch, Unknown, Candidate }

public readonly record struct AnalysisRecipeFinding(AnalysisRecipeMatch Match, string? Evidence = null,
    ulong? ContextPath = null, ulong? MarkerPath = null, ulong? AdditionalMarkerPath = null);

/// <summary>Uses captured metadata only. Findings carry no permission to read content or modify a source.</summary>
public sealed class AnalysisRecipes
{
    public const ulong DefaultOldLargeBytes = 1_073_741_824;
    public const int DefaultOldLargeDays = 180;
    private readonly ImmutableHashSet<string> _rootIds;
    private readonly AnalysisFilter? _olderThan;

    public static ImmutableArray<AnalysisRecipeDefinition> Definitions { get; } =
    [
        new(AnalysisMetadataRecipe.LargestFiles, "largest-files", 1, "Largest files",
            "Rank recorded regular files by the selected byte measure.",
            "Partial observations may change the ranking. Observed size does not establish recoverable space."),
        new(AnalysisMetadataRecipe.LargestFolders, "largest-folders", 1, "Largest folders",
            "Rank captured folders by their known subtree totals in the selected measure.",
            "Use the totals for each folder individually because ancestor and descendant rows overlap. Partial observations may change the ranking."),
        new(AnalysisMetadataRecipe.OldLargeFiles, "old-large-files", 1, "Old large files",
            "Match recorded file size and modification age at the observation time.",
            "Size and modification time must both be known. Modification time does not establish when a file was last used."),
        new(AnalysisMetadataRecipe.DownloadsInstallers, "downloads-installers", 1, "Downloads and installers",
            "Look for installer and archive extensions within the one captured location you designate as Downloads.",
            "The extension is a hint. Other locations are outside this preview."),
        new(AnalysisMetadataRecipe.GeneratedBuildOutput, "generated-build-output", 1, ".NET restore output",
            "Look for project.assets.json and project.nuget.cache in an obj directory beside a recorded .NET project file.",
            "A same-named C#, F#, or Visual Basic project file supplies context. Review the exact files and whether they can be rebuilt."),
        new(AnalysisMetadataRecipe.ApplicationCache, "app-cache", 1, "Chrome HTTP cache records",
            "Look for known cache entry names in Google/Chrome/User Data/Default/Cache/Cache_Data, with recorded Local State and Preferences files.",
            "The rule covers the Default profile in this recorded layout. The browser may use these files. Synchronized or offline content can have other side effects."),
        new(AnalysisMetadataRecipe.AllocationDifference, "allocation-difference", 1, "Logical and allocated size differences",
            "Rank files by the absolute difference between their logical size and reported primary allocation.",
            "Sparse data, compression, sharing, and measurement basis can affect the difference. Shared objects can appear under more than one name."),
        new(AnalysisMetadataRecipe.ManySmallFiles, "many-small-files", 1, "Folders with many small files",
            "Rank folders with at least 32 directly observed files of 64 KiB or less by their small-file count.",
            "Allocation shown is the known reported portion for verified single-link files. Folder metadata, shared objects, and unmeasured allocation stay outside that portion."),
        new(AnalysisMetadataRecipe.UnmeasuredLocations, "unmeasured-locations", 1, "Unmeasured and unreadable locations",
            "Show recorded issues for failed, unsupported, pending, or changed observations in the selected locations.",
            "Issue records can be sampled. Unknown size remains unknown, and a measurement issue alone gives no reason to remove an entry."),
        new(AnalysisMetadataRecipe.LargeExtraData, "large-extra-data", 1, "Large extra data",
            "Review separately qualified stream or fork measurements.",
            "Extra data may be required by the application or record its provenance.")
    ];

    public AnalysisRecipeDefinition Definition { get; }
    public UInt128? MinimumBytes { get; }
    public int? MinimumAgeDays { get; }

    public AnalysisRecipes(AnalysisRecipeRequest request, ImmutableArray<RootBinding> roots, DateTimeOffset observedUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        Definition = Definitions.FirstOrDefault(definition => definition.Recipe == request.Recipe) ??
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.RootIds.IsDefaultOrEmpty || request.RootIds.Length > AnalysisLimits.Roots ||
            request.RootIds.Distinct(StringComparer.Ordinal).Count() != request.RootIds.Length ||
            request.RootIds.Any(id => !roots.Any(root => root.Id == id && root.Disposition == AnalysisRootDisposition.Active)))
            throw new ArgumentException("Choose distinct, captured locations with recorded entries.", nameof(request));
        if (request.Recipe == AnalysisMetadataRecipe.DownloadsInstallers &&
            (request.RootIds.Length != 1 || !roots.Any(root => root.Id == request.RootIds[0] && root.Kind == AnalysisKind.Directory)))
            throw new ArgumentException("Choose one captured Downloads folder.", nameof(request));
        _rootIds = request.RootIds.ToImmutableHashSet(StringComparer.Ordinal);

        if (request.Recipe == AnalysisMetadataRecipe.OldLargeFiles)
        {
            MinimumBytes = request.MinimumBytes ?? DefaultOldLargeBytes;
            MinimumAgeDays = request.MinimumAgeDays ?? DefaultOldLargeDays;
            if (MinimumAgeDays is < 1 or > 36_500 || TimeSpan.FromDays(MinimumAgeDays.Value).Ticks > observedUtc.UtcTicks)
                throw new ArgumentOutOfRangeException(nameof(request), "Choose an age within 1–36,500 days and the recorded calendar range.");
            string instant = observedUtc.ToUniversalTime().AddDays(-MinimumAgeDays.Value).ToString("O", CultureInfo.InvariantCulture);
            _olderThan = AnalysisFilter.Parse($"modified<{instant}", TimeZoneInfo.Utc);
        }
        else if (request.MinimumBytes.HasValue || request.MinimumAgeDays.HasValue)
        {
            throw new ArgumentException("Size and age thresholds apply to Old large files.", nameof(request));
        }
    }

    internal bool IncludesRoot(string id) => _rootIds.Contains(id);

    internal AnalysisRecipeFinding Evaluate(AnalysisNode node, AnalysisObject? recordedObject, AnalysisMeasurement rankingBytes,
        Func<ulong, AnalysisNode?> readPath, Func<AnalysisNode, string, AnalysisNode?> readChild)
    {
        if (!IncludesRoot(node.RootId)) return new(AnalysisRecipeMatch.OutsideScope);
        if (node.Kind != AnalysisKind.File) return new(AnalysisRecipeMatch.NoMatch);
        if (!node.IncludedByPolicy || node.MetadataState == MeasurementState.Excluded || node.Cloud == true)
            return new(AnalysisRecipeMatch.Excluded);
        if (node.MetadataState != MeasurementState.Known) return new(AnalysisRecipeMatch.Unknown);

        return Definition.Recipe switch
        {
            AnalysisMetadataRecipe.LargestFiles => rankingBytes.IsKnown
                ? new(AnalysisRecipeMatch.Candidate, "Known recorded size in the selected measure.")
                : new(AnalysisRecipeMatch.Unknown),
            AnalysisMetadataRecipe.OldLargeFiles => OldLarge(node, recordedObject),
            AnalysisMetadataRecipe.DownloadsInstallers => Downloads(node),
            AnalysisMetadataRecipe.GeneratedBuildOutput => Generated(node, readPath, readChild),
            AnalysisMetadataRecipe.ApplicationCache => Cache(node, readPath, readChild),
            _ => throw new InvalidOperationException("Invalid metadata recipe.")
        };
    }

    private AnalysisRecipeFinding OldLarge(AnalysisNode node, AnalysisObject? recordedObject)
    {
        if (recordedObject?.LogicalLength is not { IsKnown: true, Value: { } size }) return new(AnalysisRecipeMatch.Unknown);
        if (size < MinimumBytes!.Value) return new(AnalysisRecipeMatch.NoMatch);
        var item = new AnalysisFilterItem(node, recordedObject, "", AnalysisMeasurement.Missing(MeasurementState.Unsupported));
        return _olderThan!.Evaluate(item) switch
        {
            AnalysisTruth.True => new(AnalysisRecipeMatch.Candidate, "Recorded file size and modification age meet the thresholds."),
            AnalysisTruth.False => new(AnalysisRecipeMatch.NoMatch),
            _ => new(AnalysisRecipeMatch.Unknown)
        };
    }

    private static AnalysisRecipeFinding Downloads(AnalysisNode node)
    {
        if (!AnalysisRawNames.HasLosslessText(node)) return new(AnalysisRecipeMatch.Unknown);
        string extension = AnalysisNames.Extension(node.Name);
        return AnalysisClassification.Family(node.Name) == "archive" ||
            extension.ToLowerInvariant() is "exe" or "msi" or "msix" or "appx" or "deb" or "rpm" or "pkg" or "appimage"
            ? new(AnalysisRecipeMatch.Candidate, "Extension matches an archive or installer in the selected Downloads location.")
            : new(AnalysisRecipeMatch.NoMatch);
    }

    private static AnalysisRecipeFinding Generated(AnalysisNode node, Func<ulong, AnalysisNode?> readPath,
        Func<AnalysisNode, string, AnalysisNode?> readChild)
    {
        if (!AnalysisRawNames.HasLosslessText(node)) return new(AnalysisRecipeMatch.Unknown);
        if (node.Name is not ("project.assets.json" or "project.nuget.cache")) return new(AnalysisRecipeMatch.NoMatch);
        var output = Parent(node, readPath);
        if (!NamedDirectory(output, "obj")) return new(AnalysisRecipeMatch.Unknown);
        var project = Parent(output!, readPath);
        if (project is null || !AnalysisRawNames.HasLosslessText(project) || project.Name.Length > 240 ||
            !AnalysisNames.IsOrdinaryChild(project.Name, windows: true)) return new(AnalysisRecipeMatch.Unknown);
        foreach (string extension in ProjectExtensions)
        {
            var marker = Marker(project, project.Name + extension, readChild);
            if (marker is not null)
                return new(AnalysisRecipeMatch.Candidate, "Known .NET restore filename in obj with a recorded project file beside the folder.",
                    output!.Id, marker.Id);
        }
        return new(AnalysisRecipeMatch.Unknown);
    }

    private static readonly ImmutableArray<string> ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    private static AnalysisRecipeFinding Cache(AnalysisNode node, Func<ulong, AnalysisNode?> readPath,
        Func<AnalysisNode, string, AnalysisNode?> readChild)
    {
        if (!AnalysisRawNames.HasLosslessText(node)) return new(AnalysisRecipeMatch.Unknown);
        if (!IsCacheEntry(node.Name)) return new(AnalysisRecipeMatch.NoMatch);
        var data = Parent(node, readPath);
        if (!NamedDirectory(data, "Cache_Data")) return new(AnalysisRecipeMatch.NoMatch);
        var cache = Parent(data!, readPath);
        if (!NamedDirectory(cache, "Cache")) return new(AnalysisRecipeMatch.Unknown);
        var profile = Parent(cache!, readPath);
        if (!NamedDirectory(profile, "Default")) return new(AnalysisRecipeMatch.Unknown);
        var userData = Parent(profile!, readPath);
        if (!NamedDirectory(userData, "User Data")) return new(AnalysisRecipeMatch.Unknown);
        var application = Parent(userData!, readPath);
        if (!NamedDirectory(application, "Chrome") || !NamedDirectory(Parent(application!, readPath), "Google"))
            return new(AnalysisRecipeMatch.Unknown);
        var localState = Marker(userData!, "Local State", readChild);
        var preferences = Marker(profile!, "Preferences", readChild);
        return localState is not null && preferences is not null
            ? new(AnalysisRecipeMatch.Candidate, "Known cache entry filename in Chrome's recorded Default profile layout.",
                data!.Id, localState.Id, preferences.Id)
            : new(AnalysisRecipeMatch.Unknown);
    }

    private static bool IsCacheEntry(string name)
    {
        if (name == "index") return true;
        if (name.Length != 18 || name[16] != '_' || name[17] is not ('0' or '1')) return false;
        foreach (char c in name.AsSpan(0, 16))
            if (!char.IsAsciiHexDigitLower(c)) return false;
        return true;
    }

    private static bool Observed(AnalysisNode? node, AnalysisKind kind) => node is not null && node.Kind == kind &&
        node.IncludedByPolicy && node.MetadataState == MeasurementState.Known && node.Cloud != true;

    private static AnalysisNode? Parent(AnalysisNode node, Func<ulong, AnalysisNode?> readPath)
    {
        if (node.ParentId == 0) return null;
        var parent = readPath(node.ParentId);
        return Observed(parent, AnalysisKind.Directory) && parent!.Id == node.ParentId && parent.RootId == node.RootId ? parent : null;
    }

    private static bool NamedDirectory(AnalysisNode? node, string name) => node is not null && node.Name == name && AnalysisRawNames.HasLosslessText(node);

    private static AnalysisNode? Marker(AnalysisNode parent, string name, Func<AnalysisNode, string, AnalysisNode?> readChild)
    {
        var child = readChild(parent, name);
        return Observed(child, AnalysisKind.File) && child!.ParentId == parent.Id && child.RootId == parent.RootId &&
            child.Name == name && AnalysisRawNames.HasLosslessText(child) ? child : null;
    }
}
