using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveAssessmentClientTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static readonly string Empty = Hash([]);
    private static ArchiveAssessmentRequest Request => new(new(ArchiveWriteFormat.Tar), 1024, Empty);

    [Fact]
    public async Task A_real_child_assesses_complete_duplicate_order_content_and_semantic_metadata_through_proxy_grants()
    {
        byte[] tar = Tar();
        var request = new ArchiveAssessmentRequest(new(ArchiveWriteFormat.Tar), tar.Length, Hash(tar));
        Child? child = null;
        var client = new ArchiveAssessmentClient((nonce, _) => child = Child.Start(nonce));
        int opens = 0;
        var result = await client.AssessAsync(request, () => { opens++; return new MemoryStream(tar, writable: false); }, TestContext.Current.CancellationToken);
        Assert.Equal(1, opens);
        Assert.True(child!.Disposed);
        Assert.True(child.Reaped);
        Assert.Equal(request.SourceSha256, result.CatalogRevision);
        Assert.Equal(new[] { "same", "same", "folder/" }, result.Manifest.Members.Select(m => m.Name));
        Assert.Equal(new[] { 0, 1, 0 }, result.Manifest.Members.Select(m => m.Occurrence));
        Assert.Equal(Hash("first"u8.ToArray()), result.Manifest.Members[0].Sha256);
        Assert.Equal(Hash("second"u8.ToArray()), result.Manifest.Members[1].Sha256);
        Assert.Equal(11, result.Manifest.LogicalBytes);
        using var metadata = new BinaryReader(new MemoryStream(result.Manifest.Members[0].Metadata.ToArray()));
        Assert.Equal(1, metadata.ReadUInt16());
        Assert.Equal(0x1a4, metadata.ReadInt32());
        Assert.Equal(123, metadata.ReadInt32()); Assert.Equal(456, metadata.ReadInt32());
        Assert.Equal(new byte[] { 1, 2 }, result.Manifest.Features.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_child_refusals_cover_corruption_and_changed_pinned_source_without_accepting_partial_catalog(bool changed)
    {
        byte[] tar = Tar();
        if (!changed) tar[0] ^= 1;
        var request = new ArchiveAssessmentRequest(new(ArchiveWriteFormat.Tar), tar.Length, changed ? Empty : Hash(tar));
        Child? child = null;
        var client = new ArchiveAssessmentClient((nonce, _) => child = Child.Start(nonce));
        var refusal = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(request,
            () => new MemoryStream(tar, writable: false), TestContext.Current.CancellationToken));
        Assert.Equal(changed ? ArchiveWriteReason.SourceChanged : ArchiveWriteReason.InvalidArchive, refusal.Reason);
        Assert.True(child!.Reaped); Assert.True(child.Disposed);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("input")]
    [InlineData("early-completed")]
    [InlineData("nonce")]
    [InlineData("hello")]
    [InlineData("read-after-catalog")]
    [InlineData("after-completed")]
    public async Task Forbidden_phase_messages_never_touch_a_source_or_stage_and_always_dispose(string fault)
    {
        Scripted? process = null;
        int opens = 0;
        var client = new ArchiveAssessmentClient((nonce, _) => process = new Scripted(Script(nonce, fault)));
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(Request,
            () => { opens++; throw new InvalidOperationException("No source access authorized by this conversation."); }, TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, error.Reason);
        Assert.Equal(0, opens);
        Assert.True(process!.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stalled_worker_is_stopped_and_reaped_on_no_progress_or_user_cancellation(bool cancel)
    {
        Scripted? process = null;
        var queue = new ArchiveWorkerQueue();
        var client = new ArchiveAssessmentClient((nonce, _) => process = new Scripted(Hello(nonce), stalled: true), queue,
            cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));
        using var token = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (cancel) token.CancelAfter(100);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AssessAsync(Request, () => throw new InvalidOperationException(), token.Token));
        else
            Assert.Equal(ArchiveWriteReason.BudgetExceeded, (await Assert.ThrowsAsync<ArchiveWriteException>(() =>
                client.AssessAsync(Request, () => throw new InvalidOperationException(), token.Token))).Reason);
        Assert.True(process!.Killed); Assert.True(process.Disposed);
        using var available = await queue.EnterAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Queue_is_not_released_until_actual_disposal_and_reaping_complete()
    {
        var queue = new ArchiveWorkerQueue();
        var reaping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ArchiveAssessmentClient((nonce, _) => new Scripted(Script(nonce, "valid")) { Reaping = reaping.Task, Reached = reached }, queue);
        Task assessment = client.AssessAsync(Request, () => throw new InvalidOperationException(), TestContext.Current.CancellationToken);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var pending = queue.EnterAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(pending.IsCompleted);
        reaping.SetResult();
        await assessment;
        using var reservation = await pending;
    }

    [Fact]
    public async Task An_output_limit_reported_by_the_final_drain_cannot_escape_as_a_successful_result()
    {
        Scripted? process = null;
        var client = new ArchiveAssessmentClient((nonce, _) => process = new Scripted(Script(nonce, "valid")) { ErrorAfterReaping = true });
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(Request,
            () => throw new InvalidOperationException(), TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.WorkerUnavailable, failure.Reason);
        Assert.True(process!.Disposed);
    }

    [Fact]
    public async Task Final_OS_CPU_accounting_catches_a_burst_between_monitor_samples_and_exit()
    {
        var client = new ArchiveAssessmentClient((nonce, _) => new Scripted(Script(nonce, "valid")) { CpuAfterReaping = TimeSpan.FromMinutes(1) });
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(Request,
            () => throw new InvalidOperationException(), TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, failure.Reason);
    }

    private static byte[] Tar()
    {
        using var bytes = new MemoryStream();
        using (var writer = new TarWriter(bytes, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (string content in new[] { "first", "second" })
            {
                using var payload = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "same")
                {
                    DataStream = payload, Mode = (UnixFileMode)0x1a4, Uid = 123, Gid = 456,
                    UserName = "owner", GroupName = "team", ModificationTime = DateTimeOffset.UnixEpoch.AddTicks(7),
                });
            }
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "folder/") { ModificationTime = DateTimeOffset.UnixEpoch });
        }
        return bytes.ToArray();
    }

    private static byte[] Hello(Guid nonce)
    {
        using var stream = new MemoryStream();
        new ArchiveWorkerProtocol(nonce, 1 << 20).Write(stream, ArchiveWorkerMessage.Hello, ArchiveAssessmentWire.Hello);
        return stream.ToArray();
    }

    private static byte[] Script(Guid nonce, string fault)
    {
        using var stream = new MemoryStream();
        var wire = new ArchiveWorkerProtocol(fault == "nonce" ? Guid.NewGuid() : nonce, 1 << 20);
        wire.Write(stream, ArchiveWorkerMessage.Hello, ArchiveAssessmentWire.Hello);
        if (fault == "nonce") return stream.ToArray();
        if (fault is "write" or "input" or "early-completed" or "hello")
        {
            byte[] payload = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(payload, 17);
            wire.Write(stream, fault switch { "write" => ArchiveWorkerMessage.Write, "input" => ArchiveWorkerMessage.Read,
                "hello" => ArchiveWorkerMessage.Hello, _ => ArchiveWorkerMessage.Completed }, payload);
            return stream.ToArray();
        }
        var manifest = new ArchiveWriteManifest(Request.Profile, [], [1, 2]);
        var records = new ArchiveWorkerRecords(wire, stream, writing: true);
        records.Write(ArchiveControlRecord.Catalog, ArchiveAssessmentWire.Catalog(new(manifest, Empty, 1024, Empty)));
        wire.Write(stream, fault == "read-after-catalog" ? ArchiveWorkerMessage.Read : ArchiveWorkerMessage.Completed,
            Convert.FromHexString(manifest.Digest));
        if (fault == "after-completed") stream.WriteByte(0);
        return stream.ToArray();
    }

    private sealed class Scripted : IArchiveWorkerProcess
    {
        private readonly ManualResetEventSlim _stop = new();
        private readonly bool _stalled;
        public Scripted(byte[] bytes, bool stalled = false) { _stalled = stalled; Output = new Tail(bytes, stalled ? _stop : null); }
        public Stream Input { get; } = new MemoryStream();
        public Stream Output { get; }
        public bool HasExited => !_stalled || _stop.IsSet;
        public uint ExitCode => 0;
        public TimeSpan TotalProcessorTime => Disposed ? CpuAfterReaping : TimeSpan.Zero;
        public TimeSpan CpuAfterReaping { get; init; }
        public bool ErrorLimitExceeded => Disposed && ErrorAfterReaping;
        public bool ErrorAfterReaping { get; init; }
        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }
        public Task? Reaping { get; init; }
        public TaskCompletionSource? Reached { get; init; }
        public void Kill() { Killed = true; _stop.Set(); }
        public async ValueTask DisposeAsync()
        {
            Kill(); Reached?.TrySetResult();
            if (Reaping is not null) await Reaping;
            Input.Dispose(); Output.Dispose(); Disposed = true;
        }
    }

    private sealed class Tail(byte[] bytes, ManualResetEventSlim? stop) : MemoryStream(bytes, writable: false)
    {
        public override int Read(Span<byte> buffer)
        {
            int read = base.Read(buffer);
            if (read == 0 && stop is not null && !stop.Wait(TimeSpan.FromSeconds(5))) throw new IOException("Test worker was not stopped.");
            return read;
        }
    }

    private sealed class Child(Process process) : IArchiveWorkerProcess
    {
        public bool Disposed { get; private set; }
        public bool Reaped { get; private set; }
        public Stream Input => process.StandardInput.BaseStream;
        public Stream Output => process.StandardOutput.BaseStream;
        private uint _exit;
        private TimeSpan _cpu;
        public bool HasExited => Disposed || process.HasExited;
        public uint ExitCode => Disposed ? _exit : (uint)process.ExitCode;
        public TimeSpan TotalProcessorTime
        {
            get
            {
                if (Disposed || Reaped) return _cpu;
                try { return _cpu = process.TotalProcessorTime; }
                catch (System.ComponentModel.Win32Exception) when (process.HasExited) { return _cpu; }
            }
        }
        public bool ErrorLimitExceeded => false;
        public void Kill() { if (!Disposed && !process.HasExited) process.Kill(entireProcessTree: true); }
        public async ValueTask DisposeAsync()
        {
            Kill(); await process.WaitForExitAsync(CancellationToken.None); Reaped = true;
            _exit = (uint)process.ExitCode;
            process.Dispose(); Disposed = true;
        }
        public static Child Start(Guid nonce)
        {
            var checkout = new DirectoryInfo(AppContext.BaseDirectory);
            while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
            Assert.NotNull(checkout);
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
            string runtimeRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            var start = new ProcessStartInfo(Path.Combine(runtimeRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true };
            foreach (string arg in new[] { probe, "archive-assess", nonce.ToString() }) start.ArgumentList.Add(arg);
            return new(Process.Start(start)!);
        }
    }
}
