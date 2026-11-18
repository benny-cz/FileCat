using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.HiddenData;

public sealed partial class UnixHiddenData
{
    public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
    {
        PathUtil.RefuseAmbiguousUnixName(path);
        ct.ThrowIfCancellationRequested();
        if (!IsSupported) return HiddenInventory.Unavailable(path, HiddenInventoryState.Unsupported, "Extended attributes are unsupported on this platform.");
        if (OperatingSystem.IsMacOS()) return MacInventory(path, request, ct);
        using var file = OpenInventoryFile(path, ct);
        var stat = UnixFiles.Stat(file) ?? throw new IOException("Held inventory identity is unavailable.");
        ct.ThrowIfCancellationRequested();
        var result = CollectInventory(path, (buffer, size) => LinuxFdList(file, buffer, size),
            name => LinuxFdGet(file, name, null, 0), ct);
        return new(path, DateTimeOffset.UtcNow, result.Items,
            [new(HiddenKind.Attribute, result.State, result.Items.Length, "visible to this user; held inode", result.Reason)], "flistxattr / fgetxattr length queries")
        { Identity = stat.Identity, BoundToObject = true, Notice = "No attribute values were read. Namespaces inaccessible to this user may be omitted by the filesystem." };
    }

    private static SafeFileHandle OpenInventoryFile(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int noFollow = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm or Architecture.Ppc64le ? 0x8000 : 0x20000;
        int fd = InventoryOpen(path, 0x200000 | 0x80000 | noFollow); // O_PATH | O_CLOEXEC | O_NOFOLLOW
        if (fd < 0) throw Error(path);
        using var pin = new SafeFileHandle((nint)fd, true);
        ct.ThrowIfCancellationRequested();
        var original = UnixFiles.Stat(pin) ?? throw new IOException("Pinned inode metadata is unavailable.");
        ct.ThrowIfCancellationRequested();
        if (!original.IsRegular && !original.IsDirectory) throw new NotSupportedException("This inode's attribute inventory is not qualified; no FIFO, device or symlink data descriptor is opened.");
        // Reopen only the pinned inode through procfs, never the mutable original path. Its type cannot change while
        // pinned. No content read is issued; fgetxattr needs a real descriptor rather than O_PATH.
        fd = InventoryOpen("/proc/self/fd/" + fd, 0x80000 | 0x800 | 0x100); // O_RDONLY | O_CLOEXEC | O_NONBLOCK | O_NOCTTY
        if (fd < 0) throw Error(path);
        var opened = new SafeFileHandle((nint)fd, true);
        try
        {
            ct.ThrowIfCancellationRequested();
            var current = UnixFiles.Stat(opened) ?? throw new IOException("Reopened inode metadata is unavailable.");
            ct.ThrowIfCancellationRequested();
            if (current.Device != original.Device || current.Inode != original.Inode || current.IsRegular != original.IsRegular || current.IsDirectory != original.IsDirectory)
                throw new IOException("The pinned inode could not be reacquired safely.");
            return opened;
        }
        catch { opened.Dispose(); throw; }
    }

    internal sealed record InventoryNames(ImmutableArray<HiddenItem> Items, HiddenInventoryState State, string? Reason);

    private static InventoryNames CollectInventory(string path, Func<byte[]?, nint, nint> list, Func<string, nint> sizeOf,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            nint required = list(null, 0);
            int error = required < 0 ? Marshal.GetLastPInvokeError() : 0;
            ct.ThrowIfCancellationRequested();
            if (required < 0) throw InventoryError(path, error);
            if (required > HiddenInventory.MaxNameBytes) return new([], HiddenInventoryState.Partial, "Native names exceed the 1 MiB acquisition limit.");
            if (required == 0) return new([], HiddenInventoryState.Complete, null);
            byte[] names = new byte[(int)required];
            ct.ThrowIfCancellationRequested();
            nint got = list(names, names.Length);
            error = got < 0 ? Marshal.GetLastPInvokeError() : 0;
            ct.ThrowIfCancellationRequested();
            if (got < 0 && error == 34 && attempt < 2) continue; // inventory grew, bounded complete restart
            if (got < 0) throw InventoryError(path, error);
            if (got > names.Length) return new([], HiddenInventoryState.Failed, "Native names length exceeds the supplied buffer.");
            var parsed = ParseInventoryNames(names.AsSpan(0, (int)got));
            if (parsed.State != HiddenInventoryState.Complete) return parsed;
            var items = ImmutableArray.CreateBuilder<HiddenItem>(parsed.Items.Length);
            bool changed = false;
            foreach (var item in parsed.Items)
            {
                ct.ThrowIfCancellationRequested();
                nint length = sizeOf(item.Name);
                ct.ThrowIfCancellationRequested();
                if (length < 0) changed = true;
                items.Add(item with { Size = length < 0 ? -1 : (long)length });
            }
            return new(items.MoveToImmutable(), changed ? HiddenInventoryState.Partial : HiddenInventoryState.Complete,
                changed ? "Some attribute lengths were unavailable or changed after the name query." : null);
        }
        return new([], HiddenInventoryState.Partial, "The names inventory kept growing beyond two resize/retry cycles.");
    }

    internal static InventoryNames ParseInventoryNames(ReadOnlySpan<byte> bytes)
    {
        var items = ImmutableArray.CreateBuilder<HiddenItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int retained = 0;
        while (!bytes.IsEmpty)
        {
            int end = bytes.IndexOf((byte)0);
            if (end <= 0) return new(items.ToImmutable(), HiddenInventoryState.Failed, "Malformed or unterminated native attribute name.");
            if (end > 16 * 1024) return new(items.ToImmutable(), HiddenInventoryState.Partial, "A native attribute name exceeds the qualified per-name bound.");
            string name;
            try { name = new UTF8Encoding(false, true).GetString(bytes[..end]); }
            catch (DecoderFallbackException) { return new(items.ToImmutable(), HiddenInventoryState.Failed, "A native name cannot round-trip as UTF-8; no replacement/normalization is used."); }
            if (!seen.Add(name)) return new(items.ToImmutable(), HiddenInventoryState.Changed, "Attribute enumeration repeated a name.");
            if (items.Count == HiddenInventory.MaxEntries || (retained += 64 + name.Length * 2) > HiddenInventory.MaxNameBytes)
                return new(items.ToImmutable(), HiddenInventoryState.Partial, "The 1,024-entry/1 MiB retained names limit was reached.");
            var item = new HiddenItem(name, OperatingSystem.IsMacOS() && name == ResourceForkName ? HiddenKind.ResourceFork : HiddenKind.Attribute, -1);
            items.Add(item with { Role = HiddenMetadataRoles.Classify(item) });
            bytes = bytes[(end + 1)..];
        }
        return new(items.ToImmutable(), HiddenInventoryState.Complete, null);
    }

    private static HiddenInventory MacInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
    {
        // Path xattr APIs are metadata-only and final-component no-follow. They do not supply a lifetime pin. Keep
        // that missing binding explicit rather than opening an arbitrary replacement device with O_RDONLY/O_EVTONLY.
        // XATTR_SHOWCOMPRESSION is reserved for a qualified bound adapter. A path-only result must not imply that
        // this ordinary view inventoried storage backing, even when the caller requested the deeper API view.
        int options = MacNoFollow;
        var result = CollectInventory(path, (buffer, size) => MacList(path, buffer, size, options), name => MacGet(path, name, null, 0, 0, options), ct);
        int attributes = result.Items.Count(item => item.Kind == HiddenKind.Attribute);
        int forks = result.Items.Length - attributes;
        var categories = ImmutableArray.CreateBuilder<HiddenCategoryInventory>();
        foreach (var kind in new[] { HiddenKind.Attribute, HiddenKind.ResourceFork })
            categories.Add(new(kind, result.State == HiddenInventoryState.Complete ? HiddenInventoryState.Partial : result.State,
                kind == HiddenKind.Attribute ? attributes : forks, "Darwin ordinary no-follow path view",
                result.Reason ?? "Same-object lifetime binding is not qualified for this API view; counts are observational lower bounds."));
        string backing = request.ShowCompressionBacking ? "Compression backing: not inventoried." : "Compression backing: not requested.";
        return new(path, DateTimeOffset.UtcNow, result.Items, categories.ToImmutable(), "listxattr / getxattr size queries")
        {
            Notice = $"Observed {attributes} ordinary attributes and {forks} resource forks. {backing} " +
                "No values read. Value, export and delete actions require a qualified bound adapter.",
        };
    }

    private static Exception InventoryError(string path, int error) => error is 1 or 13 ? new UnauthorizedAccessException($"{path}: {Marshal.GetPInvokeErrorMessage(error)}")
        : error == 2 ? new FileNotFoundException("The inventory object is missing.", path)
        : new IOException($"{path}: {Marshal.GetPInvokeErrorMessage(error)}", error);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int InventoryOpen(string path, int flags);
    [LibraryImport("libc", EntryPoint = "flistxattr", SetLastError = true)]
    private static partial nint LinuxFdList(SafeFileHandle file, byte[]? names, nint size);
    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxFdGet(SafeFileHandle file, string name, byte[]? value, nint size);
}
