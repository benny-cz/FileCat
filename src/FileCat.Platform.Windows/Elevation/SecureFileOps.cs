using System.ComponentModel;
using FileCat.Core.FileSystem;
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
    /// <summary>Delete: read-only files kept because the caller said so.</summary>
    public int ReadOnlyKept { get; set; }
    /// <summary>The first item another program holds open without sharing its deletion, as a sharing violation.</summary>
    public IOException? InUse { get; private set; }
    public List<string> Problems { get; } = [];

    /// <summary>Items left in place: a folder that holds one is kept too.</summary>
    internal int Left => Failed + ReadOnlyKept;

    public void Fail(string path, Exception ex)
    {
        Failed++;
        Note($"{path}: {ex.Message}");
        int code = ex is Win32Exception native ? native.NativeErrorCode
            : (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) ? ex.HResult & 0xFFFF : 0;
        if (code is 32 or 33 or 1224) InUse ??= new IOException($"{path}: {ex.Message}", unchecked((int)0x80070000) | code);
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
    private const string EncryptedCopySource = "The administrator helper cannot copy EFS-encrypted items without preserving their protection.";
    private const string EncryptedCopyTarget = "The administrator helper cannot replace an EFS-encrypted destination without preserving its protection.";
    private const string EncryptedCopyFolder = "The administrator helper cannot create copies in a folder with an EFS encryption default.";
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
                if (IsLink(current))
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

    /// <param name="deleteReadOnly">Asked before a read-only file is deleted, with its display path: false keeps it, and
    /// so the folders above it. Without it, read-only files are deleted as any other.</param>
    public TreeReport DeleteTree(string volumePath, Func<SafeFileHandle, string, bool>? approve = null,
        Action? beforeRemove = null, Func<string, bool>? deleteReadOnly = null)
    {
        var report = new TreeReport();
        using var parent = Walk(volumePath, includeLast: false, out var leaf);
        SafeFileHandle root;
        try { root = OpenOrThrow(parent, leaf, DeleteAccess, approve is null ? ShareAll : ShareRead, Open, OpenReparsePoint, "Open the item"); }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(volumePath), ex); return report; }
        // Each folder with the items left in place when it was entered: a folder with more left inside is kept, without
        // a second failure for it ("not empty").
        var stack = new Stack<(SafeFileHandle Handle, string Path, Queue<(string Name, uint Attributes, uint ReparseTag, FileIdentity? Identity)> Pending, int Left)>();
        try
        {
            if (approve is not null && !approve(root, volumePath))
            {
                report.Fail(_display(volumePath), new IOException("The item is no longer the fully reviewed object."));
                return report;
            }
            uint attributes = GetBasic(root).FileAttributes;
            if (!IsDirectory(attributes) || IsLink(root))
            {
                // A file, or a link (removed as a link; its target is never touched). A folder that is another kind of
                // reparse point, such as a Cloud Files placeholder, is a folder.
                RemoveUnlessKept(root, attributes, volumePath, report, beforeRemove, deleteReadOnly);
                return report;
            }
            stack.Push((root, volumePath, SnapshotChildren(root), report.Left));
            root = null!;
            while (stack.Count > 0)
            {
                checkpoint();
                var (handle, path, pending, left) = stack.Peek();
                if (pending.Count == 0)
                {
                    stack.Pop();
                    using (handle)
                    {
                        if (report.Left == left) Remove(handle, path, report, beforeRemove);
                    }
                    continue;
                }
                var listed = pending.Dequeue();
                string childPath = path + "\\" + listed.Name;
                string child = listed.Name;
                SafeFileHandle entry;
                uint entryAttributes;
                try
                {
                    entry = OpenOrThrow(handle, child, DeleteAccess, approve is null ? ShareAll : ShareRead, Open, OpenReparsePoint, "Open");
                    entryAttributes = GetBasic(entry).FileAttributes;
                }
                catch (FileNotFoundException) { continue; } // gone since it was listed: nothing is left of it
                catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(childPath), ex); continue; }
                if (!IsLink(listed.Attributes, listed.ReparseTag) && IsLink(entry))
                {
                    entry.Dispose();
                    report.Fail(_display(childPath), new IOException("The listed item became a link before deletion; it was left in place."));
                    continue;
                }
                bool sameIdentity = false;
                if (listed.Identity is { } expected)
                {
                    try { sameIdentity = ProtectedHexFile.Identity(entry) == expected; }
                    catch (Exception ex) when (IsIo(ex)) { /* An unverified child cannot be deleted. */ }
                }
                if (!sameIdentity)
                {
                    entry.Dispose();
                    report.Fail(_display(childPath), new IOException("The listed item's identity changed before deletion; it was left in place."));
                    continue;
                }
                if (approve is not null && !approve(entry, childPath))
                {
                    entry.Dispose();
                    report.Fail(_display(childPath), new IOException("The item is no longer the fully reviewed object."));
                    continue;
                }
                if (IsDirectory(entryAttributes) && !IsLink(entry))
                {
                    try { stack.Push((entry, childPath, SnapshotChildren(entry), report.Left)); }
                    catch (Exception ex) when (IsIo(ex)) { entry.Dispose(); report.Fail(_display(childPath), ex); }
                }
                else
                {
                    using (entry) RemoveUnlessKept(entry, entryAttributes, childPath, report, beforeRemove, deleteReadOnly);
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

    private Queue<(string Name, uint Attributes, uint ReparseTag, FileIdentity? Identity)> SnapshotChildren(SafeFileHandle directory)
    {
        var pending = new Queue<(string Name, uint Attributes, uint ReparseTag, FileIdentity? Identity)>();
        int count = 0;
        foreach (var listed in List(directory))
        {
            if ((++count & 255) == 0) checkpoint();
            FileIdentity? identity = null;
            try
            {
                using var entry = OpenOrThrow(directory, listed.Name, ReadAttributes | Synchronize, ShareAll,
                    Open, OpenReparsePoint, "Identify listed item");
                if (IsLink(entry) == IsLink(listed.Attributes, listed.ReparseTag))
                    identity = ProtectedHexFile.Identity(entry);
            }
            catch (Exception ex) when (IsIo(ex)) { /* A changed or inaccessible entry is refused when visited. */ }
            pending.Enqueue((listed.Name, listed.Attributes, listed.ReparseTag, identity));
        }
        return pending;
    }

    /// <summary>Removes a file or link, unless it is a read-only file the caller keeps (finding F-M4 of f134ba37).</summary>
    private void RemoveUnlessKept(SafeFileHandle handle, uint attributes, string path, TreeReport report, Action? beforeRemove,
        Func<string, bool>? deleteReadOnly)
    {
        if (deleteReadOnly is not null && !IsDirectory(attributes) && (attributes & (uint)FileAttributes.ReadOnly) != 0 &&
            !deleteReadOnly(_display(path)))
            report.ReadOnlyKept++;
        else Remove(handle, path, report, beforeRemove);
    }

    private void Remove(SafeFileHandle handle, string path, TreeReport report, Action? beforeRemove = null)
    {
        try
        {
            beforeRemove?.Invoke();
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
        if (IsEncrypted(GetBasic(destination).FileAttributes))
        {
            report.Fail(_display(destinationFolder), new IOException(EncryptedCopyFolder));
            return report;
        }
        (SafeFileHandle source, uint attributes, bool link) entry;
        try { entry = OpenCopyEntry(sourceParent, leaf); }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(sourcePath), ex); return report; }
        var (source, attributes, link) = entry;
        if (!link && IsEncrypted(attributes))
        {
            source.Dispose();
            report.Fail(_display(sourcePath), new IOException(EncryptedCopySource));
            return report;
        }
        if (link || !IsDirectory(attributes))
        {
            using (source)
            {
                if (link)
                {
                    report.LinksSkipped++;
                    report.Note($"{_display(sourcePath)} is a link; links are not copied by the administrator helper.");
                }
                else CopyFile(sourceParent, leaf, source, destination, name, replace, sourcePath, report);
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
                (SafeFileHandle childHandle, uint attributes, bool link) childEntry;
                try { childEntry = OpenCopyEntry(from, child); }
                catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(childPath), ex); continue; }
                var (childHandle, childAttributes, childLink) = childEntry;
                if (childLink)
                {
                    childHandle.Dispose();
                    report.LinksSkipped++;
                    report.Note($"{_display(childPath)} is a link; it was not copied or followed.");
                }
                else if (IsEncrypted(childAttributes))
                {
                    childHandle.Dispose();
                    report.Fail(_display(childPath), new IOException(EncryptedCopySource));
                }
                else if (IsDirectory(childAttributes))
                {
                    var target = OpenOrCreateDirectory(to, child, childPath, report);
                    if (target is null) childHandle.Dispose();
                    else
                    {
                        try { stack.Push((childHandle, target, childPath, new Queue<string>(List(childHandle).Select(e => e.Name)))); }
                        catch (Exception ex) when (IsIo(ex)) { childHandle.Dispose(); target.Dispose(); report.Fail(_display(childPath), ex); }
                    }
                }
                else
                {
                    using (childHandle) CopyFile(from, child, childHandle, to, child, replace, childPath, report);
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

    private static (SafeFileHandle Handle, uint Attributes, bool Link) OpenCopyEntry(SafeFileHandle parent, string name)
    {
        var handle = OpenOrThrow(parent, name, ReadAccess, ShareAll, Open, OpenReparsePoint, "Open the source");
        try { return (handle, GetBasic(handle).FileAttributes, IsLink(handle)); }
        catch { handle.Dispose(); throw; }
    }

    private static bool IsEncrypted(uint attributes) => (attributes & (uint)FileAttributes.Encrypted) != 0;

    private SafeFileHandle? OpenOrCreateDirectory(SafeFileHandle parent, string name, string path, TreeReport report)
    {
        if (IsEncrypted(GetBasic(parent).FileAttributes))
        {
            report.Fail(_display(path), new IOException(EncryptedCopyFolder));
            return null;
        }
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
        if (IsLink(existing))
        {
            existing.Dispose();
            report.Fail(_display(path), new IOException("The destination folder is a link; nothing is copied through it."));
            return null;
        }
        if (IsEncrypted(GetBasic(existing).FileAttributes))
        {
            existing.Dispose();
            report.Fail(_display(path), new IOException(EncryptedCopyFolder));
            return null;
        }
        return existing;
    }

    private void CopyFile(SafeFileHandle sourceParent, string sourceName, SafeFileHandle observedSource, SafeFileHandle targetDirectory,
        string targetName, bool replace, string path, TreeReport report)
    {
        FileIdentity? replaceIdentity = null;
        int existing = TryOpen(targetDirectory, targetName, ReadAttributes | Synchronize, ShareAll, Open, OpenReparsePoint, out var existingHandle);
        using (existingHandle)
        {
            if (existing >= 0)
            {
                uint attributes = GetBasic(existingHandle).FileAttributes;
                if (IsDirectory(attributes))
                {
                    report.Fail(_display(path), new IOException("A folder with this name exists at the destination."));
                    return;
                }
                if (!replace)
                {
                    report.Kept++;
                    return;
                }
                if (IsEncrypted(attributes))
                {
                    report.Fail(_display(path), new IOException(EncryptedCopyTarget));
                    return;
                }
                replaceIdentity = ProtectedHexFile.Identity(existingHandle);
            }
            else if (existing != NameNotFound)
            {
                report.Fail(_display(path), Error(existing, "Check the destination"));
                return;
            }
        }
        SafeFileHandle source;
        try { source = BindCopySource(sourceParent, sourceName, observedSource); }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(path), ex); return; }
        using var heldSource = source;
        try
        {
            if (IsEncrypted(GetBasic(targetDirectory).FileAttributes)) throw new IOException(EncryptedCopyFolder);
            using var sourceProtection = WindowsFileTransaction.PinSource(source, EncryptedCopySource);
            // The source mark remains held without write or delete sharing until publication.
            var (sourceZone, mark) = ReadZoneIdentifier(source);
            using var heldSourceZone = sourceZone;
            int skipped = replaceIdentity is { } reviewed && WindowsFileTransaction.CanEncrypt(targetDirectory)
                ? CopyReplacingTransacted(source, targetDirectory, targetName, reviewed, mark)
                : CopyStaged(source, targetDirectory, targetName, replaceIdentity, mark, report);
            report.StreamsNotCopied += skipped;
            report.Done++;
        }
        catch (Exception ex) when (IsIo(ex)) { report.Fail(_display(path), ex); return; }
    }

    private int CopyReplacingTransacted(SafeFileHandle source, SafeFileHandle directory, string name, FileIdentity reviewedTarget,
        byte[]? mark)
    {
        if (ProtectedHexFile.Identity(source) == reviewedTarget)
            throw new IOException("The copy source and replacement destination identify the same file.");
        using var transaction = new WindowsFileTransaction();
        string folder = WindowsFileTransaction.HeldPath(directory);
        string destination = Path.Combine(folder, name);
        const uint access = GenericRead | GenericWrite | Delete | Synchronize;
        using (var original = transaction.OpenExisting(destination, access, ShareRead, "Hold the reviewed replacement destination"))
        {
            WindowsFileTransaction.RequireOrdinaryFile(original, EncryptedCopyTarget);
            using (var atName = OpenOrThrow(directory, name, ReadAttributes | Synchronize, ShareAll, Open, NonDirectoryFile | OpenReparsePoint,
                "Recheck the held replacement name"))
            {
                WindowsFileTransaction.RequireOrdinaryFile(atName, EncryptedCopyTarget);
                if (ProtectedHexFile.Identity(original) != reviewedTarget || ProtectedHexFile.Identity(atName) != reviewedTarget ||
                    !string.Equals(WindowsFileTransaction.HeldPath(original), destination, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The reviewed replacement destination changed while the administrator helper was opening it.");
            }
            // All later opens occur after the exact original has been moved inside the private transaction.
            // NTFS keeps its old name visible outside this transaction and pins the modified file's ancestors.
            NtFile.Rename(original, directory, ".filecat-admin-" + Guid.NewGuid().ToString("N") + ".old", replace: false);
            WindowsFileTransaction.RemoveOnClose(original, clearReadOnly: true);
        }
        if (IsLink(directory) || !string.Equals(WindowsFileTransaction.HeldPath(directory), folder, StringComparison.Ordinal))
            throw new IOException("The held replacement folder changed during staging.");
        if (IsEncrypted(GetBasic(directory).FileAttributes)) throw new IOException(EncryptedCopyFolder);
        string stagePath = Path.Combine(folder, ".filecat-admin-" + Guid.NewGuid().ToString("N") + ".tmp");
        int skipped;
        using (var output = transaction.CreateFile(stagePath, access, ShareAll, "Create the private replacement copy"))
        {
            WindowsFileTransaction.RequireOrdinaryFile(output, EncryptedCopyFolder);
            if (!string.Equals(WindowsFileTransaction.HeldPath(output), stagePath, StringComparison.Ordinal))
                throw new IOException("The replacement stage was opened outside the held destination folder.");
            skipped = WriteAndRenameCopy(source, output, directory, name, null, mark, transaction);
            RandomAccess.FlushToDisk(output);
        }
        transaction.Commit();
        return skipped;
    }

    private int CopyStaged(SafeFileHandle source, SafeFileHandle directory, string name, FileIdentity? reviewedTarget,
        byte[]? mark, TreeReport report)
    {
        string temp = ".filecat-admin-" + Guid.NewGuid().ToString("N") + ".tmp";
        using var output = OpenOrThrow(directory, temp, GenericWrite | Delete | ReadAttributes | WriteAttributes | Synchronize, 0, Create,
            NonDirectoryFile | OpenReparsePoint, "Create the file", (uint)FileAttributes.Normal);
        bool complete = false;
        try
        {
            if (IsEncrypted(GetBasic(output).FileAttributes)) throw new IOException(EncryptedCopyFolder);
            int skipped = WriteAndRenameCopy(source, output, directory, name, reviewedTarget, mark);
            complete = true;
            return skipped;
        }
        finally
        {
            if (!complete)
            {
                try { DeleteOpen(output); }
                catch (Exception ex) when (IsIo(ex)) { report.Note($"A staged file could not be removed: {ex.Message}"); }
            }
        }
    }

    private int WriteAndRenameCopy(SafeFileHandle source, SafeFileHandle output, SafeFileHandle directory, string name,
        FileIdentity? reviewedTarget, byte[]? mark, WindowsFileTransaction? transaction = null)
    {
        var buffer = new byte[1024 * 1024];
        for (long offset = 0; ;)
        {
            checkpoint();
            RequireClearSource(source);
            int read = RandomAccess.Read(source, buffer, offset);
            RequireClearSource(source);
            if (read <= 0) break;
            RandomAccess.Write(output, buffer.AsSpan(0, read), offset);
            offset += read;
        }
        using var stagedZone = mark is null ? null : WriteZoneIdentifier(output, mark, transaction);
        int skipped = Streams(source).Count(s => !s.StartsWith(":Zone.Identifier:", StringComparison.OrdinalIgnoreCase));
        var basic = GetBasic(source);
        basic.ChangeTime = 0;
        basic.FileAttributes &= EditableAttributes;
        if (basic.FileAttributes == 0) basic.FileAttributes = (uint)FileAttributes.Normal;
        SetBasic(output, basic);
        if (mark is null) EnsureZoneIdentifierMissing(source);
        else VerifyZoneIdentifier(stagedZone!, mark);
        RequireClearSource(source);
        if (reviewedTarget is { } reviewed)
        {
            using var current = OpenOrThrow(directory, name, ReadAttributes | Synchronize, ShareAll, Open,
                OpenReparsePoint, "Recheck the replacement destination");
            if (IsEncrypted(GetBasic(current).FileAttributes)) throw new IOException(EncryptedCopyTarget);
            if (ProtectedHexFile.Identity(current) != reviewed) throw new IOException("The replacement destination changed while the source was copied.");
        }
        NtFile.Rename(output, directory, name, replace: reviewedTarget is not null);
        if (mark is null) EnsureZoneIdentifierMissing(source);
        else VerifyZoneIdentifier(stagedZone!, mark);
        return skipped;
    }

    private static SafeFileHandle BindCopySource(SafeFileHandle parent, string name, SafeFileHandle observed)
    {
        // Reopen the exact child without write or delete sharing before creating the payload stage.
        var held = OpenOrThrow(parent, name, ReadAccess, ShareRead, Open, NonDirectoryFile | OpenReparsePoint, "Hold the copy source");
        try
        {
            if (ProtectedHexFile.Identity(held) != ProtectedHexFile.Identity(observed) || IsLink(held))
                throw new IOException("The source changed while the administrator helper was opening it.");
            RequireClearSource(held);
            return held;
        }
        catch { held.Dispose(); throw; }
    }

    private static void RequireClearSource(SafeFileHandle source)
    {
        if (IsEncrypted(GetBasic(source).FileAttributes)) throw new IOException(EncryptedCopySource);
    }

    /// <summary>
    /// Mark-of-the-Web is security metadata (plan §8.1). Only an absent stream may produce an unmarked copy.
    /// The source and its stream stay bound through their handles until publication.
    /// </summary>
    private static (SafeFileHandle? Handle, byte[]? Bytes) ReadZoneIdentifier(SafeFileHandle source)
    {
        int status = TryOpen(source, ":Zone.Identifier", GenericRead | Synchronize, ShareRead, Open, NonDirectoryFile, out var zone);
        if (status == NameNotFound) { zone.Dispose(); return (null, null); }
        if (status < 0) { zone.Dispose(); throw Error(status, "Read the download mark"); }
        try { return (zone, ReadBoundedMark(zone)); }
        catch { zone.Dispose(); throw; }
    }

    private static byte[] ReadBoundedMark(SafeFileHandle zone)
    {
        const int limit = 64 * 1024;
        var buffer = new byte[limit + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = RandomAccess.Read(zone, buffer.AsSpan(length), length);
            if (read == 0) return buffer[..length];
            length += read;
        }
        throw new IOException("The download mark exceeds the 64 KiB copy limit; the file was not published.");
    }

    private static SafeFileHandle WriteZoneIdentifier(SafeFileHandle output, byte[] mark, WindowsFileTransaction? transaction)
    {
        const string stream = ":Zone.Identifier";
        const uint access = GenericRead | GenericWrite | Synchronize;
        var target = transaction is null ? OpenOrThrow(output, stream, access, ShareRead | ShareDelete,
            Create, NonDirectoryFile, "Copy the download mark") :
            transaction.CreateFile(WindowsFileTransaction.HeldPath(output) + stream, access, ShareAll, "Copy the private replacement download mark");
        try
        {
            if (transaction is not null && ProtectedHexFile.Identity(target) != ProtectedHexFile.Identity(output))
                throw new IOException("The private download mark does not belong to the staged copy.");
            RandomAccess.Write(target, mark, 0);
            VerifyZoneIdentifier(target, mark);
            if (transaction is not null) RandomAccess.FlushToDisk(target);
            return target;
        }
        catch { target.Dispose(); throw; }
    }

    private static void VerifyZoneIdentifier(SafeFileHandle zone, byte[] expected)
    {
        if (!ReadBoundedMark(zone).AsSpan().SequenceEqual(expected))
            throw new IOException("The copied download mark changed; the file was not published.");
    }

    private static void EnsureZoneIdentifierMissing(SafeFileHandle source)
    {
        var (zone, _) = ReadZoneIdentifier(source);
        using (zone)
        {
            if (zone is not null) throw new IOException("A download mark appeared while copying; the file was not published.");
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
