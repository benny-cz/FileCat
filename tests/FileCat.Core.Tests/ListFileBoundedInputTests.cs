using System.Diagnostics;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class ListFileBoundedInputTests
{
    [Fact]
    public void A_path_line_beyond_the_native_path_budget_is_refused_before_materialization()
    {
        string root = Directory.CreateTempSubdirectory("filecat-list-budget-").FullName;
        try
        {
            string path = Path.Combine(root, "oversized.lst");
            File.WriteAllText(path, new string('x', 32769));
            var set = new ResultSet("list-budget", "List budget", "test");
            Assert.Throws<IOException>(() => ListFile.Load(path, set, TestContext.Current.CancellationToken));
            Assert.Equal(0, set.Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Unix_endless_character_device_line_is_refused_promptly()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The special-file control requires /dev/zero on Unix.");
        var source = new FileInfo("/dev/zero");
        Assert.True(source.Exists);
        Assert.Equal(0, source.Length);
        var set = new ResultSet("list-device-budget", "Device budget", "test");
        var clock = Stopwatch.StartNew();
        Assert.Throws<IOException>(() => ListFile.Load(source.FullName, set, TestContext.Current.CancellationToken));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "An endless device line exceeded the finite input deadline.");
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void CrLf_split_across_read_buffers_does_not_create_an_extra_path()
    {
        string root = Directory.CreateTempSubdirectory("filecat-list-crlf-").FullName;
        try
        {
            string member = Path.Combine(root, "member.txt");
            File.WriteAllText(member, "owned");
            string list = Path.Combine(root, "paths.lst");
            File.WriteAllText(list, new string(' ', 4095) + "\r\nmember.txt\r\n");
            var set = new ResultSet("list-crlf", "CRLF", "test");
            var result = ListFile.Load(list, set, TestContext.Current.CancellationToken);
            Assert.Equal((1, 0, 0, false), (result.Added, result.Missing, result.Network, result.Truncated));
            Assert.Equal(member, Assert.Single(set.Snapshot()).Item.FileSystemPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
