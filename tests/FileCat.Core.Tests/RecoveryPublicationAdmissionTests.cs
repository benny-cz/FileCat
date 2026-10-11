using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>Actual staging/rename and recovery admission with recorded topology answers; no device is opened.</summary>
public sealed class RecoveryPublicationAdmissionTests(ITestOutputHelper output)
{
    public static IEnumerable<object?[]> Cases()
    {
        foreach (string moment in new[] { "read", "close", "verify", "verify-close", "retry", "auto" })
        foreach (bool? same in new bool?[] { true, null, false })
        foreach (bool replace in new[] { false, true })
            yield return [moment, same, replace];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Publication_rechecks_recovery_destination_after_content_and_before_every_retry(string moment, bool? same, bool replace)
    {
        using var root = new TempDir();
        string destination = root.Dir("destination"), target = Path.Combine(destination, "copy.bin");
        byte[] incoming = "complete owned recovery publication bytes"u8.ToArray(), previous = "complete previous destination"u8.ToArray();
        if (replace) File.WriteAllBytes(target, previous);
        var provider = new Provider(incoming, moment, same);
        var files = new PublicationFiles(provider, moment);
        var providers = new ProviderRegistry(); providers.Register(provider); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending =>
        {
            decisions.Add(pending.Request);
            pending.Resolve(new Decision(moment == "retry" && decisions.Count == 1 ? DecisionAction.Retry : DecisionAction.Skip));
        };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [new(new Location(provider.Scheme, ""), "copy.bin", EntryKind.File, incoming.Length)],
                Destination = Location.FileSystem(destination),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace, Verify = moment.StartsWith("verify", StringComparison.Ordinal) ? VerifyMode.ReadBack : VerifyMode.Native },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned recovery publication job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                moment, same, replace, job.State, job.BytesDone, provider.Changed, provider.Opens, provider.Closes, provider.Reads,
                files.Attempts, files.Published, Checks = provider.Checks.Select(c => new { c.Folder, c.Changed, c.Refusal }),
                IncomingBytes = Convert.ToBase64String(incoming), PreviousBytes = Convert.ToBase64String(previous),
                Decisions = decisions.Select(d => new { d.Title, d.Message }), Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                DestinationFiles = Directory.GetFiles(destination).Select(p => new { Name = Path.GetFileName(p), Bytes = Convert.ToBase64String(File.ReadAllBytes(p)) }),
            }));
            Assert.True(provider.Changed);
            Assert.Equal(moment.StartsWith("verify", StringComparison.Ordinal) ? 2 : 1, provider.Opens);
            Assert.Equal(provider.Opens, provider.Closes); Assert.True(provider.Reads > 0);
            Assert.DoesNotContain(Directory.GetFiles(destination), p => Path.GetFileName(p).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
            if (same == false)
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(incoming.Length, job.BytesDone);
                Assert.Equal(incoming, File.ReadAllBytes(target)); Assert.Equal(1, files.Published);
                Assert.Equal(moment is "retry" or "auto" ? 2 : 1, files.Attempts);
                Assert.Equal(moment == "retry" ? 1 : 0, decisions.Count);
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, job.BytesDone); Assert.Equal(0, files.Published);
                Assert.Equal(moment is "retry" or "auto" ? 1 : 0, files.Attempts);
                Assert.Equal(moment == "retry" ? 2 : 1, decisions.Count);
                Assert.Contains(job.Issues, i => i.Message.Contains(same == true ? "same physical disk" : "cannot tell", StringComparison.Ordinal));
                if (replace) Assert.Equal(previous, File.ReadAllBytes(target)); else Assert.False(File.Exists(target));
                Assert.Equal(replace ? ["copy.bin"] : Array.Empty<string>(), Directory.GetFiles(destination).Select(Path.GetFileName));
                Assert.Contains(provider.Checks, c => c.Changed && c.Refusal is not null && c.Folder == destination);
            }
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5))); }
    }

    private sealed class PublicationFiles(Provider provider, string moment) : PortableFileOperations
    {
        public int Attempts, Published;
        public override void Move(string staged, string target, bool replaceExisting, bool writeThrough = false)
        {
            Attempts++;
            if (Attempts == 1 && moment is "retry" or "auto")
            {
                provider.Changed = true;
                throw new OwnedPublicationException(moment == "auto");
            }
            base.Move(staged, target, replaceExisting, writeThrough); Published++;
        }
        private sealed class OwnedPublicationException : IOException
        {
            public OwnedPublicationException(bool sharing) : base("owned recovery publication failure")
            { HResult = sharing ? unchecked((int)0x80070020) : unchecked((int)0x8007001F); }
        }
    }

    private sealed class Provider(byte[] bytes, string moment, bool? same) : ResourceProvider
    {
        private readonly byte[] _bytes = bytes;
        private readonly string _moment = moment;
        private readonly RecoveryProvider _guard = new() { SharesDisk = (_, _) => same };
        private readonly Location _device = new(Schemes.Recovery, "", new Location(Schemes.Device, "owned-recording-device"));
        public bool Changed;
        public int Opens, Closes, Reads;
        public readonly List<(string Folder, bool Changed, string? Refusal)> Checks = [];
        public override string Scheme => "owned-recovery-publication";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override string? CheckTransferDestination(Location source, string folder)
        {
            string? refusal = Changed ? _guard.CheckTransferDestination(_device, folder) : null;
            Checks.Add((folder, Changed, refusal)); return refusal;
        }
        public override IContentSource OpenContent(ItemRef item) { Opens++; return new Source(this, Opens); }
        private sealed class Source(Provider owner, int ordinal) : IContentSource
        {
            public string DisplayName => "owned recovery publication source"; public long Length => owner._bytes.Length;
            public bool CanSeek => true; public string? LocalPath => null;
            public ContentRevision? GetRevision() => null;
            public int Read(long offset, Span<byte> buffer)
            {
                owner.Reads++;
                if (owner._moment == "read" || owner._moment == "verify" && ordinal == 2) owner.Changed = true;
                int n = (int)Math.Min(buffer.Length, Math.Max(0, owner._bytes.Length - offset));
                owner._bytes.AsSpan((int)offset, n).CopyTo(buffer); return n;
            }
            public void Dispose()
            {
                owner.Closes++;
                if (owner._moment == "close" || owner._moment == "verify-close" && ordinal == 2) owner.Changed = true;
            }
        }
    }
}
