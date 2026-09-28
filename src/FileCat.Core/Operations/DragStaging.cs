using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

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

    /// <summary>The staged files, or null and why the items cannot be dragged.</summary>
    public static (IReadOnlyList<string>? Paths, string? Refusal) Stage(IReadOnlyList<ItemRef> items, ProviderRegistry providers, IFileSystemOperations fs,
        string tempRoot, CancellationToken ct)
    {
        if (items.Count == 0) return (null, null);
        if (items.Any(i => i.Parent.Scheme is not (Schemes.Zip or Schemes.Archive or Schemes.Recovery) || Schemes.Recovery == i.Parent.Scheme && i.Parent.Container?.Scheme == Schemes.Device))
            return (null, "Only files on disk, in archives, and in disk images can be dragged to other programs. Copy these with F5 first.");
        if (items.Any(i => i.IsContainer))
            return (null, "Folders from archives and disk images cannot be dragged to other programs. Copy them with F5 first.");
        if (items.Count > MaxItems) return (null, $"Dragging copies items out of their archive first, so at most {MaxItems} can be dragged at once. Copy these with F5.");
        long total = items.Sum(i => Math.Max(0, i.Size));
        if (total > MaxBytes || items.Any(i => i.Size < 0))
            return (null, $"Dragging copies items out of their archive first, so at most {MaxBytes / (1024 * 1024)} MiB can be dragged at once. Copy these with F5.");

        string folder = Path.Combine(tempRoot, Folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var staged = new List<string>();
        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (SafeNames.Validate(item.Name) is { } bad) return Fail(folder, $"\"{item.Name}\" cannot be dragged: {bad}");
                var provider = providers.Get(item.Parent.Scheme);
                using var content = Content.ProgressiveContent.Sequential(provider.OpenContent(item));
                if (content is null) return Fail(folder, $"\"{item.Name}\" has no readable content (for example an encrypted archive entry).");
                string target = Unique(folder, item.Name);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan))
                {
                    var buffer = new byte[1024 * 1024];
                    long offset = 0;
                    int n;
                    while ((n = content.Read(offset, buffer)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, n);
                        offset += n;
                        if (offset > MaxBytes) return Fail(folder, $"\"{item.Name}\" is larger than its listing said; copy it with F5.");
                    }
                }
                // Lost bytes would travel unannounced: those items go through F5, whose report names them.
                if (content is IPartialContent { MissingRanges.Count: > 0 })
                    return Fail(folder, $"Parts of \"{item.Name}\" are lost, so it is recovered with F5, which says which bytes are zeros.");
                if (content is IPartialContent { Caveat: not null })
                    return Fail(folder, $"Where \"{item.Name}\" starts is a guess, so it is recovered with F5, whose report says so.");
                if (item.Modified > 0) File.SetLastWriteTimeUtc(target, new DateTime(item.Modified, DateTimeKind.Utc));
                if (OriginMark(item.Parent, fs, providers) is { } mark) fs.WriteOriginMark(target, mark);
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
    }

    /// <summary>Removes staged folders older than <paramref name="age"/> (at startup: other programs have long taken them).</summary>
    public static void Sweep(string tempRoot, TimeSpan age)
    {
        string root = Path.Combine(tempRoot, Folder);
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > age) Remove(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static (IReadOnlyList<string>?, string?) Fail(string folder, string reason)
    {
        Remove(folder);
        return (null, reason);
    }

    private static void Remove(string folder)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

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
