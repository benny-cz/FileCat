using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Jobs;
using FileCat.Core.State;

namespace FileCat.App.Services;

/// <summary>A bounded, read-only diagnostic snapshot. It never grants recovery or file-mutation authority.</summary>
public sealed record RecoveryReviewRecord(string JournalPath, string Title, string Kind, DateTime CapturedUtc,
    string Action, string Report, bool ClosureConfirmed)
{
    public StorageReviewAcknowledgment? StorageAcknowledgment { get; init; }
    public string? StorageRetainedLeaf { get; init; }
    public bool StorageVerified { get; init; }
    public bool StorageEffectUncertain { get; init; }
    internal long StorageOrder { get; init; }
    public bool IsStorage => Kind == nameof(JobKind.StorageTransform);
    public string Display => Formatters.SafeName(Title) + (CapturedUtc == DateTime.MinValue ? "" : " · " + CapturedUtc.ToLocalTime().ToString("g")) +
                             (StorageRetainedLeaf is not null ? !StorageVerified ? " · storage history verification required"
                                 : StorageEffectUncertain ? " · Reviewed; effect remains uncertain" : " · storage history acknowledged"
                                 : StorageAcknowledgment is { } storage ? storage.Reviewed.EffectUncertain || storage.Reviewed.Damaged || storage.Reviewed.LimitReached
                                 ? " · Reviewed; effect remains uncertain" : " · storage history acknowledged"
                                 : ClosureConfirmed ? " · recovery tracking closed" : " · closure not confirmed");
    public string OpenLabel => (IsStorage ? "Open retained storage history for " : "Open reviewed recovery report for ") + Formatters.SafeName(Title);
}

internal static class RecoveryReviewStore
{
    internal const int KeepRecords = 200;
    internal const int MaxReportCharacters = 4 * 1024 * 1024;
    private const long MaxStoredBytes = 12L * 1024 * 1024;

    internal static string DirectoryOf(string journalDirectory) => Path.Combine(journalDirectory, "reviewed");

    internal static IReadOnlyList<RecoveryReviewRecord> Load(string journalDirectory)
    {
        // An elevated or root FileCat reads none of the profile's records, which the ordinary account can change, as it
        // lists no journal (JournalRecovery.Scan): it shows no reviewed report either (I655).
        if (ProcessAuthority.IsElevatedOrRoot) return [];
        string directory = DirectoryOf(journalDirectory);
        if (!Directory.Exists(directory)) return [];
        var records = new List<RecoveryReviewRecord>();
        foreach (var file in Newest(directory))
        {
            try
            {
                if (file.Length > MaxStoredBytes) continue;
                using var stream = file.OpenRead();
                var record = JsonSerializer.Deserialize<RecoveryReviewRecord>(stream);
                if (record is null || string.IsNullOrWhiteSpace(record.Title) ||
                    record.Kind == nameof(JobKind.StorageTransform) || record.StorageAcknowledgment is not null ||
                    record.Report is null || record.Report.Length > MaxReportCharacters ||
                    record.JournalPath is null || record.Action is null) continue;
                records.Add(record);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return records;
    }

    internal static void Save(string journalDirectory, RecoveryReviewRecord record)
    {
        // Saving also prunes old records in a folder the ordinary account can replace with a link (I645's pattern). Only
        // the recovery of a listed journal saves, and an elevated or root FileCat lists none; refuse outright all the same.
        if (ProcessAuthority.IsElevatedOrRoot)
            throw new IOException("FileCat does not keep recovery reports while running " + ProcessAuthority.RunningAs + ".");
        if (record.Kind == nameof(JobKind.StorageTransform) || record.StorageAcknowledgment is not null)
            throw new InvalidOperationException("Storage review reports must use the exact-snapshot Core acknowledgment path.");
        ArgumentException.ThrowIfNullOrWhiteSpace(record.JournalPath);
        if (record.Report.Length > MaxReportCharacters)
            throw new InvalidDataException("The recovery report exceeds the retained-detail budget; its journal remains open.");
        string directory = DirectoryOf(journalDirectory);
        Directory.CreateDirectory(directory);
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(record.JournalPath))));
        string destination = Path.Combine(directory, "review-" + digest + ".json");
        string temp = Path.Combine(directory, ".review-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            {
                JsonSerializer.Serialize(stream, record);
                stream.Flush(flushToDisk: true);
                if (stream.Length > MaxStoredBytes) throw new InvalidDataException("The recovery report is too large to retain; its journal remains open.");
            }
            File.Move(temp, destination, overwrite: true);
            Prune(directory, destination);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void Prune(string directory, string newestRecord)
    {
        var keep = Newest(directory, newestRecord).Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Fixed name pattern and FileInfo.FullName keep deletion within the private review directory.
        foreach (var old in new DirectoryInfo(directory).EnumerateFiles("review-*.json"))
        {
            if (keep.Contains(old.FullName)) continue;
            try { old.Delete(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static IReadOnlyList<FileInfo> Newest(string directory, string? preserve = null)
    {
        var latest = new PriorityQueue<FileInfo, DateTime>();
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("review-*.json"))
        {
            DateTime written = string.Equals(file.FullName, preserve, StringComparison.OrdinalIgnoreCase)
                ? DateTime.MaxValue : file.LastWriteTimeUtc;
            if (latest.Count < KeepRecords) latest.Enqueue(file, written);
            else if (latest.TryPeek(out _, out var oldest) && written > oldest)
            {
                latest.Dequeue();
                latest.Enqueue(file, written);
            }
        }
        return latest.UnorderedItems.OrderByDescending(i => i.Priority).Select(i => i.Element).ToArray();
    }
}
