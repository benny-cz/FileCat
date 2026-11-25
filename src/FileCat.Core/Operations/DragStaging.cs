using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Core.Operations;

/// <summary>
/// Dragging items that are not plain files to other programs (plan §4.2, P5): members of archives and files in recovery
/// views of disk images are copied into a private temporary folder first. Staging is explicit, small, and quick: files only,
/// at most <see cref="MaxItems"/> and <see cref="MaxBytes"/>, from local containers, with the download mark of the outermost
/// file carried over. Items from servers, phones, and the Registry are refused with the way to get them (F5), because
/// other programs cannot wait for a network.
/// </summary>
public static class DragStaging
{
    public const int MaxItems = 100;
    public const long MaxBytes = 64L * 1024 * 1024;
    private const string Folder = "drag";

    /// <summary>Whether a path is one of the copies made to drag items out of an archive, a server or a device.</summary>
    public static bool IsStaged(string path, string tempRoot) => PathUtil.IsSameOrUnder(path, Path.Combine(tempRoot, Folder));

    /// <summary>Why an administrator window (Windows), or FileCat running as root, drags nothing out of a container.</summary>
    internal static string ElevatedRefusal(bool windows) => windows
        ? "Drag export from an elevated FileCat session is refused because its staging folder is accessible at ordinary integrity. Run FileCat ordinarily or copy with F5 to a reviewed destination."
        : "Drag export is refused while FileCat runs as root: the programs that take a drop run as your usual account and would " +
          "get copies of what FileCat read as root. Start FileCat as your usual user, or copy with F5 to a reviewed destination.";

    /// <summary>The staged files, or null and why the items cannot be dragged.</summary>
    public static (IReadOnlyList<string>? Paths, string? Refusal) Stage(IReadOnlyList<ItemRef> items, ProviderRegistry providers, IFileSystemOperations fs,
        string tempRoot, CancellationToken ct)
    {
        if (items.Count == 0) return (null, null);
        // A medium process of this account can read the ordinary local temp tree after an elevated
        // process stages a protected archive or recovered image member there. Root (I655) would hand the programs of
        // the ordinary account, which take a drop, copies of what it read as root.
        if (ProcessAuthority.IsElevatedOrRoot) return (null, ElevatedRefusal(OperatingSystem.IsWindows()));
        if (items.Any(i => i.Parent.Scheme is not (Schemes.Zip or Schemes.Archive or Schemes.Recovery) || Schemes.Recovery == i.Parent.Scheme && i.Parent.Container?.Scheme == Schemes.Device))
            return (null, "Only files on disk, in archives, and in disk images can be dragged to other programs. Copy these with F5 first.");
        if (items.Any(i => i.IsContainer))
            return (null, "Folders from archives and disk images cannot be dragged to other programs. Copy them with F5 first.");
        if (items.Count > MaxItems) return (null, $"Dragging copies items out of their archive first, so at most {MaxItems} can be dragged at once. Copy these with F5.");
        long total = 0;
        foreach (var item in items)
        {
            if (item.Size < 0 || item.Size > MaxBytes - total)
                return (null, $"Dragging copies items out of their archive first, so at most {MaxBytes / (1024 * 1024)} MiB can be dragged at once. Copy these with F5.");
            total += item.Size;
        }

        string folder = Path.Combine(tempRoot, Folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var staged = new List<string>();
        long copied = 0;
        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (SafeNames.Validate(item.Name) is { } bad) return Fail(folder, $"\"{item.Name}\" cannot be dragged: {bad}");
                var provider = providers.Get(item.Parent.Scheme);
                var opened = OperatingSystem.IsWindows() && provider is IUnprotectedContentOutput derived
                    ? derived.OpenUnprotectedOutput(item, ContentOutputKind.Drag)
                    : provider.OpenContent(item);
                using var content = Content.ProgressiveContent.Sequential(opened);
                if (content is null) return Fail(folder, $"\"{item.Name}\" has no readable content (for example an encrypted archive entry).");
                string target = Unique(folder, item.Name);
                bool tooLarge = false;
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan))
                {
                    var buffer = new byte[1024 * 1024];
                    long offset = 0;
                    int n;
                    while ((n = content.Read(offset, buffer)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (n > MaxBytes - copied) { tooLarge = true; break; }
                        output.Write(buffer, 0, n);
                        offset += n;
                        copied += n;
                    }
                }
                if (tooLarge) return Fail(folder, $"\"{item.Name}\" is larger than its listing said; copy it with F5.");
                // Lost bytes would travel unannounced: those items go through F5, whose report names them.
                if (content is IPartialContent { MissingRanges.Count: > 0 })
                    return Fail(folder, $"Parts of \"{item.Name}\" are lost, so it is recovered with F5, which says which bytes are zeros.");
                if (content is IPartialContent { Caveat: not null })
                    return Fail(folder, $"Where \"{item.Name}\" starts is a guess, so it is recovered with F5, whose report says so.");
                if (item.Modified > 0) File.SetLastWriteTimeUtc(target, new DateTime(item.Modified, DateTimeKind.Utc));
                if (OriginMark(item.Parent, fs, providers) is { } mark && !fs.WriteOriginMark(target, mark))
                    throw new IOException("The download mark could not be kept on the staged copy.");
                File.SetAttributes(target, File.GetAttributes(target) | FileAttributes.ReadOnly); // a copy to hand over, not to edit here
                staged.Add(target);
            }
            return (staged, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return Fail(folder, "The items could not be prepared for dragging: " + ErrorText.Describe(ex));
        }
        catch (OperationCanceledException)
        {
            Remove(folder);
            throw;
        }
        catch
        {
            Remove(folder);
            throw;
        }
    }

    /// <summary>
    /// Removes staged folders older than <paramref name="age"/> (at startup: other programs have long taken them), never
    /// through a link (I645): a link in their place is removed itself, and nothing is swept when the temporary or the
    /// staging folder is itself a link. Not for an elevated or root FileCat, whose temporary folder is the ordinary
    /// account's (<c>AppServices.SweepTemporaryFolders</c>).
    /// </summary>
    public static void Sweep(string tempRoot, TimeSpan age) => TemporarySweep.RemoveStaleFolders(tempRoot, Folder, age);

    /// <summary>Discard one unhanded staged drag. Only a direct child of this private staging root may be removed.</summary>
    public static void Discard(IReadOnlyList<string> paths, string tempRoot)
    {
        if (paths.Count == 0) return;
        string root = PathUtil.NormalizeForCompare(Path.Combine(tempRoot, Folder));
        string? directory;
        try { directory = Path.GetDirectoryName(Path.GetFullPath(paths[0])); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        if (directory is null ||
            !string.Equals(Path.GetDirectoryName(directory), root, PathUtil.SafetyComparison)) return;
        foreach (string path in paths)
        {
            string? parent;
            try { parent = Path.GetDirectoryName(Path.GetFullPath(path)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
            if (!string.Equals(parent, directory, PathUtil.SafetyComparison)) return;
        }
        if (Directory.Exists(directory)) Remove(directory);
    }

    private static (IReadOnlyList<string>?, string?) Fail(string folder, string reason)
    {
        Remove(folder);
        return (null, reason);
    }

    /// <summary>A staging folder and its read-only copies, removed without going through a link or changing an attribute.</summary>
    private static void Remove(string folder) => TemporarySweep.RemoveTree(folder);

    private static string Unique(string folder, string name)
    {
        string path = Path.Combine(folder, name);
        return File.Exists(path) ? Path.Combine(folder, PathUtil.MakeUniqueName(name, n => File.Exists(Path.Combine(folder, n)))) : path;
    }

    /// <summary>The download mark of the outermost file (an archive downloaded from the internet marks what comes out of it).</summary>
    private static string? OriginMark(Location location, IFileSystemOperations fs, ProviderRegistry providers)
    {
        for (var l = location; l is not null; l = l.Container)
        {
            if (l.IsFileSystem && fs.ReadOriginMark(l.Path) is { } mark) return mark;
            if (providers.TryGet(l.Scheme, out var p) && p is IOriginMarkSource s && s.GetOriginMark(l) is { } other) return other;
        }
        return null;
    }
}
