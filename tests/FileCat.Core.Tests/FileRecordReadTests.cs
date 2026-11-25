using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>
/// DEC-05: the full file-system record (D-56, <see cref="IFileRecords.Read"/>) read through the details reader. It runs on
/// the item's device worker under the details admission, between two metadata-only summaries of the same path, holds the
/// process's one record reservation, shares the reader's single running and pending request with the details tiers, and
/// carries the output rule the details window applies before Copy or Save.
/// </summary>
public sealed class FileRecordReadTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-11-25T09:00:00Z");

    private sealed class Records : IFileRecords, IDisposable
    {
        private int _summaries, _records;
        public bool IsSupported => true;
        public int Summaries => Volatile.Read(ref _summaries);
        public int RecordReads => Volatile.Read(ref _records);
        public System.Collections.Concurrent.ConcurrentQueue<string> Calls { get; } = new();
        public volatile bool Block;
        public volatile bool OnWorker = true, Admitted = true;
        public int MacPolicy = -1;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _summaries);
            Calls.Enqueue("summary");
            return new(path, At.AddMinutes(call), Guid.NewGuid())
            {
                Identity = ObservedFact<DetailsIdentity>.Known(new("volume", "object"), FactScope.Entry, "fixture"),
                FileEncryption = ObservedFact<bool>.Known(false, FactScope.Entry, "fixture"),
            };
        }

        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) =>
            FileDetailsReport.Metadata(ReadSummary(path, ct));

        public InspectionReport Read(string path, CancellationToken ct)
        {
            int call = Interlocked.Increment(ref _records);
            Calls.Enqueue("record");
            if (!DeviceIoScheduler.IsDeviceWorker) OnWorker = false;
            if (!FileObservationRequests.HasAdmission) Admitted = false;
            if (OperatingSystem.IsMacOS()) MacPolicy = FileSystem.MacMaterializationPolicy.Get();
            if (Block)
            {
                Entered.Set();
                Release.Wait(TimeSpan.FromSeconds(30));
            }
            return new InspectionReport("File-system record · fixture", [new("Item", [("Read", "#" + call)])], []);
        }

        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    private static FileDetailsSnapshot Snapshot(string id, ObservedFact<bool> efs, FactState identity = FactState.Known) => new("item", At, Guid.NewGuid())
    {
        Identity = identity == FactState.Known ? ObservedFact<DetailsIdentity>.Known(new("volume", id), FactScope.Entry, "fixture")
            : ObservedFact<DetailsIdentity>.Unavailable(identity, FactScope.Entry, "fixture", "fixture"),
        FileEncryption = efs,
    };

    private static ObservedFact<bool> Efs(bool encrypted) => ObservedFact<bool>.Known(encrypted, FactScope.Entry, "fixture");
    private static ObservedFact<bool> Efs(FactState state) => ObservedFact<bool>.Unavailable(state, FactScope.Entry, "fixture", "fixture");
    private static readonly InspectionReport Empty = new("File-system record · fixture", [], []);

    [Fact]
    public async Task The_record_is_read_on_the_device_worker_between_two_summaries_of_the_same_path()
    {
        var ct = TestContext.Current.CancellationToken;
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var records = new Records();
        using var reader = new FileDetailsReader(records, io, "record-" + Guid.NewGuid().ToString("N"), "captured");
        var observation = await reader.ReadRecordAsync(ct).WaitAsync(TimeSpan.FromSeconds(30), ct);
        Assert.Equal(["summary", "record", "summary"], records.Calls);
        Assert.True(records.OnWorker, "The record is read by the item's device worker.");
        Assert.True(records.Admitted, "The record read holds the details observation admission.");
        Assert.Equal("#1", Assert.Single(observation.Record.Sections).Fields[0].Value);
        Assert.Equal(At.AddMinutes(1), observation.Before.ObservedAt);
        Assert.Equal(At.AddMinutes(2), observation.After.ObservedAt);
        Assert.Equal(ReportContentKind.Payload, observation.ContentKind);
        Assert.Null(observation.OutputRefusal(windows: true));
        Assert.Null(observation.OutputRefusal(windows: false));
    }

    [Fact]
    public void Output_is_refused_unless_one_object_is_bound_and_on_Windows_no_EFS_value_can_be_decrypted()
    {
        // Bound and clear: either platform publishes. A volume without EFS (FAT, exFAT, ReFS) is clear too.
        Assert.Null(new FileRecordObservation(Snapshot("a", Efs(false)), Empty, Snapshot("a", Efs(false))).OutputRefusal(windows: true));
        Assert.Null(new FileRecordObservation(Snapshot("a", Efs(FactState.Unsupported)), Empty, Snapshot("a", Efs(FactState.Unsupported))).OutputRefusal(windows: true));
        // Not bound: on every platform.
        foreach (bool windows in new[] { true, false })
        {
            Assert.Contains("changed while its record was read",
                new FileRecordObservation(Snapshot("a", Efs(false)), Empty, Snapshot("b", Efs(false))).OutputRefusal(windows));
            Assert.Contains("identity could not be read",
                new FileRecordObservation(Snapshot("a", Efs(false), FactState.Failed), Empty, Snapshot("a", Efs(false))).OutputRefusal(windows));
            Assert.Contains("identity could not be read",
                new FileRecordObservation(Snapshot("a", Efs(false)), Empty, Snapshot("a", Efs(false), FactState.Denied)).OutputRefusal(windows));
        }
        // EFS: encrypted before or after, or unknown, refuses on Windows; elsewhere there is no EFS.
        Assert.Contains("is EFS-encrypted", new FileRecordObservation(Snapshot("a", Efs(true)), Empty, Snapshot("a", Efs(false))).OutputRefusal(windows: true));
        Assert.Contains("is EFS-encrypted", new FileRecordObservation(Snapshot("a", Efs(false)), Empty, Snapshot("a", Efs(true))).OutputRefusal(windows: true));
        Assert.Contains("is unknown", new FileRecordObservation(Snapshot("a", Efs(FactState.Failed)), Empty, Snapshot("a", Efs(false))).OutputRefusal(windows: true));
        Assert.Contains("is unknown", new FileRecordObservation(Snapshot("a", Efs(false)), Empty, Snapshot("a", Efs(FactState.NotInspected))).OutputRefusal(windows: true));
        Assert.Null(new FileRecordObservation(Snapshot("a", Efs(true)), Empty, Snapshot("a", Efs(true))).OutputRefusal(windows: false));
        Assert.Null(new FileRecordObservation(Snapshot("a", Efs(FactState.Failed)), Empty, Snapshot("a", Efs(FactState.Failed))).OutputRefusal(windows: false));
    }

    [Fact]
    public async Task Cancellation_retires_a_record_at_once_while_its_started_read_keeps_the_permit_and_the_process_reservation()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = "record-" + Guid.NewGuid().ToString("N"), other = device + "-other";
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var held = new Records { Block = true };
        using var second = new Records();
        using var heldReader = new FileDetailsReader(held, io, device, "held");
        using var secondReader = new FileDetailsReader(second, io, other, "second");
        // Three of the device's four details permits; the record read takes the fourth.
        using var lease1 = await DetailsAdmission.AcquireAsync(device, ct);
        using var lease2 = await DetailsAdmission.AcquireAsync(device, ct);
        using var lease3 = await DetailsAdmission.AcquireAsync(device, ct);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<IDisposable>? waiting = null;
        try
        {
            var result = heldReader.ReadRecordAsync(cancel.Token);
            Assert.True(held.Entered.Wait(TimeSpan.FromSeconds(30), ct));
            cancel.Cancel();
            Assert.True(result.IsCanceled); // publication retires before the native read returns
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
            waiting = DetailsAdmission.AcquireAsync(device, ct);
            Assert.False(waiting.IsCompleted); // its real owner still occupies the fourth device slot

            // Another item's record, on another device, waits for the one process reservation without reading, and says so.
            int waited = 0;
            var queued = secondReader.ReadRecordAsync(ct, () => Interlocked.Increment(ref waited));
            for (int i = 0; i < 100 && Volatile.Read(ref waited) == 0; i++) await Task.Delay(20, ct);
            Assert.Equal(1, Volatile.Read(ref waited));
            await Task.Delay(200, ct);
            Assert.False(queued.IsCompleted);
            Assert.Equal(0, second.Summaries + second.RecordReads);
            // A summary on that device does not wait for it.
            Assert.NotNull(await secondReader.ReadAsync(FileDetailsTier.Summary, ct).WaitAsync(TimeSpan.FromSeconds(30), ct));
            Assert.True(queued.IsCanceled, "The summary request retired the waiting record request of the same reader.");
            queued = secondReader.ReadRecordAsync(ct);

            held.Release.Set();
            (await waiting.WaitAsync(TimeSpan.FromSeconds(30), ct)).Dispose();
            waiting = null;
            var record = await queued.WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.Equal("#1", record.Record.Sections[0].Fields[0].Value);
            Assert.Equal(1, held.RecordReads);
            Assert.Equal(1, held.Summaries); // the canceled read never read its "after" summary
        }
        finally
        {
            held.Release.Set();
            if (waiting is not null) (await waiting.WaitAsync(TimeSpan.FromSeconds(30), ct)).Dispose();
        }
    }

    [Fact]
    public async Task A_long_record_read_is_never_taken_for_its_device_hanging()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = "record-" + Guid.NewGuid().ToString("N"), queue = device + FileDetailsReader.RecordQueueSuffix;
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromMilliseconds(200), threadsPerDevice: 1, maxThreadsPerDevice: 2);
        using var records = new Records { Block = true };
        using var reader = new FileDetailsReader(records, io, device, "captured");
        var read = reader.ReadRecordAsync(ct);
        try
        {
            Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(30), ct));
            // Control: the watchdog (every 500 ms) does see the long call, on the record's own queue.
            for (int i = 0; i < 50 && io.GetHealth(queue) != DeviceHealth.NotResponding; i++) await Task.Delay(100, ct);
            Assert.Equal(DeviceHealth.NotResponding, io.GetHealth(queue));
            // The drive itself stays responsive (Disk usage and analysis stop on a drive that is not), and its own
            // queue (one worker here) still runs other work. Its scheduler directly: the process-wide details admission
            // is shared with every other test running meanwhile.
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth(device));
            Assert.True(await io.RunOwned(device, IoPriority.Interactive, _ => DeviceIoScheduler.IsDeviceWorker, ct).WaitAsync(TimeSpan.FromSeconds(30), ct));
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth(device));
        }
        finally { records.Release.Set(); }
        Assert.Equal("#1", (await read.WaitAsync(TimeSpan.FromSeconds(30), ct)).Record.Sections[0].Fields[0].Value);
    }

    [Fact]
    public async Task A_record_request_and_a_tier_request_retire_each_other_on_the_same_reader()
    {
        var ct = TestContext.Current.CancellationToken;
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var records = new Records { Block = true };
        using var reader = new FileDetailsReader(records, io, "record-" + Guid.NewGuid().ToString("N"), "captured");
        var record = reader.ReadRecordAsync(ct);
        Assert.True(records.Entered.Wait(TimeSpan.FromSeconds(30), ct));
        var summary = reader.ReadAsync(FileDetailsTier.Summary, ct);
        Assert.True(record.IsCanceled);
        var again = reader.ReadRecordAsync(ct);
        Assert.True(summary.IsCanceled);
        records.Block = false;
        records.Release.Set();
        var latest = await again.WaitAsync(TimeSpan.FromSeconds(30), ct);
        Assert.Equal("#2", latest.Record.Sections[0].Fields[0].Value);
        reader.Dispose();
        Assert.True(reader.ReadRecordAsync(ct).IsCanceled);
    }

    [Fact]
    public async Task An_unsupported_reader_fails_the_record_and_returns_the_permit_and_the_reservation()
    {
        var ct = TestContext.Current.CancellationToken;
        string device = "record-" + Guid.NewGuid().ToString("N");
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using (var reader = new FileDetailsReader(new NoFileRecords(), io, device, "absent"))
            await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadRecordAsync(ct).WaitAsync(TimeSpan.FromSeconds(30), ct));
        using var records = new Records();
        using var next = new FileDetailsReader(records, io, device, "captured");
        Assert.NotNull(await next.ReadRecordAsync(ct).WaitAsync(TimeSpan.FromSeconds(30), ct));
        var leases = new List<IDisposable>();
        try { for (int i = 0; i < 4; i++) leases.Add(await DetailsAdmission.AcquireAsync(device, ct).WaitAsync(TimeSpan.FromSeconds(30), ct)); }
        finally { foreach (var lease in leases) lease.Dispose(); }
    }

    [Fact]
    public async Task On_macOS_the_record_is_read_with_dataless_downloads_disabled()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The dataless materialization policy is a macOS thread policy.");
        var ct = TestContext.Current.CancellationToken;
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var records = new Records();
        using var reader = new FileDetailsReader(records, io, "record-" + Guid.NewGuid().ToString("N"), "captured");
        await reader.ReadRecordAsync(ct).WaitAsync(TimeSpan.FromSeconds(30), ct);
        Assert.Equal(FileSystem.MacMaterializationPolicy.Off, records.MacPolicy);
    }

    [Fact]
    public async Task The_real_Unix_record_of_an_ordinary_temporary_file_is_bound_to_it_and_may_be_published()
    {
        // Windows runs the same route with WindowsFileRecords in FileCat.App.Tests (FileSystemRecordViewTests).
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The Linux and macOS file-system record.");
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-record-read-").FullName;
        string path = Path.Join(root, "ordinary.txt");
        try
        {
            File.WriteAllText(path, "ordinary contents");
            using var io = new DeviceIoScheduler();
            using var reader = new FileDetailsReader(new UnixFileRecords(), io, "record-" + Guid.NewGuid().ToString("N"), path);
            var observation = await reader.ReadRecordAsync(ct).WaitAsync(TimeSpan.FromSeconds(60), ct);
            Assert.StartsWith("File-system record", observation.Record.Format);
            Assert.Equal(FactState.Known, observation.Before.Identity.State);
            Assert.Equal(observation.Before.Identity.Value, observation.After.Identity.Value);
            Assert.Null(observation.OutputRefusal());
            Assert.Contains(observation.Record.Sections, s => s.Title == "Timestamp checks");
            if (OperatingSystem.IsLinux() && System.Text.RegularExpressions.Regex.IsMatch(observation.Record.Format, "· ext[234]$"))
            {
                // The raw part is reached the same way: root reads the inode from the volume, anyone else is told it needs root.
                var inode = Assert.Single(observation.Record.Sections, s => s.Title.StartsWith("Inode on disk (", StringComparison.Ordinal));
                if (Environment.IsPrivilegedProcess) Assert.NotEmpty(inode.Fields);
                else Assert.EndsWith("which needs root.", string.Join(" ", inode.Lines), StringComparison.Ordinal);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
