using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.App.ViewModels;

/// <summary>
/// Checksum manifests (plan §9.4): manifests are recognized by name but never hashed automatically; verification is an
/// explicit job, and its problems can be opened as a result set to act on.
/// </summary>
public sealed partial class MainViewModel
{
    private async Task VerifyChecksumsAsync(IReadOnlyList<ItemRef>? manifests = null)
    {
        if (manifests is null)
        {
            var sel = SourceSelection();
            if (sel is null) return;
            var files = sel.Value.Items.Where(i => !i.IsContainer && i.FileSystemPath is not null).ToList();
            // A file with an unusual name can still be verified when it is the only one chosen.
            manifests = files.Count == 1 ? files : files.Where(f => ChecksumManifests.IsManifestName(f.Name)).ToList();
            if (manifests.Count == 0)
            {
                Notify("Select a checksum manifest (.sha256, .sha512, .sha1, .md5, .sfv, SHA256SUMS…) to verify the files it lists.", true);
                return;
            }
            // A file whose name does not say it is a manifest is looked into first: a text file without a single checksum
            // line would otherwise "verify" nothing.
            if (manifests.Count == 1 && !ChecksumManifests.IsManifestName(manifests[0].Name) && manifests[0].FileSystemPath is { } path)
            {
                string? problem = await Task.Run(() =>
                {
                    try
                    {
                        return ChecksumManifests.Load(path).Verifiable > 0 ? null
                            : "none of its lines is a checksum line (as sha256sum and similar tools write them, BSD-tagged, or SFV).";
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        return ex.Message;
                    }
                });
                if (problem is not null)
                {
                    Notify($"\"{manifests[0].Name}\" is not a checksum manifest: {problem}", true);
                    return;
                }
            }
        }
        var job = Services.Jobs.Submit(new JobRequest { Kind = JobKind.VerifyChecksums, Sources = manifests });
        if (ActiveTab is { } tab) Track(job, tab);
    }

    /// <summary>Everything matched: a quiet note. Anything else: what went wrong, with the failing files one step away.</summary>
    private async Task OnVerifyFinishedAsync(Job job)
    {
        switch (job.State)
        {
            case JobState.Completed:
                Notify($"{job.Title}: {job.Summary}.");
                return;
            case JobState.Canceled:
                Notify($"{job.Title}: {job.Summary}.");
                return;
        }
        var problems = job.Issues.Where(i => i.Severity >= IssueSeverity.Warning).ToList();
        var failing = problems.Where(i => i.Severity == IssueSeverity.Error && File.Exists(i.Path)).Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var lines = problems.Take(8).Select(i => $"• {Path.GetFileName(i.Path)}: {i.Message}");
        string text = job.Summary + ".\n" + string.Join("\n", lines)
                      + (problems.Count > 8 ? $"\n… and {problems.Count - 8:N0} more (Ctrl+J shows all)." : "");
        var buttons = new List<DialogButton>();
        if (failing.Count > 0) buttons.Add(new DialogButton($"Show {Formatters.Plural(failing.Count, "file", "files")} in a panel", "show"));
        buttons.Add(new DialogButton("Close", "close", IsDefault: true, IsCancel: true));
        var answer = await Dialogs.ShowCustomAsync(job.Title,
            new Avalonia.Controls.TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 680 }, buttons);
        if (answer as string != "show") return;
        string manifest = job.Request.Sources.FirstOrDefault()?.FileSystemPath ?? "";
        string root = Path.GetDirectoryName(manifest) ?? "";
        var set = Services.ResultSets.Create($"Checksum problems: {Path.GetFileName(manifest)}", $"Files that failed verification against {manifest}");
        foreach (var path in failing)
        {
            string folder = Path.GetDirectoryName(path) ?? "";
            string relative = root.Length > 0 && Core.FileSystem.PathUtil.IsSameOrUnder(folder, root) ? Path.GetRelativePath(root, folder) : folder;
            set.Add(ItemRef.ForFileSystemPath(path, EntryKind.File), relative == "." ? "" : relative);
        }
        set.IsComplete = true;
        OpenResultSet(set, null);
    }
}
