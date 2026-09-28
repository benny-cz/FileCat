using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Operations;

/// <summary>How <see cref="JobKind.CreateLink"/> links are made; <see cref="RelativeTarget"/> applies to symbolic links.</summary>
public sealed record LinkOptions(LinkKind Kind, bool RelativeTarget = false);

/// <summary>One planned link: <see cref="LinkPath"/> will point to <see cref="Target"/> (stored as <see cref="TargetText"/>).</summary>
public sealed record LinkPreview(ItemRef Item, string LinkPath, string Target, string TargetText, string? Problem);

/// <summary>
/// Link creation (FAR's Create link; plan §23.3): checks every link against what the link's drive, the target's drive,
/// and the target's type allow before anything is created, so problems are explained rather than discovered.
/// </summary>
public static class LinkPlanner
{
    public static string KindName(LinkKind kind) => kind switch
    {
        LinkKind.Junction => "junction",
        LinkKind.Hard => "hard link",
        _ => "symbolic link",
    };

    /// <summary>Why this kind of link cannot be created here, or null.</summary>
    public static string? Check(LinkKind kind, bool targetIsDirectory, VolumeInfo linkVolume, VolumeInfo targetVolume)
    {
        string Drive(VolumeInfo v) => v.FileSystem is { Length: > 0 } f ? $"The link's drive ({f})" : "The link's drive";
        switch (kind)
        {
            case LinkKind.Hard:
                if (targetIsDirectory) return "Hard links point to files; use a junction or a symbolic link for a folder.";
                if (!PathUtil.SafetyComparer.Equals(linkVolume.DeviceKey, targetVolume.DeviceKey)) return "A hard link must be on the same drive as its file.";
                if (!linkVolume.SupportsHardLinks) return $"{Drive(linkVolume)} does not support hard links.";
                return null;
            case LinkKind.Junction:
                if (!OperatingSystem.IsWindows()) return "Junctions exist only on Windows; use a symbolic link.";
                if (!targetIsDirectory) return "Junctions point to folders; use a symbolic link or a hard link for a file.";
                if (linkVolume.IsRemote || targetVolume.IsRemote) return "Junctions work only between local drives; use a symbolic link for network locations.";
                if (!linkVolume.SupportsSymbolicLinks) return $"{Drive(linkVolume)} does not support junctions.";
                return null;
            default:
                if (!linkVolume.SupportsSymbolicLinks) return $"{Drive(linkVolume)} does not support symbolic links.";
                return null;
        }
    }

    /// <summary>The target as the link stores it: relative to the link's folder when asked and possible (same root).</summary>
    public static string TargetText(LinkKind kind, string target, string linkPath, bool relative)
    {
        if (kind != LinkKind.Symbolic || !relative) return target;
        string relativePath = Path.GetRelativePath(Path.GetDirectoryName(linkPath)!, target);
        return Path.IsPathFullyQualified(relativePath) ? target : relativePath;
    }

    public static IReadOnlyList<LinkPreview> Preview(IReadOnlyList<ItemRef> items, string destinationFolder, IReadOnlyList<string>? names,
        LinkOptions options, Func<string, VolumeInfo> volumeOf, Func<string, bool> exists)
    {
        var linkVolume = volumeOf(destinationFolder);
        var volumes = new Dictionary<string, VolumeInfo>(PathUtil.SafetyComparer);
        var seen = new HashSet<string>(PathUtil.SafetyComparer);
        var rows = new List<LinkPreview>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            string name = names?[i] ?? item.Name;
            string linkPath = Path.Combine(destinationFolder, name);
            if (item.FileSystemPath is not { } target)
            {
                rows.Add(new LinkPreview(item, linkPath, item.Name, item.Name, "Links point only to files and folders on disk."));
                continue;
            }
            string folder = Path.GetDirectoryName(target) ?? target;
            if (!volumes.TryGetValue(folder, out var targetVolume)) volumes[folder] = targetVolume = volumeOf(folder);
            string text = TargetText(options.Kind, target, linkPath, options.RelativeTarget);
            string? problem = PathUtil.ValidateNewName(name)
                ?? Check(options.Kind, item.IsContainer, linkVolume, targetVolume)
                ?? (PathUtil.SafetyComparer.Equals(linkPath, target) ? "The link would take the place of the item itself; choose another folder or name." : null)
                ?? (item.IsContainer && options.Kind != LinkKind.Hard && PathUtil.IsSameOrUnder(linkPath, target)
                    ? "The link would be inside the folder it points to, which makes a loop." : null)
                ?? (!seen.Add(linkPath) ? "Another link gets the same name." : null)
                ?? (exists(linkPath) ? "An item with this name exists." : null);
            rows.Add(new LinkPreview(item, linkPath, target, text, problem));
        }
        return rows;
    }
}

/// <summary>
/// Creates links: <see cref="JobRequest.Sources"/> are the targets, <see cref="JobRequest.Destination"/> the folder, and
/// <see cref="JobRequest.NewNames"/> optionally the link names. Each created link can be undone while it is unchanged.
/// </summary>
internal sealed class LinkExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public override void Execute()
    {
        var r = Job.Request;
        var options = r.Link ?? throw new InvalidOperationException("Link options are required.");
        if (r.Destination is not { IsFileSystem: true } destination) throw new NotSupportedException("Links are created only in folders on disk.");
        if (r.NewNames is { } names && names.Count != r.Sources.Count) throw new InvalidOperationException("Every link needs exactly one name.");
        Job.AddTotals(r.Sources.Count, 0);
        for (int i = 0; i < r.Sources.Count; i++)
        {
            Job.Checkpoint();
            var item = r.Sources[i];
            string name = r.NewNames?[i] ?? item.Name;
            string linkPath = Path.Combine(destination.Path, name);
            Job.SetCurrent(linkPath);
            string? problem = item.FileSystemPath is null ? "Links point only to files and folders on disk." : PathUtil.ValidateNewName(name);
            var info = item.FileSystemPath is { } p ? Fs.TryGetInfo(p) : null;
            if (problem is null && info is null) problem = "The item no longer exists.";
            if (problem is null && Fs.TryGetInfo(linkPath) is not null) problem = "An item with this name exists now.";
            if (problem is not null)
            {
                Fail(i, linkPath, problem);
                continue;
            }
            string target = item.FileSystemPath!;
            string text = LinkPlanner.TargetText(options.Kind, target, linkPath, options.RelativeTarget);
            int step = Journal.Intent("create-link", linkPath, text);
            try
            {
                Fs.CreateLink(linkPath, text, options.Kind, info!.IsDirectory);
                Journal.Done(step, StepOutcome.Committed);
                Job.ItemDone();
                Job.RootCompleted(i);
                // A hard link is undone only where the platform proves it is still another name of the same file.
                if (options.Kind != LinkKind.Hard) Job.AddUndo(new UndoStep(UndoKind.RemoveCreatedLink, linkPath, text, info.IsDirectory ? 1 : 0, 0));
                else if (Fs.GetFileIdentity(linkPath) is { } identity) Job.AddUndo(new UndoStep(UndoKind.RemoveCreatedHardLink, linkPath, target, 0, 0, identity));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Journal.Done(step, StepOutcome.Failed, ex.Message);
                Fail(i, linkPath, ErrorText.Describe(ex));
            }
        }
    }

    private void Fail(int index, string linkPath, string problem)
    {
        Job.ItemFailed();
        Job.RootFailed(index);
        Issue(IssueSeverity.Error, linkPath, $"Link not created: {problem}", StepOutcome.Failed);
    }
}
