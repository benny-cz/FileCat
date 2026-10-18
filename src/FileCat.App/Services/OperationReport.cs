using System.Text;
using System.Text.Json;
using System.Globalization;
using FileCat.Core.Jobs;

namespace FileCat.App.Services;

/// <summary>Bounded ordinary-job report. Paths and reasons use JSON strings so hidden characters remain exact.</summary>
internal static class OperationReport
{
    internal const int ClipboardCharacterLimit = 4 * 1024 * 1024;
    internal const int ReadBatch = 256;

    internal sealed record Snapshot(Job Job, DateTime CapturedUtc, JobState State, JobIssueTotals Issues,
        long ItemsDone, long ItemsSkipped, long ItemsFailed, long ItemsTotal, bool TotalsFinal);

    internal static Snapshot Capture(Job job) => new(job, DateTime.UtcNow, job.State, job.IssueTotals,
        job.ItemsDone, job.ItemsSkipped, job.ItemsFailed, job.ItemsTotal, job.TotalsFinal);

    internal static string? ForClipboard(Snapshot snapshot, int maximumCharacters = ClipboardCharacterLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);
        var text = new StringBuilder();
        using var writer = new StringWriter(text);
        try
        {
            Write(writer, snapshot, CancellationToken.None, maximumCharacters);
            return text.ToString();
        }
        catch (ReportTooLargeException) { return null; }
    }

    internal static void Write(TextWriter writer, Snapshot snapshot, CancellationToken cancellationToken,
        int maximumCharacters = int.MaxValue)
    {
        void Line(string value = "")
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteLine(value);
            if (writer is StringWriter s && s.GetStringBuilder().Length > maximumCharacters)
                throw new ReportTooLargeException();
        }

        Line("FileCat operation report");
        Line("Title: " + Quoted(snapshot.Job.Title));
        Line("Captured (UTC): " + snapshot.CapturedUtc.ToString("O"));
        Line("State: " + snapshot.State.Describe());
        Line("Requested action: " + snapshot.Job.Kind);
        Line("Destination: " + (snapshot.Job.Request.Destination is { } destination ? Quoted(destination.ToString()) : "none"));
        Line($"Items: {Count(snapshot.ItemsDone)} done; {Count(snapshot.ItemsSkipped)} skipped; {Count(snapshot.ItemsFailed)} failed; " +
             (snapshot.TotalsFinal ? $"{Count(snapshot.ItemsTotal)} planned" : "total scope was still being counted"));
        Line($"Issues: {Count(snapshot.Issues.Total)} total; {Count(snapshot.Issues.Info)} info; {Count(snapshot.Issues.Warning)} warnings; {Count(snapshot.Issues.Error)} errors");
        Line($"Details: first {Count(snapshot.Issues.Retained)} retained; {Count(snapshot.Issues.Omitted)} omitted after the retention limit");
        Line("Quoted paths and reasons use JSON escaping; decoding the quoted value recovers the exact text.");

        int written = 0;
        while (written < snapshot.Issues.Retained)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = snapshot.Job.ReadIssues(written, Math.Min(ReadBatch, snapshot.Issues.Retained - written));
            if (batch.Count == 0) break;
            foreach (var issue in batch)
            {
                Line();
                Line($"[{Count(written + 1)}] {issue.Severity} · {issue.Outcome}");
                Line("Path: " + Quoted(issue.Path));
                Line("Source: " + (issue.SourcePath is { } source ? Quoted(source) : "not established by this issue"));
                if (issue.SourcePath is not null) Line("Source location: " + LocationKind(issue.SourceIsLocal));
                Line("Target: " + (issue.TargetPath is { } target ? Quoted(target) : "not established by this issue"));
                if (issue.TargetPath is not null) Line("Target location: " + LocationKind(issue.TargetIsLocal));
                Line("Requested action: " + (issue.RequestedAction ?? snapshot.Job.Kind.ToString()));
                Line("Committed effect: " + (issue.CommittedEffect ?? "not established by this issue"));
                Line("Source disposition: " + (issue.SourceDisposition ?? "not established by this issue"));
                Line("Target disposition: " + (issue.TargetDisposition ?? "not established by this issue"));
                Line("Certainty: " + (issue.Certainty ?? "see outcome and reason; no further claim"));
                Line("Reason: " + Quoted(issue.Message));
                if (issue.Cause is { } cause) Line("Cause: " + Quoted(cause));
                written++;
            }
        }
        if (written != snapshot.Issues.Retained)
            Line($"Report warning: only {Count(written)} of {Count(snapshot.Issues.Retained)} retained details were readable at export time.");
    }

    internal static void SaveAtomic(string destination, Snapshot snapshot, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(destination)) throw new ArgumentException("A local absolute report path is required.", nameof(destination));
        string directory = Path.GetDirectoryName(destination)!;
        string temp = Path.Combine(directory, ".filecat-report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                Write(writer, snapshot, cancellationToken);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Quoted(string value) => JsonSerializer.Serialize(value);
    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string LocationKind(bool? local) => local switch
    {
        true => "local filesystem",
        false => "non-local provider",
        null => "not established by this issue",
    };

    private sealed class ReportTooLargeException : Exception { }
}
