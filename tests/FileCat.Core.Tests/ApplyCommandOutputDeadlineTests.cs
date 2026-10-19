using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Tools;

namespace FileCat.Core.Tests;

public sealed class ApplyCommandOutputDeadlineTests(ITestOutputHelper output)
{
    [Fact]
    public void A_still_open_output_reader_exposes_its_bounded_captured_tail()
    {
        var tail = new ApplyCommandExecutor.OutputTailCollector();
        tail.Append("early error".AsSpan());
        Assert.Equal("early error", tail.Snapshot());
        tail.Append(new string('x', 3_000).AsSpan());
        Assert.Equal(new string('x', 2_000), tail.Snapshot());
        tail.Append("last error".AsSpan());
        Assert.Equal(new string('x', 1_990) + "last error", tail.Snapshot());
    }

    [Fact]
    public async Task Exited_command_does_not_wait_for_a_descendant_holding_its_output_pipes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This control uses the Unix shell and an owned sleep child.");
        string root = Directory.CreateTempSubdirectory("filecat-apply-output-").FullName;
        Job? job = null;
        int? childPid = null;
        try
        {
            string input = Path.Combine(root, "owned.txt");
            File.WriteAllText(input, "owned input");
            var item = ItemRef.ForFileSystemPath(input, EntryKind.File);
            var registry = new ProviderRegistry();
            registry.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), registry, Path.Combine(root, "journal"));
            var invocation = new ApplyInvocation(item, "/bin/sh",
                ["-c", "/bin/sleep 8 & echo $! > held.pid; printf 'owned helper error' >&2; exit 7"],
                root, "owned held-pipe control", null);
            var clock = Stopwatch.StartNew();
            job = jobs.Submit(new JobRequest { Kind = JobKind.ApplyCommand,
                Sources = [item], Invocations = [invocation] });
            while (!job.State.IsFinished() && clock.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(20, TestContext.Current.CancellationToken);
            bool prompt = job.State.IsFinished();
            while (!job.State.IsFinished() && clock.Elapsed < TimeSpan.FromSeconds(12))
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(Path.Combine(root, "held.pid")), "The owned child PID was not captured.");
            childPid = int.Parse(File.ReadAllText(Path.Combine(root, "held.pid")));
            output.WriteLine($"APPLY_OUTPUT_DEADLINE elapsedMs={clock.ElapsedMilliseconds} prompt={prompt} finished={job.State.IsFinished()} state={job.State} childPid={childPid}");
            Assert.True(job.State.IsFinished(), "The command job remained blocked after its owned child exited.");
            Assert.True(prompt, "The command job waited for an exited helper's descendant-held output pipe.");
            Assert.Equal(JobState.Failed, job.State);
            Assert.Contains(job.Issues, issue => issue.Message.Contains("owned helper error", StringComparison.Ordinal));
        }
        finally
        {
            if (childPid is { } pid)
            {
                try
                {
                    using var child = Process.GetProcessById(pid);
                    if (!child.HasExited) { child.Kill(); child.WaitForExit(2000); }
                }
                catch (ArgumentException) { }
            }
            if (job is not null)
            {
                var until = Stopwatch.StartNew();
                while (!job.State.IsFinished() && until.Elapsed < TimeSpan.FromSeconds(3))
                    await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Directory.Delete(root, recursive: true);
        }
    }
}
