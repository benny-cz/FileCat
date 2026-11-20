using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.HiddenData;
using FileCat.Core.Inspect;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const string LinuxCapabilitiesSource = "security.capability from fgetxattr on the held ext4 file";

    private static FileDetailsReport AddLinuxFileCapabilities(FileDetailsReport report, SafeFileHandle? file,
        FactState openState, string? openReason, CancellationToken ct)
    {
        var observation = file is null
            ? LinuxCapabilitiesUnavailable(openState, openReason!)
            : ReadLinuxFileCapabilities(file, ct);
        var section = new InspectionSection("File capabilities", [
            ("Recorded execution attributes", observation is { State: FactState.Known, Value: null }
                ? "None returned" : observation.Describe()),
            ("Observation", observation.Source),
        ])
        {
            Lines = observation is { State: FactState.Known, Value: not null }
                ? ["Execution policy, nosuid and no_new_privs can suppress privilege changes. A root ID scopes a namespaced attribute."]
                : [],
        };
        return report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };
    }

    private static ObservedFact<string?> ReadLinuxFileCapabilities(SafeFileHandle file, CancellationToken ct)
    {
        // Read the one fixed-format attribute. A reply above the latest known size is reported as partial.
        Span<byte> bytes = stackalloc byte[24];
        ct.ThrowIfCancellationRequested();
        nint length;
        int error;
        fixed (byte* data = bytes)
        {
            length = LinuxDetailsFGetXattr((int)file.DangerousGetHandle(), "security.capability", data, (nuint)bytes.Length);
            error = length < 0 ? Marshal.GetLastPInvokeError() : 0;
        }
        ct.ThrowIfCancellationRequested();
        if (error == 61) // ENODATA
            return ObservedFact<string?>.Known(null, FactScope.Entry, LinuxCapabilitiesSource);
        if (error == 34 || length > bytes.Length) // ERANGE
            return LinuxCapabilitiesUnavailable(FactState.Partial, "The attribute exceeds the 24-byte inspection limit.");
        if (error != 0)
            return LinuxCapabilitiesUnavailable(LinuxMetadataFailure(error), $"The capability query returned errno {error}.");
        return ParseLinuxFileCapabilities(bytes[..(int)length]);
    }

    internal static ObservedFact<string?> ParseLinuxFileCapabilities(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
            return LinuxCapabilitiesUnavailable(FactState.Failed, "The capability header is incomplete.");
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        int expectedLength = (magic & 0xFF000000) switch { 0x02000000 => 20, 0x03000000 => 24, _ => 0 };
        if (expectedLength == 0 || (magic & 0x00FFFFFE) != 0)
            return LinuxCapabilitiesUnavailable(FactState.Unsupported, "The kernel returned an unrecognized capability revision or flags.");
        if (bytes.Length != expectedLength || HiddenDataDecoder.Capabilities(bytes) is not { } decoded)
            return LinuxCapabilitiesUnavailable(FactState.Failed, "The attribute length does not match its capability revision.");
        return ObservedFact<string?>.Known(decoded.Item1["Capabilities: ".Length..], FactScope.Entry, LinuxCapabilitiesSource);
    }

    private static ObservedFact<string?> LinuxCapabilitiesUnavailable(FactState state, string reason) =>
        ObservedFact<string?>.Unavailable(state, FactScope.Entry, LinuxCapabilitiesSource, reason);

    [LibraryImport("libc", EntryPoint = "fgetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint LinuxDetailsFGetXattr(int fd, string name, byte* value, nuint size);
}
