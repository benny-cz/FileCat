using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Network;
using FileCat.Platform.Windows.Elevation;
using FileCat.Recovery;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Recovery;

/// <summary>
/// The read session between FileCat and the administrator helper (ADR-08): FileCat asks for byte ranges of one device,
/// the helper answers with bytes or an error code. There is no request that writes, and none that names another device.
/// </summary>
public static class RawReadProtocol
{
    public const byte Info = 1, Read = 2, Close = 3;
    public const int MaxRead = 4 * 1024 * 1024;
    public const int RequestSize = 13; // op, offset (8), length (4)
    private const int ErrorInvalidParameter = 87;

    /// <summary>
    /// The helper's side: bounded, sector-aligned reads of an open device for one client, until it closes or goes away.
    /// Returns why the session ended.
    /// </summary>
    public static string Serve(Stream pipe, SafeFileHandle device, long length, int sectorSize)
    {
        var reader = new AlignedDeviceReader(device, length, sectorSize);
        var request = new byte[RequestSize];
        var header = new byte[16];
        while (true)
        {
            if (!ReadExactly(pipe, request)) return "FileCat closed the session.";
            byte op = request[0];
            long offset = BinaryPrimitives.ReadInt64LittleEndian(request.AsSpan(1));
            int count = BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(9));
            switch (op)
            {
                case Info:
                    BinaryPrimitives.WriteInt32LittleEndian(header, 0);
                    BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(4), length);
                    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), reader.SectorSize);
                    pipe.Write(header, 0, 16);
                    break;
                case Read:
                    if (offset < 0 || count is < 0 or > MaxRead)
                    {
                        Reply(pipe, header, ErrorInvalidParameter, []);
                        break;
                    }
                    try
                    {
                        Reply(pipe, header, 0, reader.Read(offset, count));
                    }
                    catch (IOException ex)
                    {
                        Reply(pipe, header, ex.HResult & 0xFFFF, []);
                    }
                    break;
                case Close:
                    return "FileCat closed the session.";
                default:
                    return "FileCat sent a request the helper does not know; it stopped.";
            }
            pipe.Flush();
        }
    }

    private static void Reply(Stream pipe, byte[] header, int status, ReadOnlySpan<byte> data)
    {
        BinaryPrimitives.WriteInt32LittleEndian(header, status);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), data.Length);
        pipe.Write(header, 0, 8);
        pipe.Write(data);
    }

    internal static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = stream.Read(buffer[done..]);
            if (n <= 0) return false;
            done += n;
        }
        return true;
    }
}

/// <summary>Reads of an open volume or disk: raw devices read whole sectors only, so each read is widened to them.</summary>
internal sealed class AlignedDeviceReader
{
    private readonly SafeFileHandle _device;
    private readonly byte[] _buffer;

    public AlignedDeviceReader(SafeFileHandle device, long length, int sectorSize)
    {
        if (sectorSize is < 512 or > 65536 || (sectorSize & (sectorSize - 1)) != 0) sectorSize = 512;
        _device = device;
        Length = length;
        SectorSize = sectorSize;
        _buffer = new byte[RawReadProtocol.MaxRead + 2 * sectorSize];
    }

    public long Length { get; }
    public int SectorSize { get; }

    /// <summary>Up to <paramref name="count"/> (at most <see cref="RawReadProtocol.MaxRead"/>) bytes at <paramref name="offset"/>; valid until the next read.</summary>
    public ReadOnlySpan<byte> Read(long offset, int count)
    {
        long end = Math.Min(Length, offset + Math.Min(count, RawReadProtocol.MaxRead));
        if (offset < 0 || offset >= end) return [];
        long start = offset / SectorSize * SectorSize;
        long stop = Math.Min((end + SectorSize - 1) / SectorSize * SectorSize, (Length + SectorSize - 1) / SectorSize * SectorSize);
        int span = (int)(stop - start);
        int got = 0;
        while (got < span)
        {
            int n = RandomAccess.Read(_device, _buffer.AsSpan(got, span - got), start + got);
            if (n <= 0) break;
            got += n;
        }
        int skip = (int)(offset - start);
        int available = (int)Math.Max(0, Math.Min(got - skip, end - offset));
        return _buffer.AsSpan(skip, available);
    }
}

/// <summary>
/// A drive FileCat reads itself because it already runs as administrator: the helper would ask for rights FileCat
/// has. It opens the device for reading only (other programs keep reading and writing it), and nothing is ever written.
/// </summary>
public sealed class DirectDeviceSource : IBlockSource
{
    private readonly SafeFileHandle _handle;
    private readonly AlignedDeviceReader _reader;
    private readonly object _lock = new();

    private DirectDeviceSource(SafeFileHandle handle, string description)
    {
        _handle = handle;
        Description = description;
        long length = DeviceTopology.Length(handle);
        if (length <= 0) throw new IOException($"The size of {description} could not be read.");
        _reader = new AlignedDeviceReader(handle, length, DeviceTopology.SectorSize(handle));
    }

    /// <summary>Opens a volume device (\\?\Volume{…}) or a disk for reading; access denied without administrator rights.</summary>
    public static DirectDeviceSource Open(string device, string description)
    {
        var handle = DeviceReadHost.OpenForReading(device);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error == 5 /* ERROR_ACCESS_DENIED */) throw new UnauthorizedAccessException($"Reading {description} needs administrator rights.");
            throw new IOException($"{description} could not be opened for reading: {new Win32Exception(error).Message}") { HResult = error };
        }
        try
        {
            return new DirectDeviceSource(handle, description);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public string Description { get; }
    public long Length => _reader.Length;
    public int SectorSize => _reader.SectorSize;

    public int Read(long offset, Span<byte> buffer)
    {
        int done = 0;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_handle.IsClosed, this);
            while (done < buffer.Length && offset + done < Length)
            {
                ReadOnlySpan<byte> data;
                try
                {
                    data = _reader.Read(offset + done, buffer.Length - done);
                }
                catch (IOException ex)
                {
                    throw new IOException($"The drive could not be read at byte {offset + done:N0}: {ex.Message}", ex) { HResult = ex.HResult };
                }
                if (data.IsEmpty) break;
                data.CopyTo(buffer[done..]);
                done += data.Length;
            }
        }
        return done;
    }

    public void Dispose()
    {
        lock (_lock) _handle.Dispose();
    }
}

/// <summary>A device read through a session: FileCat parses, the elevated helper only reads (ADR-08).</summary>
public class PipeDeviceSource : IBlockSource
{
    private readonly Stream _pipe;
    private readonly object _lock = new();
    private readonly byte[] _request = new byte[RawReadProtocol.RequestSize];
    private bool _closed;

    public PipeDeviceSource(Stream pipe, string description)
    {
        _pipe = pipe;
        Description = description;
        _request[0] = RawReadProtocol.Info;
        _pipe.Write(_request);
        _pipe.Flush();
        var info = new byte[16];
        if (!RawReadProtocol.ReadExactly(_pipe, info) || BinaryPrimitives.ReadInt32LittleEndian(info) != 0)
            throw new IOException("The administrator helper did not describe the drive.");
        Length = BinaryPrimitives.ReadInt64LittleEndian(info.AsSpan(4));
        SectorSize = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(12));
    }

    public string Description { get; }
    public long Length { get; }
    public int SectorSize { get; }

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= Length || buffer.IsEmpty) return 0;
        int done = 0;
        Span<byte> header = stackalloc byte[8];
        lock (_lock)
        {
            if (_closed) throw new IOException("The drive is no longer being read: the administrator helper has stopped.");
            while (done < buffer.Length && offset + done < Length)
            {
                int want = Math.Min(buffer.Length - done, RawReadProtocol.MaxRead);
                _request[0] = RawReadProtocol.Read;
                BinaryPrimitives.WriteInt64LittleEndian(_request.AsSpan(1), offset + done);
                BinaryPrimitives.WriteInt32LittleEndian(_request.AsSpan(9), want);
                try
                {
                    _pipe.Write(_request);
                    _pipe.Flush();
                    if (!RawReadProtocol.ReadExactly(_pipe, header)) throw new EndOfStreamException();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    _closed = true;
                    throw new IOException("The drive is no longer being read: the administrator helper has stopped.", ex);
                }
                int status = BinaryPrimitives.ReadInt32LittleEndian(header);
                int count = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
                if (count < 0 || count > want) throw new IOException("The administrator helper answered out of bounds.");
                if (!RawReadProtocol.ReadExactly(_pipe, buffer.Slice(done, count))) throw new IOException("The administrator helper stopped mid-answer.");
                if (status != 0) throw new IOException($"The drive could not be read at byte {offset + done:N0}: {new Win32Exception(status).Message}") { HResult = status };
                if (count == 0) break;
                done += count;
            }
        }
        return done;
    }

    public virtual void Dispose()
    {
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            try
            {
                _request[0] = RawReadProtocol.Close;
                _pipe.Write(_request);
                _pipe.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            _pipe.Dispose();
        }
    }
}

/// <summary>
/// Starts the administrator helper for one device through UAC and reads it over the helper's private pipe. The plan the
/// helper displays names the device and says that nothing is written; the helper exits when this source is disposed.
/// </summary>
public sealed class BrokeredDeviceSource : PipeDeviceSource
{
    private readonly ElevatedProcess _process;
    private readonly ElevationExchange _exchange;

    private BrokeredDeviceSource(Stream pipe, string description, ElevatedProcess process, ElevationExchange exchange) : base(pipe, description)
    {
        _process = process;
        _exchange = exchange;
    }

    /// <summary>
    /// Asks for approval and connects. Throws <see cref="OperationCanceledException"/> when the user declines,
    /// <see cref="NotSupportedException"/> without the installed helper, and <see cref="IOException"/> when the helper refuses.
    /// </summary>
    public static BrokeredDeviceSource Open(string device, string description, bool portable, string exchangeRoot, CancellationToken ct)
    {
        string broker = ElevationBroker.Locate(portable, out var reason) ?? throw new NotSupportedException(reason?.Replace("operations as administrator", "drives directly").Replace("administrator retry", "reading drives directly"));
        string nonce = ElevationPlanCodec.NewNonce();
        using var identity = WindowsIdentity.GetCurrent();
        var plan = new ElevationPlan
        {
            Nonce = nonce,
            CreatedUtc = DateTime.UtcNow,
            UserSid = identity.User!.Value,
            UserName = identity.Name,
            RequesterProcessId = Environment.ProcessId,
            Title = "Read " + description + " to find deleted files",
            Steps = [new ElevatedStep(ElevatedVerb.ReadDevice) { Path = device, Name = PipeName(nonce) }],
        };
        var exchange = ElevationExchange.Create(exchangeRoot, plan);
        ElevatedProcess process;
        try
        {
            process = ElevationBroker.Launch(broker, exchange.VolumePlanPath, exchange.Hash, WindowsFileOperations.OwnerWindow);
        }
        catch
        {
            exchange.Dispose();
            throw;
        }
        var pipe = new NamedPipeClientStream(".", PipeName(nonce), PipeDirection.InOut, PipeOptions.None);
        try
        {
            // The helper opens its pipe only after the user approves in its window.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (process.WaitForExit(0))
                {
                    var result = exchange.ReadResult();
                    if (result is { Consented: false, Refused: ElevationMessages.Declined } || result is null) throw new OperationCanceledException("Reading the drive was declined; nothing was read.");
                    throw new IOException("The administrator helper refused to read the drive: " + (result.Refused ?? "it ended early."));
                }
                try
                {
                    pipe.Connect(250);
                    break;
                }
                catch (TimeoutException) { }
            }
            return new BrokeredDeviceSource(pipe, description, process, exchange);
        }
        catch
        {
            pipe.Dispose();
            process.Dispose();
            exchange.Dispose();
            throw;
        }
    }

    public static string PipeName(string nonce) => "FileCat-read-" + nonce;

    public override void Dispose()
    {
        base.Dispose();
        _process.WaitForExit(3000);
        _process.Dispose();
        _exchange.Dispose();
    }
}

/// <summary>The helper's side of a read session: it opens the device and its pipe only after the user approved.</summary>
public static partial class DeviceReadHost
{
    /// <summary>Serves one device to the requesting FileCat; returns why it ended (the helper's report says it).</summary>
    public static string Run(string device, string pipeName, string userSid, int requesterProcessId)
    {
        using var handle = OpenForReading(device);
        if (handle.IsInvalid) return "The drive could not be opened: " + new Win32Exception(Marshal.GetLastPInvokeError()).Message;
        long length = DeviceTopology.Length(handle);
        int sector = DeviceTopology.SectorSize(handle);
        if (length <= 0) return "The drive's size could not be read.";
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(userSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        using (var self = WindowsIdentity.GetCurrent()) security.AddAccessRule(new PipeAccessRule(self.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        // One instance only: if the random name already exists, someone else holds it and nothing is served.
        using var pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        using (var wait = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
        {
            try { pipe.WaitForConnectionAsync(wait.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { return "FileCat did not connect."; }
        }
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint client) || client != requesterProcessId)
            return "A program other than the FileCat that asked connected; nothing was read.";
        return RawReadProtocol.Serve(pipe, handle, length, sector);
    }

    /// <summary>
    /// A device handle that can only read: GENERIC_READ, with read and write sharing so Windows and other programs go on
    /// using the drive. Invalid (with the error set) when it cannot be opened.
    /// </summary>
    internal static SafeFileHandle OpenForReading(string device) =>
        CreateFile(device, 0x80000000 /* GENERIC_READ */, 3 /* read, write sharing */, 0, 3 /* OPEN_EXISTING */, 0, 0);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}

/// <summary>
/// A physical disk as recovery offers it (D-46): its number, size, the name its maker gave it, how it is attached, and
/// the drives (letters) on it.
/// </summary>
public sealed record PhysicalDisk(int Number, long Length, string? Model, string Bus, bool Removable, IReadOnlyList<string> Drives)
{
    public string Device => @"\\.\PhysicalDrive" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Which physical disks volumes and folders are on, so recovery never writes to the disk it reads (plan §17.2).</summary>
public static unsafe partial class DeviceTopology
{
    /// <summary>
    /// The physical disks of this computer that hold a medium. Asking needs no rights: each disk is opened for queries
    /// only, never for reading.
    /// </summary>
    public static IReadOnlyList<PhysicalDisk> Disks()
    {
        var letters = new Dictionary<int, List<string>>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || VolumeDevice(drive.Name) is not { } volume || DisksOf(volume) is not { } disks) continue;
            foreach (int disk in disks)
            {
                if (!letters.TryGetValue(disk, out var list)) letters[disk] = list = [];
                list.Add(drive.Name.TrimEnd('\\'));
            }
        }
        var result = new List<PhysicalDisk>();
        for (int n = 0; n < 64; n++)
        {
            using var handle = CreateFile(@"\\.\PhysicalDrive" + n.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, 3, 0, 3, 0, 0);
            if (handle.IsInvalid) continue;
            long length = GeometryLength(handle);
            if (length <= 0) continue; // no medium (an empty card reader)
            var (model, bus, removable) = Describe(handle);
            result.Add(new PhysicalDisk(n, length, model, bus, removable, letters.GetValueOrDefault(n) ?? []));
        }
        return result;
    }

    /// <summary>A disk's size from its geometry (a query any handle may make, unlike the length a reading handle asks for).</summary>
    private static long GeometryLength(SafeFileHandle disk)
    {
        var geometry = stackalloc byte[256];
        uint returned;
        if (!DeviceIoControl(disk, 0x000700A0 /* IOCTL_DISK_GET_DRIVE_GEOMETRY_EX */, null, 0, geometry, 256, &returned, 0) || returned < 32) return -1;
        return BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(geometry + 24, 8)); // DISK_GEOMETRY_EX.DiskSize
    }

    /// <summary>What a disk says it is (STORAGE_DEVICE_DESCRIPTOR): its maker's names for it, its bus, and whether its medium is removable.</summary>
    private static (string? Model, string Bus, bool Removable) Describe(SafeFileHandle disk)
    {
        var query = stackalloc byte[12]; // STORAGE_PROPERTY_QUERY: StorageDeviceProperty, PropertyStandardQuery
        new Span<byte>(query, 12).Clear();
        var output = new byte[1024];
        uint returned;
        fixed (byte* o = output)
            if (!DeviceIoControl(disk, 0x002D1400 /* IOCTL_STORAGE_QUERY_PROPERTY */, query, 12, o, (uint)output.Length, &returned, 0) || returned < 36)
                return (null, "", false);
        int size = (int)Math.Min(returned, (uint)output.Length);
        string? Text(int at)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(at));
            if (offset <= 0 || offset >= size) return null;
            int end = Array.IndexOf(output, (byte)0, offset, size - offset);
            string text = System.Text.Encoding.ASCII.GetString(output, offset, (end < 0 ? size : end) - offset).Trim();
            return text.Length > 0 ? string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : null;
        }
        string? model = string.Join(' ', new[] { Text(12), Text(16) }.Where(s => s is not null));
        string bus = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(28)) switch
        {
            1 => "SCSI", 3 => "ATA", 4 => "FireWire", 7 => "USB", 8 => "RAID", 9 => "iSCSI", 0xA => "SAS", 0xB => "SATA",
            0xC => "SD card", 0xD => "MMC", 0xE or 0xF => "virtual", 0x10 => "Storage Spaces", 0x11 => "NVMe", 0x13 => "UFS",
            _ => "",
        };
        return (model.Length > 0 ? model : null, bus, output[10] != 0);
    }

    /// <summary>The volume device (\\?\Volume{…}, no trailing backslash) of a drive root such as "E:\"; null when there is none.</summary>
    public static string? VolumeDevice(string root)
    {
        var buffer = new char[64];
        fixed (char* chars = buffer)
            if (!GetVolumeNameForVolumeMountPoint(root.EndsWith('\\') ? root : root + "\\", chars, buffer.Length)) return null;
        return new string(buffer).TrimEnd('\0').TrimEnd('\\');
    }

    /// <summary>The physical disk numbers a volume device or a local folder lies on; null when unknown (or not local).</summary>
    public static IReadOnlyList<int>? DisksOf(string pathOrDevice)
    {
        if (pathOrDevice.StartsWith(@"\\.\PhysicalDrive", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(pathOrDevice.AsSpan(17), out int n) ? [n] : null;
        string? volume = pathOrDevice.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ? pathOrDevice.TrimEnd('\\') : Locate(pathOrDevice)?.Volume;
        if (volume is null) return null;
        using var handle = CreateFile(volume, 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) return null;
        var output = new byte[8 + 24 * 32];
        uint returned;
        fixed (byte* o = output)
            if (!DeviceIoControl(handle, 0x00560000 /* IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS */, null, 0, o, (uint)output.Length, &returned, 0)) return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(output);
        var disks = new List<int>();
        for (int i = 0; i < count && 8 + i * 24 + 4 <= output.Length; i++)
        {
            int disk = BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(8 + i * 24));
            if (!disks.Contains(disk)) disks.Add(disk);
        }
        return disks;
    }

    /// <summary>
    /// Whether writing into <paramref name="folder"/> would write to a disk <paramref name="device"/> lies on: true, false,
    /// or null when that cannot be told, which never counts as another disk (release plan V09). Another computer's share
    /// is on no disk here; a share this computer serves itself is on one of its own, which one is not known.
    /// </summary>
    public static bool? SharesDisk(string device, string folder)
    {
        if (Locate(folder) is not { } place) return null;
        if (place.Server is { } server) return ThisComputer.Is(server, TimeSpan.FromSeconds(2)) ? null : false;
        var source = DisksOf(device);
        if (source is null || DisksOf(place.Volume!) is not { } volume || WrittenDisks(volume, 0) is not { } target) return null;
        return source.Intersect(target).Any();
    }

    /// <summary>
    /// The disks writing to <paramref name="disks"/> writes to. A disk Windows makes from a file (VHD, VHDX) is itself and
    /// the disks its file lies on (a VHD in a VHD too); read, it stays a disk of its own, since writing next to its file
    /// never changes what it holds. Null when that cannot be told: a storage space (made from other disks), a VHD whose
    /// file the system does not name.
    /// </summary>
    private static List<int>? WrittenDisks(IReadOnlyList<int> disks, int depth)
    {
        var all = new List<int>();
        foreach (int disk in disks)
        {
            all.Add(disk);
            string? bus = BusOf(disk);
            if (bus is null or "Storage Spaces") return null;
            if (bus != "virtual") continue;
            if (depth > 4 || HostVolumesOf(disk) is not { Count: > 0 } hosts) return null;
            foreach (var host in hosts)
            {
                if (DisksOf(host) is not { } under || WrittenDisks(under, depth + 1) is not { } deeper) return null;
                all.AddRange(deeper);
            }
        }
        return all.Distinct().ToList();
    }

    /// <summary>The bus a physical disk names (virtual for a VHD, Storage Spaces for a space), or null when it cannot be opened.</summary>
    private static string? BusOf(int disk)
    {
        using var handle = CreateFile(@"\\.\PhysicalDrive" + disk.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, 3, 0, 3, 0, 0);
        return handle.IsInvalid ? null : Describe(handle).Bus;
    }

    /// <summary>
    /// The volumes (\\?\Volume{…}) holding the file a VHD or VHDX disk is made from, as the virtual disk service names
    /// them (GetStorageDependencyInformation); null when it names none.
    /// </summary>
    internal static IReadOnlyList<string>? HostVolumesOf(int disk)
    {
        using var handle = CreateFile(@"\\.\PhysicalDrive" + disk.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) return null;
        uint size = 4096;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            nint buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                new Span<byte>((void*)buffer, (int)size).Clear();
                Marshal.WriteInt32(buffer, 2); // STORAGE_DEPENDENCY_INFO_VERSION_2
                uint used;
                uint error = GetStorageDependencyInformation(handle, 1 | 2 /* HOST_VOLUMES | DISK_HANDLE */, size, buffer, &used);
                if (error == 122 /* ERROR_INSUFFICIENT_BUFFER */)
                {
                    size = Math.Max(used, size * 2);
                    continue;
                }
                if (error != 0) return null;
                int count = Marshal.ReadInt32(buffer, 4);
                var volumes = new List<string>();
                for (int i = 0; i < count && 8 + (i + 1) * 64 <= size; i++)
                {
                    // STORAGE_DEPENDENCY_INFO_TYPE_2: flags, provider flags, storage type (20), ancestor level, then four names.
                    nint host = Marshal.ReadIntPtr(buffer, 8 + i * 64 + 40);
                    if (host != 0 && Marshal.PtrToStringUni(host) is { Length: > 0 } name && name.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                        volumes.Add(name.TrimEnd('\\'));
                }
                return volumes;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return null;
    }

    /// <summary>
    /// Where writing into a folder goes: the volume (\\?\Volume{…}) its real path lies on, every link, junction and
    /// mapped drive on the way resolved, or the server of the share it reaches. Null when neither can be told.
    /// </summary>
    internal static (string? Volume, string? Server)? Locate(string folder)
    {
        string full = Path.GetFullPath(folder);
        string? existing = full;
        while (existing is not null && !Directory.Exists(existing)) existing = Path.GetDirectoryName(existing);
        string? share = PathUtil.GetUncServer(full)?.TrimStart('\\');
        if (existing is not null)
        {
            string native = existing.Length < 248 || existing.StartsWith(@"\\?\", StringComparison.Ordinal) ? existing
                : PathUtil.IsUncPath(existing) ? @"\\?\UNC\" + existing[2..] : @"\\?\" + existing;
            using var handle = CreateFile(native, 0x80 /* FILE_READ_ATTRIBUTES */, 7, 0, 3, 0x02000000 /* FILE_FLAG_BACKUP_SEMANTICS */, 0);
            if (!handle.IsInvalid)
            {
                if (ElevationPaths.FinalPath(handle, 0x1 /* VOLUME_NAME_GUID */) is { } guid && guid.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) &&
                    guid.IndexOf('}') is var close and > 0)
                    return (guid[..(close + 1)], null);
                if (ElevationPaths.FinalPath(handle, 0 /* VOLUME_NAME_DOS */) is { } dos && dos.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    share = dos[8..].Split('\\')[0];
                else return null;
            }
        }
        // A WebDAV share's server is written with its port or "@SSL" after it.
        return share is { Length: > 0 } ? (null, share.Split('@')[0]) : null;
    }


    internal static long Length(SafeFileHandle device)
    {
        long length;
        uint returned;
        return DeviceIoControl(device, 0x0007405C /* IOCTL_DISK_GET_LENGTH_INFO */, null, 0, &length, 8, &returned, 0) ? length : -1;
    }

    internal static int SectorSize(SafeFileHandle device)
    {
        var geometry = stackalloc byte[256];
        uint returned;
        if (!DeviceIoControl(device, 0x000700A0 /* IOCTL_DISK_GET_DRIVE_GEOMETRY_EX */, null, 0, geometry, 256, &returned, 0)) return 512;
        int size = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(geometry + 20, 4)); // DISK_GEOMETRY.BytesPerSector
        return size is >= 512 and <= 65536 ? size : 512;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, char* volumeName, int length);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, uint* returned, nint overlapped);

    [LibraryImport("virtdisk.dll")]
    private static partial uint GetStorageDependencyInformation(SafeFileHandle handle, uint flags, uint size, nint info, uint* used);
}
