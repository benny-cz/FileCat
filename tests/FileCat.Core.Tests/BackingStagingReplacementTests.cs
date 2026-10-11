using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>Actual owned source-container renames into a retired staging name; Unix identities are native; the portable Windows identity answer is an explicit control.</summary>
public sealed class BackingStagingReplacementTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string route in new[] { "close", "close-io", "close-unexpected", "verify", "publish-retry", "publish-skip" })
        foreach (bool changed in new[] { false, true }) yield return [route, changed];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Replaced_staging_paths_never_delete_or_publish_a_selected_source_container(string route, bool changed)
    {
        using var root = new TempDir();
        using var ready = new ManualResetEventSlim(false);
        string destination = root.Dir("destination"), source = root.File("source-image.bin", "complete owned source image bytes"), target = Path.Combine(destination, "copy.bin");
        byte[] original = File.ReadAllBytes(source), incoming = "complete extracted member bytes"u8.ToArray();
        var files = new Files(source, original, route, changed);
        files.CaptureOriginalIdentity();
        var provider = new Provider(files, incoming, ready);
        var providers = new ProviderRegistry(); providers.Register(provider); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        jobs.JobAdded += job => { files.Staged = Path.Combine(destination, $"{JournalRecovery.StagedPrefix}{job.ShortId}-1.tmp"); ready.Set(); };
        int decisions = 0;
        jobs.DecisionRequested += pending => { decisions++; pending.Resolve(new Decision(route == "publish-retry" && decisions == 1 ? DecisionAction.Retry : DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [new(new Location(provider.Scheme, "", Location.FileSystem(source)), "copy.bin", EntryKind.File, incoming.Length)],
                Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Replace, Verify = route == "verify" ? VerifyMode.ReadBack : VerifyMode.Native },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned staging replacement job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            string? retained = File.Exists(source) ? source : File.Exists(files.Staged) ? files.Staged : File.Exists(target) ? target : null;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                route, changed, job.State, job.BytesDone, provider.Opens, provider.Closes, files.Replaced, files.Attempts, files.Published, decisions, files.OriginalIdentity, files.NativeIdentityUsed,
                OriginalSourcePathExists = File.Exists(source), StagedExists = File.Exists(files.Staged), TargetExists = File.Exists(target),
                KnownSourceBytes = Convert.ToBase64String(original), KnownIncomingBytes = Convert.ToBase64String(incoming),
                RetainedSourcePath = retained, RetainedSourceBytes = retained is null ? null : Convert.ToBase64String(File.ReadAllBytes(retained)),
                StagedBytes = File.Exists(files.Staged) ? Convert.ToBase64String(File.ReadAllBytes(files.Staged)) : null,
                TargetBytes = File.Exists(target) ? Convert.ToBase64String(File.ReadAllBytes(target)) : null,
                Queries = files.Queries, Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
            }));
            Assert.Equal(provider.Opens, provider.Closes); Assert.Equal(changed, files.Replaced);
            if (files.NativeIdentityUsed) Assert.False(string.IsNullOrEmpty(files.OriginalIdentity));
            if (changed)
            {
                Assert.False(File.Exists(source)); Assert.Equal(original, File.ReadAllBytes(files.Staged));
                Assert.False(File.Exists(target)); Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, job.BytesDone); Assert.Equal(0, files.Published);
            }
            else
            {
                Assert.Equal(original, File.ReadAllBytes(source)); Assert.Equal(incoming, File.ReadAllBytes(target));
                Assert.False(File.Exists(files.Staged)); Assert.Equal(JobState.Completed, job.State); Assert.Equal(incoming.Length, job.BytesDone); Assert.Equal(1, files.Published);
            }
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5))); }
    }

    private sealed class Files(string source, byte[] original, string route, bool changed) : PortableFileOperations
    {
        public string Staged = "";
        public bool Replaced;
        public int Attempts, Published;
        public string? OriginalIdentity;
        public bool NativeIdentityUsed => !OperatingSystem.IsWindows();
        public List<object> Queries { get; } = [];
        public string Route => route;
        public void Replace()
        {
            if (!changed || Replaced) return;
            File.Delete(Staged); File.Move(source, Staged); Replaced = true;
        }
        // Capture while the exact owned source path exists. Product canonicalization may query
        // /private/var for a /var source on macOS; path spelling must not gate this observation.
        public void CaptureOriginalIdentity() => OriginalIdentity = GetFileIdentity(source);
        public override string? GetFileIdentity(string path)
        {
            string? identity = NativeIdentityUsed ? base.GetFileIdentity(path) : File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(original) ? "controlled-owned-source-image" : null;
            Queries.Add(new { Path = path, Identity = identity, Replaced }); return identity;
        }
        public override void Move(string from, string to, bool replaceExisting, bool writeThrough = false)
        {
            Attempts++;
            if (changed && route.StartsWith("publish-") && Attempts == 1) { Replace(); throw new IOException("owned publication attempt refused after source rename"); }
            base.Move(from, to, replaceExisting, writeThrough); Published++;
        }
    }

    private sealed class Provider(Files files, byte[] incoming, ManualResetEventSlim ready) : ResourceProvider
    {
        public int Opens, Closes;
        public override string Scheme => "owned-staging-source-image";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource OpenContent(ItemRef item)
        {
            if (!ready.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned source-image fixture not installed.");
            Opens++; return new Source(this, files, incoming);
        }
        private sealed class Source(Provider owner, Files files, byte[] incoming) : IContentSource
        {
            public string DisplayName => "owned source-image member";
            public long Length => incoming.Length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision() => null;
            public int Read(long offset, Span<byte> buffer)
            {
                int n = (int)Math.Min(buffer.Length, Math.Max(0, incoming.Length - offset)); incoming.AsSpan((int)offset,n).CopyTo(buffer); return n;
            }
            public void Dispose()
            {
                owner.Closes++;
                if (files.Route.StartsWith("publish-")) return;
                files.Replace();
                if (!files.Replaced) return;
                if (files.Route == "close-io") throw new IOException("owned source close failure");
                if (files.Route == "close-unexpected") throw new InvalidOperationException("owned unexpected source close failure");
            }
        }
    }
}
