using System.IO.Pipes;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class PictureWorkerLifetimeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("not-a-descriptor")]
    [InlineData(" 1")]
    [InlineData("0x10")]
    [InlineData("2147483648")]
    public void Malformed_parent_channels_refuse_before_input(string handle) =>
        Assert.Throws<IOException>(() => PictureWorkerLifetime.Open(handle));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parent_channel_closure_or_unexpected_data_requests_termination(bool writeUnexpectedData)
    {
        var (parent, child) = await Connected();
        using var writer = parent;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var lifetime = new PictureWorkerLifetime(child, () => { Interlocked.Increment(ref calls); stopped.SetResult(); });
        // A live parent leaves the dedicated channel idle; an encoded source can finish independently.
        Assert.False(stopped.Task.IsCompleted);
        if (writeUnexpectedData) { parent.WriteByte(1); parent.Flush(); }
        else parent.Dispose();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Disposing_a_completed_worker_does_not_request_termination()
    {
        var (parent, child) = await Connected();
        using var writer = parent;
        int calls = 0;
        var lifetime = new PictureWorkerLifetime(child, () => Interlocked.Increment(ref calls));
        lifetime.Dispose();
        parent.Dispose();
        await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Parent_channel_read_failure_requests_termination()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lifetime = new PictureWorkerLifetime(new FailingRead(), stopped.SetResult);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private async Task<(NamedPipeServerStream Parent, NamedPipeClientStream Child)> Connected()
    {
        string name = OperatingSystem.IsWindows() ? "filecat-picture-lifetime-" + Guid.NewGuid().ToString("N")
            : Path.Combine("/tmp", "fc-pw-" + Guid.NewGuid().ToString("N"));
        var parent = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var child = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
        try
        {
            await Task.WhenAll(parent.WaitForConnectionAsync(TestContext.Current.CancellationToken),
                child.ConnectAsync(TestContext.Current.CancellationToken)).WaitAsync(TimeSpan.FromSeconds(5));
            output.WriteLine("OwnedPipePath=" + name);
            return (parent, child);
        }
        catch { parent.Dispose(); child.Dispose(); throw; }
    }

    private sealed class FailingRead : MemoryStream
    {
        public override int ReadByte() => throw new IOException("Owned channel failure control.");
    }
}
