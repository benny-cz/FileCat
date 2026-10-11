using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>Owned empty folders and recorded recovery topology answers; no device is opened.</summary>
public sealed class RecoveryFolderAttemptAdmissionTests(ITestOutputHelper output)
{
    public static IEnumerable<object?[]> Cases()
    {
        foreach (string moment in new[] { "root-retry", "folder-retry", "folder-auto", "backing-review" })
        foreach (bool? same in new bool?[] { true, null, false })
        foreach (int sources in new[] { 1, 2 })
            yield return [moment, same, sources];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Each_folder_creation_attempt_rechecks_all_selected_recovery_sources(string moment, bool? same, int sources)
    {
        using var root = new TempDir();
        byte[] marker = "complete owned source container marker"u8.ToArray();
        string backing = Path.Combine(root.Path, "source.img"); File.WriteAllBytes(backing, marker);
        string blocker = Path.Combine(root.Path, "blocker");
        string destination = moment == "root-retry" ? Path.Combine(blocker, "destination") : Path.Combine(root.Path, "destination");
        if (moment == "root-retry") File.WriteAllBytes(blocker, "owned initial folder blocker"u8.ToArray());
        else if (moment != "backing-review") Directory.CreateDirectory(destination);
        var provider = new Provider(same);
        var files = new FolderFiles(provider, moment);
        var providers = new ProviderRegistry(); providers.Register(provider); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending =>
        {
            decisions.Add(pending.Request);
            bool firstRetry = moment is "root-retry" or "folder-retry" && decisions.Count == 1;
            if (firstRetry && moment == "root-retry")
            {
                File.Delete(blocker); Directory.CreateDirectory(blocker); provider.Changed = true;
            }
            pending.Resolve(new Decision(firstRetry ? DecisionAction.Retry : DecisionAction.Skip));
        };
        try
        {
            Location Parent(string name) => new(provider.Scheme, name, moment == "backing-review" ? Location.FileSystem(backing) : null);
            ItemRef[] selected = sources == 1 ? [new(Parent("protected"), "first", EntryKind.Directory)]
                : [new(Parent("first"), "first", EntryKind.Directory), new(Parent("protected"), "second", EntryKind.Directory)];
            var job = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = selected, Destination = Location.FileSystem(destination) });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned folder job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            string[] folders = Directory.Exists(destination) ? Directory.GetDirectories(destination).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal).ToArray() : [];
            output.WriteLine(JsonSerializer.Serialize(new
            {
                moment, same, sources, job.State, job.BytesDone, job.ItemsFailed, provider.Changed, provider.Enumerations,
                files.Attempts, files.Created, DestinationExists = Directory.Exists(destination), Folders = folders,
                Checks = provider.Checks.Select(c => new { c.Source, c.Folder, c.Changed, c.Refusal }),
                Decisions = decisions.Select(d => new { d.Title, d.Message }), Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                SourceBefore = Convert.ToBase64String(marker), SourceAfter = Convert.ToBase64String(File.ReadAllBytes(backing)),
            }));
            Assert.True(provider.Changed); Assert.Equal(marker, File.ReadAllBytes(backing)); Assert.Equal(0, job.BytesDone);
            if (same == false)
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(sources, provider.Enumerations);
                Assert.Equal(sources == 1 ? ["first"] : new[] { "first", "second" }, folders);
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, provider.Enumerations); Assert.Empty(folders);
                if (moment is "root-retry" or "backing-review") Assert.False(Directory.Exists(destination));
                Assert.Contains(provider.Checks, c => c.Source == "protected" && c.Changed && c.Refusal is not null);
                Assert.Contains(job.Issues, i => i.Message.Contains(same == true ? "same physical disk" : "cannot tell", StringComparison.Ordinal));
            }
            Assert.Equal(moment == "folder-auto" ? (same == false ? 0 : 1)
                : moment is "root-retry" or "folder-retry" ? (same == false ? 1 : 2) : (same == false ? 0 : 1), decisions.Count);
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5))); }
    }

    private sealed class FolderFiles(Provider provider, string moment) : PortableFileOperations
    {
        public int Attempts, Created;
        public override string? GetFinalPath(string path)
        {
            if (moment == "backing-review") provider.Changed = true;
            return base.GetFinalPath(path);
        }
        public override void CreateDirectory(string path)
        {
            Attempts++;
            if (Attempts == 1 && moment is "folder-retry" or "folder-auto")
            {
                provider.Changed = true; throw new OwnedFolderException(moment == "folder-auto");
            }
            base.CreateDirectory(path); Created++;
        }
        private sealed class OwnedFolderException : IOException
        {
            public OwnedFolderException(bool sharing) : base("owned folder creation failure")
            { HResult = sharing ? unchecked((int)0x80070020) : unchecked((int)0x8007001F); }
        }
    }

    private sealed class Provider(bool? same) : ResourceProvider
    {
        private readonly RecoveryProvider _guard = new() { SharesDisk = (_, _) => same };
        private readonly Location _device = new(Schemes.Recovery, "", new Location(Schemes.Device, "owned-recording-device"));
        public bool Changed; public int Enumerations;
        public readonly List<(string Source, string Folder, bool Changed, string? Refusal)> Checks = [];
        public override string Scheme => "owned-recovery-folder-attempt";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => parent.WithPath(parent.Path + "/" + entry.Name);
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent | LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) { Enumerations++; return Task.CompletedTask; }
        public override string? CheckTransferDestination(Location source, string folder)
        {
            string? refusal = Changed && source.Path == "protected" ? _guard.CheckTransferDestination(_device, folder) : null;
            Checks.Add((source.Path, folder, Changed, refusal)); return refusal;
        }
    }
}
