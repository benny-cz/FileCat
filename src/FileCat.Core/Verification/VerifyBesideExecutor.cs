using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;

namespace FileCat.Core.Verification;

/// <summary>
/// Checks the chosen files against the checksum files and signatures beside them (D-57), however large: a read-only,
/// cancellable job with byte progress. Each result is kept, so the files' rows show it. A mismatch or a bad signature is
/// an error, a result that vouches for nothing (an unknown key, no gpg) a warning, and a file nothing covers is skipped
/// with a note saying so.
/// </summary>
internal sealed class VerifyBesideExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public const string FailedCause = "verification-failed";

    public override void Execute()
    {
        var service = VerificationService.Current ?? throw new NotSupportedException("Checking files against the checksums beside them is not set up.");
        int good = 0, bad = 0, unsure = 0, uncovered = 0, unreadable = 0;
        var files = new List<(string Path, long Size)>();
        foreach (var source in Job.Request.Sources)
        {
            if (source.FileSystemPath is { } path && VerificationService.Stamp(path) is { } stamp)
            {
                files.Add((path, stamp.Size));
                continue;
            }
            unreadable++;
            Job.ItemFailed();
            Issue(IssueSeverity.Error, source.FileSystemPath ?? source.Name, "It is not there any more.", StepOutcome.Failed);
        }
        Job.AddTotals(files.Count + unreadable, files.Sum(f => f.Size));
        try
        {
            foreach (var (path, size) in files)
            {
                Job.Checkpoint();
                Job.SetCurrent(path);
                // A signature reads the file a second time: the bar counts each file once.
                long counted = 0;
                void Progress(long n)
                {
                    long add = Math.Min(n, size - counted);
                    if (add <= 0) return;
                    counted += add;
                    Job.AddBytes(add);
                }
                VerificationResult? result;
                try { result = service.OnRequest(path, Job.Token, Progress); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadable++;
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, path, "It could not be checked: " + ErrorText.Describe(ex), StepOutcome.Failed);
                    continue;
                }
                finally
                {
                    if (size > counted) Job.AddBytes(size - counted); // known already, or cut short
                }
                if (result is null or { State: VerificationState.Sidecar })
                {
                    uncovered++;
                    Job.ItemSkipped();
                    Issue(IssueSeverity.Info, path, result is null
                        ? "Nothing beside it says what it should be: no checksum file or signature in its folder names it."
                        : $"It is a checksum file or signature ({result.Text}): its own row shows no result; choose the files it covers.", StepOutcome.Skipped);
                    continue;
                }
                string details = string.Join(" ", result.Details);
                if (result.IsGood)
                {
                    good++;
                    Job.ItemDone();
                }
                else if (result.IsBad)
                {
                    bad++;
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, path, $"{result.Text}. {details}", StepOutcome.Failed, FailedCause);
                }
                else if (result.State == VerificationState.Unreadable)
                {
                    unreadable++;
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, path, details, StepOutcome.Failed);
                }
                else
                {
                    unsure++;
                    Job.ItemDone();
                    Issue(IssueSeverity.Warning, path, $"{result.Text}. {details}", StepOutcome.Committed);
                }
            }
        }
        finally
        {
            var parts = new List<string>();
            if (Job.IsCancellationRequested) parts.Add($"canceled after {good + bad + unsure + uncovered + unreadable:N0} of {files.Count:N0} files");
            if (good > 0 || bad + unsure + unreadable == 0 && uncovered == 0) parts.Add(bad + unsure + unreadable + uncovered == 0 && !Job.IsCancellationRequested ? $"{good:N0} verified, all good" : $"{good:N0} verified");
            if (bad > 0) parts.Add($"{bad:N0} failed");
            if (unsure > 0) parts.Add($"{unsure:N0} unsure");
            if (unreadable > 0) parts.Add($"{unreadable:N0} not read");
            if (uncovered > 0) parts.Add($"{uncovered:N0} with nothing to check against");
            Job.SetSummary(string.Join(", ", parts));
        }
    }
}
