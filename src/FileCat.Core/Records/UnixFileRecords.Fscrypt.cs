using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const string FscryptSource = "FS_IOC_GET_ENCRYPTION_POLICY_EX on the held ext4 object";
    private const string FscryptKeySource = "FS_IOC_GET_ENCRYPTION_KEY_STATUS on the same ext4 descriptor";
    internal readonly record struct LinuxFscryptPolicy(int Version, byte ContentsMode, byte FilenamesMode, byte Flags);
    internal enum LinuxFscryptKeyStatus { Absent = 1, Present = 2, IncompletelyRemoved = 3 }

    private static ObservedFact<LinuxFscryptPolicy?> ReadLinuxFscrypt(SafeFileHandle file, Span<byte> bytes, CancellationToken ct)
    {
        // The x64 ioctl number encodes only the size field and version byte, although the kernel
        // writes up to the caller's capacity. Leave room for the largest qualified policy (v2).
        const ulong getPolicy = 0xc0096616;
        bytes.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, 24);
        ct.ThrowIfCancellationRequested();
        int error;
        fixed (byte* data = bytes)
            error = Ioctl((int)file.DangerousGetHandle(), getPolicy, data) == 0 ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        return error switch
        {
            0 => ParseLinuxFscryptPolicy(bytes),
            61 => ObservedFact<LinuxFscryptPolicy?>.Known(null, FactScope.Entry, FscryptSource), // ENODATA
            22 => FscryptUnavailable(FactState.Unsupported, "The kernel does not recognize the policy version."),
            75 => FscryptUnavailable(FactState.Partial, "The policy exceeds the qualified 24-byte view."),
            _ => FscryptUnavailable(LinuxMetadataFailure(error), $"The policy query returned errno {error}."),
        };
    }

    internal static ObservedFact<LinuxFscryptPolicy?> ParseLinuxFscryptPolicy(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 9) return FscryptUnavailable(FactState.Failed, "The policy header is truncated.");
        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        if (size == 0 || size > (ulong)(bytes.Length - 8))
            return FscryptUnavailable(FactState.Failed, "The policy length exceeds its returned buffer or is empty.");

        int version = bytes[8] switch { 0 => 1, 2 => 2, _ => 0 };
        if (version == 0) return FscryptUnavailable(FactState.Unsupported, $"Unrecognized fscrypt policy version byte {bytes[8]}.");
        if (size != (version == 1 ? 12u : 24u))
            return FscryptUnavailable(FactState.Failed, "The returned length does not match the fscrypt policy version.");
        return ObservedFact<LinuxFscryptPolicy?>.Known(new LinuxFscryptPolicy(version, bytes[9], bytes[10], bytes[11]), FactScope.Entry, FscryptSource);
    }

    private static ObservedFact<LinuxFscryptPolicy?> FscryptUnavailable(FactState state, string reason) =>
        ObservedFact<LinuxFscryptPolicy?>.Unavailable(state, FactScope.Entry, FscryptSource, reason);

    private static ObservedFact<LinuxFscryptKeyStatus> ReadLinuxFscryptKeyStatus(SafeFileHandle file,
        ReadOnlySpan<byte> identifier, CancellationToken ct)
    {
        // fscrypt_get_key_status_arg contains a 40-byte key specifier, 24 reserved input bytes,
        // and 64 output bytes. The identifier is an input reference from the verified v2 policy.
        const ulong getKeyStatus = 0xc080661a;
        Span<byte> bytes = stackalloc byte[128];
        bytes.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 2); // FSCRYPT_KEY_SPEC_TYPE_IDENTIFIER
        identifier.CopyTo(bytes[8..24]);
        ct.ThrowIfCancellationRequested();
        int error;
        fixed (byte* data = bytes)
            error = Ioctl((int)file.DangerousGetHandle(), getKeyStatus, data) == 0 ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        return error == 0 ? ParseLinuxFscryptKeyStatus(bytes) : ObservedFact<LinuxFscryptKeyStatus>.Unavailable(
            LinuxMetadataFailure(error), FactScope.Volume, FscryptKeySource, $"The key status query returned errno {error}.");
    }

    internal static ObservedFact<LinuxFscryptKeyStatus> ParseLinuxFscryptKeyStatus(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 128)
            return ObservedFact<LinuxFscryptKeyStatus>.Unavailable(FactState.Failed, FactScope.Volume, FscryptKeySource, "The key status reply is incomplete.");
        uint status = BinaryPrimitives.ReadUInt32LittleEndian(bytes[64..]);
        return status is >= 1 and <= 3
            ? ObservedFact<LinuxFscryptKeyStatus>.Known((LinuxFscryptKeyStatus)status, FactScope.Volume, FscryptKeySource)
            : ObservedFact<LinuxFscryptKeyStatus>.Unavailable(FactState.Unsupported, FactScope.Volume, FscryptKeySource, "The kernel returned an unrecognized key status.");
    }

    private static FileDetailsReport AddLinuxFscrypt(FileDetailsReport report, SafeFileHandle? file,
        FactState openState, string? openReason, CancellationToken ct)
    {
        Span<byte> policyBytes = stackalloc byte[8 + 24];
        var policy = file is null ? FscryptUnavailable(openState, openReason!) : ReadLinuxFscrypt(file, policyBytes, ct);
        if (policy.State == FactState.Known && report.Summary.FileEncryption is { State: FactState.Known } flag && flag.Value != policy.Value.HasValue)
            policy = FscryptUnavailable(FactState.Changed, "The native policy and the earlier statx encrypted flag disagree.");

        string state = policy.State == FactState.Known ? policy.Value.HasValue ? "Policy present" : "No policy on this object" : policy.State.ToString();
        var fields = new List<(string, string)>
        {
            ("Status", state),
            ("Scope", report.Summary.Kind == FileObjectKind.Directory ? "Directory policy" : "Regular file"),
            ("Source", policy.Source),
        };
        if (policy.Value is { } value)
        {
            fields.Add(("Policy version", value.Version.ToString()));
            fields.Add(("Content mode", FscryptMode(value.ContentsMode)));
            fields.Add(("Filename mode", FscryptMode(value.FilenamesMode)));
            fields.Add(("Policy flags", $"0x{value.Flags:X2}"));
            if (value.Version == 2 && file is not null)
            {
                var key = ReadLinuxFscryptKeyStatus(file, policyBytes[16..32], ct);
                fields.Add(("Filesystem keyring", key.Describe(status => status switch
                {
                    LinuxFscryptKeyStatus.Present => "Policy key present",
                    LinuxFscryptKeyStatus.Absent => "Policy key absent",
                    _ => "Policy key removal incomplete",
                })));
                fields.Add(("Key status source", key.Source));
            }
            else fields.Add(("Filesystem keyring", "Not inspected for version 1 policies, which can use process keyrings"));
        }
        const string limit = "Item access also depends on permissions and active file handles. Other policies and volume encryption were not assessed.";
        var section = new InspectionSection("fscrypt policy", fields) { Lines = policy.Reason is { } reason ? [reason, limit] : [limit] };
        return report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };
    }

    private static string FscryptMode(byte mode) => mode switch
    {
        1 => "AES-256-XTS",
        4 => "AES-256-CTS",
        5 => "AES-128-CBC",
        6 => "AES-128-CTS",
        7 => "SM4-XTS",
        8 => "SM4-CTS",
        9 => "Adiantum",
        10 => "AES-256-HCTR2",
        _ => $"Unrecognized mode {mode}",
    };
}
