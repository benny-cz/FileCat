using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class FollowLinkFileCopyTests(ITestOutputHelper output)
{
    public static TheoryData<JobKind, VerifyMode, int, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<JobKind, VerifyMode, int, bool, bool>();
            foreach (var kind in new[] { JobKind.Copy, JobKind.Move })
                foreach (var verify in new[] { VerifyMode.Native, VerifyMode.ReadBack })
                    foreach (int length in new[] { 17, 1048577 })
                        foreach (bool existing in new[] { false, true })
                            foreach (bool fastEligible in new[] { false, true }) cases.Add(kind, verify, length, existing, fastEligible);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Explicitly_followed_file_links_copy_bytes_and_keep_move_sources(JobKind kind, VerifyMode verify, int length, bool existing, bool fastEligible)
    {
        if (new PortableFileOperations().CanCreateSymbolicLinks != true)
            Assert.Skip("This account cannot create symbolic links; actual followed-link copying is unavailable.");
        string root = Directory.CreateTempSubdirectory("fc-follow-file-").FullName;
        JobManager? jobs = null;
        try
        {
            string sourceRoot = Directory.CreateDirectory(Path.Join(root, "source")).FullName;
            string targetRoot = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
            string source = Path.Join(sourceRoot, "notes.txt"), target = Path.Join(targetRoot, "notes.txt"), referent = Path.Join(root, "referent.txt"), sentinel = Path.Join(root, "sentinel");
            byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 31 + 7)).ToArray();
            File.WriteAllBytes(referent, bytes); File.SetLastWriteTimeUtc(referent, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            File.CreateSymbolicLink(source, referent); File.WriteAllText(sentinel, "independent owned file");
            if (existing) File.WriteAllText(target, "base");
            var files = new Files(sourceRoot, fastEligible); var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
            jobs = new JobManager(files, providers, Path.Join(root, "journal")) { MaxConcurrent = 0 };
            int decisions = 0; jobs.DecisionRequested += d =>
            {
                Interlocked.Increment(ref decisions);
                d.Resolve(new Decision(decisions == 1 ? DecisionAction.FollowLink : DecisionAction.Skip));
            };
            var job = jobs.Submit(new JobRequest { Kind = kind, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(targetRoot),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace, Verify = verify } });
            jobs.MaxConcurrent = 4; jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20)); await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            var after = files.TryGetInfo(target);
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            output.WriteLine("FOLLOW_LINK_FILE_COPY " + JsonSerializer.Serialize(new
            {
                Kind = kind.ToString(), Verify = verify.ToString(), length, existing, fastEligible, decisions, State = job.State.ToString(),
                job.ItemsTotal, job.ItemsDone, job.BytesTotal, job.BytesDone, job.VerifyBytesDone,
                After = after is null ? null : new { after.IsLink, after.IsDirectory, after.Size, SHA256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target))) },
                ExpectedHash = hash, SourceHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))), SourceLink = new FileInfo(source).LinkTarget,
                ReferentHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(referent))), Sentinel = File.ReadAllText(sentinel),
                Copies = files.Copies.ToArray(), Deletes = files.Deletes.ToArray(), Names = Directory.GetFileSystemEntries(targetRoot).Select(Path.GetFileName).Order().ToArray(),
                Issues = job.Issues.Select(i => new { i.Message, Outcome = i.Outcome.ToString() }).ToArray(), root, source, target, referent,
                ActualOwnedSymbolicLinksAndPortableFileOperations = true, ControlledLiteralCopyFailureAndVolumeRouting = true,
                NativeWindowsCopyEngineAtomicCASOrCandidateQualified = false,
            }));
            Assert.NotNull(after); Assert.False(after.IsLink); Assert.False(after.IsDirectory); Assert.Equal(length, after.Size);
            Assert.Equal(bytes, File.ReadAllBytes(target)); Assert.Equal(bytes, File.ReadAllBytes(source)); Assert.Equal(bytes, File.ReadAllBytes(referent));
            Assert.Equal(referent, new FileInfo(source).LinkTarget); Assert.Equal("independent owned file", File.ReadAllText(sentinel));
            Assert.Equal(new[] { "notes.txt" }, Directory.GetFileSystemEntries(targetRoot).Select(Path.GetFileName).Order().ToArray()!);
            Assert.Equal(1, decisions); Assert.Equal(1, job.ItemsTotal); Assert.Equal(length, job.BytesDone);
            Assert.NotEmpty(files.Copies); Assert.All(files.Copies, c => Assert.False(c.CopyLinkAsLink)); Assert.Empty(files.Deletes);
            if (kind == JobKind.Copy) Assert.Equal(JobState.Completed, job.State);
            else Assert.Contains(job.Issues, i => i.Message == "Copied the link's contents; the source link and its target were kept.");
        }
        finally
        {
            if (jobs is not null) { foreach (var j in jobs.Jobs) j.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(3))); }
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(root)); Directory.Delete(root, recursive: true); Assert.False(Directory.Exists(root));
        }
    }

    private sealed record Copy(string Source, string Destination, bool CopyLinkAsLink, bool DisablePreallocation);
    private sealed class Files(string sourceRoot, bool fastEligible) : PortableFileOperations
    {
        public ConcurrentQueue<Copy> Copies { get; } = new();
        public ConcurrentQueue<string> Deletes { get; } = new();
        public override bool CopyPreservesMetadata => fastEligible;
        public override string GetVolumeRoot(string path) => path.StartsWith(sourceRoot, StringComparison.Ordinal) ? "<owned-source-volume>" : "<owned-target-volume>";
        public override VolumeInfo GetVolumeInfo(string path) => VolumeInfo.Unknown(path) with { SupportsNamedStreams = fastEligible };
        public override bool TryCopyLink(string source, string destination, bool isDirectory, out string? error) { error = "Owned forced literal-link creation failure"; return false; }
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            Copies.Enqueue(new(source, destination, options.CopyLinkAsLink, options.DisablePreallocation)); base.CopyFile(source, destination, options, progress, ct);
        }
        public override void DeleteFile(string path) { Deletes.Enqueue(path); base.DeleteFile(path); }
    }
}
