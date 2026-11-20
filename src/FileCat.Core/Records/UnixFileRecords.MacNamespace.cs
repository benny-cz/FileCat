using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const uint MacVolumeType = 0x00100000; // ATTR_VOL_FSTYPENAME
    private const uint MacVolumeCapabilities = 0x00020000; // ATTR_VOL_CAPABILITIES
    private const uint MacVolumeSubtype = 0x00200000; // ATTR_VOL_FSSUBTYPE
    private const string MacNamespaceSource = "Darwin volume capabilities on the observed device";

    private static FileDetailsReport AddMacVolumeDetails(FileDetailsReport report, ReadOnlySpan<byte> initial, CancellationToken ct)
    {
        string path = report.Summary.Path;
        uint device = BinaryPrimitives.ReadUInt32LittleEndian(initial[24..]);
        Span<byte> buffer = stackalloc byte[256];
        // Check the mount's type through the fast statfs attributes before requesting capabilities.
        if (ReadMacVolumeFields(path, MacVolumeType, buffer, ct) != 0)
            return report;
        bool apfs = MatchesMacVolume(buffer, device, "apfs\0"u8, capabilities: false);
        if (!apfs && !MatchesMacVolume(buffer, device, "hfs\0"u8, capabilities: false)) return report;

        uint requested = MacVolumeType | MacVolumeCapabilities | (apfs ? 0 : MacVolumeSubtype);
        var (sensitive, preserving, volumeVerified) = ReadMacVolumeFields(path, requested, buffer, ct) switch
        {
            0 => ParseMacVolumeNamespace(buffer, device, apfs),
            1 or 13 => MacNamespaceUnavailable(FactState.Denied, "The volume capabilities could not be read with this access."),
            2 or 20 => MacNamespaceUnavailable(FactState.Changed, "The path disappeared during the volume observation."),
            22 or 45 => MacNamespaceUnavailable(FactState.Unsupported, "The volume did not support the capability query."),
            _ => MacNamespaceUnavailable(FactState.Failed, "The volume capability query failed."),
        };
        const string apfsNormalization = "Canonically equivalent names match and the stored spelling is preserved (documented APFS behavior).";
        const string hfsNormalization = "Names are stored in a decomposed Unicode form under HFS Plus rules.";
        string normalization = "Not inspected";
        if (volumeVerified)
        {
            if (apfs && OperatingSystem.IsMacOSVersionAtLeast(10, 13)) normalization = apfsNormalization;
            else if (!apfs && (BinaryPrimitives.ReadUInt32LittleEndian(buffer[8..]) & MacVolumeSubtype) != 0 &&
                BinaryPrimitives.ReadUInt32LittleEndian(buffer[68..]) <= 3) normalization = hfsNormalization;
        }
        var section = new InspectionSection(apfs ? "APFS volume namespace" : "HFS volume namespace", [
            ("Case sensitive", sensitive.Describe(YesNo)), ("Case preserving", preserving.Describe(YesNo)),
            ("Unicode normalization", normalization), ("Observation", MacNamespaceSource)]);
        report = report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };
        if (!apfs) return report;
        report = AddMacApfsProtection(report, device, ct);
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

    internal static (ObservedFact<bool> Sensitive, ObservedFact<bool> Preserving, bool VolumeVerified) ParseMacVolumeNamespace(
        ReadOnlySpan<byte> fields, uint device, bool apfs)
    {
        if (!MatchesMacVolume(fields, device, apfs ? "apfs\0"u8 : "hfs\0"u8, capabilities: true, subtype: !apfs))
            return MacNamespaceUnavailable(FactState.Partial, "The second response did not establish the capabilities of the observed volume.");
        // vol_capabilities_attr_t contains four capability words followed by four validity words.
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(fields[28..]);
        uint valid = BinaryPrimitives.ReadUInt32LittleEndian(fields[44..]);
        ObservedFact<bool> Flag(uint bit) => (valid & bit) != 0
            ? ObservedFact<bool>.Known((flags & bit) != 0, FactScope.Volume, MacNamespaceSource)
            : ObservedFact<bool>.Unavailable(FactState.Unsupported, FactScope.Volume, MacNamespaceSource, "The capability bit is not valid on this volume.");
        return (Flag(0x100), Flag(0x200), true); // VOL_CAP_FMT_CASE_SENSITIVE | VOL_CAP_FMT_CASE_PRESERVING
    }

    private static bool MatchesMacVolume(ReadOnlySpan<byte> fields, uint device, ReadOnlySpan<byte> name, bool capabilities, bool subtype = false)
    {
        // Packed length, attribute_set_t, device, optional 32-byte capabilities, type attrreference_t and optional subtype.
        int reference = capabilities ? 60 : 28;
        int fixedLength = reference + (subtype ? 12 : 8);
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
        return bytes == name.Length && offset >= fixedLength - reference && offset <= (int)length - reference - name.Length &&
            fields.Slice(reference + offset, name.Length).SequenceEqual(name);
    }

    private static (ObservedFact<bool>, ObservedFact<bool>, bool) MacNamespaceUnavailable(FactState state, string reason)
    {
        var fact = ObservedFact<bool>.Unavailable(state, FactScope.Volume, MacNamespaceSource, reason);
        return (fact, fact, false);
    }
}
