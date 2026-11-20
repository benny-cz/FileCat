using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.HiddenData;

public sealed partial class UnixHiddenData
{
    private const int LinuxGetXattrAtNumber = 464, LinuxListXattrAtNumber = 465; // Linux x64 syscall table
    private const int LinuxNoSys = 38;
    private const int MacDirectoryFlags = 0x00100000 | 0x00008000 | 0x00000100 | 0x01000000; // O_DIRECTORY | O_EVTONLY | O_NOFOLLOW | O_CLOEXEC
    private const int MacShowCompression = 0x0020;
    private static readonly Lazy<bool> LinuxHasXattrAt = new(() =>
    {
        // Ask the kernel about an invalid descriptor before inspecting any object. A provider's own error must not
        // send an inventory through the legacy data-descriptor route.
        _ = LinuxListXattrAt(LinuxListXattrAtNumber, -1, string.Empty, 0x1000 /* AT_EMPTY_PATH */, null, 0);
        return Marshal.GetLastPInvokeError() != LinuxNoSys;
    });

    public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
    {
        PathUtil.RefuseAmbiguousUnixName(path);
        ct.ThrowIfCancellationRequested();
        if (!IsSupported) return HiddenInventory.Unavailable(path, HiddenInventoryState.Unsupported, "Extended attributes are unsupported on this platform.");
        if (OperatingSystem.IsMacOS()) return MacMaterializationPolicy.Run(ct, () => MacInventory(path, request, ct),
            (failure, reason) => MacInventoryUnavailable(path, failure, reason));
        using var pin = PinInventoryFile(path, ct, out var stat);
        using var directory = OpenInventoryDescriptorDirectory(path, ct);
        var (result, source) = CollectLinuxInventory(path, pin, directory, stat, ct);
        return new(path, DateTimeOffset.UtcNow, result.Items,
            [new(HiddenKind.Attribute, result.State, result.Items.Length, "visible to this user; held inode", result.Reason)], source)
        { Identity = stat.Identity, BoundToObject = true, Notice = "No attribute values were read. Namespaces inaccessible to this user may be omitted by the filesystem." };
    }

    private static (InventoryNames Names, string Source) CollectLinuxInventory(string path, SafeFileHandle pin, SafeFileHandle directory,
        UnixStat original, CancellationToken ct)
    {
        string entry = pin.DangerousGetHandle().ToInt32().ToString(CultureInfo.InvariantCulture);
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64 && LinuxHasXattrAt.Value)
        {
            // Resolve only the live pin's procfs magic link, relative to the checked directory. Path-based xattr
            // syscalls query the inode without opening its data fork. An empty path on O_PATH returns EBADF on older kernels.
            int fd = directory.DangerousGetHandle().ToInt32();
            var inventory = CollectInventory(path,
                (buffer, size) => LinuxListXattrAt(LinuxListXattrAtNumber, fd, entry, 0, buffer, size),
                name => LinuxGetXattrAtSize(directory, entry, name), ct);
            return (inventory, "listxattrat / getxattrat length queries on the held inode");
        }

        using var file = ReopenInventoryFile(path, directory, entry, original, ct);
        return (CollectInventory(path, (buffer, size) => LinuxFdList(file, buffer, size), name => LinuxFdGet(file, name, null, 0), ct),
            "flistxattr / fgetxattr length queries on the held inode");
    }

    private static SafeFileHandle OpenInventoryFile(string path, CancellationToken ct)
    {
        using var pin = PinInventoryFile(path, ct, out var original);
        using var directory = OpenInventoryDescriptorDirectory(path, ct);
        string entry = pin.DangerousGetHandle().ToInt32().ToString(CultureInfo.InvariantCulture);
        return ReopenInventoryFile(path, directory, entry, original, ct);
    }

    private static SafeFileHandle PinInventoryFile(string path, CancellationToken ct, out UnixStat original)
    {
        ct.ThrowIfCancellationRequested();
        int noFollow = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm or Architecture.Ppc64le ? 0x8000 : 0x20000;
        int fd = InventoryOpen(path, 0x200000 | 0x80000 | noFollow); // O_PATH | O_CLOEXEC | O_NOFOLLOW
        if (fd < 0) throw Error(path);
        var pin = new SafeFileHandle(fd, ownsHandle: true);
        try
        {
            ct.ThrowIfCancellationRequested();
            original = UnixFiles.Stat(pin) ?? throw new IOException("Pinned inode metadata is unavailable.");
            ct.ThrowIfCancellationRequested();
            if (!original.IsRegular && !original.IsDirectory)
                throw new NotSupportedException("Supplementary data is unavailable for this object kind.");
            return pin;
        }
        catch { pin.Dispose(); throw; }
    }

    private static SafeFileHandle OpenInventoryDescriptorDirectory(string path, CancellationToken ct)
    {
        var (directory, error) = LinuxPinnedDescriptors.OpenDirectory(ct);
        return directory ?? throw InventoryError(path, error);
    }

    private static SafeFileHandle ReopenInventoryFile(string path, SafeFileHandle directory, string entry, UnixStat original, CancellationToken ct)
    {
        var (opened, error) = LinuxPinnedDescriptors.ReopenReadOnly(directory, entry, ct);
        if (opened is null) throw InventoryError(path, error);
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

    private static unsafe nint LinuxGetXattrAtSize(SafeFileHandle directory, string entry, string name)
    {
        // struct xattr_args contains a 64-bit value pointer, 32-bit size and 32-bit flags. All zero requests only the length.
        ulong* arguments = stackalloc ulong[2];
        arguments[0] = arguments[1] = 0;
        return LinuxGetXattrAt(LinuxGetXattrAtNumber, directory, entry, 0, name, arguments, 16);
    }

    internal sealed record InventoryNames(ImmutableArray<HiddenItem> Items, HiddenInventoryState State, string? Reason);

    internal static InventoryNames CollectInventory(string path, Func<byte[]?, nint, nint> list, Func<string, nint> sizeOf,
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
            if (got < 0 && error == 34) continue; // inventory grew, bounded complete restart
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
        // O_DIRECTORY makes the open fail before acquiring a FIFO or device descriptor if a selected directory
        // changes kind. O_EVTONLY then permits metadata queries on the held directory without opening its data.
        int descriptor = InventoryOpen(path, MacDirectoryFlags);
        if (descriptor >= 0)
        {
            using var directory = new SafeFileHandle(descriptor, ownsHandle: true);
            ct.ThrowIfCancellationRequested();
            var stat = UnixFiles.Stat(directory) ?? throw new IOException("Held directory metadata is unavailable.");
            if (!stat.IsDirectory) throw new IOException("The held xattr object is not a directory.");
            var (held, compressionView) = CollectMacHeldInventory(path, directory, request.ShowCompressionBacking, ct);
            int heldAttributes = held.Items.Count(item => item.Kind == HiddenKind.Attribute);
            int heldForks = held.Items.Length - heldAttributes;
            string scope = compressionView ? "Darwin held directory compression-visible view" : "Darwin held directory ordinary view";
            string heldBacking = compressionView ? "Compression backing was included in the requested API view."
                : request.ShowCompressionBacking ? "Compression backing: not inventoried; this filesystem refused the requested API view."
                : "Compression backing was not requested.";
            return new(path, DateTimeOffset.UtcNow, held.Items,
                [new(HiddenKind.Attribute, held.State, heldAttributes, scope, held.Reason),
                 new(HiddenKind.ResourceFork, held.State, heldForks, scope, held.Reason)],
                "flistxattr / fgetxattr size queries on a held directory")
            {
                Identity = stat.Identity,
                BoundToObject = true,
                ActionsAvailable = held.State == HiddenInventoryState.Complete,
                DeletionAvailable = compressionView,
                Notice = $"{heldBacking} No values read for this inventory. Explicit directory value reads and exports use a fresh held descriptor." +
                    (compressionView ? " Deletion requires a fresh held descriptor and complete compression-visible inventory." : " Deletion requires a complete compression-visible inventory."),
            };
        }
        if (Marshal.GetLastPInvokeError() == MacMaterializationPolicy.Required)
            throw InventoryError(path, MacMaterializationPolicy.Required);
        // Path xattr APIs are metadata-only and final-component no-follow. They do not supply a lifetime pin. Keep
        // that missing binding explicit rather than opening an arbitrary replacement device with O_RDONLY/O_EVTONLY.
        // The path-only fallback cannot claim the held directory's XATTR_SHOWCOMPRESSION view. Its ordinary view
        // must not imply that storage backing was inventoried, even when the caller requested the deeper view.
        int options = MacNoFollow;
        var result = CollectInventory(path, (buffer, size) => MacList(path, buffer, size, options), SizeWithoutMaterialization, ct);
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

        nint SizeWithoutMaterialization(string name)
            => MacSizeWithoutMaterialization(path, () => MacGet(path, name, null, 0, 0, options));
    }

    private static (InventoryNames Names, bool CompressionView) CollectMacHeldInventory(string path,
        SafeFileHandle directory, bool showCompression, CancellationToken ct)
    {
        InventoryNames Read(int options) => CollectInventory(path,
            (buffer, size) => MacFdList(directory, buffer, size, options),
            name => MacSizeWithoutMaterialization(path, () => MacFdGet(directory, name, null, 0, 0, options)), ct);
        try { return (Read(showCompression ? MacShowCompression : 0), showCompression); }
        catch (IOException ex) when (showCompression && ex.HResult is 22 or 45) // EINVAL / ENOTSUP
        { return (Read(0), false); }
    }

    private static SafeFileHandle OpenMacActionDirectory(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int descriptor = InventoryOpen(path, MacDirectoryFlags);
        if (descriptor < 0) throw InventoryError(path, Marshal.GetLastPInvokeError());
        var directory = new SafeFileHandle(descriptor, ownsHandle: true);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (UnixFiles.Stat(directory)?.IsDirectory != true)
                throw new IOException("The held supplementary-data object is not a directory.");
            return directory;
        }
        catch { directory.Dispose(); throw; }
    }

    private static byte[] ReadMacDirectoryValue(string path, HiddenItem item, int max,
        OutputEfsObservation? observation, FileIdentity? reviewed)
    {
        if (item.Kind is not (HiddenKind.Attribute or HiddenKind.ResourceFork) ||
            item.Name.Length == 0 || item.Name.Contains('\0') ||
            !Encoding.UTF8.GetString(new UTF8Encoding(false, true).GetBytes(item.Name)).Equals(item.Name, StringComparison.Ordinal))
            throw new NotSupportedException("The attribute category or native name is invalid; refresh required.");
        return MacMaterializationPolicy.Run(default, () =>
        {
            using var directory = OpenMacActionDirectory(path, default);
            var stat = UnixFiles.Stat(directory) ?? throw new IOException("Held directory metadata is unavailable.");
            if (reviewed is { } expected && stat.Identity != expected)
                throw new IOException("The supplementary value belongs to a different directory. Refresh the inventory and try again.");
            var (names, compressionView) = CollectMacHeldInventory(path, directory, showCompression: true, ct: default);
            if (names.State != HiddenInventoryState.Complete)
                throw new IOException("The attribute category could not be completely resolved; refresh required. " + names.Reason);
            if (!names.Items.Any(current => current.Kind == item.Kind && current.Name == item.Name))
                throw new FileNotFoundException("The exact attribute is missing; refresh required.", path);
            string? before = SourceChangeToken.FromHandle(directory);
            if (before is null) throw new IOException("Held directory revision is unavailable; refresh required.");
            observation?.ObserveOpenedMac(directory);
            byte[] value = ReadMacHeldAttribute(directory, path, item.Name, max, compressionView ? MacShowCompression : 0);
            string? after = SourceChangeToken.FromHandle(directory);
            if (after is null || after != before)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(value);
                throw new IOException("The directory changed while its attribute was read; refresh required.");
            }
            observation?.ObserveOpenedMac(directory);
            return value;
        }, (failure, reason) => throw new IOException(reason));
    }

    private static void DeleteMacDirectoryValue(string path, HiddenItem item, FileIdentity? reviewed, CancellationToken ct)
    {
        if (item.Kind is not (HiddenKind.Attribute or HiddenKind.ResourceFork) || item.Name.Length == 0 || item.Name.Contains('\0') ||
            !Encoding.UTF8.GetString(new UTF8Encoding(false, true).GetBytes(item.Name)).Equals(item.Name, StringComparison.Ordinal))
            throw new NotSupportedException("The attribute category or native name is invalid; refresh required.");
        bool removed = false;
        // Cancellation is checked before the native call, never after its effect may have occurred.
        _ = MacMaterializationPolicy.Run(default, () =>
        {
            using var directory = OpenMacActionDirectory(path, ct);
            var stat = UnixFiles.Stat(directory) ?? throw new IOException("Held directory metadata is unavailable.");
            if (reviewed is { } expected && stat.Identity != expected)
                throw new IOException("The supplementary value belongs to a different directory. Refresh the inventory and try again.");
            string? before = SourceChangeToken.FromHandle(directory);
            if (before is null) throw new IOException("Held directory revision is unavailable; refresh required.");
            var (names, compressionView) = CollectMacHeldInventory(path, directory, showCompression: true, ct);
            if (!compressionView) throw new NotSupportedException("Deletion requires a complete compression-visible directory inventory.");
            if (names.State != HiddenInventoryState.Complete)
                throw new IOException("The attribute category could not be completely resolved; refresh required. " + names.Reason);
            var current = names.Items.SingleOrDefault(i => i.Kind == item.Kind && i.Name == item.Name)
                ?? throw new FileNotFoundException("The exact attribute is missing; refresh required.", path);
            if (HiddenMetadataRoles.DeleteRefusal(current) is { } reason) throw new NotSupportedException(reason);
            if (SourceChangeToken.FromHandle(directory) != before)
                throw new IOException("The directory changed before its attribute could be removed; refresh required.");
            ct.ThrowIfCancellationRequested();
            if (MacFdRemove(directory, current.Name, 0) != 0) throw InventoryError(path, Marshal.GetLastPInvokeError());
            removed = true;
            return new object();
        }, (failure, reason) =>
        {
            if (failure == MacMaterializationPolicy.Failure.RestoreFailed && removed)
                throw new HiddenDataMutationUncertainException("The attribute may have been removed, but FileCat could not restore the metadata policy. Refresh the inventory before another action.");
            throw new IOException(reason);
        });
    }

    private static byte[] ReadMacHeldAttribute(SafeFileHandle directory, string path, string name, int max, int options)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            nint length = MacFdGet(directory, name, null, 0, 0, options);
            if (length < 0) throw InventoryError(path, Marshal.GetLastPInvokeError());
            if (length > MaxValue) throw new NotSupportedException("The attribute exceeds the 16 MiB explicit-value acquisition limit.");
            if (max == 0) return [];
            byte[] buffer = new byte[(int)length];
            try
            {
                nint got = MacFdGet(directory, name, buffer, buffer.Length, 0, options);
                if (got < 0 && Marshal.GetLastPInvokeError() == 34 && attempt < 2) continue;
                if (got < 0) throw InventoryError(path, Marshal.GetLastPInvokeError());
                if (got > buffer.Length) throw new IOException("The attribute query returned an invalid length.");
                return buffer.AsSpan(0, (int)Math.Min(got, max)).ToArray();
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer); }
        }
        throw new IOException("The attribute kept growing beyond two retries; refresh required.");
    }

    internal static nint MacSizeWithoutMaterialization(string path, Func<nint> query)
    {
        nint size = query();
        if (size < 0 && Marshal.GetLastPInvokeError() == MacMaterializationPolicy.Required)
            throw InventoryError(path, MacMaterializationPolicy.Required);
        return size;
    }

    private static HiddenInventory MacInventoryUnavailable(string path, MacMaterializationPolicy.Failure failure, string reason)
    {
        var state = failure switch
        {
            MacMaterializationPolicy.Failure.WouldMaterialize => HiddenInventoryState.NotInspected,
            MacMaterializationPolicy.Failure.RestoreFailed => HiddenInventoryState.Failed,
            _ => HiddenInventoryState.Unsupported,
        };
        return new(path, DateTimeOffset.UtcNow, [],
            [new(HiddenKind.Attribute, state, 0, "Darwin ordinary no-follow path view", reason),
             new(HiddenKind.ResourceFork, state, 0, "Darwin ordinary no-follow path view", reason)],
            "macOS metadata with materialization disabled") { Notice = reason };
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
    [LibraryImport("libc", EntryPoint = "flistxattr", SetLastError = true)]
    private static partial nint MacFdList(SafeFileHandle directory, byte[]? names, nint size, int options);
    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacFdGet(SafeFileHandle directory, string name, byte[]? value, nint size, uint position, int options);
    [LibraryImport("libc", EntryPoint = "fremovexattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacFdRemove(SafeFileHandle directory, string name, int options);
    [LibraryImport("libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxListXattrAt(nint syscall, int directory, string entry, uint flags, byte[]? names, nint size);
    [LibraryImport("libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static unsafe partial nint LinuxGetXattrAt(nint syscall, SafeFileHandle directory, string entry, uint flags,
        string name, ulong* arguments, nuint argumentSize);
}
