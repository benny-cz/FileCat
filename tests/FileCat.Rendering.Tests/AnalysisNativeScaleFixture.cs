using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FileCat.Rendering.Tests;

internal static class AnalysisNativeScaleFixture
{
    internal const string MarkerKind = "FileCat retained native scale fixture v1";

    internal static string? ReusableSource(int count, string shape, Action checkCancellation)
    {
        string? value = Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_SCALE_SOURCE");
        if (string.IsNullOrEmpty(value)) return null;
        string source = Path.GetFullPath(value); var container = Directory.GetParent(source) ?? throw new IOException("Missing fixture container.");
        var retained = container.Parent ?? throw new IOException("Missing retained fixture root.");
        var batch = retained.Parent ?? throw new IOException("Missing fixture batch.");
        if (Path.GetFileName(source) != "source" || !Regex.IsMatch(container.Name, "^native-[0-9a-f]{32}$", RegexOptions.CultureInvariant) ||
            retained.Name != "retained-fixtures" || !Regex.IsMatch(batch.Name, "^(dsa-batch-20261107-r|batch-r)[0-9]+[a-z]?$", RegexOptions.CultureInvariant))
            throw new IOException("The reused source is outside a generated retained fixture container.");
        foreach (string path in new[] { source, container.FullName, retained.FullName, batch.FullName })
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("A reused fixture cannot use a reparse point.");
        string marker = Path.Combine(container.FullName, "fixture.json");
        if (new FileInfo(marker).Length is < 1 or > 32 * 1024) throw new IOException("Invalid retained fixture marker size.");
        using var json = JsonDocument.Parse(File.ReadAllText(marker), new JsonDocumentOptions { MaxDepth = 8 }); var root = json.RootElement;
        if (root.GetProperty("kind").GetString() != MarkerKind || root.GetProperty("count").GetInt32() != count || root.GetProperty("shape").GetString() != shape ||
            !string.Equals(Path.GetFullPath(root.GetProperty("source").GetString()!), source, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The retained fixture does not match the requested case.");
        bool complete = !root.TryGetProperty("sourceCreationComplete", out var created) || created.GetBoolean();
        if (!complete)
        {
            string bottom = source;
            if (shape == "deep") for (int depth = 0; depth < 32; depth++) bottom = Path.Combine(bottom, "d");
            var seen = new BitArray(count);
            foreach (var file in new DirectoryInfo(bottom).EnumerateFiles())
            {
                checkCancellation();
                if (file.Name == "anchor.bin" && shape == "wide" || file.Name == "anchor-alias.txt") continue;
                string name = file.Name;
                if (name.Length != 16 || !name.StartsWith("row-", StringComparison.Ordinal) || name[12] != '.' ||
                    !int.TryParse(name.AsSpan(4, 8), NumberStyles.None, CultureInfo.InvariantCulture, out int at) || at < 2 || at >= count ||
                    name[13..] != (at % 2 == 0 ? "bin" : "txt") || seen[at]) throw new IOException("Unexpected row in an incomplete generated fixture.");
                seen[at] = true;
                if (file.Length != at % 17) File.WriteAllBytes(file.FullName, new byte[at % 17]);
            }
            for (int at = 2; at < count; at++)
            {
                if (at % 128 == 0) checkCancellation();
                if (!seen[at]) File.WriteAllBytes(Path.Combine(bottom, $"row-{at:D8}." + (at % 2 == 0 ? "bin" : "txt")), new byte[at % 17]);
            }
        }
        return source;
    }

    internal static string? RetainCreatedSource(string source, string owner, string @case, int count, string shape, bool complete)
    {
        if (count < 1_000_000 || Environment.GetEnvironmentVariable("FILECAT_TEST_NATIVE_SCALE_KEEP_SOURCE") != "1" || !Directory.Exists(source)) return null;
        source = Path.GetFullPath(source); owner = Path.GetFullPath(owner); @case = Path.GetFullPath(@case);
        // Reused sources belong to their original container and must never be moved or deleted here.
        if (Path.GetDirectoryName(source) != owner) return source;
        if (Path.GetDirectoryName(owner) != @case || !Regex.IsMatch(Path.GetFileName(owner), "^native-[0-9a-f]{32}$", RegexOptions.CultureInvariant))
            throw new IOException("Owned fixture retention escaped the current case.");
        string batch = Path.GetDirectoryName(@case)!; string retained = Path.Combine(batch, "retained-fixtures");
        if (Directory.Exists(retained) && (File.GetAttributes(retained) & FileAttributes.ReparsePoint) != 0) throw new IOException("Retained root changed.");
        string container = Path.Combine(retained, Path.GetFileName(owner)), target = Path.GetFullPath(Path.Combine(container, "source"));
        if (Directory.Exists(container) || !target.StartsWith(Path.GetFullPath(batch) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("A fresh contained retained source is required.");
        Directory.CreateDirectory(container); Directory.Move(source, target);
        File.WriteAllText(Path.Combine(container, "fixture.json"), JsonSerializer.Serialize(new
        { kind = MarkerKind, count, shape, source = target, movedFrom = source, originalCase = @case, sourceCreationComplete = complete, utc = DateTimeOffset.UtcNow }));
        return target;
    }
}
