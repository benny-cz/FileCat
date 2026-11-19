using System.Buffers.Binary;
using System.Buffers.Text;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const uint MacVolumeMountedDevice = 0x00008000; // ATTR_VOL_MOUNTEDDEVICE
    private const uint MacVolumeUuid = 0x00040000; // ATTR_VOL_UUID
    private const uint MacProtectionFields = MacVolumeMountedDevice | MacVolumeUuid | MacVolumeType;
    private const string DiskArbitration = "/System/Library/Frameworks/DiskArbitration.framework/DiskArbitration";
    private const string ProtectionCoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string MacProtectionSource = "Disk Arbitration on the APFS volume matched by device and UUID";

    internal sealed record MacApfsVolumeIdentity(string Device, Guid Uuid);
    internal sealed record MacApfsProtection(bool FileVault, bool BackingMedia, bool Changing);

    private static FileDetailsReport AddMacApfsProtection(FileDetailsReport report, uint device, CancellationToken ct)
    {
        // The public Disk Arbitration path has been exercised on Apple silicon.
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) return report;
        string path = report.Summary.Path;
        Span<byte> fields = stackalloc byte[512];
        int error = ReadMacVolumeFields(path, MacProtectionFields, fields, ct);
        var volume = error == 0 ? ParseMacApfsVolumeIdentity(fields, device) : null;
        var protection = volume is not null
            ? ReadMacDiskProtection(volume, ct)
            : MacProtectionUnavailable(error switch
            {
                1 or 13 => FactState.Denied,
                2 or 20 => FactState.Changed,
                22 or 45 => FactState.Unsupported,
                0 => FactState.Partial,
                _ => FactState.Failed,
            }, "The selected volume could not be identified by its mounted APFS device and UUID.");
        if (protection.State == FactState.Known &&
            (ReadMacVolumeFields(path, MacProtectionFields, fields, ct) != 0 || ParseMacApfsVolumeIdentity(fields, device) != volume))
            protection = MacProtectionUnavailable(FactState.Changed, "The second metadata sample could not confirm the same mounted volume.");

        var section = new InspectionSection("Location · APFS volume protection",
        [
            ("FileVault", protection.Describe(value => value.Changing ? "Changing" : value.FileVault ? "On" : "Off")),
            ("Backing media encryption reported", protection.Describe(value => value.BackingMedia ? "Yes" : "No")),
            ("Observation", protection.Source),
        ])
        {
            Lines = ["FileVault describes protection of this volume. Hardware encryption and individual files have separate scopes."],
        };
        return report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };
    }

    internal static MacApfsVolumeIdentity? ParseMacApfsVolumeIdentity(ReadOnlySpan<byte> fields, uint device)
    {
        // Length, returned masks, device, mounted device reference, UUID, filesystem type reference.
        const int FixedLength = 60;
        if (fields.Length < FixedLength) return null;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(fields);
        if (length < FixedLength || length > fields.Length ||
            (BinaryPrimitives.ReadUInt32LittleEndian(fields[4..]) & 2) == 0 ||
            (BinaryPrimitives.ReadUInt32LittleEndian(fields[8..]) & MacProtectionFields) != MacProtectionFields ||
            BinaryPrimitives.ReadUInt32LittleEndian(fields[24..]) != device)
            return null;
        fields = fields[..(int)length];
        var uuid = new Guid(fields[36..52], bigEndian: true);
        if (uuid == Guid.Empty || !ReadReference(fields, 52).SequenceEqual("apfs"u8)) return null;
        ReadOnlySpan<byte> source = ReadReference(fields, 28);
        const int PrefixLength = 9;
        if (source.Length is <= PrefixLength or > 64 || !source.StartsWith("/dev/disk"u8)) return null;
        var remaining = source[PrefixLength..];
        int digits = remaining.IndexOfAnyExceptInRange((byte)'0', (byte)'9');
        if (digits == 0 || digits == -1) return null; // An APFS volume is a slice, not a whole device.
        remaining = remaining[digits..];
        while (!remaining.IsEmpty)
        {
            if (remaining[0] != (byte)'s' || remaining.Length < 2) return null;
            remaining = remaining[1..];
            digits = remaining.IndexOfAnyExceptInRange((byte)'0', (byte)'9');
            if (digits == 0) return null;
            remaining = digits == -1 ? [] : remaining[digits..];
        }
        return new(Encoding.ASCII.GetString(source), uuid);

        static ReadOnlySpan<byte> ReadReference(ReadOnlySpan<byte> fields, int reference)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(fields[reference..]);
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(fields[(reference + 4)..]);
            if (offset < FixedLength - reference || offset > fields.Length - reference ||
                count == 0 || count > fields.Length - reference - offset)
                return [];
            var bytes = fields.Slice(reference + offset, (int)count);
            return bytes.IndexOf((byte)0) == bytes.Length - 1 ? bytes[..^1] : [];
        }
    }

    internal static MacApfsProtection? ParseMacApfsProtection(bool encrypted, long flags)
    {
        const long BackingMedia = 1, FullDiskEncryption = 2, Changing = 4;
        if ((flags & ~(BackingMedia | FullDiskEncryption | Changing)) != 0 ||
            !encrypted && (flags & (BackingMedia | FullDiskEncryption)) != 0)
            return null;
        return new((flags & FullDiskEncryption) != 0, (flags & BackingMedia) != 0, (flags & Changing) != 0);
    }

    private static ObservedFact<MacApfsProtection> ReadMacDiskProtection(MacApfsVolumeIdentity volume, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!NativeLibrary.TryLoad(DiskArbitration, out nint library))
            return MacProtectionUnavailable(FactState.Unsupported, "Disk Arbitration is unavailable.");
        nint session = 0, disk = 0, description = 0, uuidText = 0;
        try
        {
            if (!NativeLibrary.TryGetExport(library, "kDADiskDescriptionVolumeUUIDKey", out nint uuidKey) ||
                !NativeLibrary.TryGetExport(library, "kDADiskDescriptionMediaEncryptedKey", out nint encryptedKey) ||
                !NativeLibrary.TryGetExport(library, "kDADiskDescriptionMediaEncryptionDetailKey", out nint detailKey))
                return MacProtectionUnavailable(FactState.Unsupported, "The operating system does not expose these Disk Arbitration properties.");
            session = DASessionCreate(0);
            ct.ThrowIfCancellationRequested();
            if (session != 0) disk = DADiskCreateFromBSDName(0, session, volume.Device);
            ct.ThrowIfCancellationRequested();
            if (disk != 0) description = DADiskCopyDescription(disk);
            ct.ThrowIfCancellationRequested();
            if (description == 0) return MacProtectionUnavailable(FactState.Failed, "Disk Arbitration did not describe this device.");

            nint uuid = GetProperty(description, uuidKey, CFUUIDGetTypeID());
            if (uuid != 0) uuidText = CFUUIDCreateString(0, uuid);
            byte* text = stackalloc byte[37]; // 36 ASCII UUID bytes and the terminator
            if (uuidText == 0 || ProtectionStringGetCString(uuidText, text, 37, 0x0600 /* ASCII */) == 0 ||
                !Utf8Parser.TryParse(new ReadOnlySpan<byte>(text, 36), out Guid actual, out int bytesRead, 'D') || bytesRead != 36 || actual != volume.Uuid)
                return MacProtectionUnavailable(FactState.Partial, "Disk Arbitration did not confirm the observed filesystem UUID.");

            nint encrypted = GetProperty(description, encryptedKey, CFBooleanGetTypeID());
            nint detail = GetProperty(description, detailKey, CFNumberGetTypeID());
            if (encrypted == 0 || detail == 0 || CFNumberGetValue(detail, 4 /* kCFNumberSInt64Type */, out long flags) == 0 ||
                ParseMacApfsProtection(CFBooleanGetValue(encrypted) != 0, flags) is not { } protection)
                return MacProtectionUnavailable(FactState.Unsupported, "Disk Arbitration did not return recognized encryption details for this volume.");
            return ObservedFact<MacApfsProtection>.Known(protection, FactScope.Volume, MacProtectionSource);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return MacProtectionUnavailable(FactState.Unsupported, "The operating system does not expose the required native calls.");
        }
        finally
        {
            if (uuidText != 0) ProtectionRelease(uuidText);
            if (description != 0) ProtectionRelease(description);
            if (disk != 0) ProtectionRelease(disk);
            if (session != 0) ProtectionRelease(session);
            NativeLibrary.Free(library);
        }

        static nint GetProperty(nint description, nint exportedKey, nuint type)
        {
            nint key = Marshal.ReadIntPtr(exportedKey);
            nint value = key == 0 ? 0 : CFDictionaryGetValue(description, key);
            return value != 0 && CFGetTypeID(value) == type ? value : 0;
        }
    }

    private static ObservedFact<MacApfsProtection> MacProtectionUnavailable(FactState state, string reason) =>
        ObservedFact<MacApfsProtection>.Unavailable(state, FactScope.Volume, MacProtectionSource, reason);

    [LibraryImport(DiskArbitration)] private static partial nint DASessionCreate(nint allocator);
    [LibraryImport(DiskArbitration, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint DADiskCreateFromBSDName(nint allocator, nint session, string name);
    [LibraryImport(DiskArbitration)] private static partial nint DADiskCopyDescription(nint disk);
    [LibraryImport(ProtectionCoreFoundation)] private static partial nint CFDictionaryGetValue(nint dictionary, nint key);
    [LibraryImport(ProtectionCoreFoundation)] private static partial nuint CFGetTypeID(nint value);
    [LibraryImport(ProtectionCoreFoundation)] private static partial nuint CFUUIDGetTypeID();
    [LibraryImport(ProtectionCoreFoundation)] private static partial nuint CFBooleanGetTypeID();
    [LibraryImport(ProtectionCoreFoundation)] private static partial nuint CFNumberGetTypeID();
    [LibraryImport(ProtectionCoreFoundation)] private static partial nint CFUUIDCreateString(nint allocator, nint uuid);
    [LibraryImport(ProtectionCoreFoundation)] private static partial byte CFBooleanGetValue(nint value);
    [LibraryImport(ProtectionCoreFoundation)] private static partial byte CFNumberGetValue(nint number, nint type, out long value);
    [LibraryImport(ProtectionCoreFoundation, EntryPoint = "CFStringGetCString")]
    private static partial byte ProtectionStringGetCString(nint value, byte* text, nint length, uint encoding);
    [LibraryImport(ProtectionCoreFoundation, EntryPoint = "CFRelease")] private static partial void ProtectionRelease(nint value);
}
