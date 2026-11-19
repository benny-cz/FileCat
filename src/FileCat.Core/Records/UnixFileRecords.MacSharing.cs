using System.Buffers.Binary;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed partial class UnixFileRecords
{
    private const string MacSharingSource = "Darwin extended getattrlist fields for the sampled APFS file";

    private static FileDetailsReport AddMacApfsSharing(FileDetailsReport report, ReadOnlySpan<byte> initial, CancellationToken ct)
    {
        InspectionSection section;
        try
        {
            byte[] fields = ReadMacDetailsFields(report.Summary.Path, ct, apfsSharing: true);
            var sampled = ParseMacDetails(report.Summary.Path, fields).Summary;
            section = sampled.Kind == FileObjectKind.File && sampled.Identity.State == FactState.Known &&
                sampled.Identity.Value == report.Summary.Identity.Value && sampled.NativeAttributes.State == FactState.Known &&
                (sampled.NativeAttributes.Value & MacMaterializationPolicy.DatalessFlag) == 0 && HasMacIdentityTimestamps(initial) && HasMacIdentityTimestamps(fields) &&
                initial.Slice(32, 16).SequenceEqual(fields.AsSpan(32, 16)) && initial.Slice(64, 16).SequenceEqual(fields.AsSpan(64, 16))
                ? ParseMacApfsSharing(fields)
                : Unavailable(FactState.Changed, "The second sample did not confirm the same ordinary file and timestamps.");
        }
        catch (FileNotFoundException) { section = Unavailable(FactState.Changed, "The file disappeared during observation."); }
        catch (UnauthorizedAccessException) { section = Unavailable(FactState.Denied, "The sharing fields could not be read with this access."); }
        catch (IOException ex) when (ex.HResult == MacMaterializationPolicy.Required)
        { section = Unavailable(FactState.NotInspected, MacMaterializationRequired); }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        { section = Unavailable(FactState.Failed, "Darwin did not return a usable sharing observation."); }
        return report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };

        static InspectionSection Unavailable(FactState state, string reason) => new("APFS file sharing",
            [("Observation", $"{state}: {reason}"), ("Source", MacSharingSource)]);
    }

    private static InspectionSection ParseMacApfsSharing(ReadOnlySpan<byte> fields)
    {
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(fields)); // The common parser validated the response first.
        uint mask = BinaryPrimitives.ReadUInt32LittleEndian(fields[20..]);
        var privateBytes = length >= 148 ? BinaryPrimitives.ReadInt64LittleEndian(fields[140..]) : -1;
        var privateSize = Field(0x8, length >= 148 && privateBytes >= 0, privateBytes);
        var clone = Field(0x100, length >= 156, length >= 156 ? BinaryPrimitives.ReadUInt64LittleEndian(fields[148..]) : 0);
        var flags = Field(0x200, length >= 164, length >= 164 ? BinaryPrimitives.ReadUInt64LittleEndian(fields[156..]) : 0);
        return new("APFS file sharing", [("Private bytes reported", privateSize.Describe(RecordText.Bytes)),
            ("Clone ID", clone.Describe(id => id == 0 ? "None reported" : $"0x{id:X16}")),
            ("Sharing", flags.Describe(value => (value & 0x40) != 0 ? "All blocks reported shared" :
                (value & 1) != 0 ? "May share blocks" : "No sharing flag reported")),
            ("Sparse representation flag", flags.Describe(value => (value & 0x10) != 0 ? "Set" : "Clear")),
            ("Extended flags", flags.Describe(value => $"0x{value:X}")), ("Source", MacSharingSource)])
        {
            Lines = ["These fields describe one file. Total shared storage and space reclaimed by removal require separate observations."],
        };

        ObservedFact<T> Field<T>(uint bit, bool valid, T value) => (mask & bit) == 0
            ? ObservedFact<T>.Unavailable(FactState.Unsupported, FactScope.Entry, MacSharingSource, "Darwin did not return this field.")
            : valid ? ObservedFact<T>.Known(value, FactScope.Entry, MacSharingSource)
                : ObservedFact<T>.Unavailable(FactState.Failed, FactScope.Entry, MacSharingSource, "The returned field has no valid scalar value.");
    }
}
