using System.Globalization;
using System.Text.Json;

namespace FileCat.Core.Commands;

/// <summary>
/// Language files for command titles and key-bar labels: <c>lang/&lt;culture&gt;.json</c> next to the executable, an
/// object mapping command ids (and "id#bar") to text. The most specific culture wins ("cs-CZ", then "cs").
/// Missing files or entries fall back to English.
/// </summary>
public static class CommandTranslations
{
    public static IReadOnlyDictionary<string, string>? Load(string directory, CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            var path = Path.Combine(directory, c.Name + ".json");
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } text) map[p.Name] = text;
                }
                return map;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Diagnostics.AppLog.Warn($"Language file {c.Name}.json could not be read; using English.");
                return null;
            }
        }
        return null;
    }
}
