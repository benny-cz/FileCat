using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// "Resume where safe" (plan P6, §14): a download that fails part way continues where it stopped only when the source is
/// provably the same file; otherwise it starts again, and the job says which.
/// </summary>
public sealed class ResumeTransferTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>A server with one file that can drop the connection part way and change the file in between.</summary>
    private sealed class FlakyProvider : ResourceProvider
    {
        public byte[] Content { get; set; } = [];
        public long ModifiedTicks { get; set; } = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        /// <summary>Byte offsets at which the next opened streams fail, one per open.</summary>
        public Queue<long> FailAt { get; } = new();
        public List<long> FirstReads { get; } = [];
        public Action? BeforeReopen { get; set; }
        public int Opens { get; private set; }

        public override string Scheme => "flaky";
        public override string GetDisplayPath(Location location) => "flaky:" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;

        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch(new[] { new EntryData("big.bin", EntryKind.File, Content.Length, ModifiedTicks) });
            return Task.CompletedTask;
        }

        public override IContentSource? OpenContent(ItemRef item)
        {
            if (Opens++ > 0) BeforeReopen?.Invoke();
            return new Source(this, FailAt.Count > 0 ? FailAt.Dequeue() : long.MaxValue);
        }

        private sealed class Source(FlakyProvider owner, long failAt) : IContentSource
        {
            private bool _first = true;
            public string DisplayName => "big.bin";
            public long Length => owner.Content.Length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision() => new(owner.Content.Length, owner.ModifiedTicks);

            public int Read(long offset, Span<byte> buffer)
            {
                if (_first)
                {
                    owner.FirstReads.Add(offset);
                    _first = false;
                }
                if (offset >= failAt) throw new IOException("The connection to the server was lost.");
                int n = (int)Math.Max(0, Math.Min(Math.Min(buffer.Length, owner.Content.Length - offset), failAt - offset));
                owner.Content.AsSpan((int)offset, n).CopyTo(buffer);
                return n;
            }

            public void Dispose() { }
        }
    }

    private (JobManager Jobs, FlakyProvider Server) Rig(byte[] content)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var server = new FlakyProvider { Content = content };
        providers.Register(server);
        return (new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal")), server);
    }

    private static byte[] Data(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private async Task<Job> Download(JobManager jobs, Func<PendingDecision, Decision>? answer = null)
    {
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [new ItemRef(new Location("flaky", "/"), "big.bin", EntryKind.File)],
            Destination = Location.FileSystem(_dir.Dir("down")),
        });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!job.State.IsFinished())
        {
            if (job.Decision is { } asked)
            {
                if (answer is null) Assert.Fail($"Unexpected question: {asked.Request.Title}: {asked.Request.Message}");
                asked.Resolve(answer(asked));
            }
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public async Task An_unchanged_file_continues_where_the_connection_dropped()
    {
        var data = Data(5 * 1024 * 1024 + 123, 1);
        var (jobs, server) = Rig(data);
        server.FailAt.Enqueue(3 * 1024 * 1024 + 7);
        var job = await Download(jobs);
        Assert.Equal(JobState.Completed, job.State); // a resumed copy is complete; the note says it was resumed
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(_dir.Path, "down", "big.bin")));
        // The second connection read the check window, then continued: nothing before it was fetched again.
        Assert.Equal([0L, 3 * 1024 * 1024 + 7 - 64 * 1024], server.FirstReads);
        Assert.Contains(job.Issues, i => i.Message.Contains("resumed at", StringComparison.Ordinal));
        Assert.Equal(data.Length, job.BytesDone);
    }

    [Fact]
    public async Task A_file_that_changed_meanwhile_is_copied_again_from_the_start()
    {
        var data = Data(3 * 1024 * 1024, 2);
        var (jobs, server) = Rig(data);
        server.FailAt.Enqueue(2 * 1024 * 1024);
        var changed = Data(3 * 1024 * 1024, 3);
        server.BeforeReopen = () =>
        {
            server.Content = changed;
            server.ModifiedTicks += TimeSpan.TicksPerMinute;
        };
        var job = await Download(jobs);
        Assert.Equal(changed, File.ReadAllBytes(Path.Combine(_dir.Path, "down", "big.bin")));
        Assert.Contains(job.Issues, i => i.Message.Contains("copied again from the start", StringComparison.Ordinal));
        Assert.Equal(changed.Length, job.BytesDone);
    }

    [Fact]
    public async Task Bytes_that_differ_behind_an_unchanged_size_and_time_are_caught()
    {
        var data = Data(3 * 1024 * 1024, 4);
        var (jobs, server) = Rig(data);
        server.FailAt.Enqueue(2 * 1024 * 1024);
        var tampered = (byte[])data.Clone();
        tampered[2 * 1024 * 1024 - 100] ^= 0xFF; // same size and time, different bytes just before the break
        server.BeforeReopen = () => server.Content = tampered;
        var job = await Download(jobs);
        Assert.Equal(tampered, File.ReadAllBytes(Path.Combine(_dir.Path, "down", "big.bin")));
        Assert.Contains(job.Issues, i => i.Message.Contains("copied again from the start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lasting_failure_asks_and_skipping_leaves_no_partial_file()
    {
        var data = Data(2 * 1024 * 1024, 5);
        var (jobs, server) = Rig(data);
        foreach (var at in new long[] { 1024 * 1024, 1024 * 1024, 1024 * 1024 }) server.FailAt.Enqueue(at);
        int asked = 0;
        var job = await Download(jobs, d =>
        {
            asked++;
            Assert.Contains("stopped part way", d.Request.Title, StringComparison.Ordinal);
            return new Decision(asked == 1 ? DecisionAction.Retry : DecisionAction.Skip);
        });
        Assert.Equal(2, asked); // the first failure retried on its own, then two questions
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_dir.Path, "down")));
        Assert.Contains(job.Issues, i => i.Outcome == StepOutcome.Skipped);
    }
}
