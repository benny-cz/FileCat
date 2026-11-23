using System.Buffers.Binary;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisAdmissionTests
{
    private const string Caps = "CapInh:\t0000000000000000\nCapPrm:\t0000000000000000\nCapEff:\t0000000000000000\nCapAmb:\t0000000000000000\n";
    [Theory]
    [InlineData("1000 1000 1000 1000", "1000 1000 1000 1000", true)]
    [InlineData("1000 1000 0 1000", "1000 1000 1000 1000", false)]
    [InlineData("1000 1000 1000 0", "1000 1000 1000 1000", false)]
    [InlineData("1000 1000 1000 1000", "1000 1000 1000 0", false)]
    [InlineData("1000 1001 1001 1001", "1000 1000 1000 1000", false)]
    [InlineData("1000 1000 1000", "1000 1000 1000 1000", false)]
    public void LinuxChecksAllFourThreadIdentityColumns(string uid, string gid, bool expected) =>
        Assert.Equal(expected, AnalysisAdmission.OrdinaryLinuxStatus("Uid:\t" + uid + "\nGid:\t" + gid + "\n" + Caps));

    [Fact]
    public void LinuxCannotIgnoreDuplicateIdentityOrRetainedCapabilities()
    {
        string status = "Uid: 1000 1000 1000 1000\nGid: 1000 1000 1000 1000\n" + Caps;
        Assert.False(AnalysisAdmission.OrdinaryLinuxStatus(status + "Uid: 1000 1000 1000 0\n"));
        Assert.False(AnalysisAdmission.OrdinaryLinuxStatus(status.Replace("CapPrm:\t0000000000000000", "CapPrm:\t0000000000000001")));
        // An Ubuntu 26.04 desktop session's inheritable CAP_WAKE_ALARM is not retained authority.
        Assert.True(AnalysisAdmission.OrdinaryLinuxStatus(status.Replace("CapInh:\t0000000000000000", "CapInh:\t0000000800000000")));
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData(2u, true)]
    [InlineData(4u, false)]
    public void WindowsDangerousPrivilegesAreRejectedEvenWhenDisabled(uint attributes, bool refused)
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), attributes);
        Assert.Equal(refused, AnalysisAdmission.HasRetainedPrivileges(bytes, new HashSet<ulong> { 42 }));
        Assert.False(AnalysisAdmission.HasRetainedPrivileges(bytes, new HashSet<ulong> { 43 }));
    }

    [Fact]
    public void MalformedPrivilegeBuffersFailClosed()
    {
        Assert.True(AnalysisAdmission.HasRetainedPrivileges([], new HashSet<ulong>()));
        byte[] bytes = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.MaxValue);
        Assert.True(AnalysisAdmission.HasRetainedPrivileges(bytes, new HashSet<ulong>()));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 2);
        Assert.True(AnalysisAdmission.HasRetainedPrivileges(bytes, new HashSet<ulong>()));
    }
}
