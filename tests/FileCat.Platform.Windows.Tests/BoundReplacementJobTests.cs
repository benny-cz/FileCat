using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundReplacementJobTests
{
    private sealed class SwapAtBoundPublication(string retired, byte[] replacement) : WindowsFileOperations
    {
        public bool Called { get; private set; }

        public override BoundReplacement? ReplaceReviewed(string source, string destination, string? expectedIdentity,
            string expectedSha256, long expectedLength, CancellationToken ct, string? recoveryPath = null,
            DateTime? expectedModifiedUtc = null)
        {
            Called = true;
            DateTime stamp = File.GetLastWriteTimeUtc(destination);
            File.Move(destination, retired);
            File.WriteAllBytes(destination, replacement);
            File.SetLastWriteTimeUtc(destination, stamp);
            return base.ReplaceReviewed(source, destination, expectedIdentity, expectedSha256, expectedLength, ct, recoveryPath);
        }
    }

    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Copy, true)]
    [InlineData(JobKind.Move, false)]
    [InlineData(JobKind.Move, true)]
    public async Task Local_replace_keeps_a_substituted_destination_and_publishes_an_unchanged_one(JobKind kind, bool substitute)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows object-bound rename required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-replace-job-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string source = Path.Combine(root, "source.bin");
            string destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            string target = Path.Combine(destination, "source.bin");
            byte[] incoming = Enumerable.Repeat((byte)'I', 4096).ToArray();
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            File.WriteAllBytes(source, incoming);
            File.WriteAllBytes(target, a);
            DateTime stamp = File.GetLastWriteTimeUtc(target);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(root, "journal")) { MaxConcurrent = 0 };
            bool answered = false;
            jobs.DecisionRequested += decision =>
            {
                answered = true;
                if (substitute)
                {
                    File.Move(target, Path.Combine(root, "retired-A.bin"));
                    File.WriteAllBytes(target, b);
                    File.SetLastWriteTimeUtc(target, stamp);
                }
                decision.Resolve(new Decision(DecisionAction.Replace));
            };
            var job = jobs.Submit(new JobRequest
            {
                Kind = kind,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(destination),
            });
            jobs.MaxConcurrent = 2;
            jobs.Schedule();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline, "The owned replacement job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(answered);
            Assert.Equal(substitute ? b : incoming, File.ReadAllBytes(target));
            Assert.Equal(!substitute && kind == JobKind.Move ? false : true, File.Exists(source));
            Assert.Equal(!substitute, job.State == JobState.Completed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// A destination larger than the content-review budget (64 MiB) is replaced bound by its identity, length and last
    /// write time, rather than refused as changed after the whole copy (review finding F-H2 of f134ba37); a different file
    /// put in its place, of the same length and time, is still kept.
    /// </summary>
    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Move, false)]
    [InlineData(JobKind.Copy, true)]
    public async Task A_destination_beyond_the_content_budget_is_replaced_bound_by_its_identity(JobKind kind, bool substitute)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows object-bound rename required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-replace-large-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            const int Size = 70 << 20;
            string source = Path.Combine(root, "source.bin");
            string destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            string target = Path.Combine(destination, "source.bin");
            byte[] incoming = new byte[Size], a = new byte[Size], b = new byte[Size];
            Array.Fill(incoming, (byte)'I');
            Array.Fill(a, (byte)'A');
            Array.Fill(b, (byte)'B');
            File.WriteAllBytes(source, incoming);
            File.WriteAllBytes(target, a);
            DateTime stamp = File.GetLastWriteTimeUtc(target);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(root, "journal")) { MaxConcurrent = 0 };
            bool answered = false;
            jobs.DecisionRequested += decision =>
            {
                answered = true;
                if (substitute)
                {
                    File.Move(target, Path.Combine(root, "retired-A.bin"));
                    File.WriteAllBytes(target, b);
                    File.SetLastWriteTimeUtc(target, stamp);
                }
                decision.Resolve(new Decision(DecisionAction.Replace));
            };
            var job = jobs.Submit(new JobRequest
            {
                Kind = kind,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(destination),
            });
            jobs.MaxConcurrent = 2;
            jobs.Schedule();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline, "The owned replacement job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(answered);
            Assert.True((substitute ? b : incoming).AsSpan().SequenceEqual(File.ReadAllBytes(target)), "The destination holds the wrong bytes.");
            Assert.Equal(!substitute, job.State == JobState.Completed);
            Assert.Empty(Directory.GetFiles(destination, ".filecat-replaced-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    public async Task Local_replace_keeps_a_target_swapped_after_the_job_recheck(JobKind kind)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows object-bound rename required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),
            "filecat-bound-replace-window-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string source = Path.Combine(root, "source.bin");
            string destination = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            string target = Path.Combine(destination, "source.bin");
            byte[] incoming = Enumerable.Repeat((byte)'I', 4096).ToArray();
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            File.WriteAllBytes(source, incoming);
            File.WriteAllBytes(target, a);
            var files = new SwapAtBoundPublication(Path.Combine(root, "retired-A.bin"), b);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(files, providers, Path.Combine(root, "journal")) { MaxConcurrent = 0 };
            jobs.DecisionRequested += decision => decision.Resolve(new Decision(DecisionAction.Replace));
            var job = jobs.Submit(new JobRequest
            {
                Kind = kind,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(destination),
            });
            jobs.MaxConcurrent = 2;
            jobs.Schedule();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!job.State.IsFinished())
            {
                Assert.True(DateTime.UtcNow < deadline);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            Assert.True(files.Called);
            Assert.NotEqual(JobState.Completed, job.State);
            Assert.Equal(b, File.ReadAllBytes(target));
            Assert.Equal(a, File.ReadAllBytes(Path.Combine(root, "retired-A.bin")));
            Assert.Equal(incoming, File.ReadAllBytes(source));
            Assert.Contains(job.Issues, i => i.Message.Contains("Not replaced", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
