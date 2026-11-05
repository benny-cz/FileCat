using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FileCat.Core.Analysis;

public enum AnalysisHostObservation { WindowsHardwareProfile, DarwinHostUuid, LinuxApplicationMachineId }

/// <summary>Recorded, private correlation data. Parsing this value never grants runtime source authority.</summary>
public sealed record AnalysisHostProfileIdentity
{
    public Guid ProfileId { get; }
    public AnalysisHostObservation? Method { get; }
    public string? HostToken { get; }
    public bool HasHostContext => Method.HasValue;
    private AnalysisHostProfileIdentity(Guid profile, AnalysisHostObservation? method, string? token)
    { ProfileId = profile; Method = method; HostToken = token; }
    private static string MethodName(AnalysisHostObservation method) => method switch
    {
        AnalysisHostObservation.WindowsHardwareProfile => "windows-hardware-profile",
        AnalysisHostObservation.DarwinHostUuid => "darwin-host-uuid",
        AnalysisHostObservation.LinuxApplicationMachineId => "linux-app-machine-id",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
    public static AnalysisHostProfileIdentity Create(Guid profile, AnalysisHostObservation method, ReadOnlySpan<byte> observation)
    {
        if (profile == Guid.Empty || !Enum.IsDefined(method) || observation.Length != 16 || observation.IndexOfAnyExcept((byte)0) < 0 || observation.IndexOfAnyExcept((byte)255) < 0)
            throw new ArgumentException("A generated profile and nonempty native host observation are required.");
        Span<byte> key = stackalloc byte[16]; profile.TryWriteBytes(key, bigEndian: true, out _);
        byte[] domain = Encoding.ASCII.GetBytes("FileCat/analysis-host-profile/v1/" + MethodName(method) + "/");
        byte[] message = new byte[domain.Length + observation.Length]; domain.CopyTo(message, 0); observation.CopyTo(message.AsSpan(domain.Length));
        try { return new(profile, method, Convert.ToHexStringLower(HMACSHA256.HashData(key, message))); }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(message); }
    }
    public static AnalysisHostProfileIdentity Unverified(Guid profile) => profile != Guid.Empty ? new(profile, null, null) : throw new ArgumentException("A generated private profile identity is required.", nameof(profile));
    public override string ToString() => Method is { } method ? "hp1:" + ProfileId.ToString("N") + ":" + MethodName(method) + ":" + HostToken : "hp0:" + ProfileId.ToString("N");
    public static bool TryParse(string? value, out AnalysisHostProfileIdentity? identity)
    {
        identity = null;
        if (value is null || value.Length > 160) return false;
        if (value.Length == 36 && value.StartsWith("hp0:", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out var unverified) &&
            unverified != Guid.Empty && value[4..] == unverified.ToString("N"))
        { identity = Unverified(unverified); return true; }
        if (!value.StartsWith("hp1:", StringComparison.Ordinal)) return false;
        string[] parts = value.Split(':');
        if (parts.Length != 4 || !Guid.TryParseExact(parts[1], "N", out var profile) || profile == Guid.Empty || parts[1] != profile.ToString("N") ||
            parts[3].Length != 64 || parts[3].Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) return false;
        AnalysisHostObservation? method = parts[2] switch
        { "windows-hardware-profile" => AnalysisHostObservation.WindowsHardwareProfile, "darwin-host-uuid" => AnalysisHostObservation.DarwinHostUuid, "linux-app-machine-id" => AnalysisHostObservation.LinuxApplicationMachineId, _ => null };
        if (method is null) return false;
        identity = new(profile, method.Value, parts[3]); return true;
    }
}

/// <summary>Ordinary-worker host metadata. Native identifiers are profile-pseudonymized and never returned or logged.</summary>
public static unsafe partial class AnalysisNativeHostProfile
{
    [StructLayout(LayoutKind.Sequential)] private struct Id128 { public ulong Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct Timespec { public long Seconds, Nanoseconds; }
    [LibraryImport("advapi32.dll", EntryPoint = "GetCurrentHwProfileW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool HardwareProfile(byte* information);
    [LibraryImport("libc", EntryPoint = "gethostuuid")] private static partial int HostUuid(byte* id, ref Timespec timeout);
    [LibraryImport("libsystemd.so.0", EntryPoint = "sd_id128_get_machine_app_specific")]
    private static partial int ApplicationMachineId(Id128 application, out Id128 id);

    public static AnalysisHostProfileIdentity Read(Guid profile)
    {
        if (profile == Guid.Empty) throw new ArgumentException("A generated private profile identity is required.", nameof(profile));
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        Span<byte> observation = stackalloc byte[16]; observation.Clear();
        try
        {
            AnalysisHostObservation method;
            if (OperatingSystem.IsWindows())
            {
                // HW_PROFILE_INFOW: DWORD, WCHAR[39], WCHAR[80], padded to a DWORD boundary.
                byte* information = stackalloc byte[244]; new Span<byte>(information, 244).Clear();
                if (!HardwareProfile(information)) return AnalysisHostProfileIdentity.Unverified(profile);
                var text = new ReadOnlySpan<char>((char*)(information + 4), 39); int end = text.IndexOf('\0');
                if (end < 0 || !Guid.TryParseExact(text[..end], "B", out var id) || id == Guid.Empty) return AnalysisHostProfileIdentity.Unverified(profile);
                id.TryWriteBytes(observation, bigEndian: true, out _); method = AnalysisHostObservation.WindowsHardwareProfile;
                new Span<byte>(information, 244).Clear();
            }
            else if (OperatingSystem.IsMacOS())
            {
                var timeout = new Timespec { Nanoseconds = 250_000_000 };
                fixed (byte* bytes = observation) if (HostUuid(bytes, ref timeout) != 0) return AnalysisHostProfileIdentity.Unverified(profile);
                method = AnalysisHostObservation.DarwinHostUuid;
            }
            else if (OperatingSystem.IsLinux())
            {
                // Fixed FileCat-specific ID; never export the system machine-id itself.
                ReadOnlySpan<byte> application = [0x46, 0x09, 0x50, 0x69, 0x3c, 0xfa, 0x4b, 0x95, 0xb3, 0x44, 0xfb, 0x4d, 0x8b, 0x08, 0x3c, 0x76];
                var app = new Id128 { Low = BinaryPrimitives.ReadUInt64LittleEndian(application), High = BinaryPrimitives.ReadUInt64LittleEndian(application[8..]) };
                if (!BitConverter.IsLittleEndian || ApplicationMachineId(app, out var id) != 0) return AnalysisHostProfileIdentity.Unverified(profile);
                BinaryPrimitives.WriteUInt64LittleEndian(observation, id.Low); BinaryPrimitives.WriteUInt64LittleEndian(observation[8..], id.High);
                method = AnalysisHostObservation.LinuxApplicationMachineId;
            }
            else return AnalysisHostProfileIdentity.Unverified(profile);
            if (observation.IndexOfAnyExcept((byte)0) < 0 || observation.IndexOfAnyExcept((byte)255) < 0) return AnalysisHostProfileIdentity.Unverified(profile);
            return AnalysisHostProfileIdentity.Create(profile, method, observation);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { return AnalysisHostProfileIdentity.Unverified(profile); }
        finally { CryptographicOperations.ZeroMemory(observation); }
    }
}
