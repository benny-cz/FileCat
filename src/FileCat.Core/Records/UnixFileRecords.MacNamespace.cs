using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const uint MacVolumeType = 0x00100000; // ATTR_VOL_FSTYPENAME
    private const uint MacVolumeCapabilities = 0x00020000; // ATTR_VOL_CAPABILITIES
    private const string MacNamespaceSource = "Darwin volume capabilities on the observed device";

    private static FileDetailsReport AddMacApfsDetails(FileDetailsReport report, ReadOnlySpan<byte> initial, CancellationToken ct)
    {
        string path = report.Summary.Path;
        uint device = BinaryPrimitives.ReadUInt32LittleEndian(initial[24..]);
        Span<byte> buffer = stackalloc byte[256];
        // Check the mount's type through the fast statfs attributes before requesting capabilities.
        if (ReadMacVolumeFields(path, MacVolumeType, buffer, ct) != 0 || !MatchesMacApfsVolume(buffer, device, capabilities: false))
            return report;

        var (sensitive, preserving, volumeVerified) = ReadMacVolumeFields(path, MacVolumeType | MacVolumeCapabilities, buffer, ct) switch
        {
            0 => ParseMacApfsNamespace(buffer, device),
            1 or 13 => MacNamespaceUnavailable(FactState.Denied, "The volume capabilities could not be read with this access."),
            2 or 20 => MacNamespaceUnavailable(FactState.Changed, "The path disappeared during the volume observation."),
            22 or 45 => MacNamespaceUnavailable(FactState.Unsupported, "The volume did not support the capability query."),
            _ => MacNamespaceUnavailable(FactState.Failed, "The volume capability query failed."),
        };
        const string apfsNormalization = "Canonically equivalent names match and the stored spelling is preserved (documented APFS behavior).";
        string normalization = volumeVerified && OperatingSystem.IsMacOSVersionAtLeast(10, 13) ? apfsNormalization : "Not inspected";
        var section = new InspectionSection("APFS volume namespace", [
            ("Case sensitive", sensitive.Describe(YesNo)), ("Case preserving", preserving.Describe(YesNo)),
            ("Unicode normalization", normalization), ("Observation", MacNamespaceSource)]);
        report = report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };
        return report.Summary.Kind == FileObjectKind.File ? AddMacApfsSharing(report, initial, ct) : report;

        static string YesNo(bool value) => value ? "Yes" : "No";
    }

    private static int ReadMacVolumeFields(string path, uint requested, Span<byte> buffer, CancellationToken ct)
    {
        byte* list = stackalloc byte[24];
        new Span<byte>(list, 24).Clear();
        *(ushort*)list = 5; // ATTR_BIT_MAP_COUNT
        *(uint*)(list + 4) = 0x80000002; // ATTR_CMN_RETURNED_ATTRS | ATTR_CMN_DEVID
        *(uint*)(list + 8) = 0x80000000 | requested; // ATTR_VOL_INFO required for volume attributes
        buffer.Clear();
        ct.ThrowIfCancellationRequested();
        int status;
        fixed (byte* data = buffer) status = GetAttrList(path, list, data, (nuint)buffer.Length, 1 | 8);
        int error = status == 0 ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        return error;
    }

    internal static (ObservedFact<bool> Sensitive, ObservedFact<bool> Preserving, bool VolumeVerified) ParseMacApfsNamespace(
        ReadOnlySpan<byte> fields, uint device)
    {
        if (!MatchesMacApfsVolume(fields, device, capabilities: true))
            return MacNamespaceUnavailable(FactState.Partial, "The second response did not establish APFS capabilities for the observed device.");
        // vol_capabilities_attr_t contains four capability words followed by four validity words.
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(fields[28..]);
        uint valid = BinaryPrimitives.ReadUInt32LittleEndian(fields[44..]);
        ObservedFact<bool> Flag(uint bit) => (valid & bit) != 0
            ? ObservedFact<bool>.Known((flags & bit) != 0, FactScope.Volume, MacNamespaceSource)
            : ObservedFact<bool>.Unavailable(FactState.Unsupported, FactScope.Volume, MacNamespaceSource, "The capability bit is not valid on this volume.");
        return (Flag(0x100), Flag(0x200), true); // VOL_CAP_FMT_CASE_SENSITIVE | VOL_CAP_FMT_CASE_PRESERVING
    }

    private static bool MatchesMacApfsVolume(ReadOnlySpan<byte> fields, uint device, bool capabilities)
    {
        // Packed length, attribute_set_t, device, optional 32-byte capability record, then type attrreference_t.
        int reference = capabilities ? 60 : 28;
        int fixedLength = reference + 8;
        if (fields.Length < fixedLength) return false;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(fields);
        if (length < fixedLength || length > fields.Length) return false;
        uint common = BinaryPrimitives.ReadUInt32LittleEndian(fields[4..]);
        uint volume = BinaryPrimitives.ReadUInt32LittleEndian(fields[8..]);
        uint required = MacVolumeType | (capabilities ? MacVolumeCapabilities : 0);
        if ((common & 2) == 0 || (volume & required) != required || BinaryPrimitives.ReadUInt32LittleEndian(fields[24..]) != device)
            return false;
        int offset = BinaryPrimitives.ReadInt32LittleEndian(fields[reference..]);
        uint bytes = BinaryPrimitives.ReadUInt32LittleEndian(fields[(reference + 4)..]);
        return bytes == 5 && offset >= 8 && offset <= (int)length - reference - 5 &&
            fields.Slice(reference + offset, 5).SequenceEqual("apfs\0"u8);
    }

    private static (ObservedFact<bool>, ObservedFact<bool>, bool) MacNamespaceUnavailable(FactState state, string reason)
    {
        var fact = ObservedFact<bool>.Unavailable(state, FactScope.Volume, MacNamespaceSource, reason);
        return (fact, fact, false);
    }
}
