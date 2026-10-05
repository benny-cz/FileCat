using System.Diagnostics;

namespace FileCat.App.Tests;

public sealed class MacProcessCensusTests
{
    [Theory]
    [InlineData(0, 64, 0u, 2u, 1u, 0u, 0u, false)]
    [InlineData(0, 64, 0u, 2u, 0u, 0u, 0u, null)]
    [InlineData(0, 64, 0u, 2u, 1u, 501u, 0u, null)]
    [InlineData(0, 64, 0u, 2u, 1u, 0u, 1u, null)]
    [InlineData(0, 64, 0u, 1u, 1u, 0u, 0u, null)]
    [InlineData(7, 64, 7u, 5u, 0u, 501u, 1u, false)]
    [InlineData(7, 64, 7u, 1u, 0u, 501u, 1u, true)]
    [InlineData(7, 64, 7u, 2u, 0u, 501u, 1u, true)]
    [InlineData(7, 64, 7u, 2u, 1u, 0u, 1u, true)]
    [InlineData(7, 64, 7u, 3u, 0u, 501u, 1u, true)]
    [InlineData(7, 64, 7u, 4u, 0u, 501u, 1u, true)]
    [InlineData(7, 64, 7u, 6u, 0u, 501u, 1u, null)]
    [InlineData(7, 63, 7u, 5u, 0u, 501u, 1u, null)]
    [InlineData(7, 0, 0u, 0u, 0u, 0u, 0u, null)]
    [InlineData(7, -1, 0u, 0u, 0u, 0u, 0u, null)]
    [InlineData(7, 64, 8u, 5u, 0u, 501u, 1u, null)]
    [InlineData(-1, 64, 0u, 5u, 0u, 0u, 0u, null)]
    public void Only_complete_native_records_of_nonexecuting_tasks_can_leave_the_census(
        int requested, int bytes, uint pid, uint state, uint flags, uint uid, uint parent, bool? expected)
    {
        var info = new SingleInstance.MacProcessInfo { Pid = pid, Status = state, Flags = flags, UserId = uid, ParentPid = parent };
        Assert.Equal(expected, SingleInstance.MacProcessCanExecute(requested, bytes, info));
    }

    [Fact]
    public void The_Mac_kernel_is_excluded_while_a_live_process_keeps_its_identity_check()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Darwin's native process inventory requires macOS.");
        Assert.False(SingleInstance.MacProcessCanExecute(0));
        Assert.True(SingleInstance.MacProcessCanExecute(Environment.ProcessId));
        Assert.False(SingleInstance.OtherFileCatRunning(() => [Process.GetProcessById(0)]));
    }

    [Fact]
    public void An_unavailable_native_Mac_process_record_remains_unknown()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Darwin's native process inventory requires macOS.");
        Assert.Null(SingleInstance.MacProcessCanExecute(int.MaxValue));
    }
}
