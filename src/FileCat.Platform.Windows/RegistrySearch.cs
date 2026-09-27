using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

public sealed record RegistrySearchQuery(Location Root, string Text, bool KeyNames, bool ValueNames,
    bool TypedData, byte[]? RawData, bool MatchCase, bool Recursive);

public sealed record RegistrySearchReport(int KeysVisited, int ValuesVisited, int Matches, int LinksSkipped, bool StoppedAtLimit);

/// <summary>Bounded local Registry search. It never follows link objects or expands stored strings.</summary>
public static class RegistrySearch
{
    public const int MaxKeys = 100_000;
    public const int MaxValues = 250_000;
    public const int MaxMatches = 100_000;

    public static RegistrySearchReport Run(RegistrySearchQuery q, Action<ItemRef, string> found,
        Action<string> issue, CancellationToken ct)
    {
        if (q.Root.Scheme != Schemes.Registry || q.Root.Path.Length == 0)
            throw new ArgumentException("Choose a Registry root or key to search.");
        if (!q.KeyNames && !q.ValueNames && !q.TypedData && q.RawData is null)
            throw new ArgumentException("Choose at least one Registry search field.");
        if (q.Text.Length == 0 && q.RawData is not { Length: > 0 }) throw new ArgumentException("Enter text or raw bytes to find.");
        var comparison = q.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var stack = new Stack<Location>();
        stack.Push(q.Root);
        int keys = 0, values = 0, matches = 0, links = 0;
        bool limited = false;
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (keys >= MaxKeys || values >= MaxValues || matches >= MaxMatches) { limited = true; break; }
            var location = stack.Pop();
            keys++;
            try
            {
                using var key = WindowsRegistryProvider.Open(location, false);
                foreach (var valueName in RegistryRaw.ValueNames(key))
                {
                    ct.ThrowIfCancellationRequested();
                    if (values++ >= MaxValues || matches >= MaxMatches) { limited = true; break; }
                    bool hit = q.Text.Length > 0 && q.ValueNames && valueName.Contains(q.Text, comparison);
                    int length = -1;
                    if (q.TypedData || q.RawData is not null)
                    {
                        try
                        {
                            var data = RegistryRaw.Read(key, valueName);
                            length = data.Length;
                            if (data.Data.Length == data.Length)
                            {
                                hit |= q.Text.Length > 0 && q.TypedData && TypedText(data).Contains(q.Text, comparison);
                                hit |= q.RawData is { Length: > 0 } pattern && data.Data.AsSpan().IndexOf(pattern) >= 0;
                            }
                            else issue($"Value data too large to search: {location.Path}\\{valueName}");
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                        { issue($"Cannot search value {location.Path}\\{valueName}: {ex.Message}"); }
                    }
                    if (!hit) continue;
                    found(new ItemRef(location, valueName, EntryKind.RegistryValue, length), Relative(q.Root, location));
                    matches++;
                }
                if (limited) continue;
                foreach (var name in RegistryRaw.SubKeyNames(key))
                {
                    ct.ThrowIfCancellationRequested();
                    if (keys + stack.Count >= MaxKeys) { limited = true; break; }
                    try
                    {
                        var link = RegistryRaw.LinkTarget(key, name);
                        if (q.Text.Length > 0 && q.KeyNames && name.Contains(q.Text, comparison) && matches < MaxMatches)
                        {
                            found(new ItemRef(location, name, EntryKind.RegistryKey) { Flags = link is null ? EntryFlags.None : EntryFlags.Link }, Relative(q.Root, location));
                            matches++;
                        }
                        if (link is not null) { links++; continue; }
                        if (q.Recursive) stack.Push(location.WithPath(location.Path + "\\" + name));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                    { issue($"Cannot inspect key {location.Path}\\{name}: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { issue($"Cannot search key {location.Path}: {ex.Message}"); }
        }
        if (limited) issue($"Registry search stopped at its bounded limit ({MaxKeys:N0} keys, {MaxValues:N0} values, or {MaxMatches:N0} matches). Narrow the root or query.");
        return new RegistrySearchReport(keys, values, matches, links, limited);
    }

    private static string Relative(Location root, Location parent) =>
        parent.Path.Length <= root.Path.Length ? "" : parent.Path[(root.Path.Length + 1)..];

    private static string TypedText(RegistryValueData data)
    {
        if (data.Type is 1 or 2 or 7)
        {
            if ((data.Data.Length & 1) != 0) return "";
            var text = Encoding.Unicode.GetString(data.Data);
            return Encoding.Unicode.GetBytes(text).AsSpan().SequenceEqual(data.Data) ? text.Replace('\0', '\n') : "";
        }
        if (data.Type == 4 && data.Data.Length == 4)
        {
            var n = BitConverter.ToUInt32(data.Data);
            return $"{n} 0x{n:X8}";
        }
        if (data.Type == 11 && data.Data.Length == 8)
        {
            var n = BitConverter.ToUInt64(data.Data);
            return $"{n} 0x{n:X16}";
        }
        return "";
    }
}
