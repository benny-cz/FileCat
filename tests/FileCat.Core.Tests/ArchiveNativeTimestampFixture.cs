using System.Runtime.InteropServices;

namespace FileCat.Core.Tests;

internal static class ArchiveNativeTimestampFixture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec { internal long Seconds; internal long Nanoseconds; }
    [DllImport("libc", EntryPoint = "utimensat", SetLastError = true)]
    private static extern int Utimensat(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [In] TimeSpec[] times, int flags);

    internal static void SetModified(string absolutePath, long seconds = 1700000000, int nanos = 123456789)
    {
        Assert.True(Path.IsPathFullyQualified(absolutePath)); Assert.True(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS());
        var times = new[] { new TimeSpec { Seconds = seconds, Nanoseconds = nanos }, new TimeSpec { Seconds = seconds, Nanoseconds = nanos } };
        Assert.True(Utimensat(OperatingSystem.IsLinux() ? -100 : -2, absolutePath, times, 0) == 0, "utimensat failed: " + Marshal.GetLastPInvokeError());
    }
}
