using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FileCat.Core.Archives;

/// <summary>Bounded plain-text previews. The underlying exact names and reviewed keys never change.</summary>
internal static class ArchiveJobReviewText
{
    internal const int MaximumCharacters = 8192, MaximumNameCharacters = 256, MaximumSamples = 128;
    private static readonly JsonSerializerOptions readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    internal static string Samples(IEnumerable<string> names)
    {
        var output = new StringBuilder(); int count = 0;
        foreach (string name in names)
        {
            if (count++ == MaximumSamples) { More(); break; }
            string preview = name;
            if (name.Length > MaximumNameCharacters)
            {
                int head = MaximumNameCharacters / 2, tail = name.Length - MaximumNameCharacters / 2;
                if (char.IsHighSurrogate(name[head - 1])) head--;
                if (char.IsLowSurrogate(name[tail])) tail++;
                preview = string.Concat(name.AsSpan(0, head), "…", name.AsSpan(tail));
            }
            bool escaped = preview.Any(character => char.GetUnicodeCategory(character) is UnicodeCategory.Format or
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);
            string quoted = JsonSerializer.Serialize(preview, escaped ? null : readable);
            string suffix = name.Length > MaximumNameCharacters ? $" ({name.Length:N0} characters; preview shortened)" : "";
            if (output.Length + quoted.Length + suffix.Length + 1 > MaximumCharacters - 64) { More(); break; }
            if (output.Length != 0) output.Append('\n'); output.Append(quoted).Append(suffix);
        }
        return output.ToString();
        void More() { if (output.Length != 0) output.Append('\n'); output.Append("… additional names omitted from this preview."); }
    }
}
