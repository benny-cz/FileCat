using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>Release issue I26: what the Operations view shows while a job works.</summary>
public sealed class JobProgressViewTests
{
    [Fact]
    public async Task A_verified_copy_never_shows_100_percent_before_it_ends()
    {
        string root = Directory.CreateTempSubdirectory("fc-progress-view-").FullName;
        try
        {
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root, "journal"));
            string source = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
            string file = Path.Combine(source, "big.bin");
            using (var fs = File.Create(file))
            {
                var block = new byte[1 << 20];
                new Random(2).NextBytes(block);
                for (int i = 0; i < 384; i++) fs.Write(block);
            }
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
                Destination = Location.FileSystem(Directory.CreateDirectory(Path.Combine(root, "dst")).FullName),
                Options = new TransferOptions { Verify = VerifyMode.ReadBack },
            });
            var view = new JobViewModel(job);
            var seen = new List<(double Percent, bool Active, string Detail)>();
            while (!job.State.IsFinished())
            {
                view.Refresh();
                seen.Add((view.Percent, view.IsActive, view.DetailText));
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            view.Refresh();
            Assert.Equal(JobState.Completed, job.State);
            Assert.All(seen.Where(s => s.Active), s => Assert.True(s.Percent < 100, $"{s.Percent}% shown while working ({s.Detail})."));
            // Reading back is most of this job: when the copy's last byte is written, the bar is near a third, not full.
            Assert.Contains(seen, s => s.Active && s.Percent is > 20 and < 60);
            Assert.Contains(seen, s => s.Detail.Contains("estimating the time left", StringComparison.Ordinal));
            Assert.Equal(100, view.Percent);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
