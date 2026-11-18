using System.Text.Json;
using System.Text.RegularExpressions;

namespace FileCat.Rendering.Tests;

internal static class AnalysisNativeScaleFixture
{
    internal const string MarkerKind = "FileCat retained native scale fixture v1";
    internal const int HardLinkCohortSize = 128;

    internal static string? ReusableSource(int count, string shape, Action checkCancellation, Action<string, string> createAlias)
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
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != FileAttributes.Directory)
                throw new IOException("A reused fixture must use ordinary directories.");
        string marker = Path.Combine(container.FullName, "fixture.json");
        var markerInfo = new FileInfo(marker);
        if ((markerInfo.Attributes & FileAttributes.ReparsePoint) != 0 || markerInfo.Length is < 1 or > 32 * 1024)
            throw new IOException("Invalid retained fixture marker.");
        using var json = JsonDocument.Parse(File.ReadAllText(marker), new JsonDocumentOptions { MaxDepth = 8 }); var root = json.RootElement;
        if (root.GetProperty("kind").GetString() != MarkerKind || root.GetProperty("count").GetInt32() != count || root.GetProperty("shape").GetString() != shape ||
            !string.Equals(Path.GetFullPath(root.GetProperty("source").GetString()!), source, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The retained fixture does not match the requested case.");
        bool complete = !root.TryGetProperty("sourceCreationComplete", out var created) || created.GetBoolean();
        if (!complete)
            CompleteSource(source, container.FullName, count, shape, checkCancellation, createAlias);
        return source;
    }

    internal static int RowOwner(int row, string shape) =>
        shape == "wide-linked" ? 2 + (row - 2) / HardLinkCohortSize * HardLinkCohortSize : row;

    private static string RowPath(string directory, int row) =>
        Path.Combine(directory, $"row-{row:D8}." + (row % 2 == 0 ? "bin" : "txt"));

    internal static void WriteRow(string directory, int row, int owner, Action<string, string> createAlias)
    {
        string path = RowPath(directory, row);
        if (row != owner)
        {
            createAlias(RowPath(directory, owner), path);
            return;
        }
        Span<byte> zeroes = stackalloc byte[16];
        zeroes.Clear();
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(zeroes[..(owner % 17)]);
    }

    private static void CompleteSource(string source, string container, int count, string shape, Action checkCancellation,
        Action<string, string> createAlias)
    {
        string bottom = source;
        if (shape == "deep")
        {
            for (int depth = 0; depth < 32; depth++)
            {
                bottom = Path.Combine(bottom, "d");
                if ((File.GetAttributes(bottom) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != FileAttributes.Directory)
                    throw new IOException("A retained fixture directory changed.");
            }
        }

        const string kind = "FileCat retained native scale progress v1";
        const int checkpointInterval = 16_384;
        string progress = Path.Combine(container, "fixture-progress.json");
        int next = 2;
        if (File.Exists(progress))
        {
            var info = new FileInfo(progress);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length is < 1 or > 32 * 1024)
                throw new IOException("Invalid retained fixture progress.");
            using var json = JsonDocument.Parse(File.ReadAllText(progress), new JsonDocumentOptions { MaxDepth = 4 });
            var cursor = json.RootElement;
            next = cursor.GetProperty("next").GetInt32();
            if (cursor.GetProperty("kind").GetString() != kind || cursor.GetProperty("source").GetString() != source ||
                cursor.GetProperty("count").GetInt32() != count || cursor.GetProperty("shape").GetString() != shape || next < 2 || next > count)
                throw new IOException("The retained fixture progress does not match this source.");
        }

        while (next < count)
        {
            checkCancellation();
            int end = Math.Min(count, next + checkpointInterval);
            for (; next < end; next++)
            {
                if ((next & 127) == 0) checkCancellation();
                var row = new FileInfo(RowPath(bottom, next));
                int owner = RowOwner(next, shape);
                int expectedLength = owner % 17;
                if (row.Exists)
                {
                    if ((row.Attributes & FileAttributes.ReparsePoint) != 0 || row.Length != expectedLength)
                        throw new IOException($"Generated row {next} does not match the retained fixture.");
                }
                else
                {
                    WriteRow(bottom, next, owner, createAlias);
                }
            }

            string staging = Path.Combine(container, $"fixture-progress-{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, new { kind, source, count, shape, next });
            File.Move(staging, progress, overwrite: true);
        }
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
