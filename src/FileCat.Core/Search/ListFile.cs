using System.Text;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Search;

/// <summary>
/// A list file (plan §19.1, Total Commander's LOADLIST; FileCat's --list): paths, one per line, in UTF-8 or the encoding
/// its byte order mark names, opened as a result set. A relative path is taken from the list's own folder. Lines that
/// name nothing are counted; network, linked and unsafe paths are counted and left out, never contacted on the list's behalf (a list can
/// come from anywhere, and asking a server whether a path exists hands it the user's sign-in; release issue I16's rule).
/// </summary>
public static partial class ListFile
{
    /// <summary>The largest list read (a million paths fit several times over).</summary>
    public const long MaxBytes = 64L << 20;

    // A path cannot usefully exceed the Windows extended-length path limit. Keep one
    // unterminated line bounded even when a special file reports a zero length.
    private const int MaxLineChars = 32_768;

    /// <summary>The most members one list adds.</summary>
    public const int MaxItems = 500_000;

    /// <summary>What a list gave: members added, lines naming nothing, network/linked/unsafe paths left out, and whether it went on past <see cref="MaxItems"/>.</summary>
    public sealed record Outcome(int Added, int Missing, int Network, bool Truncated);

    public static Outcome Load(string path, ResultSet set, CancellationToken ct)
    {
        // A source on a network, mapped drive or link must be refused before an existence
        // check can contact it. On Windows, keep every path component open against rename
        // while opening the final object, then read from that held object.
        if (IsNetwork(path)) throw new IOException("Network list sources are not opened.");
        bool sourceAbsent = false;
        using var sourceLease = OperatingSystem.IsWindows() ? OpenWindowsLocal(path, readContent: true, out sourceAbsent) : null;
        if (OperatingSystem.IsWindows() && sourceLease is null)
        {
            if (sourceAbsent) throw new FileNotFoundException("The list file does not exist.", path);
            throw new IOException("Linked, network or inaccessible list sources are not opened.");
        }
        var info = sourceLease is null ? new FileInfo(path) : null;
        if (info is not null && !info.Exists) throw new FileNotFoundException("The list file does not exist.", path);
        string fullSource = sourceLease?.FullPath ?? info!.FullName;
        using var stream = sourceLease is null
            ? new FileStream(fullSource, FileMode.Open, FileAccess.Read, FileShare.Read)
            : new FileStream(sourceLease.Final, FileAccess.Read);
        if (stream.Length > MaxBytes) throw new IOException($"The list file is larger than {MaxBytes >> 20} MiB.");
        string folder = Path.GetDirectoryName(fullSource)!;
        int added = 0, missing = 0, network = 0;
        bool truncated = false;
        var batch = new List<(ItemRef Item, string Relative)>(1024);
        using (var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
        {
            foreach (string line in ReadBoundedLines(reader, ct))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (IsNetwork(line))
                {
                    network++;
                    continue;
                }
                string full;
                try { full = Path.GetFullPath(line, folder); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    missing++;
                    continue;
                }
                // A relative line in a list kept on a share is a network path too.
                EntryKind? kind;
                if (OperatingSystem.IsWindows())
                {
                    using var candidate = OpenWindowsLocal(full, readContent: false, out bool absent);
                    if (candidate is null)
                    {
                        if (absent) missing++; else network++;
                        continue;
                    }
                    kind = candidate.IsDirectory ? EntryKind.Directory : EntryKind.File;
                }
                else kind = Directory.Exists(full) ? EntryKind.Directory : File.Exists(full) ? EntryKind.File : null;
                if (kind is null)
                {
                    missing++;
                    continue;
                }
                if (added + batch.Count >= MaxItems)
                {
                    truncated = true;
                    break;
                }
                batch.Add((ItemRef.ForFileSystemPath(full, kind.Value), ""));
                if (batch.Count == batch.Capacity)
                {
                    added += set.AddRange(batch);
                    batch.Clear();
                }
            }
        }
        added += set.AddRange(batch);
        return new Outcome(added, missing, network, truncated);
    }

    private static IEnumerable<string> ReadBoundedLines(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        long decodedChars = 0;
        bool afterCarriageReturn = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int count = reader.Read(buffer, 0, buffer.Length);
            if (count == 0) break;
            decodedChars += count;
            if (decodedChars > MaxBytes)
                throw new IOException($"The list file is larger than {MaxBytes >> 20} MiB.");

            for (int i = 0; i < count; i++)
            {
                char c = buffer[i];
                if (afterCarriageReturn)
                {
                    afterCarriageReturn = false;
                    if (c == '\n') continue;
                }
                if (c is '\r' or '\n')
                {
                    yield return line.ToString();
                    line.Clear();
                    afterCarriageReturn = c == '\r';
                }
                else
                {
                    if (line.Length == MaxLineChars)
                        throw new IOException($"A list path exceeds {MaxLineChars} characters.");
                    line.Append(c);
                }
            }
        }
        if (line.Length != 0) yield return line.ToString();
    }

    private static bool IsNetwork(string path) =>
        PathUtil.IsUncPath(path) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || path.StartsWith("//?/UNC/", StringComparison.OrdinalIgnoreCase);

    private const uint ReadAttributes = 0x80, GenericRead = 0x80000000, OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const int AttributeTagInfo = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { public uint Attributes, Tag; }

    private sealed class WindowsLease(string fullPath, List<SafeFileHandle> held, bool isDirectory) : IDisposable
    {
        public string FullPath { get; } = fullPath;
        public SafeFileHandle Final => held[^1];
        public bool IsDirectory { get; } = isDirectory;
        public void Dispose() { foreach (var handle in held) handle.Dispose(); }
    }

    // Open each component itself, not its target. Holding all earlier directories without
    // sharing write/delete prevents a pathname swap while a later component is opened.
    // This policy intentionally refuses local links as well as network links.
    private static WindowsLease? OpenWindowsLocal(string path, bool readContent, out bool absent)
    {
        absent = false;
        if (IsNetwork(path)) return null;
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        if (IsNetwork(full)) return null;
        string? root = Path.GetPathRoot(full);
        if (root is null) return null;
        string[] parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 512) return null;
        var held = new List<SafeFileHandle>(parts.Length + 1);
        WindowsLease? lease = null;
        try
        {
            if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram)) return null;
            string current = root;
            for (int i = -1; i < parts.Length; i++)
            {
                if (i >= 0) current = Path.Combine(current, parts[i]);
                bool last = i == parts.Length - 1;
                uint access = last && readContent ? GenericRead : ReadAttributes;
                string native = current.Length >= 248 && !current.StartsWith(@"\\?\", StringComparison.Ordinal) ? @"\\?\" + current : current;
                var handle = CreateFile(native, access, FileShare.Read, 0, OpenExisting,
                    BackupSemantics | OpenReparsePoint, 0);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    absent = error is 2 or 3;
                    return null;
                }
                held.Add(handle);
                if (!GetFileInformationByHandleEx(handle, AttributeTagInfo, out AttributeTag tag, 8) ||
                    (tag.Attributes & (uint)FileAttributes.ReparsePoint) != 0) return null;
                bool directory = (tag.Attributes & (uint)FileAttributes.Directory) != 0;
                if (!last && !directory) { absent = true; return null; }
                if (last)
                {
                    if (readContent && directory) return null;
                    lease = new WindowsLease(full, held, directory);
                    return lease;
                }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        finally
        {
            if (lease is null) foreach (var handle in held) handle.Dispose();
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string name, uint access, FileShare share, nint security,
        uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTag info, uint size);
}
