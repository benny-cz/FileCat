using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class AnalysisWindowsTests
{
    [Theory]
    [InlineData(1u)] // R
    [InlineData(3u)] // RH
    [InlineData(5u)] // RW
    [InlineData(7u)] // RWH
    public async Task OplockDoesNotExcludeAttributeOnlyVersionMutation(uint level)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() || AnalysisAdmission.Refusal() is not null, "Needs ordinary Windows native fixtures.");
        using var fixture = new PrivateFixture(); string path = Path.Combine(fixture.Path, "chosen.bin"); File.WriteAllBytes(path, [1, 2]);
        using var parent = LocalIconFileLease.TryOpenDirectory(fixture.Path, LeaseLinks.Refuse, removable: true); Assert.NotNull(parent);
        using var request = new FixtureOplock(path, level);
        Task? mutation = null;
        try
        {
            var before = GetBasic(request.Guard); long changed = before.LastWriteTime - TimeSpan.TicksPerSecond * 10;
            mutation = Task.Run(() =>
            {
                using var attributes = OpenOrThrow(parent.LastHandle, "chosen.bin", WriteAttributes | Synchronize,
                    ShareAll, Open, NonDirectoryFile | OpenReparsePoint, "Open generated attribute mutator");
                SetBasic(attributes, new BasicInfo { LastWriteTime = changed });
            }, TestContext.Current.CancellationToken);
            // Attribute-only create and FileBasicInformation are not protected by these oplocks.
            // Success demonstrates a missing guard, never permission to expose an analyzer action.
            await mutation.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Equal(changed, GetBasic(request.Guard).LastWriteTime);
            Assert.False(request.Broken, "The attribute mutation broke this oplock; investigate its commit semantics.");
        }
        finally
        {
            request.Dispose();
            // Cancellation does not abandon the actual native mutator or its held parent.
            if (mutation is not null) await mutation.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        }
        Assert.True(File.Exists(path));
    }

    private sealed unsafe class FixtureOplock : IDisposable
    {
        private readonly EventWaitHandle _signal = new(false, EventResetMode.ManualReset);
        private SafeFileHandle? _guard;
        private OplockInput* _input;
        private OplockOutput* _output;
        private OplockOverlap* _overlap;
        private bool _pending, _disposed;
        public SafeFileHandle Guard => _guard!;
        public bool Broken => _signal.WaitOne(0);
        public FixtureOplock(string path, uint level)
        {
            try
            {
                _guard = OpenOplockFixture(@"\\?\" + path, ReadAttributes | Delete | Synchronize, ShareRead, 0, 3,
                    0x40000000 | OpenReparsePoint, 0); // OVERLAPPED; non-inheritable, fixture-local regular file
                Assert.False(_guard.IsInvalid, "Open overlapped metadata guard errno=" + Marshal.GetLastPInvokeError());
                Assert.Equal(12, sizeof(OplockInput)); Assert.Equal(24, sizeof(OplockOutput));
                _input = (OplockInput*)NativeMemory.AllocZeroed((nuint)sizeof(OplockInput));
                _output = (OplockOutput*)NativeMemory.AllocZeroed((nuint)sizeof(OplockOutput));
                _overlap = (OplockOverlap*)NativeMemory.AllocZeroed((nuint)sizeof(OplockOverlap));
                *_input = new OplockInput { Version = 1, Length = 12, Level = level, Flags = 1 };
                _overlap->Event = _signal.SafeWaitHandle.DangerousGetHandle();
                bool completed = RequestFixtureOplock(_guard, 0x90240, _input, 12, _output, 24, 0, _overlap);
                int error = Marshal.GetLastPInvokeError(); _pending = !completed && error == 997;
                Assert.True(_pending, "Oplock was not granted: completed=" + completed + ", errno=" + error);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (_pending)
            {
                _ = CancelFixtureOplock(Guard, _overlap);
                // Native buffers and event/guard ownership outlive actual completion, even after an assertion fails.
                _ = CompleteFixtureOplock(Guard, _overlap, out _, true);
            }
            _guard?.Dispose();
            NativeMemory.Free(_overlap); NativeMemory.Free(_output); NativeMemory.Free(_input); _signal.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct OplockInput { public ushort Version, Length; public uint Level, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct OplockOutput
    { public ushort Version, Length; public uint OriginalLevel, NewLevel, Flags, Access; public ushort Share; }
    [StructLayout(LayoutKind.Sequential)] private struct OplockOverlap
    { public nuint Internal, InternalHigh; public uint Offset, OffsetHigh; public nint Event; }
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle OpenOplockFixture(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool RequestFixtureOplock(SafeFileHandle handle, uint code, OplockInput* input, uint inputSize,
        OplockOutput* output, uint outputSize, nint returned, OplockOverlap* overlap);
    [LibraryImport("kernel32.dll", EntryPoint = "CancelIoEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool CancelFixtureOplock(SafeFileHandle handle, OplockOverlap* overlap);
    [LibraryImport("kernel32.dll", EntryPoint = "GetOverlappedResult", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool CompleteFixtureOplock(SafeFileHandle handle, OplockOverlap* overlap,
        out uint transferred, [MarshalAs(UnmanagedType.Bool)] bool wait);
}
