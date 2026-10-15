using System.Text.Json;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.Platform.Windows.Tests;

public sealed class DirectDeviceShortReadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-direct-short-read", Guid.NewGuid().ToString("N"));

    public DirectDeviceShortReadTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_read_that_reaches_an_unserved_range_fails_instead_of_returning_a_short_count()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The native direct device reader is for Windows.");
        using var file = CreateFile();
        using var source = new DirectDeviceSource(file.SafeFileHandle, "owned changing file", 2048, 512);
        file.SetLength(1024);
        file.Flush(true);
        var bytes = new byte[1536];
        int returned = -1;
        var failure = Record.Exception(() => returned = source.Read(0, bytes));
        TestContext.Current.TestOutputHelper?.WriteLine("DIRECT_SHORT " + JsonSerializer.Serialize(new
        {
            DeclaredLength = source.Length,
            ActualLength = file.Length,
            Requested = bytes.Length,
            Returned = returned,
            Error = failure?.GetType().Name,
        }));
        Assert.IsType<IOException>(failure);
    }

    [Fact]
    public void A_read_starting_at_the_new_end_fails_before_the_reported_end()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The native direct device reader is for Windows.");
        using var file = CreateFile();
        using var source = new DirectDeviceSource(file.SafeFileHandle, "owned changing file", 2048, 512);
        file.SetLength(1024);
        file.Flush(true);
        var bytes = new byte[512];
        int returned = -1;
        var failure = Record.Exception(() => returned = source.Read(1024, bytes));
        TestContext.Current.TestOutputHelper?.WriteLine("DIRECT_NEW_END " + JsonSerializer.Serialize(new
        {
            DeclaredLength = source.Length,
            ActualLength = file.Length,
            Offset = 1024,
            Returned = returned,
            Error = failure?.GetType().Name,
        }));
        Assert.IsType<IOException>(failure);
    }

    [Fact]
    public void A_stable_source_still_returns_exact_bytes_and_zero_at_its_advertised_end()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The native direct device reader is for Windows.");
        using var file = CreateFile();
        using var source = new DirectDeviceSource(file.SafeFileHandle, "owned stable file", 2048, 512);
        var bytes = new byte[1536];
        Assert.Equal(1024, source.Read(1024, bytes));
        Assert.Equal(Enumerable.Range(1024, 1024).Select(i => (byte)i).ToArray(), bytes[..1024]);
        Assert.Equal(0, source.Read(2048, bytes));
    }

    private FileStream CreateFile()
    {
        string path = Path.Combine(_dir, "source.bin");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = Enumerable.Range(0, 2048).Select(i => (byte)i).ToArray();
        file.Write(bytes);
        file.Flush(true);
        return file;
    }
}
