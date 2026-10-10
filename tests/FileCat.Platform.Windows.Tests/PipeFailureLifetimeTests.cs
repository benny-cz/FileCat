using System.Text.Json;
using FileCat.Platform.Windows.Recovery;

namespace FileCat.Platform.Windows.Tests;

public sealed class PipeFailureLifetimeTests
{
    [Theory]
    [InlineData("read-header")]
    [InlineData("request-write")]
    public void A_failed_read_still_disposes_its_owned_pipe(string failure)
    {
        var pipe = new HeldDeviceScriptPipe(new byte[4096], () => null);
        var source = new PipeDeviceSource(pipe, "owned failing helper");
        pipe.FailReadHeader = failure == "read-header"; pipe.FailWrite = failure == "request-write";
        Assert.Throws<IOException>(() => source.Read(0, new byte[16]));
        source.Dispose(); source.Dispose();
        TestContext.Current.TestOutputHelper?.WriteLine("PIPE_FAILURE_LIFETIME " + JsonSerializer.Serialize(new { failure, pipe.Disposed }));
        Assert.True(pipe.Disposed);
        Assert.Throws<IOException>(() => source.Read(0, new byte[16]));
    }
}
