using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveProxyTests
{
    [Fact]
    public async Task Queue_has_one_active_eight_pending_and_releases_cancelled_reservations()
    {
        var queue = new ArchiveWorkerQueue();
        using var first = await queue.EnterAsync(TestContext.Current.CancellationToken);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var waiting = Enumerable.Range(0, 8).Select(_ => queue.EnterAsync(cancel.Token).AsTask()).ToArray();
        Assert.All(waiting, task => Assert.False(task.IsCompleted));
        await Assert.ThrowsAsync<ArchiveWriteException>(async () => await queue.EnterAsync(TestContext.Current.CancellationToken));
        cancel.Cancel();
        foreach (var task in waiting) await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        var next = queue.EnterAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(next.IsCompleted);
        first.Dispose();
        first.Dispose(); // a double release cannot admit a second active worker
        using var entered = await next;
        using var nextCancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var last = queue.EnterAsync(nextCancel.Token).AsTask();
        Assert.False(last.IsCompleted);
        nextCancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await last);
    }

    [Fact]
    public void Source_ids_offsets_and_repeated_reads_are_checked_before_opening_any_source()
    {
        using var stage = new MemoryStream();
        int opens = 0;
        using var authority = new ArchiveIoAuthority([new(0, 3)], _ => { opens++; return new MemoryStream("abc"u8.ToArray()); },
            new ArchiveStageProxy(stage, 20, 100), 6);
        var response = authority.Handle(Read(0, 0, 3));
        Assert.Equal("abc"u8.ToArray(), response.Payload[12..]);
        authority.Handle(Read(0, 0, 3));
        Assert.Equal(1, opens);
        Assert.Throws<ArchiveWriteException>(() => authority.Handle(Read(0, 0, 1)));
        Assert.Throws<ArchiveWriteException>(() => authority.Handle(Read(0, 0, 0)));
        Assert.Equal(1, opens);

        foreach (var request in new[] { Read(1, 0, 1), Read(0, long.MaxValue, 1), Read(0, -1, 1), Read(0, 0, int.MaxValue) })
        {
            using var invalid = new ArchiveIoAuthority([new(0, 3)], _ => throw new InvalidOperationException("Must not open."),
                new ArchiveStageProxy(stage, 20, 100), 6);
            Assert.Throws<ArchiveWriteException>(() => invalid.Handle(request));
        }
    }

    [Theory]
    [InlineData(ArchiveWorkerMessage.Completed)]
    [InlineData(ArchiveWorkerMessage.Control)]
    [InlineData(ArchiveWorkerMessage.Hello)]
    [InlineData(ArchiveWorkerMessage.Progress)]
    public void Control_and_terminal_messages_cannot_acquire_filesystem_authority(ArchiveWorkerMessage kind)
    {
        using var stage = new MemoryStream();
        using var authority = new ArchiveIoAuthority([], _ => throw new InvalidOperationException(), new ArchiveStageProxy(stage, 100, 100), 0);
        Assert.Throws<ArchiveWriteException>(() => authority.Handle(new(kind, "C:/outside.txt"u8.ToArray())));
        Assert.Equal(0, stage.Length);
    }

    [Fact]
    public void A_misaddressed_response_poisoned_connection_cannot_continue_with_a_different_stream()
    {
        var connection = new ArchiveProxyConnection(_ => new ArchiveWorkerFrame(ArchiveWorkerMessage.Data, new byte[13]));
        using var source = new ArchiveProxyStream(connection, 1, 10, 10);
        Assert.Throws<ArchiveWriteException>(() => source.ReadByte());
        using var other = new ArchiveProxyStream(connection, 0, 10, 10);
        Assert.Throws<ArchiveWriteException>(() => other.ReadByte());
    }

    [Fact]
    public void Proxy_roundtrip_can_seek_backpatch_and_read_complete_staged_content()
    {
        using var actual = new MemoryStream();
        using var authority = new ArchiveIoAuthority([], _ => throw new InvalidOperationException(), new ArchiveStageProxy(actual, 200_000, 1_000_000), 0);
        var connection = new ArchiveProxyConnection(authority.Handle);
        using var proxy = new ArchiveProxyStream(connection, ArchiveIoAuthority.StageId, 0, 200_000, writable: true);
        byte[] bytes = new byte[150_000];
        new Random(102).NextBytes(bytes);
        proxy.Write(bytes);
        proxy.Position = 0;
        proxy.Write("header"u8);
        "header"u8.CopyTo(bytes);
        proxy.Position = 0;
        Assert.Equal(SHA256.HashData(bytes), SHA256.HashData(proxy));
        Assert.Equal(bytes, actual.ToArray());
        Assert.Throws<ArchiveWriteException>(() => proxy.Seek(long.MaxValue, SeekOrigin.Current));
        Assert.Equal(bytes, actual.ToArray());
    }

    [Fact]
    public async Task A_real_child_uses_only_framed_grants_and_its_parent_owned_stage()
    {
        byte[] bytes = new byte[200_000];
        new Random(515).NextBytes(bytes);
        using var actual = new MemoryStream();
        using var authority = new ArchiveIoAuthority([new(0, bytes.Length)], _ => new MemoryStream(bytes, writable: false),
            new ArchiveStageProxy(actual, bytes.Length, bytes.Length * 4L), bytes.Length * 4L);
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", config, "net10.0", "FileCat.CoreCrashHarness.dll");
        string runtimeRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtimeRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        Guid nonce = Guid.NewGuid();
        start.ArgumentList.Add(harness);
        start.ArgumentList.Add("--archive-proxy");
        start.ArgumentList.Add(nonce.ToString());
        start.ArgumentList.Add(bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var terminate = timeout.Token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        try
        {
            var receiver = new ArchiveWorkerProtocol(nonce, 16 << 20);
            var sender = new ArchiveWorkerProtocol(nonce, 16 << 20);
            byte[] digest = await Task.Run(() =>
            {
                while (true)
                {
                    var request = receiver.Read(process.StandardOutput.BaseStream);
                    if (request.Kind == ArchiveWorkerMessage.Completed) return request.Payload;
                    var reply = authority.Handle(request);
                    sender.Write(process.StandardInput.BaseStream, reply.Kind, reply.Payload);
                }
            }, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(SHA256.HashData(bytes), digest);
            Assert.Equal(bytes, actual.ToArray());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static ArchiveWorkerFrame Read(int id, long offset, int count)
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, id);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), offset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), count);
        return new ArchiveWorkerFrame(ArchiveWorkerMessage.Read, bytes);
    }
}
