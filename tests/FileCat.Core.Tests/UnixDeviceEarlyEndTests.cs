using System.Text.Json;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

public sealed class UnixDeviceEarlyEndTests : IDisposable
{
    private readonly TempDir _dir = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_held_source_that_shrinks_during_a_read_fails_instead_of_returning_a_partial_count(bool wholeSectors)
    {
        using var source = Open(wholeSectors, out string path);
        Truncate(path);
        var bytes = new byte[1536];
        Exception? failure = Record.Exception(() => source.Read(0, bytes));
        TestContext.Current.TestOutputHelper?.WriteLine("UNIX_EARLY_END " + JsonSerializer.Serialize(new
        {
            wholeSectors, source.Length, Offset = 0, Requested = bytes.Length, Error = failure?.GetType().Name,
        }));
        Assert.IsType<IOException>(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_read_starting_at_the_new_end_fails_before_the_reported_end(bool wholeSectors)
    {
        using var source = Open(wholeSectors, out string path);
        Truncate(path);
        var bytes = new byte[512];
        Exception? failure = Record.Exception(() => source.Read(1024, bytes));
        TestContext.Current.TestOutputHelper?.WriteLine("UNIX_EARLY_END " + JsonSerializer.Serialize(new
        {
            wholeSectors, source.Length, Offset = 1024, Requested = bytes.Length, Error = failure?.GetType().Name,
        }));
        Assert.IsType<IOException>(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stable_source_keeps_exact_bytes_and_normal_end_of_source_zero(bool wholeSectors)
    {
        using var source = Open(wholeSectors, out _);
        var bytes = new byte[1536];
        Assert.Equal(bytes.Length, source.Read(0, bytes));
        Assert.All(bytes, value => Assert.Equal((byte)0xA5, value));
        var tail = new byte[1024];
        Assert.Equal(512, source.Read(1536, tail));
        Assert.All(tail[..512], value => Assert.Equal((byte)0xA5, value));
        Assert.Equal(0, source.Read(2048, tail));
    }

    private UnixDeviceSource Open(bool wholeSectors, out string path)
    {
        path = Path.Combine(_dir.Path, wholeSectors ? "whole.img" : "direct.img");
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0xA5, 2048).ToArray());
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return UnixDeviceSource.From(handle, path, "owned image", wholeSectors, sectorSize: 512);
    }

    private static void Truncate(string path)
    {
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.SetLength(1024);
    }

    public void Dispose() => _dir.Dispose();
}
