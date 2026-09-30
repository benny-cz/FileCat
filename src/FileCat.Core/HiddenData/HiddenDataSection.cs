using System.Globalization;
using FileCat.Core.Inspect;

namespace FileCat.Core.HiddenData;

/// <summary>
/// A file's streams and attributes in its file-system record (D-56 with D-55): each with its size and what it says
/// (a download's origin, a quarantine flag, a hidden note), so the record answers "what else does this file carry".
/// Alt+Shift+Enter lists them to view, copy out, or delete.
/// </summary>
public static class HiddenDataSection
{
    private const int MaxRows = 40;
    private const int ReadBytes = 4096;

    public static InspectionSection? Of(IHiddenData hidden, string path)
    {
        if (!hidden.IsSupported) return null;
        IReadOnlyList<HiddenItem> items;
        try { items = hidden.List(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (items.Count == 0) return null;
        var rows = new List<string[]>();
        foreach (var item in items.Take(MaxRows))
        {
            string said;
            try { said = HiddenDataDecoder.Describe(item, hidden.Read(path, item, ReadBytes)).Summary; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { said = "not read: " + ex.Message; }
            rows.Add([item.Name, item.KindText, item.Size >= 0 ? item.Size.ToString("N0", CultureInfo.CurrentCulture) : "?", said.Length > 100 ? said[..99] + "…" : said]);
        }
        return new InspectionSection($"Streams and attributes ({items.Count})", [])
        {
            Table = new InspectionTable(["Name", "Kind", "Bytes", "What it says"], rows)
            {
                More = items.Count > MaxRows ? $"{items.Count - MaxRows:N0} more are not listed." : null,
            },
            Lines = ["Alt+Shift+Enter lists them to view, copy out, or delete."],
        };
    }
}
