using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

// A stack overflow cannot be caught. Each untrusted member is therefore opened in an owned child,
// on a measured-size thread stack, without risking the parent test runner or other tests.
public sealed class ArchiveDeepPathTests(ITestOutputHelper output)
{
    private const string ChildRoot = "FILECAT_DEEP_ARCHIVE_TEST_ROOT";
    private const int StackBytes = 192 * 1024;
    [Fact] public Task Deep_zip_file() => Check(true, false, 4096, nameof(Deep_zip_file));
    [Fact] public Task Deep_zip_directory() => Check(true, true, 4096, nameof(Deep_zip_directory));
    [Fact] public Task Deep_tar_file() => Check(false, false, 4096, nameof(Deep_tar_file));
    [Fact] public Task Deep_tar_directory() => Check(false, true, 4096, nameof(Deep_tar_directory));
    [Fact] public Task Shallow_zip() => Check(true, true, 3, nameof(Shallow_zip));
    [Fact] public Task Shallow_tar() => Check(false, true, 3, nameof(Shallow_tar));

    private async Task Check(bool zip, bool explicitDirectory, int depth, string method)
    {
        if (Environment.GetEnvironmentVariable(ChildRoot) is { } owned)
        {
            if (OperatingSystem.IsWindows()) SetErrorMode(0x0002 /* SEM_NOGPFAULTERRORBOX */);
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { Open(owned, zip, explicitDirectory, depth); }
                catch (Exception ex) { error = ex; }
            }, StackBytes) { IsBackground = true };
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Owned archive indexing thread did not finish.");
            Assert.Null(error);
            return;
        }

        string root = Directory.CreateTempSubdirectory("filecat-deep-archive-").FullName;
        string host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var start = new ProcessStartInfo(host)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string arg in new[] { typeof(ArchiveDeepPathTests).Assembly.Location, "-method",
                     typeof(ArchiveDeepPathTests).FullName + "." + method, "-noAutoReporters", "-noColor", "-noLogo" })
            start.ArgumentList.Add(arg);
        start.Environment[ChildRoot] = root;
        start.Environment["DOTNET_DbgEnableMiniDump"] = "0";
        using var child = Process.Start(start) ?? throw new IOException("The owned archive child did not start.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        bool finished = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await child.WaitForExitAsync(timeout.Token); finished = true; }
            catch (OperationCanceledException) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            string so = await stdout, se = await stderr;
            byte[] input = File.ReadAllBytes(Path.Combine(root, zip ? "owned.zip" : "owned.tar"));
            string? observation = File.Exists(Path.Combine(root, "result.json")) ? File.ReadAllText(Path.Combine(root, "result.json")) : null;
            output.WriteLine("ARCHIVE_DEPTH_CHILD " + JsonSerializer.Serialize(new
            {
                zip, explicitDirectory, depth, StackBytes, ChildPID = child.Id, Finished = finished,
                ExitCode = child.ExitCode, Stdout = so, Stderr = se,
                InputBase64 = Convert.ToBase64String(input), InputSHA256 = Convert.ToHexStringLower(SHA256.HashData(input)),
                Observation = observation, ActualCore = typeof(ZipProvider).Assembly.Location,
                ActualArchives = typeof(ArchiveProvider).Assembly.Location,
            }));
            Assert.True(finished, "Owned archive child exceeded its bound.");
            Assert.Equal(0, child.ExitCode);
            Assert.NotNull(observation);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            string allowed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(allowed, Path.GetFullPath(root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
            output.WriteLine($"ARCHIVE_DEPTH_RESTORED Root={root}; ChildExited={child.HasExited}; RootAbsent=True");
        }
    }

    private static void Open(string root, bool zip, bool explicitDirectory, int depth)
    {
        string folder = string.Join('/', Enumerable.Repeat("d", depth));
        string member = folder + "/payload.bin";
        byte[] expected = Enumerable.Range(0, 4096).Select(i => (byte)(i * 29 + 17)).ToArray();
        string path = Path.Combine(root, zip ? "owned.zip" : "owned.tar");
        using (var file = File.Create(path))
        {
            if (zip)
            {
                using var writer = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
                if (explicitDirectory) writer.CreateEntry(folder + "/");
                using var stream = writer.CreateEntry(member, CompressionLevel.NoCompression).Open();
                stream.Write(expected);
            }
            else
            {
                using var writer = new TarWriter(file, TarEntryFormat.Pax, leaveOpen: true);
                if (explicitDirectory) writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, folder));
                using var bytes = new MemoryStream(expected);
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, member) { DataStream = bytes });
            }
        }
        var registry = new ProviderRegistry(); registry.Register(new LocalFileSystemProvider());
        var identity = new IdentityAwareTestFiles();
        var zp = new ZipProvider(root); var ap = new ArchiveProvider(root, registry);
        zp.GetSourceIdentity = ap.GetSourceIdentity = identity.GetFileIdentity;
        zp.GetOpenedSourceIdentity = ap.GetOpenedSourceIdentity = identity.GetHandleIdentity;
        ResourceProvider provider = zip ? zp : ap;
        Location location = zip ? ZipProvider.ForFile(path) : ArchiveProvider.ForFile(path);
        try
        {
            var top = new Sink(); provider.EnumerateAsync(location, top, CancellationToken.None).GetAwaiter().GetResult();
            var leaf = new Sink(); provider.EnumerateAsync(location.WithPath(folder), leaf, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Empty(top.Warnings); Assert.Empty(leaf.Warnings);
            Assert.Collection(top.Entries, entry => { Assert.Equal("d", entry.Name); Assert.Equal(EntryKind.Directory, entry.Kind); });
            Assert.Collection(leaf.Entries, entry => { Assert.Equal("payload.bin", entry.Name); Assert.Equal(EntryKind.File, entry.Kind); });
            using var content = provider.OpenContent(new ItemRef(location.WithPath(folder), "payload.bin", EntryKind.File))!;
            byte[] actual = new byte[expected.Length + 1]; int count = 0;
            while (count < actual.Length) { int n = content.Read(count, actual.AsSpan(count)); if (n == 0) break; count += n; }
            Assert.Equal(expected, actual[..count]);
            File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                zip, explicitDirectory, depth, StackBytes, Member = member, RootEntries = top.Entries.Count,
                LeafEntries = leaf.Entries.Count, FullReadBytes = count, ActualBase64 = Convert.ToBase64String(actual[..count]),
                ExpectedBase64 = Convert.ToBase64String(expected), NoWarnings = true,
            }));
        }
        finally { zp.Release(path); ap.Release(path); }
    }
    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public List<string> Warnings { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Entries.Add(entry); }
        public void ReportIssue(string message) => Warnings.Add(message);
    }
}
