using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Verification;

namespace FileCat.App.ViewModels;

/// <summary>
/// Checksum manifests (plan §9.4) and checksums and signatures beside files (D-57). Chosen files are checked against every
/// checksum file and signature beside them, however large; chosen checksum files are checked after that, for what that
/// check did not compare (files in other folders, missing ones, a list changed meanwhile); the rows show the results. Both
/// are explicit jobs; whatever they found opens as one set of results when the last ends (and again from the operations
/// strip), and their failing files can be opened as a result set.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// The jobs one Verify started (files beside checksum files, then checksum files): the last to end shows them all. Kept a
    /// while after, so the results can be shown again.
    /// </summary>
    private sealed class VerifyGroup(string? title, TabViewModel? origin)
    {
        /// <summary>Where it was asked for, while the tab still shows it (finished jobs unmark their files there).</summary>
        private readonly Location? _where = origin?.Location;

        public List<Job> Jobs { get; } = [];
        /// <summary>The results' title when it is not the job's own (a folder's check names the folder).</summary>
        public string? Title => title;
        /// <summary>The checksum files to check once the files beside them are checked, until they are submitted.</summary>
        public PendingLists? Then { get; set; }
        /// <summary>The one checksum file checked (alone): the failing files' list is named after it.</summary>
        public string? Manifest { get; init; }
        /// <summary>What the results say besides what the jobs found.</summary>
        public List<string> Notes { get; } = [];
        public int Ended { get; set; }

        public TabViewModel? Origin => origin is { } tab && tab.Location == _where ? tab : null;
    }

    /// <summary>Checksum files to check after the files beside them: the lines that check compared are left out.</summary>
    private sealed record PendingLists(List<ItemRef> Sources, bool IgnoreMissing, string? Description)
    {
        public JobRequest Request(ComparedLines? compared) => new()
        {
            Kind = JobKind.VerifyChecksums,
            Sources = Sources,
            IgnoreMissing = IgnoreMissing,
            Description = Description,
            ComparedBeside = compared,
        };
    }

    /// <summary>Checks with a job still to end, by job.</summary>
    private readonly Dictionary<Job, VerifyGroup> _runningChecks = [];
    /// <summary>Finished checks whose results can be shown again, the newest last; older ones let go of their results.</summary>
    private readonly List<VerifyGroup> _finishedChecks = [];
    private const int KeptVerifications = 8;

    /// <summary>Results of a check that took longer than this open with Enter and Esc held off a moment (keys may be meant for something else).</summary>
    internal TimeSpan VerifyKeysGuardedAfter { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Verify for the whole folder (chosen in the command search with nothing marked); a failure is shown, as for any command.</summary>
    private async void VerifyFolder()
    {
        try
        {
            await VerifyChecksumsAsync(wholeFolder: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Command {CommandIds.VerifyChecksums} failed", ex);
            Notify($"{Services.Commands.Get(CommandIds.VerifyChecksums)?.Title ?? CommandIds.VerifyChecksums} failed: {ex.Message}", true);
        }
    }

    /// <param name="chosenFiles">The files to verify (Calculate checksums… on checksum files); otherwise what is chosen in the active panel.</param>
    /// <param name="wholeFolder">
    /// Every file the active panel shows instead of what is chosen (the command search with nothing marked, or nothing
    /// chosen at all); its checksum files are checked as found, not chosen (a line for a file not there is left out).
    /// </param>
    private async Task VerifyChecksumsAsync(IReadOnlyList<ItemRef>? chosenFiles = null, bool wholeFolder = false)
    {
        // Where it was asked for, before anything is waited for (the active tab may change meanwhile).
        var origin = ActiveTab;
        string? title = null, description = null;
        IReadOnlyList<ItemRef> chosen;
        if (chosenFiles is not null) chosen = chosenFiles;
        else
        {
            // Nothing marked and nothing under the cursor (it is on ".."): the folder is what there is to check.
            wholeFolder |= origin is { Location.IsFileSystem: true, Listing: { HasMarks: false } listed }
                           && !(listed.TryGetFocused(out var focused) && focused.Kind != EntryKind.Parent);
            if (wholeFolder)
            {
                if (origin is not { Location.IsFileSystem: true }) return;
                // The results speak for the whole folder: checked in part (still listing, or a listing that broke off or was
                // stopped), it would say "all intact" of files it never saw.
                var shownNow = origin.Listing;
                if (!shownNow.IsWhole)
                {
                    Notify(shownNow.State == ListingState.Failed ? $"This folder could not be listed: {Formatters.SafeName(shownNow.Error ?? "")}"
                        : shownNow.IsPartial ? "Only part of this folder is listed (its listing broke off or was stopped): reread it to verify every file, or mark the files to check."
                        : "This folder is still being listed: verify it once all its files are shown.", true);
                    return;
                }
                if (origin.Listing.CaptureShownFiles() is not { } shown) return;
                chosen = shown;
                title = $"Verify checksums and signatures in \"{origin.Title}\"";
                description = $"Verify the files in \"{origin.Title}\"";
            }
            else
            {
                var sel = SourceSelection();
                if (sel is null) return;
                chosen = sel.Value.Items;
            }
        }
        VerificationPlan plan;
        try
        {
            plan = await PlanVerificationAsync(chosen, wholeFolder);
        }
        finally
        {
            // The plan holds items of its own: a captured selection is let go of.
            if (!ReferenceEquals(chosen, chosenFiles)) ItemSources.Release(chosen);
        }
        if (plan.Beside.Count == 0 && plan.Lists.Count == 0)
        {
            if (plan.Unread is { } why)
                Notify($"The checksum files and signatures here could not be read: {Formatters.SafeName(why)}", true);
            else if (wholeFolder)
                Notify("Nothing in this folder says what its files should be: no checksum file (SHA256SUMS, name.sha256, .md5, .sfv…) "
                       + "or signature (name.asc, .sig, .minisig) here names a file that is here. To compare with a checksum from a web page, "
                       + "use File → Calculate checksums and paste it there.", true);
            else if (plan.NotOnDisk)
                Notify("Checksums and signatures are verified for files on a disk: copy these out of the archive or off the server first.", true);
            else if (plan.OnlyFolders)
                Notify("Verifying checks files, not folders: to check every file in a folder, open it and choose Verify with nothing marked "
                       + "from the command search (or with the cursor on \"..\"); or mark the files to check.", true);
            else
                Notify("Nothing beside the chosen files says what they should be: choose files next to a checksum file (SHA256SUMS, name.sha256, .md5, .sfv…) "
                       + "or a signature (name.asc, .sig, .minisig), or a checksum file itself. To compare with a checksum from a web page, "
                       + "use File → Calculate checksums and paste it there.", true);
            return;
        }
        // A single file whose name does not say it lists checksums, and that nothing names, is looked into first: a text file
        // without a single checksum line would otherwise "verify" nothing.
        if (plan.Probe && plan.Lists is [{ FileSystemPath: { } path } probed])
        {
            string? problem = await Task.Run(() =>
            {
                try
                {
                    return ChecksumManifests.Load(path).Verifiable > 0 ? null
                        : "none of its lines is a checksum line (as sha256sum and similar tools write them, BSD-tagged, or SFV).";
                }
                catch (InvalidDataException)
                {
                    return "it is too large to be a list of checksums.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return ex.Message;
                }
            });
            if (problem is not null)
            {
                Notify($"\"{Formatters.SafeName(probed.Name)}\" is not a checksum file, and no checksum file or signature beside it names it: {Formatters.SafeName(problem)}", true);
                return;
            }
        }
        var lists = plan.Lists.Count == 0 ? null
            : new PendingLists(plan.Lists, IgnoreMissing: wholeFolder, wholeFolder ? $"Verify the checksum files in \"{origin!.Title}\"" : null);
        var group = new VerifyGroup(title, origin) { Manifest = plan.Beside.Count == 0 && plan.Lists is [{ FileSystemPath: { } only }] ? only : null };
        if (plan.Beside.Count == 0)
        {
            StartCheck(group, lists!.Request(null));
            return;
        }
        // The chosen checksum files go after the files beside them: then only lines that check did not compare are read again.
        group.Then = lists;
        StartCheck(group, new JobRequest { Kind = JobKind.VerifyBeside, Sources = plan.Beside, Description = description });
    }

    /// <summary>Starts one of a check's jobs; its group shows the results once the last has ended.</summary>
    private void StartCheck(VerifyGroup group, JobRequest request)
    {
        Services.Jobs.SubmitWithRegistration(request, job =>
        {
            group.Jobs.Add(job);
            _runningChecks[job] = group;
            if (group.Origin is { } origin) Track(job, origin);
        });
    }

    /// <summary>What one Verify checks, and how (worked out off the UI thread: it reads the folders' checksum files).</summary>
    /// <param name="Beside">Files to check against everything beside them (every claim on them, signatures included), and files nothing names, to say so.</param>
    /// <param name="Lists">Chosen checksum files, checked after that for the lines it did not compare: files in other folders, missing ones.</param>
    /// <param name="Probe">The one chosen file, nothing names it, is to be tried as a list of checksums (it is the one in <paramref name="Lists"/>).</param>
    /// <param name="Unread">Why a folder's checksum files and signatures could not be read, when one could not.</param>
    /// <param name="OnlyFolders">Only folders were chosen.</param>
    /// <param name="NotOnDisk">Only files not on a disk were chosen (in an archive, on a server).</param>
    private sealed record VerificationPlan(List<ItemRef> Beside, List<ItemRef> Lists, bool Probe, string? Unread, bool OnlyFolders, bool NotOnDisk);

    /// <summary>
    /// Splits what is chosen (D-57) so each file is checked once and no claim is left out: a signature stands for the file it
    /// signs; files a checksum file or signature beside them names are checked against all of it; a checksum file holding a
    /// bare checksum stands for the file it is named after; other chosen checksum files are checked after those files, for
    /// what that check did not compare. Chosen files nothing names are listed to say so (when anything is checked); a single
    /// chosen file nothing names is tried as a list of checksums. Each file counts once, by its folder's rule for letter case.
    /// </summary>
    private static Task<VerificationPlan> PlanVerificationAsync(IReadOnlyList<ItemRef> chosen, bool wholeFolder)
    {
        var service = VerificationService.Current;
        return Task.Run(() =>
        {
            string? unread = null;
            // Each folder's checksum files and signatures, looked up once (a folder of many files asks for the same one each time).
            var folders = new Dictionary<string, FolderSidecars?>(StringComparer.Ordinal);
            FolderSidecars? SidecarsOf(string folder)
            {
                if (service is null) return null;
                if (folders.TryGetValue(folder, out var known)) return known;
                FolderSidecars? found;
                // A folder that cannot be read leaves the others to be checked (its files then have nothing to check against).
                try { found = service.SidecarsOf(folder, CancellationToken.None); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warn($"The checksum files in {folder} could not be read", ex);
                    unread ??= ex.Message;
                    found = null;
                }
                return folders[folder] = found;
            }
            bool Exists(string path)
            {
                try { return File.Exists(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            }
            // One key per file however its path is spelled in its folder: a case-sensitive folder may hold "a.iso" and "A.ISO".
            var fileKeys = new SameFileKeys();
            string KeyOf(string path) => fileKeys.Of(path) ?? path;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var files = new List<ItemRef>();
            bool anyFile = false, elsewhere = false;
            foreach (var item in chosen)
            {
                if (item.IsContainer) continue;
                if (item.FileSystemPath is not { } path)
                {
                    elsewhere = true;
                    continue;
                }
                anyFile = true;
                // A signature stands for the file it signs.
                var file = SidecarNames.IsSignatureFile(item.Name) && SidecarNames.Signed(path) is var signed && Exists(signed)
                    ? ItemRef.ForFileSystemPath(signed, EntryKind.File) : item;
                if (keys.Add(KeyOf(file.FileSystemPath!))) files.Add(file);
            }
            var beside = new List<ItemRef>();
            var lists = new List<ItemRef>();
            var unnamed = new List<ItemRef>();
            foreach (var file in files)
            {
                string path = file.FileSystemPath!, name = Path.GetFileName(path);
                var sidecars = Path.GetDirectoryName(path) is { } folder ? SidecarsOf(folder) : null;
                if (!SidecarNames.IsChecksumFile(name))
                {
                    if (sidecars?.Covers(name) == true) beside.Add(file);
                    else unnamed.Add(file);
                }
                // A checksum file holding a bare checksum (as the scan read it) stands for the file it is named after.
                else if (sidecars is not null && sidecars.IsBare(name) && sidecars.Covered(name) is { Count: > 0 } bare)
                {
                    foreach (string target in bare)
                    {
                        var targetFile = ItemRef.ForFileSystemPath(Path.Join(sidecars.Folder, target), EntryKind.File);
                        if (keys.Add(KeyOf(targetFile.FileSystemPath!))) beside.Add(targetFile);
                    }
                }
                else lists.Add(file);
            }
            // A chosen signature whose file is not here says so in the results (outside a whole folder, where it is one file of many).
            bool orphan = !wholeFolder && unnamed.Any(f => SidecarNames.IsSignatureFile(f.Name));
            // A single file nothing names, whatever its name (a signature aside): perhaps a list of checksums.
            if (!wholeFolder && files.Count == 1 && unnamed.Count == 1 && !orphan) return new VerificationPlan([], unnamed, Probe: true, unread, OnlyFolders: false, NotOnDisk: false);
            // Files nothing names are listed too ("nothing to check against"; a chosen checksum file may still check them), when
            // anything is checked at all.
            if (beside.Count > 0 || lists.Count > 0 || orphan) beside.AddRange(unnamed);
            return new VerificationPlan(beside, lists, Probe: false, unread, OnlyFolders: !anyFile && !elsewhere, NotOnDisk: !anyFile && elsewhere);
        });
    }

    /// <summary>
    /// Whatever the check found, its results open (D-57), once for all the jobs one Verify started: every file with its claims
    /// in full, and the failing files one step from a list in a panel. Only the rows of the files read are asked again:
    /// nothing else changed. The chosen checksum files are checked once the files beside them are.
    /// </summary>
    private async Task OnVerifyFinishedAsync(Job job)
    {
        try
        {
            var report = job.VerificationReport;
            // The rows of the files read show what the job found (it kept its results and the hashes it read). A check past
            // what the results list (a very long checksum file) has no list of every file: then every row asks again.
            if (report is null || report.Unlisted > 0) Services.Metadata.Invalidate();
            else Services.Metadata.Forget("verified", [.. report.Files.Select(f => f.Path).OfType<string>()]);
            if (!_runningChecks.Remove(job, out var group))
            {
                group = new VerifyGroup(null, null);
                group.Jobs.Add(job);
            }
            group.Ended++;
            var compared = report?.TakeCompared();
            if (group.Then is { } lists)
            {
                group.Then = null;
                // Canceled, the check is over: the checksum files are not started after all.
                if (job.State != JobState.Canceled)
                {
                    StartCheck(group, lists.Request(compared));
                    return;
                }
                group.Notes.Add(lists.Sources.Count == 1
                    ? $"{lists.Sources[0].Name} was not checked: the check was canceled first."
                    : $"The {lists.Sources.Count:N0} checksum files were not checked: the check was canceled first.");
            }
            if (group.Ended < group.Jobs.Count) return;
            _finishedChecks.Add(group);
            while (_finishedChecks.Count > KeptVerifications)
            {
                foreach (var old in _finishedChecks[0].Jobs)
                {
                    old.ReleaseVerificationReport();
                    // Its Results button goes with them.
                    Operations.Refresh(old);
                }
                _finishedChecks.RemoveAt(0);
            }
            // A check that took a while may end while the user does something else: keys pressed for that do not close it, and
            // while they type (a rename, a path) the results wait: a dialog would take the keyboard and end what they type.
            bool unexpected = DateTime.UtcNow - group.Jobs.Min(j => j.CreatedUtc) > VerifyKeysGuardedAfter;
            if (unexpected && Typing())
            {
                Notify("The check has ended: its results open once you are done typing (Results in the operations strip shows them now).");
                while (Typing()) await Task.Delay(250);
                if (!_finishedChecks.Contains(group) || group.Jobs.All(j => j.VerificationReport is null)) return;
            }
            await ShowVerificationAsync(group, unexpected);
        }
        catch (Exception ex)
        {
            AppLog.Error("Showing verification results failed", ex);
            Notify($"{Formatters.SafeName(job.Title)}: {Formatters.SafeName(job.Summary)}. The results could not be shown: {ex.Message}", true);
        }
    }

    /// <summary>The keyboard is in a text box (a rename, the path, the command search) or in a dialog.</summary>
    private bool Typing() => Dialogs.IsOpen || View.TopLevel?.FocusManager?.GetFocusedElement() is Avalonia.Controls.TextBox;

    /// <summary>Shows a finished check's results again (the operations strip's Results), while they are kept.</summary>
    internal async Task ShowVerificationResultsAsync(Job job)
    {
        var group = _finishedChecks.LastOrDefault(g => g.Jobs.Contains(job));
        if (group is null && _runningChecks.Values.Any(g => g.Jobs.Contains(job)))
        {
            Notify("The rest of this check is still running: its results open by themselves when it ends.");
            return;
        }
        if (group is null || group.Jobs.All(j => j.VerificationReport is null))
        {
            Notify("These results are no longer kept: verify the files again to see them.");
            return;
        }
        await ShowVerificationAsync(group, guardKeys: false);
    }

    private async Task ShowVerificationAsync(VerifyGroup group, bool guardKeys)
    {
        var reports = group.Jobs.Select(j => j.VerificationReport).OfType<VerificationReport>().ToList();
        // Canceled while it waited its turn: nothing was read, which the results say (and what that left unchecked).
        if (reports.Count == 0 && group.Jobs.All(j => j.State == JobState.Canceled)) reports.Add(new VerificationReport(""));
        if (reports.Count == 0)
        {
            // It ended before it began (not set up, say): what went wrong is among its issues.
            var first = group.Jobs[0];
            Notify($"{Formatters.SafeName(first.Title)}: {Formatters.SafeName(JobIssue.Summarize(first.Issues) ?? first.Summary)}", first.State == JobState.Failed);
            return;
        }
        // Put together, each file once, and the failing files still there found: off the UI thread (they may be on a share).
        var (all, failing) = await Task.Run(() =>
        {
            var all = VerificationReport.Combine(reports);
            var failing = all.Files
                .Where(f => f.Outcome is VerifiedOutcome.Failed or VerifiedOutcome.Unreadable && f.Path is { } p && File.Exists(p))
                .DistinctBy(f => f.Path!, PathUtil.SafetyComparer).ToList();
            return (all, failing);
        });
        var answer = await Views.VerificationResultsDialog.ShowAsync(this, group.Jobs, all, failing, group.Title, group.Notes, guardKeys);
        if (answer as string != Views.VerificationResultsDialog.ShowFailed) return;
        string root = all.Folder;
        var set = group.Manifest is { } manifest
            ? Services.ResultSets.Create($"Checksum problems: {Path.GetFileName(manifest)}", $"Files that failed verification against {manifest}")
            : Services.ResultSets.Create("Verification problems", $"Files that failed their checksums or signatures in {root}");
        foreach (var file in failing)
        {
            string path = file.Path!, folder = Path.GetDirectoryName(path) ?? "";
            string relative = root.Length > 0 && PathUtil.IsSameOrUnder(folder, root) ? Path.GetRelativePath(root, folder) : folder;
            set.Add(ItemRef.ForFileSystemPath(path, EntryKind.File), relative == "." ? "" : relative);
        }
        set.IsComplete = true;
        OpenResultSet(set, null);
    }
}
