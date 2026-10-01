using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using STATSTG = System.Runtime.InteropServices.ComTypes.STATSTG;

namespace FileCat.Platform.Windows.Mtp;

public sealed record PortableDeviceInfo(string Id, string Name, string Manufacturer);

/// <summary>
/// What a storage lets a computer do (WPD_STORAGE_ACCESS_CAPABILITY): read-write as a rule; read-only for a write-protected
/// card, or read-only apart from deleting, as some cameras offer their pictures.
/// </summary>
public enum StorageAccess { ReadWrite = 0, ReadOnly = 1, ReadOnlyWithDeletion = 2 }

/// <summary>
/// What a device can do to its objects, from the commands its driver lists as supported: a phone in file-transfer mode
/// lists them all; an iPhone lists deleting only (its storage still says read-write), so nothing can be added to it or
/// renamed.
/// </summary>
public sealed record DeviceAbilities(bool CreateFolders, bool CreateFiles, bool Rename, bool Delete)
{
    /// <summary>Assumed when the driver lists nothing: everything, and the device decides each time.</summary>
    public static readonly DeviceAbilities Unknown = new(true, true, true, true);
}

/// <summary>One object on a device: a storage (internal memory, SD card), a folder, or a file.</summary>
public sealed record PortableObject(string Id, string Name, bool IsFolder, bool IsStorage, long Size, DateTime ModifiedUtc, bool CanDelete, bool IsHidden,
    string? ParentId = null, StorageAccess Access = StorageAccess.ReadWrite);

/// <summary>A device write stream, once disposed: the ID of the object it created, when the device says (null otherwise).</summary>
public interface ICreatedObject
{
    string? CreatedObjectId { get; }
}

/// <summary>
/// One opened portable device (MTP phone, camera, player). MTP runs one operation at a time, so calls are serialized.
/// Failures surface as <see cref="IOException"/> (or <see cref="UnauthorizedAccessException"/> when the device refuses,
/// usually because it is locked or not in file-transfer mode); nothing from COM leaks out.
/// </summary>
public sealed class WpdSession : IDisposable
{
    private readonly object _lock = new();
    private readonly IPortableDevice _device;
    private readonly IPortableDeviceContent _content;
    private readonly IPortableDeviceProperties _properties;
    private readonly IPortableDeviceResources _resources;
    private readonly IPortableDeviceKeyCollection _keys;
    private readonly IPortableDeviceKeyCollection _storageKeys;
    private bool _disposed;

    private WpdSession(string id, IPortableDevice device)
    {
        Id = id;
        _device = device;
        device.Content(out _content);
        _content.Properties(out _properties);
        _content.Transfer(out _resources);
        _keys = Wpd.Create<IPortableDeviceKeyCollection>(Wpd.CLSID_PortableDeviceKeyCollection);
        foreach (var key in new[] { Wpd.Name, Wpd.OriginalFileName, Wpd.ContentType, Wpd.Size, Wpd.DateModified, Wpd.CanDelete, Wpd.IsHidden, Wpd.ParentId })
        {
            var k = key;
            _keys.Add(ref k);
        }
        _storageKeys = Wpd.Create<IPortableDeviceKeyCollection>(Wpd.CLSID_PortableDeviceKeyCollection);
        var access = Wpd.StorageAccessCapability;
        _storageKeys.Add(ref access);
        Abilities = AbilitiesOf(device);
    }

    public string Id { get; }

    public DeviceAbilities Abilities { get; }

    private static DeviceAbilities AbilitiesOf(IPortableDevice device)
    {
        IPortableDeviceCapabilities? capabilities = null;
        IPortableDeviceKeyCollection? commands = null;
        try
        {
            device.Capabilities(out capabilities);
            capabilities.GetSupportedCommands(out commands);
            uint count = 0;
            commands.GetCount(ref count);
            var supported = new HashSet<PropertyKey>();
            for (uint i = 0; i < count; i++)
            {
                var key = default(PropertyKey);
                commands.GetAt(i, ref key);
                supported.Add(key);
            }
            return supported.Count == 0 ? DeviceAbilities.Unknown
                : new DeviceAbilities(supported.Contains(Wpd.CommandCreateWithPropertiesOnly), supported.Contains(Wpd.CommandCreateWithPropertiesAndData),
                    supported.Contains(Wpd.CommandSetProperties), supported.Contains(Wpd.CommandDeleteObjects));
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return DeviceAbilities.Unknown; }
        finally
        {
            if (commands is not null) Marshal.ReleaseComObject(commands);
            if (capabilities is not null) Marshal.ReleaseComObject(capabilities);
        }
    }

    public bool IsBroken { get; private set; }

    /// <summary>
    /// Phones, cameras, and players. Drives that Windows also exposes through WPD (USB sticks, volumes) are left out:
    /// they appear as drives already.
    /// </summary>
    public static IReadOnlyList<PortableDeviceInfo> ListDevices()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            var manager = Wpd.Create<IPortableDeviceManager>(Wpd.CLSID_PortableDeviceManager);
            try
            {
                manager.RefreshDeviceList();
                uint count = 0;
                manager.GetDevices(null, ref count);
                if (count == 0) return [];
                var ids = new IntPtr[count];
                manager.GetDevices(ids, ref count);
                var list = new List<PortableDeviceInfo>();
                foreach (var ptr in ids.Take((int)count))
                {
                    string id = Marshal.PtrToStringUni(ptr) ?? string.Empty;
                    Marshal.FreeCoTaskMem(ptr);
                    if (id.Length == 0 || id.Contains("wpdbusenum", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new PortableDeviceInfo(id, Text(manager.GetDeviceFriendlyName, id) is { Length: > 0 } name ? name : "Portable device",
                        Text(manager.GetDeviceManufacturer, id)));
                }
                return list;
            }
            finally
            {
                Marshal.ReleaseComObject(manager);
            }
        }
        catch (COMException) { return []; }
    }

    private delegate void TextGetter(string id, char[]? buffer, ref uint length);

    private static string Text(TextGetter get, string id)
    {
        try
        {
            uint length = 0;
            get(id, null, ref length);
            if (length == 0) return string.Empty;
            var buffer = new char[length];
            get(id, buffer, ref length);
            return new string(buffer, 0, (int)Math.Max(0, Math.Min(length, buffer.Length)) - 0).TrimEnd('\0');
        }
        catch (COMException) { return string.Empty; }
    }

    public static WpdSession Open(string deviceId)
    {
        var device = Wpd.Create<IPortableDevice>(Wpd.CLSID_PortableDeviceFTM);
        var info = Wpd.Create<IPortableDeviceValues>(Wpd.CLSID_PortableDeviceValues);
        var name = Wpd.ClientName;
        info.SetStringValue(ref name, "FileCat");
        var major = Wpd.ClientMajorVersion;
        info.SetUnsignedIntegerValue(ref major, 1);
        var minor = Wpd.ClientMinorVersion;
        info.SetUnsignedIntegerValue(ref minor, 0);
        var revision = Wpd.ClientRevision;
        info.SetUnsignedIntegerValue(ref revision, 0);
        try
        {
            device.Open(deviceId, info);
            return new WpdSession(deviceId, device);
        }
        catch (COMException ex)
        {
            Marshal.ReleaseComObject(device);
            throw Explain(ex, "open the device", out _);
        }
        catch (UnauthorizedAccessException ex)
        {
            Marshal.ReleaseComObject(device);
            throw Refused(ex);
        }
        finally
        {
            Marshal.ReleaseComObject(info);
        }
    }

    private static UnauthorizedAccessException Refused(Exception inner) =>
        new("The device refused access. Unlock it and allow this computer (Trust on an iPhone, File transfer in an Android phone's USB options), then try again.", inner);

    private Exception Translate(COMException ex, string what)
    {
        var translated = Explain(ex, what, out bool broken);
        IsBroken |= broken;
        return translated;
    }

    private static Exception Explain(COMException ex, string what, out bool broken)
    {
        const int DeviceNotConnected = unchecked((int)0x8007048F), NotFound = unchecked((int)0x80070002), Busy = unchecked((int)0x800700AA),
            GenFailure = unchecked((int)0x8007001F), Disconnected = unchecked((int)0x80042009), NotSupported = unchecked((int)0x80070032);
        // COM's own message usually ends with the code already ("The request is not supported. (0x80070032)").
        string message = ex.Message.Trim(), code = $"0x{ex.HResult:X8}";
        broken = ex.HResult is DeviceNotConnected or GenFailure or Disconnected;
        return ex.HResult switch
        {
            unchecked((int)0x80070005) => Refused(ex),
            NotFound => new FileNotFoundException($"Could not {what}: the item is no longer on the device.", ex),
            Busy => new IOException($"Could not {what}: the device is busy with another transfer.", ex),
            DeviceNotConnected or Disconnected => new IOException($"Could not {what}: the device was disconnected.", ex),
            NotSupported => new IOException($"Could not {what}: the device does not support it.", ex),
            _ => new IOException($"Could not {what}: {message}" + (message.Contains(code, StringComparison.OrdinalIgnoreCase) ? "" : $" ({code})"), ex),
        };
    }

    private T Call<T>(string what, Func<T> action)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try { return action(); }
            catch (COMException ex) { throw Translate(ex, what); }
            catch (UnauthorizedAccessException ex) { throw Refused(ex); }
            catch (InvalidCastException ex) { throw new IOException($"Could not {what}: the device answered unexpectedly.", ex); }
        }
    }

    /// <summary>The objects inside a storage or folder ("DEVICE" lists the storages).</summary>
    public IReadOnlyList<PortableObject> Children(string parentId, CancellationToken ct) => Call("list the folder", () =>
    {
        _content.EnumObjects(0, parentId, IntPtr.Zero, out var enumerator);
        var ids = new List<string>();
        try
        {
            var batch = new IntPtr[64];
            while (ids.Count < 200_000)
            {
                ct.ThrowIfCancellationRequested();
                uint fetched = 0;
                int hr = enumerator.Next((uint)batch.Length, batch, ref fetched);
                for (int i = 0; i < fetched; i++)
                {
                    ids.Add(Marshal.PtrToStringUni(batch[i]) ?? string.Empty);
                    Marshal.FreeCoTaskMem(batch[i]);
                }
                if (hr != 0 || fetched == 0) break;
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
        var list = new List<PortableObject>(ids.Count);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (id.Length > 0 && Describe(id) is { } o) list.Add(o);
        }
        return (IReadOnlyList<PortableObject>)list;
    });

    public PortableObject? Get(string objectId) => Call("read the item's properties", () => Describe(objectId));

    private PortableObject? Describe(string id)
    {
        IPortableDeviceValues values;
        try { _properties.GetValues(id, _keys, out values); }
        catch (COMException) { return null; }
        try
        {
            Guid contentType = Guid(values, Wpd.ContentType);
            bool storage = contentType == Wpd.ContentFunctionalObject;
            bool folder = storage || contentType == Wpd.ContentFolder;
            string name = (storage ? Str(values, Wpd.Name) : null) ?? Str(values, Wpd.OriginalFileName) ?? Str(values, Wpd.Name) ?? id;
            long size = folder ? -1 : (long)Math.Min(ULong(values, Wpd.Size) ?? 0, long.MaxValue);
            return new PortableObject(id, name, folder, storage, size, Date(values, Wpd.DateModified), Bool(values, Wpd.CanDelete) ?? true, Bool(values, Wpd.IsHidden) ?? false,
                Str(values, Wpd.ParentId), storage ? AccessOf(id) : StorageAccess.ReadWrite);
        }
        finally
        {
            Marshal.ReleaseComObject(values);
        }
    }

    /// <summary>A storage's access; read-write when the device does not say, so the device decides as before.</summary>
    private StorageAccess AccessOf(string storageId)
    {
        IPortableDeviceValues values;
        try { _properties.GetValues(storageId, _storageKeys, out values); }
        catch (COMException) { return StorageAccess.ReadWrite; }
        try
        {
            var key = Wpd.StorageAccessCapability;
            return values.GetUnsignedIntegerValue(ref key, out uint access) == 0 && access is 1 or 2 ? (StorageAccess)access : StorageAccess.ReadWrite;
        }
        finally
        {
            Marshal.ReleaseComObject(values);
        }
    }

    private static string? Str(IPortableDeviceValues v, PropertyKey key)
    {
        if (v.GetStringValue(ref key, out var ptr) != 0 || ptr == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    private static ulong? ULong(IPortableDeviceValues v, PropertyKey key) => v.GetUnsignedLargeIntegerValue(ref key, out var value) == 0 ? value : null;

    private static bool? Bool(IPortableDeviceValues v, PropertyKey key) => v.GetBoolValue(ref key, out var value) == 0 ? value != 0 : null;

    private static Guid Guid(IPortableDeviceValues v, PropertyKey key) => v.GetGuidValue(ref key, out var value) == 0 ? value : System.Guid.Empty;

    private static DateTime Date(IPortableDeviceValues v, PropertyKey key)
    {
        if (v.GetValue(ref key, out var pv) != 0) return DateTime.MinValue;
        try
        {
            if (pv.VarType == Wpd.VT_DATE && pv.Date is > 0 and < 2958466)
                return DateTime.SpecifyKind(DateTime.FromOADate(pv.Date), DateTimeKind.Local).ToUniversalTime();
            if (pv.VarType == Wpd.VT_LPWSTR && Marshal.PtrToStringUni(pv.Pointer) is { } text &&
                DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var parsed))
                return parsed.ToUniversalTime();
            return DateTime.MinValue;
        }
        finally
        {
            Wpd.PropVariantClear(ref pv);
        }
    }

    /// <summary>Reads a file's content (sequential; the caller reopens to go back).</summary>
    public Stream OpenRead(string objectId) => Call("read the file", () =>
    {
        uint optimal = 0;
        var key = Wpd.ResourceDefault;
        _resources.GetStream(objectId, ref key, Wpd.STGM_READ, ref optimal, out var stream);
        return (Stream)new ComStream(this, stream, writable: false);
    });

    /// <summary>A new folder; returns its object ID.</summary>
    public string CreateFolder(string parentId, string name) => Call("create the folder", () =>
    {
        var values = ObjectValues(parentId, name, Wpd.ContentFolder, Wpd.FormatPropertiesOnly, size: null);
        try
        {
            IntPtr id = IntPtr.Zero;
            _content.CreateObjectWithPropertiesOnly(values, ref id);
            try { return Marshal.PtrToStringUni(id) ?? string.Empty; }
            finally { Marshal.FreeCoTaskMem(id); }
        }
        finally
        {
            Marshal.ReleaseComObject(values);
        }
    });

    /// <summary>
    /// A new file of exactly <paramref name="size"/> bytes (MTP needs the size first). Disposing the stream commits the file
    /// only when all of it was written; anything less (a cancel, an error part way) is reverted, so no truncated file is left.
    /// </summary>
    public Stream CreateFile(string parentId, string name, long size) => Call("create the file", () =>
    {
        var values = ObjectValues(parentId, name, Wpd.ContentGenericFile, Wpd.FormatUnspecified, (ulong)size);
        try
        {
            uint optimal = 0;
            _content.CreateObjectWithPropertiesAndData(values, out var stream, ref optimal, IntPtr.Zero);
            return (Stream)new ComStream(this, stream, writable: true, size);
        }
        finally
        {
            Marshal.ReleaseComObject(values);
        }
    });

    private static IPortableDeviceValues ObjectValues(string parentId, string name, Guid contentType, Guid format, ulong? size)
    {
        var values = Wpd.Create<IPortableDeviceValues>(Wpd.CLSID_PortableDeviceValues);
        var parent = Wpd.ParentId;
        values.SetStringValue(ref parent, parentId);
        var objectName = Wpd.Name;
        values.SetStringValue(ref objectName, name);
        var fileName = Wpd.OriginalFileName;
        values.SetStringValue(ref fileName, name);
        var type = Wpd.ContentType;
        values.SetGuidValue(ref type, ref contentType);
        var fmt = Wpd.Format;
        values.SetGuidValue(ref fmt, ref format);
        if (size is { } s)
        {
            var sizeKey = Wpd.Size;
            values.SetUnsignedLargeIntegerValue(ref sizeKey, s);
        }
        return values;
    }

    /// <summary>Deletes one object; a folder with its contents only when <paramref name="recursive"/>.</summary>
    public void Delete(string objectId, bool recursive) => Call("delete the item", () =>
    {
        var ids = Wpd.Create<IPortableDevicePropVariantCollection>(Wpd.CLSID_PortableDevicePropVariantCollection);
        var pv = new PropVariant { VarType = Wpd.VT_LPWSTR, Pointer = Marshal.StringToCoTaskMemUni(objectId) };
        try
        {
            ids.Add(ref pv);
            _content.Delete(recursive ? Wpd.DeleteWithRecursion : 0, ids, IntPtr.Zero);
            return true;
        }
        finally
        {
            Wpd.PropVariantClear(ref pv);
            Marshal.ReleaseComObject(ids);
        }
    });

    /// <summary>Renames one object; fails when the device does not allow it.</summary>
    public void Rename(string objectId, string name) => Call("rename the item", () =>
    {
        foreach (var key in new[] { Wpd.OriginalFileName, Wpd.Name })
        {
            var values = Wpd.Create<IPortableDeviceValues>(Wpd.CLSID_PortableDeviceValues);
            try
            {
                var k = key;
                values.SetStringValue(ref k, name);
                _properties.SetValues(objectId, values, out var results);
                try
                {
                    var rk = key;
                    if (results.GetErrorValue(ref rk, out int hr) != 0 || hr >= 0) return true;
                }
                finally
                {
                    Marshal.ReleaseComObject(results);
                }
            }
            catch (COMException) { }
            finally
            {
                Marshal.ReleaseComObject(values);
            }
        }
        throw new IOException("The device does not allow renaming this item.");
    });

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _device.Close(); } catch (COMException) { }
            foreach (var o in new object[] { _storageKeys, _keys, _resources, _properties, _content, _device })
            {
                try { Marshal.ReleaseComObject(o); } catch (ArgumentException) { }
            }
        }
    }

    /// <summary>An IStream from the device as a .NET stream; a write stream commits when disposed, if it is complete.</summary>
    private sealed class ComStream(WpdSession owner, IStream stream, bool writable, long expected = -1) : Stream, ICreatedObject
    {
        public string? CreatedObjectId { get; private set; }

        private bool _closed;
        private long _position;

        public override bool CanRead => !writable;
        public override bool CanSeek => false;
        public override bool CanWrite => writable;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override unsafe int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            var chunk = offset == 0 ? buffer : new byte[count];
            int read = 0;
            lock (owner._lock)
            {
                try { stream.Read(chunk, count, (IntPtr)(&read)); }
                catch (COMException ex) { throw owner.Translate(ex, "read the file"); }
            }
            if (offset != 0) Buffer.BlockCopy(chunk, 0, buffer, offset, read);
            _position += read;
            return read;
        }

        public override unsafe void Write(byte[] buffer, int offset, int count)
        {
            var chunk = offset == 0 ? buffer : buffer[offset..(offset + count)];
            int written = 0;
            lock (owner._lock)
            {
                try { stream.Write(chunk, count, (IntPtr)(&written)); }
                catch (COMException ex) { throw owner.Translate(ex, "write the file"); }
            }
            if (written != count) throw new IOException("The device accepted only part of the data.");
            _position += count;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_closed)
            {
                _closed = true;
                try
                {
                    if (writable)
                    {
                        lock (owner._lock)
                        {
                            if (_position == expected)
                            {
                                try { stream.Commit(0); }
                                catch (COMException ex) { throw owner.Translate(ex, "finish writing the file"); }
                                // The new object's ID, so its size can be checked without listing the whole folder.
                                try
                                {
                                    ((IPortableDeviceDataStream)stream).GetObjectID(out string id);
                                    CreatedObjectId = id;
                                }
                                catch (Exception ex) when (ex is InvalidCastException or COMException) { }
                            }
                            else
                            {
                                try { stream.Revert(); }
                                catch (COMException) { } // the caller removes whatever the device kept anyway
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(stream);
                }
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
