using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ResumeLifetimeEvidenceTests(ITestOutputHelper output)
{
    public static TheoryData<int, string, string, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, string, string, bool, bool>();
            foreach (int length in new[] { 131073, 262145 })
            foreach (string stage in new[] { "revision", "head-read", "tail-read", "read-after-resume", "unchanged", "changed" })
            foreach (string exception in stage is "unchanged" or "changed" ? new[] { "none" } : new[] { "not-supported", "disposed", "invalid-op", "data", "cancel", "io", "denied" })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(length, stage, exception, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_reopened_source_is_disposed_after_refused_validation_or_owned_until_copy_finishes(int length, string stage, string exception, bool replace, bool readBack)
    {
        using var owned = new OwnedDirectory();
        byte[] original = Pattern(length), alternate = original.Select(b => (byte)(b ^ 0x5A)).ToArray(), previous = "prior resumed destination\n"u8.ToArray();
        string originalPath=owned.File("original.dat",original), alternatePath=owned.File("alternate.dat",alternate);
        var provider=new Provider(originalPath,alternatePath,length,stage,exception);
        try
        {
            using var setup=new Setup(owned.Path,provider);if(replace)setup.Write("/up/copy.dat",previous);
            var job=await setup.Run(new ItemRef(new Location(provider.Scheme,"/"),"copy.dat",EntryKind.File,length),readBack);
            bool accepted=stage is "unchanged" or "changed";
            var expected=new SortedDictionary<string,string>(StringComparer.Ordinal);
            if(accepted)expected["/up/copy.dat"]=Hash(stage=="changed"?alternate:original);
            else if(replace)expected["/up/copy.dat"]=Hash(previous);
            output.WriteLine("RESUME_LIFETIME_EVIDENCE "+JsonSerializer.Serialize(new
            {
                length,stage,exception,replace,readBack,accepted,job.State,provider.Attempts,provider.Opens,provider.Disposals,provider.DisposeCalls,
                provider.Reads,Snapshot=Snapshot(setup.Destination),ExpectedSnapshot=expected,OriginalSourceSHA256=Hash(File.ReadAllBytes(originalPath)),OriginalSHA256=Hash(original),
                AlternateSourceSHA256=Hash(File.ReadAllBytes(alternatePath)),AlternateSHA256=Hash(alternate),PreviousSHA256=Hash(previous),
                job.BytesDone,job.VerifyBytesDone,job.VerifyBytesTotal,CompletedRoots=job.CompletedRootIndices.ToArray(),setup.Decisions,DecisionMessages=setup.DecisionMessages.ToArray(),
                Issues=job.Issues.Select(i=>new{i.Severity,i.Message,i.Outcome,i.Cause}).ToArray(),
                ActualOwnedSourceAndDestinationFiles=true,ControlledDropReopenFailureAndMetadata=true,NativeHandlePhysicalServerAtomicOrCandidateQualified=false,
                ReopenedSourcesStillOwnedBeforeFixtureCleanup=provider.Opens-provider.Disposals,
            }));
            Assert.Equal(expected,Snapshot(setup.Destination));Assert.Equal(original,File.ReadAllBytes(originalPath));Assert.Equal(alternate,File.ReadAllBytes(alternatePath));
            Assert.Equal(provider.Opens,provider.Disposals);Assert.Equal(provider.Opens,provider.DisposeCalls);Assert.Equal(exception is "io" or "denied"?1:0,setup.Decisions);
            Assert.Equal(accepted?length:0,job.BytesDone);Assert.Equal(accepted&&readBack?2L*length:0,job.VerifyBytesDone);Assert.Equal(accepted&&readBack?2L*length:0,job.VerifyBytesTotal);
            Assert.Equal(accepted?new[]{0}:Array.Empty<int>(),job.CompletedRootIndices.ToArray());
            if(accepted)
            {
                Assert.Equal(JobState.Completed,job.State);Assert.Contains(job.Issues,i=>i.Message.Contains(stage=="changed"?"copied again from the start":"resumed at",StringComparison.Ordinal));
                Assert.Equal(readBack?3:2,provider.Attempts);
            }
            else
            {
                Assert.Equal(exception=="cancel"?JobState.Canceled:JobState.Failed,job.State);Assert.Equal(2,provider.Attempts);
                if(exception is "io" or "denied" && stage is "revision" or "head-read" or "tail-read")
                {
                    Assert.Contains(setup.DecisionMessages,m=>m.Contains(exception=="denied"?"access":"owned resumed failure",StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(job.Issues,i=>i.Message.Contains("owned controlled connection drop",StringComparison.Ordinal));
                }
                else if(exception!="cancel")Assert.Contains(job.Issues,i=>i.Message.Contains(exception=="denied"?"access":"owned resumed failure",StringComparison.OrdinalIgnoreCase));
            }
        }
        finally { provider.CloseOwnedFixtureSources(); }
    }

    private sealed class Provider(string original,string alternate,int length,string stage,string exception):ResourceProvider
    {
        public override string Scheme=>"owned-resume-lifetime";
        public int Attempts,Opens,Disposals,DisposeCalls;
        private readonly int _length=length;
        private readonly string _stage=stage;
        public readonly List<string> Reads=[];
        private readonly List<Source> _sources=[];
        public override string GetDisplayPath(Location location)=>Scheme+":"+location.Path;
        public override Location? GetParent(Location location)=>null;
        public override LocationCapabilities GetCapabilities(Location location)=>LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent,in EntryData entry)=>null;
        public override Task EnumerateAsync(Location location,IEnumerationSink sink,CancellationToken ct)=>Task.CompletedTask;
        private void Fail()=>throw exception switch
        {
            "not-supported"=>new NotSupportedException("owned resumed failure"),"disposed"=>new ObjectDisposedException("owned resumed failure"),
            "invalid-op"=>new InvalidOperationException("owned resumed failure"),"data"=>new InvalidDataException("owned resumed failure"),
            "io"=>new IOException("owned resumed failure"),"denied"=>new UnauthorizedAccessException("owned resumed failure"),_=>new OperationCanceledException("owned resumed failure"),
        };
        public override IContentSource OpenContent(ItemRef item)
        {
            int attempt=Interlocked.Increment(ref Attempts);var source=new Source(this,attempt,attempt>1&&stage=="changed"?alternate:original);_sources.Add(source);
            Interlocked.Increment(ref Opens);return source;
        }
        public void CloseOwnedFixtureSources(){foreach(var source in _sources)source.CloseFixture();}
        private sealed class Source(Provider owner,int attempt,string path):IContentSource
        {
            private readonly FileContentSource _file=new(path);private bool _closed;
            public string DisplayName=>"owned resumed source";public long Length=>owner._length;public bool CanSeek=>true;public string? LocalPath=>null;
            public ContentRevision? GetRevision()
            {
                if(attempt==2&&owner._stage=="revision")owner.Fail();
                return new(owner._length,attempt>1&&owner._stage=="changed"?700000000000000001L:700000000000000000L,"owned-resume-id");
            }
            public int Read(long offset,Span<byte> buffer)
            {
                owner.Reads.Add($"{attempt}:{offset}:{buffer.Length}");
                if(attempt==1)
                {
                    if(offset>=131072)throw new IOException("owned controlled connection drop");
                    return _file.Read(offset,buffer[..(int)Math.Min(buffer.Length,131072-offset)]);
                }
                if(attempt==2&&(owner._stage=="head-read"&&offset==0||owner._stage=="tail-read"&&offset==65536||owner._stage=="read-after-resume"&&offset>=131072))owner.Fail();
                return _file.Read(offset,buffer);
            }
            public void Dispose(){Interlocked.Increment(ref owner.DisposeCalls);CloseFixture();}
            public void CloseFixture(){if(_closed)return;_closed=true;_file.Dispose();Interlocked.Increment(ref owner.Disposals);}
        }
    }
    private sealed class Setup : IDisposable
    {
        private readonly JobManager _jobs;
        public int Decisions;
        public readonly List<string> DecisionMessages = [];
        public Job? Current;
        public string Destination { get; }
        public Setup(string directory, ResourceProvider source)
        {
            Destination = Directory.CreateDirectory(System.IO.Path.Join(directory, "destination")).FullName;
            var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(source);
            _jobs = new JobManager(new PortableFileOperations(), providers, System.IO.Path.Join(directory, "journal")) { MaxConcurrent = 0 };
            _jobs.DecisionRequested += d => { Interlocked.Increment(ref Decisions); DecisionMessages.Add(d.Request.Message); d.Resolve(new Decision(DecisionAction.Skip)); };
        }
        public void Write(string path, byte[] bytes)
        {
            Assert.StartsWith("/up/", path); string actual = System.IO.Path.Join(Destination, path[4..].Replace('/', System.IO.Path.DirectorySeparatorChar));
            Assert.StartsWith(Destination + System.IO.Path.DirectorySeparatorChar, System.IO.Path.GetFullPath(actual));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(actual)!); File.WriteAllBytes(actual, bytes);
        }
        public async Task<Job> Run(ItemRef source, bool readBack, Core.Selection.Mask? filter = null)
        {
            Current = _jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [source], Destination = Location.FileSystem(Destination),
                Options = new TransferOptions { Verify = readBack ? VerifyMode.ReadBack : VerifyMode.Native, Conflicts = ConflictPolicy.Replace, Filter = filter } });
            _jobs.MaxConcurrent = 4; _jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!Current.State.IsFinished() || _jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20)); await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            return Current;
        }
        public void Dispose()
        {
            foreach (var job in _jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !_jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
        }
    }

    private sealed class OwnedDirectory : IDisposable
    {
        private static readonly string Root = System.IO.Path.Join(System.IO.Path.GetTempPath(), "filecat-resume-lifetime");
        public string Path { get; } = System.IO.Path.Join(Root, Guid.NewGuid().ToString("N"));
        public OwnedDirectory() => Directory.CreateDirectory(Path);
        public string File(string name, byte[] bytes) { string p = System.IO.Path.Join(Path, name); System.IO.File.WriteAllBytes(p, bytes); return p; }
        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path); Assert.Equal(System.IO.Path.GetFileName(Path), System.IO.Path.GetRelativePath(Root, resolved)); Directory.Delete(resolved, true);
        }
    }
    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static SortedDictionary<string, string> Snapshot(string destination)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(destination, "*", SearchOption.AllDirectories))
            files["/up/" + System.IO.Path.GetRelativePath(destination, path).Replace(System.IO.Path.DirectorySeparatorChar, '/')] = Hash(File.ReadAllBytes(path));
        return files;
    }
}
