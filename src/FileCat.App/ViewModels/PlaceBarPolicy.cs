using System.Globalization;

namespace FileCat.App.ViewModels;

/// <summary>Presentation only: every omitted place remains in More and in the location menu.</summary>
internal static class PlaceBarPolicy
{
    internal const int MaximumPins = 8;

    internal static string Key(Place place)
    {
        static string Part(string? value) => value is null ? "-1:" :
            value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        var location = place.Location;
        return ((int)place.Group).ToString(CultureInfo.InvariantCulture) + ":" +
            Part(place.Title) + Part(location?.Scheme) + Part(location?.Path) +
            Part(location?.Container?.ToString()) + Part(location?.Session);
    }

    internal static IReadOnlyList<Place> Select(IReadOnlyList<Place> all, IReadOnlyList<string> pinned, bool compact)
    {
        if (!compact) return all;
        var selected = new HashSet<Place>(ReferenceEqualityComparer.Instance);
        var pinKeys = pinned.Take(MaximumPins).ToHashSet(StringComparer.Ordinal);
        foreach (var place in all)
            if (pinKeys.Contains(Key(place))) selected.Add(place);
        int target = Math.Max(4, selected.Count);
        foreach (var group in new[] { PlaceGroup.Collections, PlaceGroup.Drives, PlaceGroup.Folders,
                     PlaceGroup.Bookmarks, PlaceGroup.Servers, PlaceGroup.Devices })
        {
            if (selected.Count >= target) break;
            var first = all.FirstOrDefault(place => place.Group == group && !selected.Contains(place));
            if (first is not null) selected.Add(first);
        }
        for (int i = 0; selected.Count < target && i < all.Count; i++) selected.Add(all[i]);
        return all.Where(selected.Contains).ToArray();
    }
}
