using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FileCat.Core.State;

/// <summary>Only these display values may cross the Windows integrity boundary.</summary>
public sealed class AppearanceTransferV1
{
    public const int MaxCharacters = 1024;
    private const string Header = "FileCat-Appearance-";
    private const string Prefix = Header + "1 ";

    /// <summary>Identifies the appearance transfer family for input guidance.</summary>
    public static bool HasHeader(string? text) => text.AsSpan().TrimStart().StartsWith(Header, StringComparison.Ordinal);

    public static IReadOnlyList<string> DateFormats { get; } =
        Array.AsReadOnly(new[] { "Culture", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm:ss", "MM/dd/yyyy h:mm:ss tt",
            "yyyy-MM-dd HH:mm", "dd.MM.yyyy HH:mm", "MM/dd/yyyy h:mm tt" });

    private abstract record Field(string Name, string Label)
    {
        internal abstract JsonNode? Get(AppSettings settings);
        internal abstract void Set(AppSettings settings, JsonNode? value);
        internal abstract bool Accepts(JsonElement value, IReadOnlyList<string> themes);
    }

    private sealed record Field<T>(string Name, string Label, Func<AppSettings, T> Read, Action<AppSettings, T> Write,
        Func<JsonElement, IReadOnlyList<string>, bool> Validate) : Field(Name, Label)
    {
        internal override JsonNode? Get(AppSettings settings) => Read(settings) switch
        {
            bool value => JsonValue.Create(value),
            double value => JsonValue.Create(value),
            string value => JsonValue.Create(value),
            _ => null,
        };
        internal override void Set(AppSettings settings, JsonNode? value) => Write(settings, value is null ? default! : value.GetValue<T>());
        internal override bool Accepts(JsonElement value, IReadOnlyList<string> themes) => Validate(value, themes);
    }

    private static Field Bool(string name, string label, Func<AppSettings, bool> read, Action<AppSettings, bool> write) =>
        new Field<bool>(name, label, read, write, (value, _) => value.ValueKind is JsonValueKind.True or JsonValueKind.False);

    private static Field Size(string name, string label, Func<AppSettings, double> read, Action<AppSettings, double> write, double max) =>
        new Field<double>(name, label, read, write, (value, _) =>
            value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double size) && size >= 9 && size <= max);

    private static bool Choice(JsonElement value, IReadOnlyList<string> choices) =>
        value.ValueKind == JsonValueKind.String && choices.Contains(value.GetString(), StringComparer.Ordinal);

    private static readonly Field[] Fields =
    [
        new Field<string>(nameof(AppSettings.Theme), "Theme", s => s.Theme, (s, v) => s.Theme = v, Choice),
        Bool(nameof(AppSettings.ThemeAnimations), "Theme animations", s => s.ThemeAnimations, (s, v) => s.ThemeAnimations = v),
        new Field<string>(nameof(AppSettings.RowDensity), "Row density", s => s.RowDensity, (s, v) => s.RowDensity = v,
            (value, _) => value.ValueKind == JsonValueKind.String && value.GetString() is "Compact" or "Comfortable"),
        Size(nameof(AppSettings.FontSize), "List font size", s => s.FontSize, (s, v) => s.FontSize = v, 24),
        Size(nameof(AppSettings.ContentFontSize), "Content font size", s => s.ContentFontSize, (s, v) => s.ContentFontSize = v, 32),
        new Field<string>(nameof(AppSettings.DateFormat), "Date format", s => s.DateFormat, (s, v) => s.DateFormat = v,
            (value, _) => Choice(value, DateFormats)),
        Bool(nameof(AppSettings.ShowToolbar), "Toolbar", s => s.ShowToolbar, (s, v) => s.ShowToolbar = v),
        Bool(nameof(AppSettings.ClassicToolbar), "Classic toolbar", s => s.ClassicToolbar, (s, v) => s.ClassicToolbar = v),
        Bool(nameof(AppSettings.ShowFunctionKeyBar), "Function-key bar", s => s.ShowFunctionKeyBar, (s, v) => s.ShowFunctionKeyBar = v),
        Bool(nameof(AppSettings.ShowDriveButtons), "Places row", s => s.ShowDriveButtons, (s, v) => s.ShowDriveButtons = v),
        Bool(nameof(AppSettings.CompactPlaces), "Compact Places row", s => s.CompactPlaces, (s, v) => s.CompactPlaces = v),
        Bool(nameof(AppSettings.ShowCommandLine), "Command-line visibility", s => s.ShowCommandLine, (s, v) => s.ShowCommandLine = v),
    ];

    private readonly JsonObject _values;
    private AppearanceTransferV1(JsonObject values) => _values = values;

    public string Encode() => Prefix + _values.ToJsonString();

    public static string Export(AppSettings settings, IReadOnlyList<string> themes) => Capture(settings, themes).Encode();

    public static AppearanceTransferV1 Capture(AppSettings settings, IReadOnlyList<string> themes)
    {
        var values = new JsonObject();
        var defaults = new AppSettings();
        foreach (var field in Fields)
        {
            JsonNode? value = field.Get(settings);
            if (value is JsonValue scalar &&
                !(scalar.TryGetValue<double>(out double number) && !double.IsFinite(number)) &&
                field.Accepts(JsonSerializer.SerializeToElement(value), themes))
                values.Add(field.Name, value);
            else if (field.Name != nameof(AppSettings.DateFormat))
                values.Add(field.Name, field.Get(defaults));
        }
        return new AppearanceTransferV1(values);
    }

    public static bool TryParse(string? text, IReadOnlyList<string> themes, out AppearanceTransferV1? transfer)
    {
        transfer = null;
        if (text is null || text.Length > MaxCharacters) return false;
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            using var json = JsonDocument.Parse(text[Prefix.Length..], new JsonDocumentOptions { MaxDepth = 2 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in json.RootElement.EnumerateObject())
            {
                var field = Fields.FirstOrDefault(f => f.Name == entry.Name);
                if (field is null || !seen.Add(entry.Name) || !field.Accepts(entry.Value, themes)) return false;
            }
            if (Fields.Any(f => f.Name != nameof(AppSettings.DateFormat) && !seen.Contains(f.Name))) return false;
            transfer = new AppearanceTransferV1(JsonNode.Parse(json.RootElement.GetRawText())!.AsObject());
            return true;
        }
        catch (JsonException) { return false; }
    }

    public void ApplyTo(AppSettings settings)
    {
        foreach (var field in Fields)
            if (_values.TryGetPropertyValue(field.Name, out var value)) field.Set(settings, value);
    }

    internal void Patch(JsonObject settings)
    {
        foreach (var entry in _values) settings[entry.Key] = entry.Value?.DeepClone();
    }

    internal static void RestoreFields(JsonObject settings, JsonObject backup)
    {
        foreach (var field in Fields)
        {
            if (backup.TryGetPropertyValue(field.Name, out var value)) settings[field.Name] = value?.DeepClone();
            else settings.Remove(field.Name);
        }
    }

    internal static void ApplyChanges(AppSettings settings, AppSettings before, AppSettings after)
    {
        foreach (var field in Fields)
        {
            var value = field.Get(after);
            if (!JsonNode.DeepEquals(field.Get(before), value)) field.Set(settings, value);
        }
    }

    public IReadOnlyList<string> DescribeChanges(AppSettings before)
    {
        var after = new AppSettings();
        ApplyTo(after);
        if (!_values.ContainsKey(nameof(AppSettings.DateFormat))) after.DateFormat = before.DateFormat;
        return DescribeChanges(before, after);
    }

    internal static IReadOnlyList<string> DescribeChanges(AppSettings before, AppSettings after)
    {
        var changes = new List<string>();
        foreach (var field in Fields)
        {
            var old = field.Get(before);
            var next = field.Get(after);
            if (!JsonNode.DeepEquals(old, next)) changes.Add($"{field.Label}   {Display(old)} → {Display(next)}");
        }
        return changes;
    }

    private static string Display(JsonNode? value) => value?.GetValueKind() switch
    {
        JsonValueKind.True => "On",
        JsonValueKind.False => "Off",
        JsonValueKind.Number => value.GetValue<double>().ToString(CultureInfo.CurrentCulture),
        JsonValueKind.String => new string(value.GetValue<string>().Take(64).Select(c => char.IsControl(c) ? ' ' : c).ToArray()),
        _ => "Default",
    };
}
