using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class FileDetailsFieldsTests
{
    private sealed class Records : IFileRecords
    {
        public bool IsSupported => true;
        public int Calls;
        public bool Fail;
        public FileObjectKind Kind = FileObjectKind.File;
        public long Allocation = 4096;
        public uint Tag = ReparseLinks.SymbolicLinkTag;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy full reader must not run.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Column requested the full reader.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            Assert.True(DeviceIoScheduler.IsDeviceWorker); Assert.True(FileObservationRequests.IsAdmittedFor(PathUtil.GetDeviceKey(path)));
            Calls++;
            if (Fail) throw new IOException("fixture failure");
            return new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
            {
                Kind = Kind,
                Allocated = ObservedFact<long>.Known(Allocation, Kind == FileObjectKind.Directory ? FactScope.Entry : FactScope.AllForks, "fixture"),
                LinkCount = ObservedFact<uint>.Known(7, FactScope.Entry, "fixture"),
                ReparseTag = ObservedFact<uint>.Known(Tag, FactScope.Entry, "fixture"),
            };
        }
    }

    [Fact]
    public void Qualified_offline_column_admission_is_narrow_and_folder_allocation_has_own_object_scope()
    {
        using var io = new DeviceIoScheduler(); var records = new Records { Kind = FileObjectKind.Directory };
        using var metadata = new MetadataService(io, () => 0, fileRecords: records);
        string path = Path.Combine(Path.GetTempPath(), "column-subject");
        var directory = new EntryData("column-subject", EntryKind.Directory) { Flags = EntryFlags.Offline };
        var allocated = metadata.Compute(FileDetailsFields.Allocated, path, directory, TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Available, allocated.State);
        var cell = Assert.IsType<FileDetailsFields.Cell>(allocated.Value);
        Assert.Equal(4096L, cell.Key); Assert.Contains("contents are not counted", cell.Explanation);
        Assert.Equal(MetadataState.Absent, metadata.Compute(FileDetailsFields.HardLinks, path, directory, TestContext.Current.CancellationToken).State);
        var offlineFile = directory with { Kind = EntryKind.File };
        Assert.Equal(MetadataState.Unsupported, metadata.Compute("hidden", path, offlineFile, TestContext.Current.CancellationToken).State);
        Assert.Equal(1, records.Calls);
    }

    [Fact]
    public void Native_cells_expire_without_size_time_changes_and_last_known_values_have_unknown_sort_keys()
    {
        using var io = new DeviceIoScheduler(); var records = new Records(); long now = 0;
        using var metadata = new MetadataService(io, () => now, fileRecords: records);
        var entry = new EntryData("column-subject", EntryKind.File, 10, 123);
        string path = Path.Combine(Path.GetTempPath(), entry.Name);
        var field = metadata.Field(FileDetailsFields.Allocated)!;
        var first = metadata.Compute(field.Id, path, entry, TestContext.Current.CancellationToken);
        Assert.Equal(4096L, field.CaptureKey(first));
        now = 20_001; records.Fail = true;
        Assert.Null(field.CaptureKey(metadata.Peek(field.Id, path, entry, slowLocation: false)));
        Assert.Equal(1, records.Calls); // expiry qualification itself is cache-only
        var stale = metadata.Compute(field.Id, path, entry, TestContext.Current.CancellationToken);
        Assert.Equal(MetadataObservation.LastKnown, stale.Observation); Assert.Null(field.CaptureKey(stale));
        now += 20_001; records.Fail = false; records.Allocation = 8192;
        var fresh = metadata.Compute(field.Id, path, entry, TestContext.Current.CancellationToken);
        Assert.Equal(8192L, field.CaptureKey(fresh));
        metadata.ForgetField(field.Id);
        Assert.Equal(MetadataState.NotRequested, metadata.Peek(field.Id, path, entry).State);
        Assert.Equal(3, records.Calls);
    }

    [Fact]
    public void Reparse_type_keys_require_a_recognized_type_and_hard_links_require_a_regular_observation()
    {
        using var io = new DeviceIoScheduler(); var records = new Records();
        using var metadata = new MetadataService(io, () => 0);
        foreach (var field in FileDetailsFields.Create(records, windows: true)) metadata.Register(field);
        var entry = new EntryData("column-subject", EntryKind.File);
        string path = Path.Combine(Path.GetTempPath(), entry.Name);
        var type = metadata.Field(FileDetailsFields.ReparseType)!;
        Assert.Equal("Symbolic link", type.CaptureKey(metadata.Compute(type.Id, path, entry, TestContext.Current.CancellationToken)));
        records.Tag = 0x12345678; metadata.ForgetField(type.Id);
        var unknown = metadata.Compute(type.Id, path, entry, TestContext.Current.CancellationToken);
        Assert.Contains("unknown type", type.Format(unknown.Value)); Assert.Null(type.CaptureKey(unknown));
        records.Kind = FileObjectKind.Socket;
        Assert.Equal(MetadataState.Unsupported, metadata.Compute(FileDetailsFields.HardLinks, path, entry, TestContext.Current.CancellationToken).State);
    }

    [Fact]
    public void Ordering_charges_bounded_cells_and_refuses_arbitrary_nested_keys()
    {
        var budget = new IndexMemoryBudget(1024 * 1024);
        using var snapshot = new MetadataOrderSnapshot(budget, maxChars: 1000);
        var cell = new FileDetailsFields.Cell("2+", new string('q', 700), null, Partial: true);
        Assert.True(snapshot.TryAdd(0, new(MetadataState.Available, cell)));
        Assert.True(snapshot.ReservedBytes >= 1400); Assert.True(snapshot.RetainedCharacters >= 702);
        Assert.False(snapshot.TryAdd(1, new(MetadataState.Available, cell)));
        Assert.True(snapshot.Capacity.HasFlag(MetadataCapacity.Characters));
    }
}
