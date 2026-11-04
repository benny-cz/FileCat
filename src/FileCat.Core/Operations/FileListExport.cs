using System.Globalization;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Operations;

/// <summary>A named, metadata-only file-list format. Scope and destination are chosen for each export.</summary>
public sealed record FileListRule(string Name, string Template, bool Recursive = false,
    bool IncludeFiles = true, bool IncludeFolders = true)
{
    public static readonly FileListRule[] BuiltIns =
    [
        new("Names and folders", "{RelativePath}"),
        new("All info", "{Type}\t{Size}\t{Modified}\t{RelativePath}"),
        new("Files only", "{RelativePath}", IncludeFolders: false),
        new("Full paths", "{Path}"),
    ];
}

/// <summary>
/// Lists provider metadata without opening file content. A provider issue or changed selection refuses the entire
/// result. Recursive export never follows directory links, and bounded output keeps a remote or hostile tree finite.
/// </summary>
public static class FileListExport
{
    public const int MaxEntries = 100_000;
    public const int MaxCharacters = 8 * 1024 * 1024;
    public const int MaxDepth = 64;
    private static readonly HashSet<string> Tokens = new(StringComparer.OrdinalIgnoreCase)
        { "Name", "Filename", "Ext", "Size", "Modified", "Created", "Path", "RelativePath", "Type" };

    public static string? Validate(FileListRule rule)
    {
        if (!rule.IncludeFiles && !rule.IncludeFolders) return "Include files, folders, or both.";
        if (string.IsNullOrWhiteSpace(rule.Template)) return "Enter a line format.";
        if (rule.Template.Length > 4096) return "The line format is too long (4,096 characters maximum).";
        try { Parse(rule.Template, _ => string.Empty); return null; }
        catch (FormatException ex) { return ex.Message; }
    }

    public static async Task<string> GenerateAsync(ProviderRegistry providers, Location root,
        IReadOnlyCollection<ItemRef>? selected, FileListRule rule, CancellationToken cancellationToken)
    {
        if (Validate(rule) is { } problem) throw new FormatException(problem);
        if (root.Scheme is not (Schemes.FileSystem or Schemes.Zip or Schemes.Archive or Schemes.Ftp or Schemes.Sftp or Schemes.Mtp))
            throw new NotSupportedException("Create file list works in folders, archives, and connected file panels.");
        var output = new StringBuilder();
        int count = 0;
        var pending = new Stack<(Location Folder, string Prefix, int Depth, HashSet<ItemRef>? Only)>();
        HashSet<ItemRef>? only = selected is null ? null : new(selected);
        if (only is { Count: 0 }) throw new InvalidOperationException("Select or focus a file or folder first.");
        pending.Push((root, string.Empty, 0, only));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (folder, prefix, depth, requested) = pending.Pop();
            var provider = providers.For(folder);
            if ((provider.GetCapabilities(folder) & LocationCapabilities.Enumerate) == 0)
                throw new NotSupportedException("This folder cannot be listed completely.");
            var sink = new Collector(cancellationToken);
            await provider.EnumerateAsync(folder, sink, cancellationToken).ConfigureAwait(false);
            if (sink.Issues.Count > 0)
                throw new IOException("The list was not exported because a folder could not be read: " + sink.Issues[0]);
            var children = new List<(Location Folder, string Prefix)>();
            foreach (var entry in sink.Entries.OrderBy(e => e.Kind == EntryKind.Directory ? 0 : 1)
                         .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(e => e.Name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Kind == EntryKind.Parent) continue;
                var item = provider.GetItemRef(folder, entry);
                if (requested is not null && !requested.Remove(item)) continue;
                if (entry.Kind is not (EntryKind.File or EntryKind.Directory)) continue;
                if (++count > MaxEntries) throw new InvalidDataException($"The list exceeds {MaxEntries:N0} entries. Narrow the selection.");
                string relative = prefix + entry.Name;
                if (entry.Kind == EntryKind.File && rule.IncludeFiles || entry.Kind == EntryKind.Directory && rule.IncludeFolders)
                {
                    string path = item.FileSystemPath ?? provider.GetDisplayPath(folder).TrimEnd('/', '\\') + "/" + entry.Name;
                    string line = Format(rule.Template, entry, path, relative);
                    if (output.Length + line.Length + Environment.NewLine.Length > MaxCharacters)
                        throw new InvalidDataException("The list exceeds 8 million characters. Narrow the selection or line format.");
                    output.Append(line).AppendLine();
                }
                if (rule.Recursive && entry.Kind == EntryKind.Directory && !entry.Has(EntryFlags.Link))
                {
                    if (depth >= MaxDepth) throw new InvalidDataException("The list exceeds 64 folder levels. Narrow the selection.");
                    var child = provider.GetChildLocation(folder, entry);
                    if (child is null || child.Scheme != folder.Scheme)
                        throw new IOException("A selected subfolder could not be opened: " + entry.Name);
                    children.Add((child, relative + "/"));
                }
            }
            if (requested is { Count: > 0 }) throw new IOException("The selection changed while the list was being created. Try again.");
            for (int i = children.Count - 1; i >= 0; i--)
                pending.Push((children[i].Folder, children[i].Prefix, depth + 1, null));
        }
        string result = output.ToString();
        if (Encoding.UTF8.GetByteCount(result) > 8 * 1024 * 1024)
            throw new InvalidDataException("The list exceeds 8 MiB of UTF-8 text. Narrow the selection or line format.");
        return result;
    }

    public static string Format(string template, EntryData entry, string path, string relativePath)
    {
        string Value(string token) => token.ToLowerInvariant() switch
        {
            "name" => entry.Name,
            "filename" => entry.Kind == EntryKind.Directory ? entry.Name : NameParts.GetStem(entry.Name),
            "ext" => entry.Kind == EntryKind.Directory ? string.Empty : entry.Extension,
            "size" => entry.Size >= 0 && entry.Kind == EntryKind.File ? entry.Size.ToString(CultureInfo.InvariantCulture) : string.Empty,
            "modified" => entry.ModifiedUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty,
            "created" => entry.CreatedUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty,
            "path" => path,
            "relativepath" => relativePath,
            "type" => entry.Kind == EntryKind.Directory ? "Folder" : "File",
            _ => throw new FormatException($"Unknown field {{{token}}}."),
        };
        return Parse(template, token => Escape(Value(token)));
    }

    private static string Escape(string value) => value.Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);

    private static string Parse(string template, Func<string, string> value)
    {
        var output = new StringBuilder(template.Length);
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{') { output.Append('{'); i++; continue; }
                int end = template.IndexOf('}', i + 1);
                if (end < 0) throw new FormatException("Close each field with }.");
                string token = template[(i + 1)..end];
                if (!Tokens.Contains(token)) throw new FormatException($"Unknown field {{{token}}}.");
                output.Append(value(token));
                i = end;
            }
            else if (template[i] == '}')
            {
                if (i + 1 < template.Length && template[i + 1] == '}') { output.Append('}'); i++; }
                else throw new FormatException("Use }} for a literal }.");
            }
            else output.Append(template[i]);
        }
        return output.ToString();
    }

    private sealed class Collector(CancellationToken cancellationToken) : IEnumerationSink
    {
        public bool Whole => true;
        public List<EntryData> Entries { get; } = [];
        public List<string> Issues { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Entries.Count + entries.Length > MaxEntries) throw new InvalidDataException("A folder exceeds the file-list entry limit.");
            foreach (var entry in entries) Entries.Add(entry);
        }
        public void ReportIssue(string message) { if (Issues.Count == 0) Issues.Add(message); }
    }
}
