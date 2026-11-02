using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Verification;

namespace FileCat.Core.Tests;

/// <summary>Invalid Unix filename bytes must never be rebound to a different Unicode path.</summary>
public sealed class UnixAmbiguousFilenameTests
{
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(byte[] path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "unlink", SetLastError = true)]
    private static extern int Unlink(byte[] path);

    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Entries = [];
        public void AddBatch(ReadOnlySpan<EntryData> batch)
        {
            foreach (var entry in batch) Entries.Add(entry);
        }
        public void ReportIssue(string message) { }
    }

    [Fact]
    public void Linux_listing_refuses_names_that_alias_a_different_valid_file()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var temp = new TempDir();
        string folder = temp.Dir("source");
        string valid = Path.Combine(folder, "odd_\uFFFD.txt");
        byte[] prefix = Encoding.UTF8.GetBytes(folder + "/");
        byte[][] raw = [RawName(0xFF), RawName(0xFE)];
        try
        {
            foreach (byte[] path in raw)
            {
                int fd = Open(path, 0xC1 /* O_CREAT|O_EXCL|O_WRONLY */, 0x180 /* 0600 */);
                Assert.True(fd >= 0, $"open failed: errno {Marshal.GetLastPInvokeError()}");
                Assert.Equal(0, Close(fd));
            }
            File.WriteAllText(valid, "valid replacement file");

            // The runtime maps all three raw names to one managed spelling. That path opens
            // the valid file even for entries representing either invalid-byte name.
            string[] managed = Directory.EnumerateFiles(folder).ToArray();
            Assert.Equal(3, managed.Length);
            Assert.All(managed, path => Assert.Equal(valid, path));
            Assert.All(managed, path => Assert.Equal("valid replacement file", File.ReadAllText(path)));

            var sink = new Sink();
            var provider = new LocalFileSystemProvider();
            var refusal = Assert.Throws<AmbiguousUnixNameException>(() =>
                provider.EnumerateAsync(Location.FileSystem(folder), sink, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains("U+FFFD", refusal.Message);
            Assert.Empty(sink.Entries);
            Assert.Null(new PortableFileOperations().TryGetInfo(valid));
            Assert.Null(UnixFiles.Stat(valid));
            Assert.Null(UnixFiles.RealPath(valid));
            Assert.Throws<AmbiguousUnixNameException>(() => UnixFiles.OpenRegularFileForRead(valid));
            Assert.Throws<AmbiguousUnixNameException>(() => PortableFileOperations.FromInfo(new FileInfo(valid)));
            Assert.Null(FolderContents.Read(folder, CancellationToken.None));
            Assert.Throws<AmbiguousUnixNameException>(() => VerificationFileReader.OpenRead(valid));
            Assert.NotNull(PathUtil.ValidateNewName("another_\uFFFD.txt"));

            var files = new UnixFileOperations();
            string target = Path.Combine(temp.Path, "target.txt");
            Assert.Throws<AmbiguousUnixNameException>(() => files.CopyFile(valid, target, new FileCopyOptions(), null, CancellationToken.None));
            Assert.Throws<AmbiguousUnixNameException>(() => files.Move(valid, target, replaceExisting: false));
            Assert.Throws<AmbiguousUnixNameException>(() => files.DeleteFile(valid));
            Assert.False(File.Exists(target));
            Assert.Equal("valid replacement file", File.ReadAllText(valid));

            var results = new ResultSet("ambiguous", "source", "local");
            var session = new SearchSession(new SearchQuery { Roots = [folder], Recursive = false }, results);
            session.Run(CancellationToken.None);
            Assert.Empty(results.Snapshot());
            Assert.False(results.IsComplete);
            Assert.Contains(session.Log, entry => entry.Kind == SearchLogKind.Warning && entry.Detail!.Contains("U+FFFD"));
        }
        finally
        {
            foreach (byte[] path in raw) Assert.Equal(0, Unlink(path));
            File.Delete(valid);
        }

        byte[] RawName(byte invalid)
        {
            byte[] name = [.. Encoding.ASCII.GetBytes("odd_"), invalid, .. Encoding.ASCII.GetBytes(".txt"), 0];
            return [.. prefix, .. name];
        }
    }
}
