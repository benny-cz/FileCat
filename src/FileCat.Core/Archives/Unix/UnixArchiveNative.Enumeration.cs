using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

internal static unsafe partial class UnixArchiveNative
{
    // Fixed native '..' only; never accepts a user- or worker-selected relative path.
    internal static SafeFileHandle SelectionAncestor(SafeFileHandle directory) => Handle(OpenAt(Fd(directory), "..", SafeOpen | DirectoryFlag, 0));
    internal static ArchiveSelectionEntry InspectSelection(SafeFileHandle parent, string leaf)
    {
        var facts = UnixFiles.StatChild(parent, leaf) ?? throw ArchiveCapturedSelection.Changed();
        return new(leaf, facts.IsLink ? ArchiveSelectionKind.Link : facts.IsDirectory ? ArchiveSelectionKind.Directory :
            facts.IsRegular ? ArchiveSelectionKind.File : ArchiveSelectionKind.Special, facts.Identity.ToString());
    }

    // The supported 64-bit glibc/musl dirent ABI uses name offset 19; Darwin's inode64 ABI uses 21.
    // A separate open file description avoids sharing a directory cursor with other observations.
    internal static void EnumerateSelection(SafeFileHandle parent, Action<ArchiveSelectionEntry> visit, CancellationToken ct)
    {
        using var cursor = Handle(OpenAt(Fd(parent), ".", SafeOpen | DirectoryFlag, 0));
        nint stream = FdOpenDir(Fd(cursor));
        if (stream == 0) throw ArchiveCapturedSelection.Changed();
        cursor.SetHandleAsInvalid(); // fdopendir now owns this FD, including on callback/cancellation failures.
        Exception? failure = null;
        try
        {
            int records = 0; long bytes = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                byte* record = Mac && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? ReadDirInode64(stream) : ReadDir(stream);
                if (record is null)
                {
                    if (Marshal.GetLastPInvokeError() != 0) throw ArchiveCapturedSelection.Changed();
                    break;
                }
                if (++records > ArchiveWritePolicy.MaxMembers + 2) throw ArchiveWriteBudget.Limit();
                int length = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(record + 16, 2));
                int start = Mac ? 21 : 19, maximum = Mac ? 1048 : 280;
                if (length <= start || length > maximum) throw ArchiveCapturedSelection.Changed();
                ArchiveWriteBudget.Reserve(bytes, length, ArchiveWritePolicy.MaxMetadataBytes); bytes += length;
                var available = new ReadOnlySpan<byte>(record + start, length - start); int end = available.IndexOf((byte)0);
                if (end <= 0 || end > (Mac ? 1023 : 255) || Mac && end != BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(record + 18, 2)))
                    throw ArchiveCapturedSelection.Changed();
                var rawName = available[..end]; if (rawName.SequenceEqual("."u8) || rawName.SequenceEqual(".."u8)) continue;
                string leaf;
                try { leaf = Utf8.GetString(rawName); }
                catch (DecoderFallbackException) { visit(new("", ArchiveSelectionKind.UnrepresentableName, "")); continue; }
                // Unsupported archive spellings can be excluded without opening a device or following any link.
                try { ArchiveWritePolicy.ValidateName(leaf); Leaf(leaf); }
                catch (Exception ex) when (ex is ArgumentException || ex is ArchiveWriteException { Reason: ArchiveWriteReason.InvalidRequest })
                { visit(new(leaf, ArchiveSelectionKind.UnrepresentableName, "")); continue; }
                var facts = UnixFiles.StatChild(parent, leaf) ?? throw ArchiveCapturedSelection.Changed();
                ulong inode = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(record, 8));
                if (inode != facts.Inode) throw ArchiveCapturedSelection.Changed();
                visit(new(leaf, facts.IsLink ? ArchiveSelectionKind.Link : facts.IsDirectory ? ArchiveSelectionKind.Directory :
                    facts.IsRegular ? ArchiveSelectionKind.File : ArchiveSelectionKind.Special, facts.Identity.ToString()));
            }
        }
        catch (Exception ex) { failure = ex; throw; }
        finally { if (CloseDir(stream) != 0 && failure is null) throw ArchiveCapturedSelection.Changed(); }
    }
    [LibraryImport("libc", EntryPoint = "fdopendir", SetLastError = true)]
    private static partial nint FdOpenDir(int fd);
    [LibraryImport("libc", EntryPoint = "readdir", SetLastError = true)]
    private static partial byte* ReadDir(nint stream);
    [LibraryImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)]
    private static partial byte* ReadDirInode64(nint stream);
    [LibraryImport("libc", EntryPoint = "closedir", SetLastError = true)]
    private static partial int CloseDir(nint stream);
}
