using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    private const ulong ZfsSuperMagic = 0x2fc12fc1;
    private const string ZfsSpaceSource = "fstatfs on the held ZFS object";

    internal sealed record LinuxZfsMount(string? Source, string? CaseMode);
    internal sealed record LinuxZfsSpace(UInt128 Total, UInt128 Free, UInt128 Available);

    internal static LinuxZfsMount ParseLinuxZfsMount(string source, string superOptions)
    {
        // OpenZFS escapes spaces in the mounted dataset name before exporting it to procfs.
        string name = source.Replace(@"\040", " ", StringComparison.Ordinal);
        bool valid = name.Length is > 0 and < 256 &&
            name.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.' or ':' or '@' or ' ');
        string? mode = null;
        foreach (var option in superOptions.AsSpan().Split(','))
        {
            string? reported = superOptions.AsSpan()[option] switch
            {
                "casesensitive" => "Case sensitive",
                "caseinsensitive" => "Case insensitive",
                "casemixed" => "Mixed",
                _ => null,
            };
            if (reported is null) continue;
            if (mode is not null) return new(valid ? name : null, null);
            mode = reported;
        }
        return new(valid ? name : null, mode);
    }

    private static FileDetailsReport AddLinuxZfsDataset(FileDetailsReport report, int held, LinuxZfsMount mount, CancellationToken ct)
    {
        Span<byte> buffer = stackalloc byte[256];
        buffer.Clear();
        ct.ThrowIfCancellationRequested();
        int result;
        fixed (byte* data = buffer) result = DetailsFstatfs(held, data);
        int error = result == 0 ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        var space = result == 0 ? ParseLinuxZfsSpace(buffer) : ZfsSpaceUnavailable(LinuxMetadataFailure(error),
            $"The held dataset accounting query failed (errno {error}).");

        var section = new InspectionSection("Location · ZFS dataset",
        [
            ("Mounted source", mount.Source ?? "Unavailable"),
            ("Mounted case mode", mount.CaseMode ?? "Not inspected"),
            ("Filesystem total", space.Describe(value => ToBytes(value.Total))),
            ("Filesystem free", space.Describe(value => ToBytes(value.Free))),
            ("Available to this user", space.Describe(value => ToBytes(value.Available))),
            ("Accounting source", ZfsSpaceSource),
            ("Dataset compression policy", "Not inspected"),
            ("Dataset encryption and key status", "Not inspected"),
        ])
        {
            Lines = ["These counters describe filesystem space as seen from the held item. Dataset limits, pool space and applicable quotas can affect them."],
        };
        return report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };

        static string ToBytes(UInt128 bytes) => bytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes";
    }

    internal static ObservedFact<LinuxZfsSpace> ParseLinuxZfsSpace(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 80) return ZfsSpaceUnavailable(FactState.Failed, "The Linux x64 accounting result is incomplete.");
        if (BinaryPrimitives.ReadUInt64LittleEndian(buffer) != ZfsSuperMagic)
            return ZfsSpaceUnavailable(FactState.Changed, "The held filesystem type does not match the ZFS mount observation.");

        ulong blocks = BinaryPrimitives.ReadUInt64LittleEndian(buffer[16..]);
        ulong free = BinaryPrimitives.ReadUInt64LittleEndian(buffer[24..]);
        ulong available = BinaryPrimitives.ReadUInt64LittleEndian(buffer[32..]);
        ulong unit = BinaryPrimitives.ReadUInt64LittleEndian(buffer[72..]); // Linux x64 f_frsize
        if (unit is 0 or > (1UL << 30) || free > blocks || available > free)
            return ZfsSpaceUnavailable(FactState.Failed, "The native counters or allocation unit were outside the supported range.");

        return ObservedFact<LinuxZfsSpace>.Known(new((UInt128)blocks * unit, (UInt128)free * unit, (UInt128)available * unit),
            FactScope.Dataset, ZfsSpaceSource);
    }

    private static ObservedFact<LinuxZfsSpace> ZfsSpaceUnavailable(FactState state, string reason) =>
        ObservedFact<LinuxZfsSpace>.Unavailable(state, FactScope.Dataset, ZfsSpaceSource, reason);
}
