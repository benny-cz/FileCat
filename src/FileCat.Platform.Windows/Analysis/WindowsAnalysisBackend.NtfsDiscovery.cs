using System.Buffers;
using System.Buffers.Binary;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Elevation;
using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows.Analysis;

public sealed partial class WindowsAnalysisBackend
{
    internal static NtfsScan PrepareNtfsDiscovery(string selection, bool portable, string exchangeRoot, nint owner, CancellationToken cancellation)
    {
        if (owner == 0) throw new NotSupportedException("Fast NTFS scan needs a Windows window to own the authorization prompt.");
        if (ElevationBroker.Locate(portable, out var reason) is null)
            throw new NotSupportedException(reason ?? "The installed read-only NTFS helper is unavailable.");
        var volume = NtfsMetadataVolume.CaptureSelection(selection, cancellation);
        var source = new WindowsAnalysisBackend(selection, null, null, null, volume);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (source.RootEntry.Kind != AnalysisKind.Directory || source.RootEntry.Cloud == true) throw new AnalysisSourceChangedException();
            return new NtfsScan(source, volume, portable, exchangeRoot, owner);
        }
        catch { source.Dispose(); throw; }
    }

    internal NtfsMetadataVolume CaptureDiscoveryVolume(CancellationToken cancellation)
    {
        Guard();
        var selected = NtfsMetadataVolume.CaptureSelection(Root.DisplayLocator, cancellation);
        if (RootEntry.Kind != AnalysisKind.Directory || RootEntry.Cloud == true ||
            NtfsMetadataVolume.Native.Capture(selected.GuidPath, _root.Value) != selected)
            throw new AnalysisSourceChangedException();
        return selected;
    }

    internal unsafe UInt128? DiscoveryPrivateBoundaryId(IAnalysisBackend protection)
    {
        Guard();
        if (protection is not WindowsAnalysisBackend native) throw new AnalysisSourceChangedException();
        native.Guard();
        if (native._namespace != _namespace) return null;
        if (native._volume != _volume || native.RootEntry.Kind != AnalysisKind.Directory) throw new AnalysisSourceChangedException();
        Span<byte> identity = stackalloc byte[24];
        fixed (byte* bytes = identity)
            if (!GetInfo(native._root.Value, 18 /* FileIdInfo */, bytes, 24)) throw new AnalysisSourceChangedException();
        var id = BinaryPrimitives.ReadUInt128LittleEndian(identity[8..]);
        if (id == 0 || BinaryPrimitives.ReadUInt64LittleEndian(identity) != _volume) throw new AnalysisSourceChangedException();
        return id;
    }

    private const uint DiscoveryKindMask = (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device);
    private const int DiscoveryPathChars = 32768, DiscoveryHeldDirectories = 48;

    internal AnalysisEntry?[] MeasureDiscoveryChildren(IReadOnlyList<AnalysisNtfsObservation> ancestors,
        IReadOnlyList<AnalysisNtfsObservation> children, bool detailed, CancellationToken cancellation)
    {
        Guard();
        var measured = new AnalysisEntry?[children.Count];
        if (_nativeTimePrecisionTicks != 1 || ancestors.Count > DiscoveryHeldDirectories || children.Count == 0) return measured;
        var held = new List<OwnedHandle>(ancestors.Count);
        char[] nameBuffer = ArrayPool<char>.Shared.Rent(DiscoveryPathChars);
        try
        {
            SafeHandle current = _root.Value;
            string name = DiscoveryNormalizedName(current, nameBuffer);
            foreach (var directory in ancestors)
            {
                cancellation.ThrowIfCancellationRequested();
                if ((directory.Attributes & DiscoveryKindMask) != (uint)FileAttributes.Directory)
                    throw new AnalysisSourceChangedException();
                string component = DiscoveryComponent(directory);
                var next = OpenChild(current, component, directory: false);
                held.Add(next); current = next.Value;
                name = DiscoveryChildName(name, component);
                RequireDiscoveryIdentity(current, name, directory, nameBuffer);
                if (Observe(current, [], [], _namespace) is not { Kind: AnalysisKind.Directory, Cloud: false })
                    throw new AnalysisSourceChangedException();
            }

            for (int i = 0; i < children.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var child = children[i];
                try
                {
                    string component = DiscoveryComponent(child);
                    string expected = DiscoveryChildName(name, component);
                    using var file = OpenChild(current, component, directory: false);
                    RequireDiscoveryIdentity(file.Value, expected, child, nameBuffer);
                    var observed = Observe(file.Value, [], child.RawName.ToArray(), _namespace, _nativeTimePrecisionTicks);
                    if (detailed) observed = ReadNamedAllocation(file.Value, observed, cancellation);
                    var after = Observe(file.Value, [], [], _namespace, _nativeTimePrecisionTicks);
                    RequireDiscoveryIdentity(file.Value, expected, child, nameBuffer);
                    if (after.Identity != observed.Identity || after.Version != observed.Version || after.Kind != observed.Kind ||
                        after.Cloud != observed.Cloud || after.Sparse != observed.Sparse || after.Compressed != observed.Compressed ||
                        after.LogicalLength != observed.LogicalLength || after.Allocation != observed.Allocation || after.Links != observed.Links ||
                        after.ModifiedUtc != observed.ModifiedUtc || after.CreatedUtc != observed.CreatedUtc)
                        throw new AnalysisSourceChangedException();
                    measured[i] = observed;
                }
                catch (Exception error) when (!AnalysisCloseUnconfirmedException.IsPresent(error) &&
                    error is AnalysisSourceChangedException or AnalysisEntryUnavailableException { State: MeasurementState.Changed })
                { measured[i] = ChangedDiscoveryEntry(child); }
                catch (Exception error) when (!AnalysisCloseUnconfirmedException.IsPresent(error) &&
                    error is IOException or UnauthorizedAccessException or InvalidDataException)
                { /* This is an ordinary-access refusal. The discovery row stays unmeasured. */ }
            }
            if (ancestors.Count != 0)
            {
                RequireDiscoveryIdentity(current, name, ancestors[^1], nameBuffer);
                if (Observe(current, [], [], _namespace) is not { Kind: AnalysisKind.Directory, Cloud: false })
                    throw new AnalysisSourceChangedException();
            }
            return measured;
        }
        catch (Exception error) when (!AnalysisCloseUnconfirmedException.IsPresent(error) &&
            error is AnalysisSourceChangedException or AnalysisEntryUnavailableException { State: MeasurementState.Changed })
        {
            for (int i = 0; i < children.Count; i++) measured[i] = ChangedDiscoveryEntry(children[i]);
            return measured;
        }
        catch (Exception error) when (!AnalysisCloseUnconfirmedException.IsPresent(error) &&
            error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Array.Clear(measured);
            return measured;
        }
        finally
        {
            try { AnalysisNativeHandle.CloseAll(held.AsEnumerable().Reverse()); }
            finally { ArrayPool<char>.Shared.Return(nameBuffer, clearArray: true); }
        }
    }

    private unsafe void RequireDiscoveryIdentity(SafeHandle file, string expectedName, AnalysisNtfsObservation expected,
        char[] buffer)
    {
        Span<byte> identity = stackalloc byte[24], basic = stackalloc byte[40];
        fixed (byte* id = identity, info = basic)
            if (!GetInfo(file, 18 /* FileIdInfo */, id, 24) || !GetInfo(file, 0 /* FileBasicInfo */, info, 40))
                throw new IOException("Ordinary NTFS identity is unavailable.");
        if (BinaryPrimitives.ReadUInt64LittleEndian(identity) != _volume ||
            BinaryPrimitives.ReadUInt128LittleEndian(identity[8..]) != expected.ObjectId || (expected.Attributes & (uint)FileAttributes.Device) != 0 ||
            (BinaryPrimitives.ReadUInt32LittleEndian(basic[32..]) & DiscoveryKindMask) != (expected.Attributes & DiscoveryKindMask) ||
            !string.Equals(DiscoveryNormalizedName(file, buffer), expectedName, StringComparison.Ordinal))
            throw new AnalysisSourceChangedException();
    }

    private static unsafe string DiscoveryNormalizedName(SafeHandle handle, char[] buffer)
    {
        uint count;
        fixed (char* chars = buffer)
            count = GetFinalPathNameByHandle(handle, chars, DiscoveryPathChars, 4 /* VOLUME_NAME_NONE | FILE_NAME_NORMALIZED */);
        if (count == 0 || count >= DiscoveryPathChars)
            throw new IOException("The exact native NTFS name could not be verified.");
        return new(buffer, 0, (int)count);
    }

    private static string DiscoveryComponent(AnalysisNtfsObservation row) => AnalysisNtfsObservation.ValidName(row.RawName.AsSpan())
        ? AnalysisRawNames.Decode("utf16le", row.RawName.AsSpan()) : throw new AnalysisSourceChangedException();
    private static string DiscoveryChildName(string parent, string child) => parent.EndsWith('\\') ? parent + child : parent + "\\" + child;
    private static AnalysisEntry ChangedDiscoveryEntry(AnalysisNtfsObservation row) =>
        AnalysisEntry.Unavailable([], row.RawName, "utf16le", UnavailableKind(row.Attributes), MeasurementState.Changed, "ntfs-discovery-entry-changed");

    internal sealed class NtfsScan(WindowsAnalysisBackend source, NtfsMetadataVolume volume, bool portable, string exchange, nint owner) : IAnalysisNtfsScan
    {
        internal NtfsMetadataVolume Volume => volume;
        public IAnalysisBackend Source => source;
        public UInt128 RootId { get; } = NtfsMetadataProtocol.ParseId(volume.RootFileId);

        public void ValidateVolume(CancellationToken cancellation)
        {
            if (source.CaptureDiscoveryVolume(cancellation) != volume) throw new AnalysisSourceChangedException();
        }

        public UInt128? GetPrivateBoundaryId(IAnalysisBackend protection) => source.DiscoveryPrivateBoundaryId(protection);
        public AnalysisEntry?[] Measure(IReadOnlyList<AnalysisNtfsObservation> ancestors, IReadOnlyList<AnalysisNtfsObservation> children,
            bool detailed, CancellationToken cancellation) => source.MeasureDiscoveryChildren(ancestors, children, detailed, cancellation);

        public async Task<AnalysisNtfsOutcome> CaptureAsync(AnalysisNtfsDiscovery discovery, AnalysisNtfsControl control, CancellationToken cancellation)
        {
            try
            {
                await using var client = await NtfsMetadataClient.StartAsync(volume, portable, exchange, owner, cancellation).ConfigureAwait(false);
                return await client.CaptureAsync(discovery, control, cancellation).ConfigureAwait(false) switch
                {
                    NtfsMetadataCompletion.Finished => AnalysisNtfsOutcome.Finished,
                    NtfsMetadataCompletion.Partial => AnalysisNtfsOutcome.Partial,
                    _ => AnalysisNtfsOutcome.Canceled
                };
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return AnalysisNtfsOutcome.Canceled; }
        }
    }
}
