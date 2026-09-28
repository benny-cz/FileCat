using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>What a tree operation did, what failed and why, and what it deliberately left out.</summary>
public sealed class TreeReport
{
    public int Done { get; set; }
    public int Failed { get; set; }
    /// <summary>Copy without replacing: destination files that already existed and were kept.</summary>
    public int Kept { get; set; }
    /// <summary>Links found in a copy source; they are neither copied nor followed.</summary>
    public int LinksSkipped { get; set; }
    public int StreamsNotCopied { get; set; }
    /// <summary>Download marks (Mark-of-the-Web) the destination could not store: a security-relevant loss.</summary>
    public int MarksLost { get; set; }
    public List<string> Problems { get; } = [];

    public void Fail(string path, Exception ex)
    {
        Failed++;
        Note($"{path}: {ex.Message}");
    }

    public void Note(string problem)
    {
        if (Problems.Count < 20) Problems.Add(problem);
    }
}

/// <summary>
/// Link-refusing, handle-relative file operations for the elevated broker (plan §6.2, TV-15). A path is walked from
/// its volume root one verified folder at a time, so a folder swapped for a link after the plan was made is refused
/// instead of redirecting privileged work. Inside trees, links are removed as links (delete) or skipped (copy).
/// </summary>
public sealed class SecureFileOps(Action checkpoint, Func<string, string>? display = null)
{
    private const uint FolderAccess = Traverse | ListDirectory | ReadAttributes | Synchronize;
    private const uint DeleteAccess = Delete | ListDirectory | ReadAttributes | WriteAttributes | Synchronize;
    private const uint ReadAccess = GenericRead | ReadAttributes | Synchronize;
    private const uint AddFile = 0x2;
    private const uint EditableAttributes = (uint)(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
                                                   FileAttributes.Archive | FileAttributes.NotContentIndexed);
    private readonly Func<string, string> _display = display ?? (p => p);

    /// <summary>"\\?\Volume{GUID}\a\b" as the NT root "\??\Volume{GUID}\" and its checked components.</summary>
    public static (string Root, string[] Parts) Split(string volumePath)
    {
        const string prefix = @"\\?\Volume{";
        int close = volumePath.IndexOf("}\\", StringComparison.Ordinal);
        if (!volumePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || close != prefix.Length + 36)
            throw new ArgumentException("A volume path (\\\\?\\Volume{…}\\…) is required.", nameof(volumePath));
        string rest = volumePath[(close + 2)..];
        var parts = rest.Length == 0 ? [] : rest.Split('\\');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.IndexOfAny([':', '*', '?', '"', '<', '>', '|', '/']) >= 0))
            throw new ArgumentException("The path has an empty, relative, or invalid component.", nameof(volumePath));
        return (@"\??\" + volumePath[4..(close + 2)], parts);
    }

    /// <summary>Opens a folder chain from the volume root; any link on the way is refused.</summary>
    public SafeFileHandle OpenDirectory(string volumePath) => Walk(volumePath, includeLast: true, out _);

    private SafeFileHandle Walk(string volumePath, bool includeLast, out string leaf)
    {
        var (root, parts) = Split(volumePath);
        if (!includeLast && parts.Length == 0) throw new ArgumentException("A volume root cannot be the item of this step.", nameof(volumePath));
        var current = OpenOrThrow(null, root, FolderAccess, ShareAll, Open, DirectoryFile, "Open the volume");
        try
        {
            int count = includeLast ? parts.Length : parts.Length - 1;
            for (int i = 0; i < count; i++)
            {
                var next = OpenOrThrow(current, parts[i], FolderAccess, ShareAll, Open, DirectoryFile | OpenReparsePoint, $"Open folder \"{parts[i]}\"");
                current.Dispose();
                current = next;
                if (IsReparsePoint(GetBasic(current).FileAttributes))
                    throw new IOException($"The folder \"{parts[i]}\" is now a link, so the path changed after the plan was made. Nothing was done there.");
            }
            leaf = includeLast ? "" : parts[^1];
            return current;
        }
        catch
        {
            current.Dispose();
            throw;
        }
    }

    public TreeReport DeleteTree(string volumePath)
    {
        var report = new TreeReport();
        using var parent = Walk(volumePath, includeLast: false, out var leaf);
        SafeFileHandle root;
        try { root = OpenOrThrow(parent, leaf, DeleteAccess, ShareAll, Open, OpenReparsePoint, "Open the item"); }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(volumePath), ex); return report; }
        var stack = new Stack<(SafeFileHandle Handle, string Path, Queue<string> Pending)>();
        try
        {
            uint attributes = GetBasic(root).FileAttributes;
            if (!IsDirectory(attributes) || IsReparsePoint(attributes))
            {
                // A file, or a link (removed as a link; its target is never touched).
                Remove(root, volumePath, report);
                return report;
            }
            stack.Push((root, volumePath, new Queue<string>(List(root).Select(e => e.Name))));
            root = null!;
            while (stack.Count > 0)
            {
                checkpoint();
                var (handle, path, pending) = stack.Peek();
                if (pending.Count == 0)
                {
                    stack.Pop();
                    using (handle) Remove(handle, path, report);
                    continue;
                }
                string childPath = path + "\\" + pending.Dequeue();
                string child = childPath[(path.Length + 1)..];
                SafeFileHandle entry;
                uint entryAttributes;
                try
                {
                    entry = OpenOrThrow(handle, child, DeleteAccess, ShareAll, Open, OpenReparsePoint, "Open");
                    entryAttributes = GetBasic(entry).FileAttributes;
                }
                catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(childPath), ex); continue; }
                if (IsDirectory(entryAttributes) && !IsReparsePoint(entryAttributes))
                {
                    try { stack.Push((entry, childPath, new Queue<string>(List(entry).Select(e => e.Name)))); }
                    catch (Exception ex) when (IsIo(ex)) { entry.Dispose(); report.Fail(_display(childPath), ex); }
                }
                else
                {
                    using (entry) Remove(entry, childPath, report);
                }
            }
            return report;
        }
        finally
        {
            root?.Dispose();
            while (stack.Count > 0) stack.Pop().Handle.Dispose();
        }
    }

    private void Remove(SafeFileHandle handle, string path, TreeReport report)
    {
        try
        {
            DeleteOpen(handle);
            report.Done++;
        }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(path), ex); }
    }

    public TreeReport CopyTree(string sourcePath, string destinationFolder, string name, bool replace)
    {
        ValidateName(name);
        var report = new TreeReport();
        using var sourceParent = Walk(sourcePath, includeLast: false, out var leaf);
        using var destination = OpenDirectory(destinationFolder);
        SafeFileHandle source;
        uint attributes;
        try
        {
            source = OpenOrThrow(sourceParent, leaf, ReadAccess, ShareAll, Open, OpenReparsePoint, "Open the source");
            attributes = GetBasic(source).FileAttributes;
        }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(sourcePath), ex); return report; }
        if (IsReparsePoint(attributes) || !IsDirectory(attributes))
        {
            using (source)
            {
                if (IsReparsePoint(attributes))
                {
                    report.LinksSkipped++;
                    report.Note($"{_display(sourcePath)} is a link; links are not copied by the administrator helper.");
                }
                else CopyFile(source, destination, name, replace, sourcePath, report);
            }
            return report;
        }
        var stack = new Stack<(SafeFileHandle Source, SafeFileHandle Target, string Path, Queue<string> Pending)>();
        try
        {
            // The folder is enumerated through the handle whose attributes were just checked.
            var targetDirectory = OpenOrCreateDirectory(destination, name, sourcePath, report);
            if (targetDirectory is null) { source.Dispose(); return report; }
            try { stack.Push((source, targetDirectory, sourcePath, new Queue<string>(List(source).Select(e => e.Name)))); }
            catch { source.Dispose(); targetDirectory.Dispose(); throw; }
            while (stack.Count > 0)
            {
                checkpoint();
                var (from, to, path, pending) = stack.Peek();
                if (pending.Count == 0)
                {
                    stack.Pop();
                    CopyTimes(from, to);
                    from.Dispose();
                    to.Dispose();
                    continue;
                }
                string child = pending.Dequeue();
                string childPath = path + "\\" + child;
                SafeFileHandle entry;
                uint entryAttributes;
                try
                {
                    entry = OpenOrThrow(from, child, ReadAccess, ShareAll, Open, OpenReparsePoint, "Open");
                    entryAttributes = GetBasic(entry).FileAttributes;
                }
                catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(childPath), ex); continue; }
                if (IsReparsePoint(entryAttributes))
                {
                    entry.Dispose();
                    report.LinksSkipped++;
                    report.Note($"{_display(childPath)} is a link; it was not copied or followed.");
                }
                else if (IsDirectory(entryAttributes))
                {
                    var target = OpenOrCreateDirectory(to, child, childPath, report);
                    if (target is null) entry.Dispose();
                    else
                    {
                        try { stack.Push((entry, target, childPath, new Queue<string>(List(entry).Select(e => e.Name)))); }
                        catch (Exception ex) when (IsIo(ex)) { entry.Dispose(); target.Dispose(); report.Fail(_display(childPath), ex); }
                    }
                }
                else
                {
                    using (entry) CopyFile(entry, to, child, replace, childPath, report);
                }
            }
            return report;
        }
        finally
        {
            while (stack.Count > 0)
            {
                var frame = stack.Pop();
                frame.Source.Dispose();
                frame.Target.Dispose();
            }
        }
    }

    private SafeFileHandle? OpenOrCreateDirectory(SafeFileHandle parent, string name, string path, TreeReport report)
    {
        const uint access = ListDirectory | AddFile | AddSubdirectory | ReadAttributes | WriteAttributes | Synchronize;
        int status = TryOpen(parent, name, access, ShareAll, Create, DirectoryFile, out var created);
        if (status >= 0)
        {
            report.Done++;
            return created;
        }
        created.Dispose();
        if (status != NameCollision)
        {
            report.Fail(_display(path), Error(status, "Create the folder"));
            return null;
        }
        // The folder exists: merge into it, but only if it is a real folder rather than a link or a file.
        status = TryOpen(parent, name, access, ShareAll, Open, DirectoryFile | OpenReparsePoint, out var existing);
        if (status < 0)
        {
            existing.Dispose();
            report.Fail(_display(path), status == NotADirectory ? new IOException("A file with this name exists at the destination.") : Error(status, "Open the destination folder"));
            return null;
        }
        if (IsReparsePoint(GetBasic(existing).FileAttributes))
        {
            existing.Dispose();
            report.Fail(_display(path), new IOException("The destination folder is a link; nothing is copied through it."));
            return null;
        }
        return existing;
    }

    private void CopyFile(SafeFileHandle source, SafeFileHandle targetDirectory,
        string targetName, bool replace, string path, TreeReport report)
    {
        int existing = TryOpen(targetDirectory, targetName, ReadAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, out var existingHandle);
        using (existingHandle)
        {
            if (existing >= 0)
            {
                if (IsDirectory(GetBasic(existingHandle).FileAttributes))
                {
                    report.Fail(_display(path), new IOException("A folder with this name exists at the destination."));
                    return;
                }
                if (!replace)
                {
                    report.Kept++;
                    return;
                }
            }
            else if (existing != NameNotFound)
            {
                report.Fail(_display(path), Error(existing, "Check the destination"));
                return;
            }
        }
        // Stage under a unique name, then publish with a rename; a failure never leaves a partial file under the real name.
        string temp = ".filecat-admin-" + Guid.NewGuid().ToString("N") + ".tmp";
        SafeFileHandle output;
        try
        {
            output = OpenOrThrow(targetDirectory, temp, GenericWrite | Delete | ReadAttributes | WriteAttributes | Synchronize, 0, Create,
                NonDirectoryFile | OpenReparsePoint, "Create the file", (uint)FileAttributes.Normal);
        }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(path), ex); return; }
        bool published = false;
        try
        {
            var buffer = new byte[1024 * 1024];
            for (long offset = 0; ;)
            {
                checkpoint();
                int read = RandomAccess.Read(source, buffer, offset);
                if (read <= 0) break;
                RandomAccess.Write(output, buffer.AsSpan(0, read), offset);
                offset += read;
            }
            CopyZoneIdentifier(source, output, path, report);
            report.StreamsNotCopied += Streams(source).Count(s => !s.StartsWith(":Zone.Identifier:", StringComparison.OrdinalIgnoreCase));
            var basic = GetBasic(source);
            basic.ChangeTime = 0;
            basic.FileAttributes &= EditableAttributes;
            if (basic.FileAttributes == 0) basic.FileAttributes = (uint)FileAttributes.Normal;
            SetBasic(output, basic);
            NtFile.Rename(output, targetDirectory, targetName, replace);
            published = true;
            report.Done++;
        }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(path), ex); }
        finally
        {
            if (!published)
            {
                try { DeleteOpen(output); }
                catch (Exception ex) when (IsIo(ex)) { report.Note($"A staged file could not be removed: {ex.Message}"); }
            }
            output.Dispose();
        }
    }

    /// <summary>
    /// Mark-of-the-Web is security metadata (plan §8.1): copies keep it, and a lost mark is reported. Both streams are
    /// opened relative to the already open file handles (":Zone.Identifier"), never by name.
    /// </summary>
    private void CopyZoneIdentifier(SafeFileHandle source, SafeFileHandle output, string path, TreeReport report)
    {
        int status = TryOpen(source, ":Zone.Identifier", GenericRead | Synchronize, ShareAll, Open, NonDirectoryFile, out var zone);
        using (zone)
        {
            if (status < 0) return;
            var mark = new byte[64 * 1024];
            int length = RandomAccess.Read(zone, mark, 0);
            try
            {
                using var target = OpenOrThrow(output, ":Zone.Identifier", GenericWrite | Synchronize, ShareAll, Create, NonDirectoryFile,
                    "Copy the download mark");
                RandomAccess.Write(target, mark.AsSpan(0, length), 0);
            }
            catch (Exception ex) when (IsIo(ex))
            {
                report.MarksLost++;
                report.Note($"{_display(path)}: the downloaded-from-the-internet mark could not be copied ({ex.Message}), so Windows will not warn about the copy.");
            }
        }
    }

    private static void CopyTimes(SafeFileHandle from, SafeFileHandle to)
    {
        try
        {
            var basic = GetBasic(from);
            basic.ChangeTime = 0;
            basic.FileAttributes = 0; // unchanged
            SetBasic(to, basic);
        }
        catch (Win32Exception) { }
    }

    public void MoveItem(string sourcePath, string destinationFolder, string name)
    {
        ValidateName(name);
        using var parent = Walk(sourcePath, includeLast: false, out var leaf);
        using var item = OpenOrThrow(parent, leaf, Delete | ReadAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, "Open the item");
        using var destination = OpenDirectory(destinationFolder);
        NtFile.Rename(item, destination, name, replace: false);
    }

    public void Rename(string path, string newName)
    {
        ValidateName(newName);
        using var parent = Walk(path, includeLast: false, out var leaf);
        using var item = OpenOrThrow(parent, leaf, Delete | ReadAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, "Open the item");
        NtFile.Rename(item, parent, newName, replace: false);
    }

    public void CreateDirectory(string parentFolder, string name)
    {
        ValidateName(name);
        using var parent = OpenDirectory(parentFolder);
        int status = TryOpen(parent, name, ListDirectory | Synchronize, ShareAll, Create, DirectoryFile, out var created);
        using (created)
        {
            if (status == NameCollision) throw new IOException("An item with this name already exists.");
            if (status < 0) throw Error(status, "Create the folder");
        }
    }

    public void SetAttributes(string path, FileAttributes set, FileAttributes clear)
    {
        using var parent = Walk(path, includeLast: false, out var leaf);
        using var item = OpenOrThrow(parent, leaf, ReadAttributes | WriteAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, "Open the item");
        var basic = GetBasic(item);
        uint next = basic.FileAttributes & ~((uint)clear & EditableAttributes) | (uint)set & EditableAttributes;
        basic.FileAttributes = next & (EditableAttributes | (uint)(FileAttributes.Temporary | FileAttributes.Offline));
        if (basic.FileAttributes == 0) basic.FileAttributes = (uint)FileAttributes.Normal;
        basic.CreationTime = basic.LastAccessTime = basic.LastWriteTime = basic.ChangeTime = 0;
        SetBasic(item, basic);
    }

    private static void ValidateName(string name)
    {
        if (name.Length == 0 || name.Length > 255 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':'))
            throw new ArgumentException($"\"{name}\" is not a valid file name.", nameof(name));
    }

    private static bool IsIo(Exception ex) => ex is IOException or UnauthorizedAccessException or Win32Exception;
}
