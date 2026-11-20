using Avalonia.Headless.XUnit;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("restore")]
    [InlineData("observe")]
    [InlineData("reader")]
    [InlineData("wrong-source")]
    [InlineData("wrong-directory")]
    [InlineData("readonly")]
    public async Task NativePrivateReplacementAtomicallySwitchesTheNamedFileAndRetainsThePreviousCopy(string mode)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(profile.Root), _ =>
        {
            AnalysisPrivateDirectory Open(string child) => OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(profile.Root, child) : new UnixAnalysisPrivateDirectory(profile.Root, child);
            using var directory = Open("native-replacement"); using var otherDirectory = Open("native-replacement-other");
            Guid operation = Guid.NewGuid(); string incoming = AnalysisPrivateNames.ReplacementIncoming(Guid.NewGuid(), operation);
            string previous = AnalysisPrivateNames.ReplacementPrevious(Guid.NewGuid(), operation);
            Write(directory, "named.fcdsa", "previous bytes"u8.ToArray()); Write(directory, "other.fcdsa", "other bytes"u8.ToArray());
            Write(directory, incoming, "incoming bytes"u8.ToArray()); Write(directory, previous, "previous bytes"u8.ToArray());
            Write(otherDirectory, "named.fcdsa", "foreign bytes"u8.ToArray());
            string root = Path.Combine(profile.Root, "native-replacement"), path = Path.Combine(root, "named.fcdsa");
            if (mode == "readonly")
            {
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try
                {
                    var refusal = Record.Exception(() => { using var unexpected = directory.OpenReplacementFile("named.fcdsa"); });
                    Assert.True(refusal is IOException or UnauthorizedAccessException, refusal?.ToString() ?? "The read-only file was accepted.");
                    Assert.Equal("previous bytes"u8.ToArray(), File.ReadAllBytes(path));
                }
                finally { File.SetAttributes(path, FileAttributes.Normal); }
                return true;
            }
            if (mode == "reader")
            {
                using var reader = directory.OpenRead("named.fcdsa"); Assert.ThrowsAny<IOException>(() => directory.OpenReplacementFile("named.fcdsa"));
                Assert.Equal("previous bytes"u8.ToArray(), File.ReadAllBytes(path)); return true;
            }
            using var target = directory.OpenReplacementFile("named.fcdsa");
            Assert.Throws<ArgumentException>(() => directory.OpenReplacementFile(AnalysisPrivateNames.PrivateState));
            if (mode == "wrong-source")
            {
                using var source = directory.OpenReplacementFile("other.fcdsa"); Assert.Throws<ArgumentException>(() => source.ReplaceOwned(target));
                target.Dispose(); Assert.Equal("previous bytes"u8.ToArray(), File.ReadAllBytes(path)); return true;
            }
            using var fresh = directory.OpenReplacementFile(incoming);
            if (mode == "wrong-directory")
            {
                using var foreign = otherDirectory.OpenReplacementFile("named.fcdsa"); Assert.Throws<ArgumentException>(() => fresh.ReplaceOwned(foreign));
                foreign.Dispose(); target.Dispose();
                Assert.Equal("previous bytes"u8.ToArray(), File.ReadAllBytes(path)); Assert.Equal("foreign bytes"u8.ToArray(), File.ReadAllBytes(Path.Combine(profile.Root, "native-replacement-other", "named.fcdsa")));
                return true;
            }
            if (mode == "observe")
            {
                using var stop = new CancellationTokenSource();
                var before = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var after = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // Deliberately models an external namespace observer, rather than a cooperating
                // FileCat reader (the latter must hold a shared lease and refuse replacement).
                var observer = Task.Run(() =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        using var input = OpenReplacementObserver(path);
                        using var bytes = new MemoryStream(); input.CopyTo(bytes); byte[] value = bytes.ToArray();
                        if (value.AsSpan().SequenceEqual("previous bytes"u8)) before.TrySetResult();
                        else if (value.AsSpan().SequenceEqual("incoming bytes"u8)) after.TrySetResult();
                        else throw new InvalidDataException("The named replacement exposed incomplete or mixed bytes.");
                    }
                });
                try
                {
                    Assert.True(before.Task.Wait(TimeSpan.FromSeconds(5)), observer.Exception?.ToString());
                    fresh.ReplaceOwned(target);
                    Assert.True(after.Task.Wait(TimeSpan.FromSeconds(5)), observer.Exception?.ToString());
                }
                finally { stop.Cancel(); observer.GetAwaiter().GetResult(); }
            }
            else fresh.ReplaceOwned(target);
            Assert.True(target.RemovalRequested); Assert.Equal("named.fcdsa", fresh.MaintenanceName);
            Assert.Equal("previous bytes"u8.ToArray(), Bytes(target.Stream)); Assert.Equal("incoming bytes"u8.ToArray(), Bytes(fresh.Stream));
            if (mode == "restore")
            {
                using var backup = directory.OpenReplacementFile(previous); backup.ReplaceOwned(fresh); Assert.True(fresh.RemovalRequested); backup.Dispose();
            }
            fresh.Dispose(); target.Dispose();
            Assert.Equal(mode == "restore" ? "previous bytes"u8.ToArray() : "incoming bytes"u8.ToArray(), File.ReadAllBytes(path));
            Assert.False(File.Exists(Path.Combine(root, incoming))); Assert.Equal("other bytes"u8.ToArray(), File.ReadAllBytes(Path.Combine(root, "other.fcdsa")));
            if (mode != "restore") Assert.Equal("previous bytes"u8.ToArray(), File.ReadAllBytes(Path.Combine(root, previous)));
            return true;

            static void Write(AnalysisPrivateDirectory directory, string name, byte[] bytes)
            { using var stage = directory.CreateStaging(); stage.Stream.Write(bytes); stage.Flush(); stage.PublishNew(name); }
            static byte[] Bytes(Stream stream) { using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray(); }
        }, token);
    }
    private static Stream OpenReplacementObserver(string path)
    {
        if (OperatingSystem.IsWindows()) return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // FileStream's Unix path constructor takes a flock lease. An already-open raw
        // descriptor intentionally observes atomic namespace visibility without that lease.
        int flags = OperatingSystem.IsMacOS() ? 0x100 | 0x1000000 : 0x20000 | 0x80000; // O_RDONLY | O_NOFOLLOW | O_CLOEXEC
        int descriptor = ReplacementObserverOpen(path, flags);
        if (descriptor < 0) throw new IOException("The external replacement observer could not open the named file (errno " + Marshal.GetLastPInvokeError() + ").");
        var handle = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try { return new FileStream(handle, FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int ReplacementObserverOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}
