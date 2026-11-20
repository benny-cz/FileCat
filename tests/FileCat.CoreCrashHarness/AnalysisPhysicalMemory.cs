using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Test instrumentation only. Physical footprint is deliberately reported
// separately from committed private bytes; it must not silently replace them.
internal sealed class AnalysisPhysicalMemory
{
    private readonly AnalysisPhysicalMemoryNative.Sample? _baseline;
    private ulong _peak, _kernelPeak, _currentPeak, _maximumReadSkew;
    private long _nonAtomicReads;
    private bool _failed;
    private string? _reason;
    internal AnalysisPhysicalMemory()
    {
        _baseline = AnalysisPhysicalMemoryNative.Read(out _reason);
        if (_baseline is { } sample) ObserveSample(sample);
    }
    internal void Observe()
    {
        if (_baseline is null) return;
        var sample = AnalysisPhysicalMemoryNative.Read(out var reason);
        if (sample is null) { _failed = true; _reason = reason; return; }
        ObserveSample(sample);
    }
    private void ObserveSample(AnalysisPhysicalMemoryNative.Sample sample)
    {
        _kernelPeak = Math.Max(_kernelPeak, sample.LifetimePeak);
        _currentPeak = Math.Max(_currentPeak, sample.Current);
        _peak = Math.Max(_peak, Math.Max(sample.Current, sample.LifetimePeak));
        // XNU gathers the lifetime peak before fill_task_rusage reads current
        // footprint. Another thread can grow the process between those reads.
        if (sample.Current > sample.LifetimePeak)
        { _nonAtomicReads++; _maximumReadSkew = Math.Max(_maximumReadSkew, sample.Current - sample.LifetimePeak); }
    }
    private bool WithinCeiling => _baseline is not null && !_failed && _peak - Math.Min(_peak, _baseline.Current) <= 512UL * 1024 * 1024;
    private AnalysisPhysicalMemory(AnalysisPhysicalMemoryNative.Sample baseline)
    { _baseline = baseline; ObserveSample(baseline); }
    internal static bool AccountingControl()
    {
        const ulong mib = 1024 * 1024;
        var meter = new AnalysisPhysicalMemory(new(100 * mib, 100 * mib));
        meter.ObserveSample(new(700 * mib, 600 * mib));
        if (meter.WithinCeiling || meter._failed) return false;
        meter.ObserveSample(new(120 * mib, 750 * mib));
        return !meter.WithinCeiling && meter._peak == 750 * mib && meter._nonAtomicReads == 1 && meter._maximumReadSkew == 100 * mib;
    }
    internal object Describe() => new
    {
        available = _baseline is not null,
        complete = _baseline is not null && !_failed,
        basis = "Maximum of macOS proc_pid_rusage current footprint samples and kernel lifetime peaks, which are gathered separately; distinct from committed private bytes",
        nativeLayoutControlSha256 = AnalysisPhysicalMemoryNative.LayoutControlSha256,
        baselineBytes = _baseline?.Current,
        baselineLifetimePeakBytes = _baseline?.LifetimePeak,
        lifetimePeakBytes = _baseline is null ? (ulong?)null : _kernelPeak,
        lifetimePeakIncrementBytes = _baseline is null ? (ulong?)null : _kernelPeak - Math.Min(_kernelPeak, _baseline.Current),
        sampledPeakCurrentBytes = _baseline is null ? (ulong?)null : _currentPeak,
        observedPeakBytes = _baseline is null ? (ulong?)null : _peak,
        observedPeakIncrementBytes = _baseline is null ? (ulong?)null : _peak - Math.Min(_peak, _baseline.Current),
        nonAtomicCounterPairs = _nonAtomicReads,
        maximumCurrentAboveEarlierPeakBytes = _maximumReadSkew,
        withinPhysicalReferenceCeiling = WithinCeiling,
        committedPrivateByteBudgetQualified = false,
        reason = _reason,
    };
}

internal static unsafe partial class AnalysisPhysicalMemoryNative
{
    internal sealed record Sample(ulong Current, ulong LifetimePeak);
    private sealed record Layout(string? Sha256, string? Error);
    private static readonly Lazy<Layout> NativeLayout = new(ReadLayout);
    private const int StructBytes = 296, CurrentOffset = 72, LifetimePeakOffset = 240;
    internal static string? LayoutControlSha256 => OperatingSystem.IsMacOS() ? NativeLayout.Value.Sha256 : null;
    internal static int NativeReads { get; private set; }

    private static Layout ReadLayout()
    {
        try
        {
            string? path = Environment.GetEnvironmentVariable("FILECAT_TEST_MAC_RUSAGE_CONTROL");
            if (string.IsNullOrEmpty(path)) return new(null, "A compiled native C layout/control receipt was not supplied.");
            using var stream = File.OpenRead(path);
            if (stream.Length is < 1 or > 16 * 1024) return new(null, "The native layout receipt exceeds its bound.");
            byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return new(null, "The native layout receipt changed during reading.");
            ReadOnlyMemory<byte> json = bytes;
            if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = bytes.AsMemory(3);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var value = doc.RootElement;
            if (!value.GetProperty("passed").GetBoolean() || value.GetProperty("structBytes").GetInt32() != StructBytes ||
                value.GetProperty("footprintOffset").GetInt32() != CurrentOffset || value.GetProperty("lifetimePeakOffset").GetInt32() != LifetimePeakOffset)
                return new(null, "The compiled native layout/control does not agree with this binding.");
            return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException or NotSupportedException)
        { return new(null, "The native layout receipt is unavailable or invalid: " + error.GetType().Name); }
    }

    internal static Sample? Read(out string? reason)
    {
        if (!OperatingSystem.IsMacOS()) { reason = "This additional physical-footprint counter is macOS-specific."; return null; }
        var layout = NativeLayout.Value;
        if (layout.Error is not null) { reason = layout.Error; return null; }
        if (!BitConverter.IsLittleEndian || IntPtr.Size != 8) { reason = "This native layout was qualified only for little-endian 64-bit macOS."; return null; }
        try
        {
            Span<byte> bytes = stackalloc byte[StructBytes]; bytes.Clear();
            int result;
            fixed (byte* buffer = bytes)
            {
                NativeReads++;
                result = GetUsage(Environment.ProcessId, 4, buffer);
            }
            if (result != 0) { reason = "Native process footprint is unavailable (error " + Marshal.GetLastPInvokeError() + ")."; return null; }
            ulong current = BinaryPrimitives.ReadUInt64LittleEndian(bytes[CurrentOffset..]);
            ulong peak = BinaryPrimitives.ReadUInt64LittleEndian(bytes[LifetimePeakOffset..]);
            if (current == 0 || peak == 0) { reason = $"The native process footprint counters are unavailable (current {current}, lifetime peak {peak})."; return null; }
            reason = null; return new(current, peak);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { reason = "The native process footprint API is unavailable: " + error.GetType().Name; return null; }
    }

    internal static int Control()
    {
        if (!AnalysisPhysicalMemory.AccountingControl()) throw new InvalidDataException("Non-atomic memory observations were not accounted conservatively.");
        using var process = Process.GetCurrentProcess();
        // Warm both metric paths before the measured allocation.
        _ = Read(out _); process.Refresh();
        var beforePhysical = Read(out var reason); process.Refresh(); long beforePrivate = process.PrivateMemorySize64;
        ulong? before = OperatingSystem.IsMacOS() ? beforePhysical?.Current : beforePrivate > 0 ? (ulong)beforePrivate : null;
        if (before is null)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { purpose = "native memory counter control", passed = false,
                reason, allocationAttempted = false, nativeReads = NativeReads })); return 31;
        }
        const nuint length = 64 * 1024 * 1024;
        nint memory = OperatingSystem.IsWindows() ? AllocateWindows(0, length, 0x3000, 4) :
            MapUnix(0, length, 3, OperatingSystem.IsMacOS() ? 0x1002 : 0x22, -1, 0);
        if (memory == 0 || memory == -1) throw new Win32Exception(Marshal.GetLastPInvokeError());
        Sample? touchedPhysical; long touchedPrivate;
        try
        {
            for (nuint at = 0; at < length; at += (nuint)Environment.SystemPageSize)
                System.Threading.Volatile.Write(ref ((byte*)memory)[at], (byte)0xa5);
            touchedPhysical = Read(out reason); process.Refresh(); touchedPrivate = process.PrivateMemorySize64;
        }
        finally
        {
            int released = OperatingSystem.IsWindows() ? ReleaseWindows(memory, 0, 0x8000) : UnmapUnix(memory, length) == 0 ? 1 : 0;
            if (released == 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        var releasedPhysical = Read(out var releaseReason); process.Refresh(); long releasedPrivate = process.PrivateMemorySize64;
        ulong? touched = OperatingSystem.IsMacOS() ? touchedPhysical?.Current : touchedPrivate > 0 ? (ulong)touchedPrivate : null;
        ulong? after = OperatingSystem.IsMacOS() ? releasedPhysical?.Current : releasedPrivate > 0 ? (ulong)releasedPrivate : null;
        bool passed = touched >= before + 56UL * 1024 * 1024 && after <= before + 12UL * 1024 * 1024;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            purpose = "64 MiB private OS mapping touch/release counter control; no disk-space workload qualification",
            counter = OperatingSystem.IsMacOS() ? "Native physical footprint; independently compiled C layout checked" : ".NET process private bytes",
            nativeLayoutControlSha256 = LayoutControlSha256, before, touched, released = after,
            touchedBytes = (ulong)length, kernelLifetimePeakBytes = touchedPhysical?.LifetimePeak,
            allocationAttempted = true, nativeReads = NativeReads, reason = OperatingSystem.IsMacOS() ? reason ?? releaseReason : null, passed,
            nonAtomicAccountingControlPassed = true,
        }));
        return passed ? 0 : 32;
    }

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint = "proc_pid_rusage", SetLastError = true)]
    private static partial int GetUsage(int pid, int flavor, byte* buffer);
    [LibraryImport("kernel32.dll", EntryPoint = "VirtualAlloc", SetLastError = true)]
    private static partial nint AllocateWindows(nint address, nuint size, uint allocation, uint protection);
    [LibraryImport("kernel32.dll", EntryPoint = "VirtualFree", SetLastError = true)]
    private static partial int ReleaseWindows(nint address, nuint size, uint freeType);
    [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
    private static partial nint MapUnix(nint address, nuint size, int protection, int flags, int descriptor, long offset);
    [LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
    private static partial int UnmapUnix(nint address, nuint size);
}
