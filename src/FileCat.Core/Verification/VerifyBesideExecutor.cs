using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;

namespace FileCat.Core.Verification;

/// <summary>
/// Checks the chosen files against the checksum files and signatures beside them (D-57), however large: a read-only,
/// cancellable job with byte progress. Each result is kept, so the files' rows show it. A mismatch or a bad signature is
/// an error, a result that vouches for nothing (an unknown key, no gpg) a warning, and a file nothing covers is skipped
/// with a note saying so. The checksum lines it compared are noted in its report: chosen checksum files, checked after it,
/// leave them out.
/// </summary>
internal sealed class VerifyBesideExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public const string FailedCause = "verification-failed";

    public override void Execute()
    {
        var service = VerificationService.Current ?? throw new NotSupportedException("Checking files against the checksums beside them is not set up.");
        int good = 0, bad = 0, unsure = 0, uncovered = 0, unreadable = 0, missing = 0;
        var files = new List<(string Path, long Size)>();
        string folder = Job.Request.Sources.Select(s => s.FileSystemPath).OfType<string>().Select(Path.GetDirectoryName).FirstOrDefault() ?? "";
        var report = Job.VerificationReport = new VerificationReport(folder);
        // Each folder's checksum files and signatures are read once for the whole job, each list's signature checked once.
        var scope = new VerificationService.RequestScope();
        // Named as in the folder the check started from; a file elsewhere (a list of found files) by its whole path.
        string NameOf(string path) => string.Equals(Path.GetDirectoryName(path), folder, PathUtil.SafetyComparison) ? Path.GetFileName(path) : path;
        void Gone(string path, string name, string why)
        {
            missing++;
            Job.ItemFailed();
            Issue(IssueSeverity.Error, path, why, StepOutcome.Failed);
            report.Add(new VerifiedFile(path, name, VerifiedOutcome.Missing, "not there any more") { Notes = [why] });
        }
        foreach (var source in Job.Request.Sources)
        {
            if (source.FileSystemPath is { } path && VerificationService.Stamp(path) is { } stamp)
            {
                files.Add((path, stamp.Size));
                continue;
            }
            Gone(source.FileSystemPath ?? source.Name, source.FileSystemPath is { } gone ? NameOf(gone) : source.Name,
                "It is not there any more: it was moved or deleted after it was chosen.");
        }
        int goneBefore = missing;
        // Folder by folder (files from a list of found files may come from many): each folder's sidecars are read once.
        files = [.. files.OrderBy(f => Path.GetDirectoryName(f.Path), StringComparer.Ordinal)];
        Job.AddTotals(files.Count + missing, files.Sum(f => f.Size));
        bool completed = false;
        try
        {
            foreach (var (path, size) in files)
            {
                Job.Checkpoint();
                Job.SetCurrent(path);
                // A signature reads the file a second time: the bar counts each file once, what was read counts it all.
                long counted = 0;
                void Progress(long n)
                {
                    Job.AddDataProcessed(n);
                    long add = Math.Min(n, size - counted);
                    if (add <= 0) return;
                    counted += add;
                    Job.AddBytes(add);
                }
                VerificationResult? result;
                try { result = service.OnRequest(path, Job.Token, Progress, scope); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (VerificationService.Stamp(path) is null)
                    {
                        Gone(path, NameOf(path), "It is not there any more: it was moved or deleted while the check ran.");
                        continue;
                    }
                    unreadable++;
                    Job.ItemFailed();
                    string why = "It could not be checked: " + ErrorText.Describe(ex);
                    Issue(IssueSeverity.Error, path, why, StepOutcome.Failed);
                    report.Add(new VerifiedFile(path, NameOf(path), VerifiedOutcome.Unreadable, "could not be read") { Notes = [why] });
                    continue;
                }
                finally
                {
                    if (size > counted) Job.AddBytes(size - counted); // known already, or cut short
                }
                // A file that went while it waited, or while it was read, is missing, not "unreadable" or covered by nothing.
                if (result is null or { State: VerificationState.Unreadable } && VerificationService.Stamp(path) is null)
                {
                    Gone(path, NameOf(path), "It is not there any more: it was moved or deleted while the check ran.");
                    continue;
                }
                if (result is null or { State: VerificationState.Sidecar })
                {
                    uncovered++;
                    Job.ItemSkipped();
                    string name = Path.GetFileName(path);
                    (string text, string why) = result is not null
                        ? (result.Text, $"It is a checksum file or signature ({result.Text}): its own row shows no result; choose the files it covers.")
                        : SidecarNames.IsSignatureFile(name)
                            ? ("signs a file that is not here", $"It is a signature, but the file it signs ({SidecarNames.Signed(name)}) is not here.")
                            : ("nothing to check against", "Nothing beside it says what it should be: no checksum file or signature in its folder names it.");
                    Issue(IssueSeverity.Info, path, why, StepOutcome.Skipped);
                    report.Add(new VerifiedFile(path, NameOf(path), VerifiedOutcome.NotChecked, text) { Notes = [why] });
                    continue;
                }
                // The job's counts and the results shown when it ends come from the same outcome.
                var entry = VerificationReport.Entry(path, NameOf(path), result);
                report.Add(entry);
                report.Compared(path, result.Checksums);
                string details = string.Join(" ", result.Details);
                switch (entry.Outcome)
                {
                    case VerifiedOutcome.Good:
                        good++;
                        Job.ItemDone();
                        break;
                    case VerifiedOutcome.Failed:
                        bad++;
                        Job.ItemFailed();
                        Issue(IssueSeverity.Error, path, $"{result.Text}. {details}", StepOutcome.Failed, FailedCause);
                        break;
                    case VerifiedOutcome.Unreadable:
                        unreadable++;
                        Job.ItemFailed();
                        Issue(IssueSeverity.Error, path, details, StepOutcome.Failed);
                        break;
                    case VerifiedOutcome.Unsure:
                        unsure++;
                        Job.ItemDone();
                        Issue(IssueSeverity.Warning, path, $"{result.Text}. {details}", StepOutcome.Committed);
                        break;
                    default:
                        uncovered++;
                        Job.ItemSkipped();
                        Issue(IssueSeverity.Info, path, details.Length > 0 ? details : "Nothing was compared.", StepOutcome.Skipped);
                        break;
                }
            }
            completed = true;
            report.IsComplete = true;
        }
        finally
        {
            var parts = new List<string>();
            int reached = good + bad + unsure + uncovered + unreadable + missing - goneBefore;
            bool ended = completed && !Job.IsCancellationRequested;
            // Never "all good" about a check that did not reach every file.
            if (!ended) parts.Add($"{(Job.IsCancellationRequested ? "canceled" : "stopped by an error")} after {reached:N0} of {files.Count:N0} files");
            bool clean = ended && bad + unsure + unreadable + missing + uncovered == 0;
            if (good > 0 || clean) parts.Add(clean ? $"{good:N0} verified, all good" : $"{good:N0} verified");
            if (bad > 0) parts.Add($"{bad:N0} failed");
            if (unsure > 0) parts.Add($"{unsure:N0} unsure");
            if (missing > 0) parts.Add($"{missing:N0} missing");
            if (unreadable > 0) parts.Add($"{unreadable:N0} not read");
            if (uncovered > 0) parts.Add($"{uncovered:N0} with nothing to check against");
            Job.SetSummary(string.Join(", ", parts));
        }
    }
}
