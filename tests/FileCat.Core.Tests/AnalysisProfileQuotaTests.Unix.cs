using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed unsafe partial class AnalysisProfileQuotaTests
{
    [Theory]
    [InlineData(4, 0x180, 0)]
    [InlineData(4, 0x180, 29)]
    [InlineData(5, 0x180, 0x4643)]
    [InlineData(4, 0x1b6, 0x4643)]
    [InlineData(4, 0x180, 0x4643)]
    public void UnixUnknownOrNonprivateKernelRecordsAreRefusedWithoutMutation(int count, int mode, short marker)
    {
        Ordinary(); Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix kernel IPC negative control.");
        Guid profile = Guid.NewGuid(); string domain = "FileCat.AnalysisQuota.v1." + Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(profile.ToString("N"))));
        int key = BinaryPrimitives.ReadInt32LittleEndian(SHA256.HashData(Encoding.ASCII.GetBytes(domain + ".uid=" + QuotaUid().ToString(System.Globalization.CultureInfo.InvariantCulture)))) & int.MaxValue;
        if (key == 0) key = 1;
        int id = QuotaSemGet(key, count, 0x200 | 0x400 | mode); Assert.True(id >= 0, "Owned negative-control IPC set was not created.");
        bool mac = OperatingSystem.IsMacOS(); int getValue = mac ? 5 : 12, getPid = mac ? 4 : 11;
        try
        {
            if (marker != 0)
            {
                ControlOperation* operations = stackalloc ControlOperation[2]; operations[0] = new(1, 7, 0x800); operations[1] = new(2, marker, 0x800);
                Assert.Equal(0, QuotaSemOp(id, operations, 2));
            }
            int beforePid = Control(id, 0, getPid);
            using var quota = AnalysisProfileQuota.Open(profile);
            Assert.ThrowsAny<IOException>(() => quota.TryReserve(1));
            Assert.Equal(marker == 0 ? 0 : 7, Control(id, 1, getValue)); Assert.Equal(marker, Control(id, 2, getValue));
            Assert.Equal(beforePid, Control(id, 0, getPid)); Assert.Equal(0, quota.ReservedBytes); Assert.Equal(0, quota.CapacityBytes);
        }
        finally { Assert.Equal(0, Control(id, 0, 0)); } // Only the exact IPC ID created above; never enumerate/remove other owners.
    }
    private static int Control(int id, int number, int command) => OperatingSystem.IsMacOS()
        ? RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? QuotaControlArm(id, number, command, 0, 0, 0, 0, 0, 0) : QuotaControlMac(id, number, command, 0)
        : QuotaControl(id, number, command, 0);
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private readonly struct ControlOperation(ushort number, short delta, short flags)
    { internal readonly ushort Number = number; internal readonly short Delta = delta, Flags = flags; }
    [LibraryImport("libc", EntryPoint = "geteuid")] private static partial uint QuotaUid();
    [LibraryImport("libc", EntryPoint = "semget", SetLastError = true)] private static partial int QuotaSemGet(int key, int count, int flags);
    [LibraryImport("libc", EntryPoint = "semop", SetLastError = true)] private static partial int QuotaSemOp(int id, ControlOperation* operations, nuint count);
    [LibraryImport("libc", EntryPoint = "semctl", SetLastError = true)] private static partial int QuotaControl(int id, int number, int command, nint argument);
    [LibraryImport("libc", EntryPoint = "semctl$UNIX2003", SetLastError = true)] private static partial int QuotaControlMac(int id, int number, int command, nint argument);
    [LibraryImport("libc", EntryPoint = "semctl", SetLastError = true)] private static partial int QuotaControlArm(int id, int number, int command, int unused3, int unused4, int unused5, int unused6, int unused7, nint argument);
}
