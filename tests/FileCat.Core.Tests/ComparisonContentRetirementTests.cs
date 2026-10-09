using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Compare;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>Cancellation and opening/reading errors keep their meaning when owned content cleanup also fails.</summary>
public sealed class ComparisonContentRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();
            foreach (string scenario in new[] { "left-open-cancel", "left-open-stop", "right-open-cancel", "right-open-stop", "left-read-cancel", "right-read-cancel", "left-read-invalid", "right-read-invalid", "right-open-io", "right-open-denied", "right-open-invalid", "equal", "different" })
                foreach (string cleanup in new[] { "none", "left-io", "right-denied", "both" })
                    foreach (bool sameDevice in new[] { false, true }) cases.Add(scenario, cleanup, sameDevice);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Comparison_retains_primary_errors_and_retires_all_owned_content(string scenario, string cleanup, bool sameDevice)
    {
        using var rig = new Rig(scenario, cleanup, sameDevice);
        bool? value = null;
        Exception? observed = null;
        try { value = await rig.Comparison.ContentEqualAsync(rig.Left, rig.Right, rig.Stop.Token).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
        catch (Exception problem) { observed = problem; }
        bool cancellation = scenario.EndsWith("-cancel", StringComparison.Ordinal);
        bool stopped = scenario.EndsWith("-stop", StringComparison.Ordinal);
        var expectedClose = rig.Provider.Sources.AsEnumerable().Reverse().Select(s => s.CloseFailure).FirstOrDefault(e => e is not null);
        bool primaryRetained = cancellation ? observed is OperationCanceledException canceled && canceled.CancellationToken == rig.Stop.Token
            : stopped ? observed is OperationCanceledException && observed.Message == "Comparison admission stopped."
            : rig.Provider.Primary is { } primary ? ReferenceEquals(primary, observed) : ReferenceEquals(expectedClose, observed);
        var sources = rig.Provider.Sources.Select(s => new { s.Side, s.Path, s.Closes, s.Reads, Released = Exclusive(s.Path), ErrorType = s.CloseFailure?.GetType().Name }).ToArray();
        int other = rig.Io.IsStopped ? await Task.FromResult(42) : await rig.Io.Run("unrelated-owned", IoPriority.Normal, _ => 42).WaitAsync(TimeSpan.FromSeconds(5));
        output.WriteLine("COMPARISON_CONTENT_RETIREMENT " + JsonSerializer.Serialize(new
        {
            scenario, cleanup, sameDevice, Value = value, PrimaryErrorRetained = primaryRetained,
            ErrorType = observed?.GetType().Name, ErrorStack = observed?.StackTrace,
            PrimaryObjectRetained = rig.Provider.Primary is null || ReferenceEquals(rig.Provider.Primary, observed),
            ExpectedCloseType = expectedClose?.GetType().Name, Sources = sources, Calls = rig.Provider.Calls,
            LeftSHA256 = Hash(rig.LeftPath), RightSHA256 = Hash(rig.RightPath), rig.LeftBefore, rig.RightBefore,
            UnrelatedWorkerValue = other, UnrelatedWorkerActuallyCalled = !rig.Io.IsStopped,
            ActualOwnedReadOnlyFileStreams = true, ControlledProviderNotNativeStorageFaultOrCandidate = true,
        }));
        Assert.True(primaryRetained, "Secondary cleanup errors must not replace the operation error or the first standalone close failure.");
        Assert.All(sources, source => { Assert.Equal(1, source.Closes);Assert.True(source.Released); });
        Assert.Equal(scenario.StartsWith("left-open-", StringComparison.Ordinal) || scenario is "right-open-io" or "right-open-denied" or "right-open-invalid" ? 1 : 2, sources.Length);
        Assert.Equal(rig.LeftBefore, Hash(rig.LeftPath));Assert.Equal(rig.RightBefore, Hash(rig.RightPath));Assert.Equal(42, other);
        Assert.All(rig.Provider.Calls, call => Assert.StartsWith("FileCat I/O ", call.Thread));
        if (scenario is "equal" or "different" && expectedClose is null) { Assert.Null(observed);Assert.Equal(scenario == "equal", value); }
        else { Assert.NotNull(observed);Assert.Null(value); }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool Exclusive(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);return true; }
        catch (IOException) { return false; }
    }
    private sealed class Rig : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("fc-comparison-retirement-").FullName;
        public CancellationTokenSource Stop { get; } = new();
        public DeviceIoScheduler Io { get; } = new(hangThreshold: TimeSpan.FromMinutes(1), threadsPerDevice: 1);
        public Provider Provider { get; }
        public ComparisonIo Comparison { get; }
        public string LeftPath { get; }
        public string RightPath { get; }
        public string LeftBefore { get; }
        public string RightBefore { get; }
        public ItemRef Left => new(new Location(Provider.Scheme, "left"), "one.dat", EntryKind.File);
        public ItemRef Right => new(new Location(Provider.Scheme, "right"), "one.dat", EntryKind.File);
        public Rig(string scenario, string cleanup, bool sameDevice)
        {
            LeftPath = Path.Combine(root, "left.dat");RightPath = Path.Combine(root, "right.dat");
            File.WriteAllText(LeftPath, "owned comparison bytes\n");File.WriteAllText(RightPath, scenario == "different" ? "owned comparison other\n" : "owned comparison bytes\n");
            LeftBefore = Hash(LeftPath);RightBefore = Hash(RightPath);
            Provider = new Provider(this, scenario, cleanup, sameDevice);
            var registry = new ProviderRegistry();registry.Register(Provider);Comparison = new ComparisonIo(Io, registry);
        }
        public void Dispose()
        {
            Io.Dispose();
            foreach (var source in Provider.Sources) source.Cleanup();
            Assert.All(Provider.Sources, source => Assert.True(Exclusive(source.Path)));
            Stop.Dispose();
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(root));Directory.Delete(root, recursive: true);Assert.False(Directory.Exists(root));
        }
    }
    private sealed record Call(string Side, string Operation, string Thread);
    private sealed class Provider(Rig rig, string scenario, string cleanup, bool sameDevice) : ResourceProvider
    {
        public List<Source> Sources { get; } = [];
        public List<Call> Calls { get; } = [];
        public Exception? Primary { get; private set; }
        public override string Scheme => "ownedcomparisonretirement";
        public override string GetDeviceKey(Location location) => sameDevice ? Scheme : Scheme + ":" + location.Path;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public void Boundary(string side, string operation)
        {
            Calls.Add(new(side, operation, Thread.CurrentThread.Name ?? ""));
            if (!scenario.StartsWith(side + "-" + operation + "-", StringComparison.Ordinal)) return;
            string mode = scenario[(side.Length + operation.Length + 2)..];
            if (mode == "cancel") rig.Stop.Cancel();
            else if (mode == "stop") rig.Io.Dispose();
            else
            {
                Primary ??= mode switch { "io" => new IOException("Owned primary open failure"), "denied" => new UnauthorizedAccessException("Owned primary open failure"), _ => new InvalidOperationException("Owned primary provider failure") };
                throw Primary;
            }
        }
        public override IContentSource OpenContent(ItemRef item)
        {
            string side = item.Parent.Path;
            if (scenario is "right-open-io" or "right-open-denied" or "right-open-invalid" && side == "right") { Boundary(side, "open");throw new InvalidOperationException("Unreachable owned error path"); }
            Exception? close = side == "left" && cleanup is "left-io" or "both" ? new IOException("Owned left close failure")
                : side == "right" && cleanup is "right-denied" or "both" ? new UnauthorizedAccessException("Owned right close failure") : null;
            var source = new Source(side == "left" ? rig.LeftPath : rig.RightPath, side, this, close);Sources.Add(source);
            Boundary(side, "open");return source;
        }
    }
    private sealed class Source(string path, string side, Provider provider, Exception? closeFailure) : IContentSource
    {
        private readonly FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        private bool closed;
        public string Side => side;
        public string Path => path;
        public int Closes;
        public int Reads;
        public Exception? CloseFailure => closeFailure;
        public string DisplayName => path;
        public string? LocalPath => path;
        public bool CanSeek => true;
        public long Length => stream.Length;
        public ContentRevision? GetRevision() => new(stream.Length, 1);
        public int Read(long offset, Span<byte> buffer) { Reads++;provider.Boundary(side, "read");stream.Position = offset;return stream.Read(buffer); }
        public void Dispose()
        {
            Closes++;Cleanup();if (closeFailure is { } failure) throw failure;
        }
        public void Cleanup() { if (closed) return;closed = true;stream.Dispose(); }
    }
}
