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
        public bool Reparse = true;
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
                ReparseTag = Reparse ? ObservedFact<uint>.Known(Tag, FactScope.Entry, "fixture")
                    : ObservedFact<uint>.Unavailable(FactState.NotApplicable, FactScope.Entry, "fixture", "No observed reparse tag."),
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

    /// <summary>
    /// Allocated reads as the Size column does, so a large value keeps its unit in a narrow column (exact bytes were cut to
    /// their leading digits there, hiding the magnitude); the exact bytes are the tooltip's first line, and it sorts by them.
    /// </summary>
    [Theory]
    [InlineData(4096L, false)]
    [InlineData(123_456_789_012L, true)]
    public void Allocated_reads_as_sizes_do_with_the_exact_bytes_in_its_tooltip(long bytes, bool unit)
    {
        using var io = new DeviceIoScheduler(); var records = new Records { Allocation = bytes };
        using var metadata = new MetadataService(io, () => 0, fileRecords: records);
        string path = Path.Combine(Path.GetTempPath(), "column-subject");
        var value = metadata.Compute(FileDetailsFields.Allocated, path, new EntryData("column-subject", EntryKind.File), TestContext.Current.CancellationToken);
        var cell = Assert.IsType<FileDetailsFields.Cell>(value.Value);
        Assert.Equal(SizeText.Compact(bytes), cell.Text);
        Assert.Equal(unit, cell.Text.EndsWith(" GB", StringComparison.Ordinal));
        Assert.StartsWith(bytes.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " bytes\n", cell.Explanation);
        Assert.Equal(bytes, cell.Key);
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

        // A regular file with a link tag has no qualified count; a Cloud Files placeholder is a regular file that also
        // shows its cloud subtype.
        records.Kind = FileObjectKind.File; records.Tag = ReparseLinks.SymbolicLinkTag; metadata.ForgetField(FileDetailsFields.HardLinks);
        Assert.Equal(MetadataState.Unsupported, metadata.Compute(FileDetailsFields.HardLinks, path, entry, TestContext.Current.CancellationToken).State);
        records.Tag = 0x9000701A; metadata.ForgetField(FileDetailsFields.HardLinks); metadata.ForgetField(type.Id);
        var links = metadata.Compute(FileDetailsFields.HardLinks, path, entry, TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Available, links.State);
        Assert.Equal(7u, metadata.Field(FileDetailsFields.HardLinks)!.CaptureKey(links));
        Assert.Equal("Cloud-managed item", type.CaptureKey(metadata.Compute(type.Id, path, entry, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public void Reparse_type_shows_a_quiet_none_for_ordinary_items_and_explains_each_tag()
    {
        using var io = new DeviceIoScheduler(); var records = new Records { Reparse = false };
        using var metadata = new MetadataService(io, () => 0);
        foreach (var field in FileDetailsFields.Create(records, windows: true)) metadata.Register(field);
        var entry = new EntryData("column-subject", EntryKind.File);
        string path = Path.Combine(Path.GetTempPath(), entry.Name);
        var type = metadata.Field(FileDetailsFields.ReparseType)!;

        // An ordinary item has a real, quiet value rather than a blank cell beside the next column's numbers.
        var none = metadata.Compute(type.Id, path, entry, TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.Available, none.State);
        Assert.Equal("none", type.Format(none.Value));
        Assert.True(type.Quiet!(none.Value));
        Assert.True(type.DetailLines); // its explanation's lines are FileCat's own
        Assert.Contains("No reparse point observed", type.Detail!(none.Value));
        Assert.Contains("WOF compression (compact.exe /EXE, CompactOS) hides its tag", type.Detail!(none.Value));
        Assert.Contains("Source: fixture", type.Detail!(none.Value));
        // It is definite, so it sorts before every type instead of with the unknown values (which sort last both ways).
        var noneKey = Assert.IsType<string>(type.CaptureKey(none));
        foreach (string label in new[] { "App execution alias", "Junction or volume mount", "Symbolic link", "WOF-backed file" })
            Assert.True(string.Compare(noneKey, label, StringComparison.CurrentCulture) < 0, label);

        // A reparse point says what it is, then its tag number and published name.
        records.Reparse = true; metadata.ForgetField(type.Id);
        var link = metadata.Compute(type.Id, path, entry, TestContext.Current.CancellationToken);
        Assert.Equal("Symbolic link", type.Format(link.Value));
        Assert.False(type.Quiet!(link.Value));
        string explained = type.Detail!(link.Value);
        Assert.Contains("follows the path stored in it", explained);
        Assert.Contains("Tag 0xA000000C", explained);
        Assert.Contains("IO_REPARSE_TAG_SYMLINK", explained);
        Assert.Contains("Source: fixture", explained);

        // No other column's value is quiet.
        foreach (string id in new[] { FileDetailsFields.Allocated, FileDetailsFields.HardLinks })
        {
            var field = metadata.Field(id)!;
            Assert.False(field.Quiet!(metadata.Compute(id, path, entry, TestContext.Current.CancellationToken).Value), id);
        }
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
