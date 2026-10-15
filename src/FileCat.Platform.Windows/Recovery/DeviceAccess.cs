using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
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
    public const byte Info = 1, Read = 2, Close = 3, SourceDisks = 4, DeviceIdentity = 5;
    public const int MaxSourceDisks = 32;
    public const int MaxRead = 4 * 1024 * 1024;
    public const int RequestSize = 13; // op, offset (8), length (4)
    private const int ErrorInvalidParameter = 87;

    /// <summary>
    /// The helper's side: bounded, sector-aligned reads of an open device for one client, until it closes or goes away.
    /// Returns why the session ended.
    /// </summary>
    public static string Serve(Stream pipe, SafeFileHandle device, long length, int sectorSize, bool wholeDisk = false)
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
                case SourceDisks:
                    if (offset != 0 || count != 0)
                        Reply(pipe, header, ErrorInvalidParameter, []);
                    else
                    {
                        var disks = DeviceTopology.DisksOf(device, wholeDisk);
                        var data = disks is null ? [] : new byte[disks.Count * 4];
                        for (int i = 0; disks is not null && i < disks.Count; i++)
                            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), disks[i]);
                        // Same framing as a read, but count is the number of disk numbers, not bytes.
                        BinaryPrimitives.WriteInt32LittleEndian(header, disks is null ? 50 /* ERROR_NOT_SUPPORTED */ : 0);
                        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), disks?.Count ?? 0);
                        pipe.Write(header, 0, 8);
                        pipe.Write(data);
                    }
                    break;
                case DeviceIdentity:
                    if (offset != 0 || count != 0 || !wholeDisk || DeviceTopology.DeviceIdentity(device) is not { } identity)
                        Reply(pipe, header, ErrorInvalidParameter, []);
                    else
                        Reply(pipe, header, 0, identity.ToByteArray());
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
/// Keeps a selected whole disk or single-disk volume bound to its present PnP disk interface.
/// A volume path can survive a cloned disk replacement, so its GUID alone is not a source identity.
/// </summary>
public sealed class SelectedDiskInterfaceSource : IBlockSource, IDevicePathGuard, IDeviceSelectionIdentity, IDeviceDestinationGuard
{
    private readonly string _path;
    private readonly IBlockSource _source;
    private readonly string _diskInterfacePath;
    private readonly string? _diskIdentity;
    private readonly long _diskLength;
    private readonly int _diskNumber;
    private readonly bool _volumePath;

    private SelectedDiskInterfaceSource(string path, IBlockSource source, PhysicalDisk disk, bool volumePath)
    {
        _path = path;
        _source = source;
        _diskInterfacePath = disk.InterfacePath!;
        _diskIdentity = disk.Identity;
        _diskLength = disk.Length;
        _diskNumber = disk.Number;
        _volumePath = volumePath;
    }

    public static IBlockSource Bind(string path, IBlockSource source)
    {
        try
        {
            bool volumePath = path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase);
            if (!volumePath && !DeviceTopology.IsDiskInterfacePath(path))
                throw new IOException("The selected disk interface is no longer available. Select it again.");
            // Query the already-open source. A PnP link can be reused for another disk with
            // the same storage GUID and size, so the selected number must be retained too.
            var heldDisks = source switch
            {
                DirectDeviceSource direct => direct.QuerySourceDisks(),
                PipeDeviceSource pipe => pipe.QuerySourceDisks(),
                _ => null,
            };
            if (heldDisks is not { Count: 1 })
                throw new IOException("The selected source's backing disk could not be verified. Select it again.");
            if (volumePath)
            {
                // A volume GUID can survive disk replacement. Its held disk and current path
                // must agree before we bind to the selected PnP disk interface.
                var pathDisks = DeviceTopology.DisksOf(path);
                if (pathDisks is not { Count: 1 } || heldDisks[0] != pathDisks[0])
                    throw new IOException("The selected volume's backing disk could not be verified. Select it again.");
            }
            var disk = DeviceTopology.Disks().SingleOrDefault(d => d.Number == heldDisks[0]);
            if (disk?.InterfacePath is null || disk.Identity is null || disk.Length <= 0 ||
                (!volumePath && (!string.Equals(disk.InterfacePath, path, StringComparison.OrdinalIgnoreCase) ||
                                 !string.Equals((source as IDeviceSelectionIdentity)?.SelectionIdentity, disk.Identity, StringComparison.OrdinalIgnoreCase))))
                throw new IOException("The selected disk interface could not be verified. Select it again.");
            var bound = new SelectedDiskInterfaceSource(path, source, disk, volumePath);
            if (!bound.IsCurrentDevicePath(path))
                throw new IOException("The selected disk interface changed or was removed while opening. Select it again.");
            return bound;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public string Description => _source.Description;
    public long Length => _source.Length;
    // A volume GUID can survive replacement by a byte-cloned disk with the same storage GUID.
    // Keep the selected PnP interface, disk number and length. A different disk can reuse
    // the interface and storage GUID while the confirmation or administrator dialog is open.
    public string? SelectionIdentity => _volumePath
        ? VolumeSelectionIdentity(_diskInterfacePath, _diskIdentity, _diskLength, _diskNumber)
        : WholeDiskSelectionIdentity(_diskInterfacePath, _diskIdentity, _diskLength, _diskNumber);

    public static string? WholeDiskSelectionIdentity(PhysicalDisk? disk) =>
        disk is null ? null : WholeDiskSelectionIdentity(disk.InterfacePath, disk.Identity, disk.Length, disk.Number);

    private static string? WholeDiskSelectionIdentity(string? interfacePath, string? identity, long diskLength, int diskNumber) =>
        string.IsNullOrEmpty(interfacePath) || string.IsNullOrEmpty(identity) || diskLength <= 0 || diskNumber < 0
            ? null : $"disk|{interfacePath}|{identity}|{diskLength.ToString(CultureInfo.InvariantCulture)}|{diskNumber.ToString(CultureInfo.InvariantCulture)}";

    public static string? VolumeSelectionIdentity(PhysicalDisk? disk) =>
        disk is null ? null : VolumeSelectionIdentity(disk.InterfacePath, disk.Identity, disk.Length, disk.Number);

    private static string? VolumeSelectionIdentity(string? interfacePath, string? identity, long diskLength, int diskNumber) =>
        string.IsNullOrEmpty(interfacePath) || string.IsNullOrEmpty(identity) || diskLength <= 0 || diskNumber < 0
            ? null : $"volume|{interfacePath}|{identity}|{diskLength.ToString(CultureInfo.InvariantCulture)}|{diskNumber.ToString(CultureInfo.InvariantCulture)}";
    public int Read(long offset, Span<byte> buffer) => _source.Read(offset, buffer);
    public bool? SharesDestinationDisk(string destinationDirectory) =>
        (_source as IDeviceDestinationGuard)?.SharesDestinationDisk(destinationDirectory);

    public bool IsCurrentDevicePath(string path)
    {
        if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(_diskIdentity)) return false;
        try
        {
            if (_volumePath)
            {
                var currentDisks = DeviceTopology.DisksOf(path);
                if (currentDisks is not { Count: 1 } || currentDisks[0] != _diskNumber) return false;
            }
            var matches = DeviceTopology.Disks().Where(d =>
                string.Equals(d.InterfacePath, _diskInterfacePath, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            return matches.Length == 1 && matches[0].Length == _diskLength &&
                matches[0].Number == _diskNumber &&
                string.Equals(matches[0].Identity, _diskIdentity, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    public void Dispose() => _source.Dispose();
}

/// <summary>
/// A drive FileCat reads itself because it already runs as administrator: the helper would ask for rights FileCat
/// has. It opens the device for reading only (other programs keep reading and writing it), and nothing is ever written.
/// </summary>
public sealed class DirectDeviceSource : IBlockSource, IDeviceDestinationGuard, IDeviceSelectionIdentity
{
    private readonly SafeFileHandle _handle;
    private readonly AlignedDeviceReader _reader;
    private readonly object _lock = new();

    private readonly bool _wholeDisk;

    private DirectDeviceSource(SafeFileHandle handle, string description, bool wholeDisk)
    {
        _handle = handle;
        _wholeDisk = wholeDisk;
        SelectionIdentity = wholeDisk ? DeviceTopology.DeviceIdentity(handle)?.ToString("D") : null;
        Description = description;
        long length = DeviceTopology.Length(handle);
        if (length <= 0) throw new IOException($"The size of {description} could not be read.");
        int sectorSize = DeviceTopology.SectorSize(handle);
        _reader = new AlignedDeviceReader(handle, DeviceTopology.ServedLength(handle, length, sectorSize), sectorSize);
    }

    // A held, file-backed handle lets the native read contract be checked when the
    // source stops serving bytes after its length was established. Production opens
    // continue through Open and its device topology/served-length checks.
    internal DirectDeviceSource(SafeFileHandle handle, string description, long declaredLength, int sectorSize)
    {
        _handle = handle;
        _wholeDisk = false;
        Description = description;
        _reader = new AlignedDeviceReader(handle, declaredLength, sectorSize);
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
            return new DirectDeviceSource(handle, description, DeviceTopology.IsWholeDiskPath(device));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public string Description { get; }
    public string? SelectionIdentity { get; }
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
                // The held device was described as reaching farther. Returning zero or a
                // partial count here would make unreadable bytes look like an ordinary end.
                if (data.IsEmpty)
                    throw new IOException($"The drive stopped serving bytes at {offset + done:N0} before its reported end of {Length:N0}.");
                data.CopyTo(buffer[done..]);
                done += data.Length;
            }
        }
        return done;
    }

    public bool? SharesDestinationDisk(string destinationDirectory)
    {
        return DeviceTopology.SharesDisk(QuerySourceDisks(), destinationDirectory);
    }

    internal IReadOnlyList<int>? QuerySourceDisks()
    {
        lock (_lock) return DeviceTopology.DisksOf(_handle, _wholeDisk);
    }

    public void Dispose()
    {
        lock (_lock) _handle.Dispose();
    }
}

/// <summary>A device read through a session: FileCat parses, the elevated helper only reads (ADR-08).</summary>
public class PipeDeviceSource : IBlockSource, IDeviceDestinationGuard, IDeviceSelectionIdentity
{
    /// <summary>One pebibyte: past every drive FileCat reads, and far short of a length that breaks a scan's arithmetic.</summary>
    private const long MaxDeviceLength = 1L << 50;

    private readonly Stream _pipe;
    private readonly object _lock = new();
    private readonly byte[] _request = new byte[RawReadProtocol.RequestSize];
    private bool _closed;
    private bool _disposed;

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
        long length = BinaryPrimitives.ReadInt64LittleEndian(info.AsSpan(4));
        int sectorSize = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(12));
        // Nothing else can check this description, and it becomes a scan's bounds, so an impossible one ends the
        // session here. The limit is far beyond any drive FileCat reads; a sector size is a power of two.
        if (length <= 0 || length > MaxDeviceLength)
            throw new IOException($"The administrator helper described {description} as {length:N0} bytes, which no drive is.");
        if (sectorSize is < 512 or > 65536 || (sectorSize & (sectorSize - 1)) != 0)
            throw new IOException($"The administrator helper described {description} with sectors of {sectorSize:N0} bytes, which no drive has.");
        Length = length;
        SectorSize = sectorSize;
    }

    public string Description { get; }
    public long Length { get; }
    public int SectorSize { get; }

    public string? SelectionIdentity
    {
        get
        {
            lock (_lock)
            {
                if (_closed) return null;
                try
                {
                    _request.AsSpan().Clear();
                    _request[0] = RawReadProtocol.DeviceIdentity;
                    _pipe.Write(_request);
                    _pipe.Flush();
                    Span<byte> header = stackalloc byte[8];
                    if (!RawReadProtocol.ReadExactly(_pipe, header)) throw new EndOfStreamException();
                    int status = BinaryPrimitives.ReadInt32LittleEndian(header);
                    int count = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
                    if (status != 0 && count == 0) return null;
                    if (status != 0 || count != 16) throw new InvalidDataException("The administrator helper did not give a complete device identity.");
                    Span<byte> bytes = stackalloc byte[16];
                    if (!RawReadProtocol.ReadExactly(_pipe, bytes)) throw new EndOfStreamException();
                    var identity = new Guid(bytes);
                    return identity == Guid.Empty ? null : identity.ToString("D");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
                {
                    _closed = true;
                    return null;
                }
            }
        }
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset >= Length || buffer.IsEmpty) return 0;
        int done = 0;
        Span<byte> header = stackalloc byte[8];
        lock (_lock)
        {
            if (_closed) throw new IOException("The drive is no longer being read: the administrator helper has stopped.");
            while (done < buffer.Length && offset + done < Length)
            {
                // The helper describes one finite source. Never ask for, or accept, bytes
                // beyond that reported end even if the underlying device is longer.
                int want = (int)Math.Min(Math.Min(buffer.Length - done, RawReadProtocol.MaxRead), Length - offset - done);
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
                if (count < 0 || count > want || status != 0 && count != 0)
                {
                    // A malformed frame may have unread bytes still in the pipe. They must never
                    // become the header or content of a later recovery read.
                    _closed = true;
                    throw new IOException("The administrator helper sent a malformed read answer.");
                }
                if (status != 0) throw new IOException($"The drive could not be read at byte {offset + done:N0}: {new Win32Exception(status).Message}") { HResult = status };
                if (count == 0)
                {
                    _closed = true;
                    throw new IOException("The administrator helper ended the drive before its reported length.");
                }
                try
                {
                    if (!RawReadProtocol.ReadExactly(_pipe, buffer.Slice(done, count))) throw new EndOfStreamException();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    _closed = true;
                    throw new IOException("The administrator helper stopped mid-answer.", ex);
                }
                done += count;
            }
        }
        return done;
    }

    public bool? SharesDestinationDisk(string destinationDirectory) =>
        DeviceTopology.SharesDisk(QuerySourceDisks(), destinationDirectory);

    internal IReadOnlyList<int>? QuerySourceDisks()
    {
        lock (_lock)
        {
            if (_closed) return null;
            try
            {
                _request.AsSpan().Clear();
                _request[0] = RawReadProtocol.SourceDisks;
                _pipe.Write(_request);
                _pipe.Flush();
                Span<byte> header = stackalloc byte[8];
                if (!RawReadProtocol.ReadExactly(_pipe, header)) throw new EndOfStreamException();
                int status = BinaryPrimitives.ReadInt32LittleEndian(header);
                int count = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
                if (status != 0 && count == 0) return null;
                if (status != 0 || count is < 1 or > RawReadProtocol.MaxSourceDisks)
                    throw new InvalidDataException("The administrator helper did not give a complete disk identity.");
                Span<byte> data = stackalloc byte[count * 4];
                if (!RawReadProtocol.ReadExactly(_pipe, data)) throw new EndOfStreamException();
                var disks = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    int disk = BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..]);
                    if (disk < 0) throw new InvalidDataException("The administrator helper gave an invalid disk number.");
                    if (!disks.Contains(disk)) disks.Add(disk);
                }
                return disks;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException)
            {
                _closed = true;
                return null;
            }
        }
    }

    public virtual void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            bool stopped = _closed;
            _closed = true;
            try
            {
                if (!stopped)
                {
                    _request[0] = RawReadProtocol.Close;
                    _pipe.Write(_request);
                    _pipe.Flush();
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
            finally { _pipe.Dispose(); }
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
                    // A same-user process can read the plan's nonce and bind this name before the helper.
                    // Verify the kernel peer against the held runas process before PipeDeviceSource sends Info.
                    process.VerifyReadServer(pipe.SafePipeHandle);
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
        // The helper and a direct read must agree on the source's size, so both ask what it will serve.
        length = DeviceTopology.ServedLength(handle, length, sector);
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
        return RawReadProtocol.Serve(pipe, handle, length, sector, DeviceTopology.IsWholeDiskPath(device));
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
public sealed record PhysicalDisk(int Number, long Length, string? Model, string Bus, bool Removable, IReadOnlyList<string> Drives, string? Identity = null, string? InterfacePath = null)
{
    public string Device => @"\\.\PhysicalDrive" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Which physical disks volumes and folders are on, so recovery never writes to the disk it reads (plan §17.2).</summary>
public static unsafe partial class DeviceTopology
{
    private static readonly Guid DiskInterfaceClass = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    /// <summary>Present disk interface links from the Windows PnP manager, used as the read-open path for the selected disk.</summary>
    internal static IReadOnlyList<string> DiskInterfaces()
    {
        if (!OperatingSystem.IsWindows()) return [];
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Guid kind = DiskInterfaceClass;
            if (CM_Get_Device_Interface_List_SizeW(out uint chars, ref kind, 0, 0) != 0 || chars is < 2 or > 65536) return [];
            var buffer = new char[chars];
            fixed (char* data = buffer)
            {
                uint result = CM_Get_Device_Interface_ListW(ref kind, 0, data, chars, 0);
                if (result == 0)
                {
                    var paths = new List<string>();
                    int start = 0;
                    while (start < buffer.Length && buffer[start] != '\0')
                    {
                        int end = Array.IndexOf(buffer, '\0', start);
                        if (end < 0) return [];
                        paths.Add(new string(buffer, start, end - start));
                        start = end + 1;
                    }
                    return paths;
                }
                if (result != 0x1A /* CR_BUFFER_SMALL */) return [];
            }
        }
        return [];
    }

    /// <summary>Only a currently enumerated PnP disk interface may be used as a whole-disk read path.</summary>
    public static bool IsDiskInterfacePath(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
        DiskInterfaces().Contains(path, StringComparer.OrdinalIgnoreCase);

    internal static bool IsWholeDiskPath(string path) =>
        path.StartsWith(@"\\.\PhysicalDrive", StringComparison.OrdinalIgnoreCase) || IsDiskInterfacePath(path);

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
        foreach (string path in DiskInterfaces())
        {
            using var handle = CreateFile(path, 0, 3, 0, 3, 0, 0);
            if (handle.IsInvalid) continue;
            if (DisksOf(handle, wholeDisk: true) is not { Count: 1 } numbers) continue;
            int n = numbers[0];
            long length = GeometryLength(handle);
            if (length <= 0) continue; // no medium (an empty card reader)
            var (model, bus, removable) = Describe(handle);
            result.Add(new PhysicalDisk(n, length, model, bus, removable, letters.GetValueOrDefault(n) ?? [], DeviceIdentity(handle)?.ToString("D"), path));
        }
        // Multiple live links for one disk would make selection ambiguous; offer neither.
        return result.GroupBy(d => d.Number).Where(g => g.Count() == 1).Select(g => g.Single()).ToList();
    }

    /// <summary>A disk's size from its geometry (a query any handle may make, unlike the length a reading handle asks for).</summary>
    private static long GeometryLength(SafeFileHandle disk)
    {
        var geometry = stackalloc byte[256];
        uint returned;
        if (!DeviceIoControl(disk, 0x000700A0 /* IOCTL_DISK_GET_DRIVE_GEOMETRY_EX */, null, 0, geometry, 256, &returned, 0) || returned < 32) return -1;
        return BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(geometry + 24, 8)); // DISK_GEOMETRY_EX.DiskSize
    }

    /// <summary>Windows' handle-bound storage device GUID. It is queried on both the discovery and read handles.</summary>
    internal static Guid? DeviceIdentity(SafeFileHandle disk)
    {
        if (disk.IsInvalid || disk.IsClosed) return null;
        var output = stackalloc byte[64];
        uint returned;
        if (!DeviceIoControl(disk, 0x002D1084 /* IOCTL_STORAGE_GET_DEVICE_NUMBER_EX */, null, 0, output, 64, &returned, 0) || returned < 40)
            return null;
        var bytes = new ReadOnlySpan<byte>(output, (int)Math.Min(returned, 64));
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) < 40 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) < 40)
            return null;
        var identity = new Guid(bytes.Slice(20, 16));
        return identity == Guid.Empty ? null : identity;
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
        if (IsDiskInterfacePath(pathOrDevice))
        {
            using var disk = CreateFile(pathOrDevice, 0, 3, 0, 3, 0, 0);
            return disk.IsInvalid ? null : DisksOf(disk, wholeDisk: true);
        }
        string? volume = pathOrDevice.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase) ? pathOrDevice.TrimEnd('\\') : Locate(pathOrDevice)?.Volume;
        if (volume is null) return null;
        using var handle = CreateFile(volume, 0, 3, 0, 3, 0, 0);
        if (handle.IsInvalid) return null;
        return DisksOf(handle, wholeDisk: false);
    }

    /// <summary>Queries the already held source. No path is reopened and no source data is read.</summary>
    internal static IReadOnlyList<int>? DisksOf(SafeFileHandle handle, bool wholeDisk)
    {
        if (handle.IsClosed || handle.IsInvalid) return null;
        try
        {
            uint returned;
            if (wholeDisk)
            {
                var number = stackalloc byte[12];
                if (!DeviceIoControl(handle, 0x002D1080 /* IOCTL_STORAGE_GET_DEVICE_NUMBER */, null, 0, number, 12, &returned, 0) || returned != 12)
                    return null;
                var data = new ReadOnlySpan<byte>(number, 12);
                int disk = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
                return BinaryPrimitives.ReadInt32LittleEndian(data) == 7 /* FILE_DEVICE_DISK */ && disk >= 0 ? [disk] : null;
            }
            var output = new byte[8 + 24 * RawReadProtocol.MaxSourceDisks];
            fixed (byte* o = output)
                if (!DeviceIoControl(handle, 0x00560000 /* IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS */, null, 0, o, (uint)output.Length, &returned, 0)) return null;
            return ParseDiskExtents(output, returned);
        }
        catch (ObjectDisposedException) { return null; }
    }

    internal static IReadOnlyList<int>? ParseDiskExtents(ReadOnlySpan<byte> output, uint returned)
    {
        if (returned < 8 || returned > output.Length) return null;
        int count = BinaryPrimitives.ReadInt32LittleEndian(output);
        if (count is < 1 or > RawReadProtocol.MaxSourceDisks || 8 + count * 24 > returned) return null;
        var disks = new List<int>();
        for (int i = 0; i < count; i++)
        {
            var extent = output.Slice(8 + i * 24, 24);
            int disk = BinaryPrimitives.ReadInt32LittleEndian(extent);
            if (disk < 0 || BinaryPrimitives.ReadInt64LittleEndian(extent[8..]) < 0 || BinaryPrimitives.ReadInt64LittleEndian(extent[16..]) <= 0)
                return null;
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
        return SharesDisk(DisksOf(device), folder);
    }

    internal static bool? SharesDisk(IReadOnlyList<int>? source, string folder)
    {
        if (source is not { Count: > 0 } || source.Any(d => d < 0)) return null;
        if (Locate(folder) is not { } place) return null;
        if (place.Server is { } server) return ThisComputer.Is(server, TimeSpan.FromSeconds(2)) ? null : false;
        if (DisksOf(place.Volume!) is not { } volume || WrittenDisks(volume, 0) is not { } target) return null;
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

    /// <summary>
    /// How many bytes the device will actually serve, at or below <paramref name="length"/>. A volume reports its
    /// partition's length, but NTFS keeps a backup boot sector outside the volume's own extent and the last sectors
    /// are then refused: a scan must not be told those bytes exist, and a direct/helper comparison there would
    /// otherwise compare two equally short reads and call them equal (I110). Costs one sector read when nothing is
    /// missing. Whole disks normally serve every byte.
    /// </summary>
    internal static long ServedLength(SafeFileHandle device, long length, int sectorSize)
    {
        if (length <= 0 || sectorSize is < 512 or > 65536) return length;
        var probe = new byte[sectorSize];
        long end = length / sectorSize * sectorSize;
        // A damaged source answers a refused read slowly, so the end is probed a few times at most: enough for a
        // file system that keeps a sector or two outside its own extent, not a search down a failing device.
        for (int attempt = 0; attempt < 16 && end >= sectorSize; attempt++, end -= sectorSize)
        {
            try
            {
                if (RandomAccess.Read(device, probe, end - sectorSize) == sectorSize) return end;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // Nothing near the end reads: report what the device claims rather than invent a smaller source.
        return length;
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

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Device_Interface_List_SizeW(out uint chars, ref Guid interfaceClass, nint deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    private static partial uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, nint deviceId, char* buffer, uint chars, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, uint* returned, nint overlapped);

    [LibraryImport("virtdisk.dll")]
    private static partial uint GetStorageDependencyInformation(SafeFileHandle handle, uint flags, uint size, nint info, uint* used);
}
