using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private sealed class NativeNamesFixture : IAsyncDisposable
    {
        internal sealed record FileRecord(byte[] Name, byte[] Payload, bool Alias = false);
        private readonly DeviceIoScheduler _io = new();
        private readonly string _parent = NativeValidationFixtureRoot();
        internal string Root { get; }
        internal string SourcePath => Path.Combine(Root, "source");
        internal string PrivatePath => Path.Combine(Root, "private");
        internal string EncodingName => OperatingSystem.IsWindows() ? "utf16le" : "unix-bytes";
        internal readonly List<FileRecord> Files = [];
        internal readonly List<int> UnrepresentableUnixErrors = [];
        internal bool CaseDistinct;
        internal AnalysisDatasetLease? Lease;
        internal AnalysisSnapshot? Memory;
        private NativeNamesFixture() => Root = Path.Combine(_parent, "native-names-" + Guid.NewGuid().ToString("N"));

        internal static async Task<NativeNamesFixture> CreateAsync(bool indexed)
        {
            var fixture = new NativeNamesFixture(); bool completed = false;
            Directory.CreateDirectory(fixture.SourcePath);
            try
            {
                foreach (string name in new[] { "plain.bin", "ordinary.txt", "bidi\u202E.bin", "é-one.bin", "e\u0301-two.bin", "emoji-😀.bin", "=formula.bin", "trailing. " })
                    fixture.Create(AnalysisRawNames.Encode(fixture.EncodingName, name));
                fixture.Create(AnalysisRawNames.Encode(fixture.EncodingName, "case.bin"));
                fixture.CaseDistinct = fixture.TryCreate(AnalysisRawNames.Encode(fixture.EncodingName, "CASE.bin"), collisionAllowed: true);
                if (OperatingSystem.IsWindows())
                    foreach (string name in new[] { "NUL", "high-\uD800.bin", "low-\uDC00.bin", "bmp-\uFFFF.bin" }) fixture.Create(AnalysisRawNames.Encode("utf16le", name));
                else
                {
                    fixture.Create(Encoding.UTF8.GetBytes("back\\slash.bin"));
                    foreach (byte invalid in new byte[] { 0xff, 0xfe })
                    {
                        byte[] raw = [.. "odd_"u8.ToArray(), invalid, .. ".bin"u8.ToArray()];
                        fixture.TryCreate(raw, collisionAllowed: false, unrepresentableMacAllowed: true);
                    }
                    // A distinct real U+FFFD name must never be substituted for either raw-byte entry.
                    fixture.Create(Encoding.UTF8.GetBytes("odd_\uFFFD.bin"));
                }
                byte[] alias = AnalysisRawNames.Encode(fixture.EncodingName, "plain-alias.bin");
                CreateGroupedNativeAlias(Path.Combine(fixture.SourcePath, "plain.bin"), Path.Combine(fixture.SourcePath, "plain-alias.bin"));
                fixture.Files.Add(new(alias, fixture.Files[0].Payload, Alias: true));
                fixture.AssertPayloads();
                Console.WriteLine($"Native name creation: platform={(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "Mac" : "Ubuntu")}; files={fixture.Files.Count}; caseDistinct={fixture.CaseDistinct}; invalidUnixCreationErrors={string.Join(',', fixture.UnrepresentableUnixErrors)}");

                string device = PathUtil.GetDeviceKey(fixture.Root); AnalysisPrivateDirectory? storage = null; IAnalysisBackend? backend = null; AnalysisSession? session = null;
                try
                {
                    if (indexed) storage = await AnalysisIo.Run(fixture._io, device, _ => OperatingSystem.IsWindows() ? (AnalysisPrivateDirectory)new WindowsAnalysisPrivateDirectory(fixture.Root, "private") : new UnixAnalysisPrivateDirectory(fixture.Root, "private"), TestContext.Current.CancellationToken);
                    backend = await AnalysisIo.Run(fixture._io, device, _ => OperatingSystem.IsWindows() ? (IAnalysisBackend)new WindowsAnalysisBackend(fixture.SourcePath) : new UnixAnalysisBackend(fixture.SourcePath), TestContext.Current.CancellationToken);
                    var options = indexed ? new AnalysisStorageOptions(device, () => Interlocked.Exchange(ref storage, null) ?? throw new InvalidOperationException("Storage owner transferred."),
                        [new(device, () => OperatingSystem.IsWindows() ? new WindowsAnalysisBackend(fixture.PrivatePath) : new UnixAnalysisBackend(fixture.PrivatePath))], PreviewPaths: 2) : null;
                    session = new AnalysisCoordinator(fixture._io, "native-name-fixture").Start([backend], storage: options); backend = null;
                    await session.Completion.WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
                    Assert.Equal(AnalysisPhase.Finished, session.Snapshot().Phase); Assert.Equal(0UL, session.SourceCloseFailures);
                    if (indexed) { fixture.Lease = await session.AcquireDatasetAsync(TestContext.Current.CancellationToken); Assert.NotNull(fixture.Lease); }
                    else fixture.Memory = session.Snapshot();
                }
                finally
                {
                    if (session is not null) { session.Dispose(); await session.DatasetCloseCompletion; }
                    if (backend is not null) await AnalysisIo.ReleaseAsync(fixture._io, backend);
                    if (storage is not null) await AnalysisIo.Run(fixture._io, device, _ => { storage.Dispose(); return true; }, TestContext.Current.CancellationToken);
                }
                completed = true; return fixture;
            }
            finally { if (!completed) await fixture.DisposeAsync(); }
        }

        private string WindowsPath(byte[] raw) => "\\\\?\\" + Path.Combine(SourcePath, AnalysisRawNames.Decode("utf16le", raw));
        private byte[] UnixPath(byte[] raw) => [.. Encoding.UTF8.GetBytes(SourcePath + "/"), .. raw, 0];
        private void Create(byte[] raw) => Assert.True(TryCreate(raw, collisionAllowed: false));
        private bool TryCreate(byte[] raw, bool collisionAllowed, bool unrepresentableMacAllowed = false)
        {
            byte[] payload = [0x41, (byte)Files.Count, 0, 0xff];
            if (OperatingSystem.IsWindows())
            {
                try { using var stream = new FileStream(WindowsPath(raw), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete); stream.Write(payload); stream.Flush(flushToDisk: true); }
                catch (IOException error) when (collisionAllowed && (error.HResult & 0xffff) is 80 or 183) { return false; }
            }
            else
            {
                int fd;
                if (OperatingSystem.IsMacOS())
                {
                    // Darwin ARM64 passes open's variadic mode argument on the
                    // stack. Use fixed-arity creat for these fresh owned fixtures.
                    // Probe the deliberate case-collision before any truncation.
                    if (collisionAllowed)
                    {
                        int existing = OpenNativeName(UnixPath(raw), 0x1000000, 0);
                        if (existing >= 0) { using var found = new SafeFileHandle((nint)existing, ownsHandle: true); return false; }
                        int error = Marshal.GetLastPInvokeError(); if (error != 2) throw new IOException("Native case probe failed: " + error);
                    }
                    fd = CreateNativeNameMac(UnixPath(raw), 0x180);
                }
                else fd = OpenNativeName(UnixPath(raw), 2 | 0x40 | 0x80 | 0x80000, 0x180);
                if (fd < 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (collisionAllowed && error == 17) return false;
                    if (unrepresentableMacAllowed && OperatingSystem.IsMacOS() && error is 22 or 92)
                    { UnrepresentableUnixErrors.Add(error); return false; }
                    throw new IOException("Native filename creation failed: " + error);
                }
                using var handle = new SafeFileHandle((nint)fd, ownsHandle: true);
                using var stream = new FileStream(handle, OperatingSystem.IsMacOS() ? FileAccess.Write : FileAccess.ReadWrite); stream.Write(payload); stream.Flush(flushToDisk: true);
            }
            Files.Add(new(raw, payload)); return true;
        }
        internal void AssertPayloads()
        {
            foreach (var file in Files)
            {
                if (OperatingSystem.IsWindows()) Assert.Equal(file.Payload, File.ReadAllBytes(WindowsPath(file.Name)));
                else
                {
                    int fd = OpenNativeName(UnixPath(file.Name), OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000, 0);
                    Assert.True(fd >= 0, "Native fixture read failed: " + Marshal.GetLastPInvokeError());
                    using var handle = new SafeFileHandle((nint)fd, ownsHandle: true); using var stream = new FileStream(handle, FileAccess.Read);
                    byte[] bytes = new byte[file.Payload.Length]; stream.ReadExactly(bytes); Assert.Equal(-1, stream.ReadByte()); Assert.Equal(file.Payload, bytes);
                }
            }
        }
        internal async Task ReleaseLeaseAsync() { if (Lease is { } lease) { Lease = null; await lease.DisposeAsync(); } }
        public async ValueTask DisposeAsync()
        {
            await ReleaseLeaseAsync(); _io.Dispose();
            string absolute = Path.GetFullPath(Root), parent = Path.GetFullPath(_parent) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(parent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || !Path.GetFileName(absolute).StartsWith("native-names-", StringComparison.Ordinal)) throw new IOException("Native fixture cleanup escaped its owned directory.");
            foreach (var file in Files)
            {
                if (OperatingSystem.IsWindows()) File.Delete(WindowsPath(file.Name));
                else Assert.Equal(0, UnlinkNativeName(UnixPath(file.Name)));
            }
            if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);
        }
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int OpenNativeName(byte[] path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "creat", SetLastError = true)] private static extern int CreateNativeNameMac(byte[] path, uint mode);
    [DllImport("libc", EntryPoint = "unlink", SetLastError = true)] private static extern int UnlinkNativeName(byte[] path);
}
