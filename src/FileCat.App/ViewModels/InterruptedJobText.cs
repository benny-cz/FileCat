using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>Wording shared by both reviews of an interrupted operation: Operations' review and Run again.</summary>
internal static class InterruptedJobText
{
    /// <summary>Up to <paramref name="max"/> bullet lines, then how many more there are.</summary>
    public static string Bullets(IReadOnlyList<string> items, int max = 10) =>
        string.Join("\n", items.Take(max).Select(i => "• " + Formatters.SafeName(i))) + (items.Count > max ? $"\n• … and {items.Count - max:N0} more" : string.Empty);

    public static string TransferReview(JobKind kind, Location destination, IReadOnlyList<ItemRef> sources, string? newName,
        TransferOptions options, QueueMode mode, bool continuing)
    {
        string mapping = Bullets(sources.Select(item =>
        {
            string folder = options.Flatten || string.IsNullOrEmpty(item.RelativeFolder)
                ? destination.Path : RelativeFolders.Resolve(destination.Path, item.RelativeFolder);
            return item.FileSystemPath + " → " + Path.Combine(folder, sources.Count == 1 && newName is { Length: > 0 } ? newName : item.Name);
        }).ToArray(), max: RecoverableTransferIntent.RootLimit);
        string conflict = options.Conflicts switch
        {
            ConflictPolicy.Skip => "Skip existing files" + (continuing ? " (recovery policy)" : ""),
            ConflictPolicy.Replace => "Replace existing files",
            ConflictPolicy.ReplaceIfNewer => "Replace only if newer",
            ConflictPolicy.KeepBothRenameIncoming => "Keep both; rename the incoming item",
            ConflictPolicy.KeepBothRenameExisting => "Keep both; rename the existing item",
            _ => "Ask about each conflict",
        };
        return $"\n\nDestination: {Formatters.SafeName(destination.Path)}" +
            $"\nFiles matching: {(options.Filter is null ? "All files" : Formatters.SafeName(options.Filter.Text))}" +
            (options.Filter is null ? "" : "\nResolved matching rules: " + Formatters.SafeName(options.Filter.ResolvedDescription)) +
            $"\nFolder layout: {(options.Flatten ? "Put selected roots directly into the destination" : "Keep recorded relative folders")}" +
            $"\nVerification: {(options.Verify == VerifyMode.ReadBack ? "Read back and compare copied content" : "Size and metadata")}" +
            $"\nTimestamps: {(options.PreserveTimestamps ? "Preserve" : "Do not preserve")} · Attributes: {(options.PreserveAttributes ? "Preserve" : "Do not preserve")}" +
            $"\nSpeed limit: {(options.RateLimit == 0 ? "Unlimited" : options.RateLimit.ToString("N0") + " bytes/s")} · Start: {(mode == QueueMode.Queue ? "Queue" : "Now")}" +
            $"\nExisting items: {conflict}." +
            (options.Conflicts == ConflictPolicy.Skip
                ? " Existing files are kept; skipping does not verify their contents. Existing folders can receive missing descendants. Same-item aliases and incompatible file/folder types still use the existing safe decisions."
                : " Existing folders can receive missing descendants; conflicts are resolved using the policy just selected.") +
            (kind == JobKind.Move ? " A skipped destination does not authorize deleting a remaining source." : "") +
            "\n\nSource → destination mapping for every available root:\n" + mapping;
    }

    /// <summary>
    /// What recovery leaves as it is, as paragraphs to append (empty when there is nothing to say): files that differ from
    /// their source in a way an interrupted copy does not explain are never deleted (release issue I19).
    /// </summary>
    public static string CopyNotes(CopyReview review)
    {
        var text = string.Empty;
        if (review.Differing.Count > 0)
            text += $"\n\n{Formatters.Plural(review.Differing.Count, "file this operation may have copied differs", "files this operation may have copied differ")} from the source but not as a copy cut short does (changed since, or its source changed since). FileCat leaves them as they are; compare them with their sources yourself:\n"
                + Bullets(review.Differing);
        if (review.LimitReached)
            text += "\n\nNot every file this operation copied could be checked or listed here; after this, compare the destination with the source.";
        return text;
    }
}
