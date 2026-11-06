using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

if (args is not [var output] || !Path.IsPathFullyQualified(output) || !Directory.Exists(output)) return 64;
var index = new FileInfo(Path.Combine(output, "results.json"));
if (!index.Exists || index.Length > 64 << 10) return 65;
using var facts = JsonDocument.Parse(File.ReadAllBytes(index.FullName));
var cases = facts.RootElement.GetProperty("cases"); if (cases.GetArrayLength() != 8) return 66;
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
var archives = new ArchiveProvider(Path.Combine(output, "filecat-cache"), registry); registry.Register(archives);
var verified = new List<object>(); byte[] buffer = new byte[65536], expectedBuffer = new byte[65536];
foreach (var fixture in cases.EnumerateArray())
{
    deadline.Token.ThrowIfCancellationRequested(); string label = fixture.GetProperty("case").GetString()!;
    if (label.Length is <= 0 or > 32 || label.Any(c => c is not (>= 'a' and <= 'z'))) throw new InvalidDataException("Unexpected fixture label.");
    string name = fixture.GetProperty("name").GetString()!; long length = fixture.GetProperty("input").GetInt64();
    if (length < 0 || length > 1 << 20) throw new InvalidDataException("Fixture exceeds its scope.");
    string path = Path.Combine(output, label + ".rar"); if (new FileInfo(path).Length > 2 << 20) throw new InvalidDataException("Fixture archive exceeds its scope.");
    var location = archives.GetContainerLocation(path) ?? throw new InvalidDataException("RAR was not recognized.");
    var entries = new List<EntryData>(); var issues = new List<string>();
    await archives.EnumerateAsync(location, new Sink(entries, issues), deadline.Token);
    if (issues.Count != 0 || entries.Count != 1 || entries[0].Name != name || entries[0].IsContainer) throw new InvalidDataException("RAR member scope differs.");
    long modified = DateTime.UnixEpoch.Ticks + fixture.GetProperty("mtime_seconds").GetInt64() * TimeSpan.TicksPerSecond + fixture.GetProperty("mtime_nanos").GetInt64() / 100;
    if (entries[0].Modified != modified) throw new InvalidDataException("RAR displayed time differs from its supported tick precision.");
    using var content = archives.OpenContent(archives.GetItemRef(location, entries[0])) ?? throw new InvalidDataException("RAR member did not open.");
    using var expected = File.OpenRead(Path.Combine(output, label + ".expected"));
    if (content.Length != length || expected.Length != length) throw new InvalidDataException("RAR member length differs.");
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long offset = 0;
    while (offset < length)
    {
        deadline.Token.ThrowIfCancellationRequested(); int wanted = (int)Math.Min(buffer.Length, length - offset);
        int count = content.Read(offset, buffer.AsSpan(0, wanted)); if (count <= 0 || count > wanted) throw new InvalidDataException("Short RAR content.");
        expected.ReadExactly(expectedBuffer.AsSpan(0, count));
        if (!buffer.AsSpan(0, count).SequenceEqual(expectedBuffer.AsSpan(0, count))) throw new InvalidDataException("RAR bytes differ.");
        hash.AppendData(buffer, 0, count); offset += count;
    }
    if (content.Read(offset, buffer) != 0) throw new InvalidDataException("RAR has extra content.");
    string digest = Convert.ToHexString(hash.GetHashAndReset());
    if (!digest.Equals(fixture.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("RAR digest differs.");
    verified.Add(new { label, bytes = length, sha256 = digest });
}
File.WriteAllText(Path.Combine(output, "filecat-reader.json"), JsonSerializer.Serialize(new { reader = "FileCat existing RAR reader", productionBackendQualified = false, verified }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS: FileCat lists and decodes all eight independently encoded RAR fixtures exactly."); return 0;

internal sealed class Sink(List<EntryData> entries, List<string> issues) : IEnumerationSink
{
    public void AddBatch(ReadOnlySpan<EntryData> batch) { foreach (var entry in batch) entries.Add(entry); }
    public void ReportIssue(string message) => issues.Add(message);
}
