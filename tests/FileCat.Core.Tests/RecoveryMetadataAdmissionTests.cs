using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>Owned staging bytes and recorded metadata calls; topology answers are controlled, no device opens.</summary>
public sealed class RecoveryMetadataAdmissionTests(ITestOutputHelper output)
{
    public static IEnumerable<object?[]> Cases()
    {
        foreach (string moment in new[] { "read", "length", "revision" })
        foreach (bool? same in new bool?[] { true, null, false })
        foreach (bool marked in new[] { false, true }) yield return [moment, same, marked];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Origin_metadata_does_not_write_after_recovery_destination_admission_changes(string moment, bool? same, bool marked)
    {
        using var root = new TempDir();
        string destination = root.Dir("destination"), target = Path.Combine(destination, "copy.bin");
        byte[] bytes = "complete owned metadata admission bytes"u8.ToArray();
        string marker = root.File("source-marker.bin", "complete owned source container marker");
        byte[] markerBefore = File.ReadAllBytes(marker);
        var provider = new Provider(bytes, moment, same, marked);
        var files = new MetadataFiles(provider);
        var providers = new ProviderRegistry(); providers.Register(provider); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Combine(root.Path, "journal"));
        jobs.DecisionRequested += pending => pending.Resolve(new Decision(DecisionAction.Skip));
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [new(new Location(provider.Scheme, ""), "copy.bin", EntryKind.File, bytes.Length)],
                Destination = Location.FileSystem(destination),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned metadata job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new
            {
                moment, same, marked, job.State, job.BytesDone, provider.Changed, provider.Opens, provider.Closes,
                files.Calls, files.RefusedCalls, MetadataCalls = files.Records,
                Checks = provider.Checks.Select(c => new { c.Folder, c.Changed, c.Refusal }),
                KnownSourceBytes = Convert.ToBase64String(markerBefore), SourceBytesAfter = Convert.ToBase64String(File.ReadAllBytes(marker)),
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                DestinationFiles = Directory.GetFiles(destination).Select(p => new { Name = Path.GetFileName(p), Bytes = Convert.ToBase64String(File.ReadAllBytes(p)) }),
                NativeMetadataOrPhysicalTopologyNotClaimed = true,
            }));
            Assert.True(provider.Changed); Assert.Equal(1, provider.Opens); Assert.Equal(1, provider.Closes);
            Assert.Equal(markerBefore, File.ReadAllBytes(marker)); Assert.Equal(0, files.RefusedCalls);
            if (same == false)
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(bytes.Length, job.BytesDone);
                Assert.Equal(bytes, File.ReadAllBytes(target)); Assert.Equal(marked ? 1 : 0, files.Calls);
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, job.BytesDone); Assert.Equal(0, files.Calls);
                Assert.False(File.Exists(target)); Assert.Empty(Directory.GetFiles(destination));
                Assert.Contains(provider.Checks, c => c.Changed && c.Refusal is not null && c.Folder == destination);
            }
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5))); }
    }

    private sealed class MetadataFiles(Provider provider) : PortableFileOperations
    {
        public int Calls, RefusedCalls;
        public readonly List<object> Records = [];
        public override bool WriteOriginMark(string path, string mark)
        {
            Calls++;
            string? refusal = provider.CheckTransferDestination(new Location(provider.Scheme, ""), Path.GetDirectoryName(path)!);
            if (refusal is not null) RefusedCalls++;
            Records.Add(new { Path = path, Mark = mark, StagedBytes = Convert.ToBase64String(File.ReadAllBytes(path)), Refusal = refusal });
            return true;
        }
    }

    private sealed class Provider(byte[] bytes, string moment, bool? same, bool marked) : ResourceProvider, IOriginMarkSource
    {
        private readonly RecoveryProvider _guard = new() { SharesDisk = (_, _) => same };
        private readonly Location _device = new(Schemes.Recovery, "", new Location(Schemes.Device, "owned-recording-device"));
        public bool Changed;
        public int Opens, Closes, Reads, Revisions;
        public readonly List<(string Folder, bool Changed, string? Refusal)> Checks = [];
        public override string Scheme => "owned-recovery-metadata";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public string? GetOriginMark(Location container) => marked ? "[ZoneTransfer]\nZoneId=3\nHostUrl=https://owned.invalid/source\n" : null;
        public override string? CheckTransferDestination(Location source, string folder)
        {
            string? refusal = Changed ? _guard.CheckTransferDestination(_device, folder) : null;
            Checks.Add((folder, Changed, refusal)); return refusal;
        }
        public override IContentSource OpenContent(ItemRef item) { Opens++; return new Source(this, bytes, moment); }
        private sealed class Source(Provider owner, byte[] bytes, string moment) : IContentSource
        {
            public string DisplayName => "owned recovery metadata source";
            public long Length { get { if (moment == "length" && owner.Reads > 0) owner.Changed = true; return bytes.Length; } }
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision() { if (++owner.Revisions > 1 && moment == "revision") owner.Changed = true; return null; }
            public int Read(long offset, Span<byte> buffer)
            {
                owner.Reads++; if (moment == "read") owner.Changed = true;
                int n = (int)Math.Min(buffer.Length, Math.Max(0, bytes.Length - offset));
                bytes.AsSpan((int)offset, n).CopyTo(buffer); return n;
            }
            public void Dispose() => owner.Closes++;
        }
    }
}
