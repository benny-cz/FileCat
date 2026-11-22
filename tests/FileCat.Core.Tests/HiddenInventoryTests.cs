using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class HiddenInventoryTests
{
    private sealed class Data : IHiddenData
    {
        public bool IsSupported => true;
        public ImmutableArray<HiddenItem> Items = [new("same", HiddenKind.Stream, 9), new("same", HiddenKind.NtfsAttribute, 17)];
        public int Inventories, ValueReads, LegacyLists;
        public HiddenKind? Opened;
        public IReadOnlyList<HiddenItem> List(string path) { LegacyLists++; throw new InvalidOperationException(); }
        public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Inventories);
            return new(path, DateTimeOffset.UtcNow, Items,
                [new(HiddenKind.Stream, HiddenInventoryState.Complete, Items.Count(i => i.Kind == HiddenKind.Stream), "test"),
                 new(HiddenKind.NtfsAttribute, request.IncludeEaValues ? HiddenInventoryState.Complete : HiddenInventoryState.NotInspected, Items.Count(i => i.Kind == HiddenKind.NtfsAttribute), "test")], "test") { BoundToObject = true };
        }
        public byte[] Read(string path, HiddenItem item, int max) { ValueReads++; return []; }
        public Stream Open(string path, HiddenItem item) { Opened = item.Kind; return new MemoryStream(); }
        public void Delete(string path, HiddenItem item) { }
    }
    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Items = [];
        public HiddenInventory? Inventory;
        public void AddBatch(ReadOnlySpan<EntryData> batch) => Items.AddRange(batch.ToArray());
        public void ReportIssue(string message) { }
        public void ReportSupplementaryInventory(HiddenInventory inventory) => Inventory = inventory;
    }

    [Fact]
    public async Task Listing_reads_no_values_and_actions_keep_fixed_category_identity_without_combined_lookup()
    {
        var data = new Data();
        var provider = new HiddenDataProvider(data);
        var location = HiddenDataProvider.Of(Path.Combine(Path.GetTempPath(), "subject"));
        var sink = new Sink();
        await provider.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
        Assert.Equal(0, data.ValueReads);
        Assert.Equal(0, data.LegacyLists);
        Assert.Equal("2+", sink.Inventory!.CountText);
        Assert.Contains("not inspected", sink.Inventory.Description);
        var stream = provider.GetItemRef(location, sink.Items[0]);
        var ea = provider.GetItemRef(location, sink.Items[1]);
        Assert.Equal(1, stream.Ordinal); Assert.Equal(3, ea.Ordinal); Assert.NotEqual(stream, ea);
        using (provider.OpenContent(ea)) Assert.Equal(HiddenKind.NtfsAttribute, data.Opened);
        Assert.Equal(1, data.Inventories);
        Assert.Throws<NotSupportedException>(() => provider.OpenContent(new ItemRef(location, "same", EntryKind.File)));
        data.Items = [new("same", HiddenKind.Stream, 9)];
        Assert.Null(provider.Find(location.Container!.Path, ea, TestContext.Current.CancellationToken));
        Assert.Equal(HiddenKind.Stream, provider.Find(location.Container.Path, stream, TestContext.Current.CancellationToken)!.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_retains_both_same_name_marks_and_exact_focus_and_drops_a_missing_category(bool restartDuringLoad)
    {
        using var ui = new TestDispatcher();
        using var io = new DeviceIoScheduler();
        var data = new Data();
        var provider = new HiddenDataProvider(data);
        var providers = new ProviderRegistry(); providers.Register(provider);
        var location = HiddenDataProvider.Of(Path.Combine(Path.GetTempPath(), "subject"));
        var model = await ui.InvokeAsync(() => new ListingModel(providers, io, ui));
        try
        {
            await ui.InvokeAsync(() => model.Load(location));
            await ui.WaitUntilAsync(() => model.State == ListingState.Complete);
            await ui.InvokeAsync(() =>
            {
                foreach (int i in Enumerable.Range(0, model.VisibleCount).Where(i => model.GetVisible(i).Kind != EntryKind.Parent)) model.SetMark(i, true);
                int focus = Enumerable.Range(0, model.VisibleCount).Single(i => model.GetVisible(i).Tag is HiddenEntryTag { Item.Kind: HiddenKind.NtfsAttribute });
                model.SetFocus(focus);
                data.Items = data.Items.Reverse().ToImmutableArray();
                if (restartDuringLoad)
                {
                    // Exercise the loading/restart selection path with already published rows.
                    model.Refresh(); model.Refresh();
                }
                else model.Refresh();
            });
            await ui.WaitUntilAsync(() => !model.IsRefreshing && model.State == ListingState.Complete && data.Inventories >= 2);
            await ui.InvokeAsync(() =>
            {
                Assert.Equal(new[] { 1, 3 }, model.GetSelection().Select(i => i.Ordinal).Order());
                Assert.Equal(3, model.GetItemRef(model.FocusedStoreIndex).Ordinal);
                data.Items = [new("same", HiddenKind.Stream, 9)];
                model.Refresh();
            });
            await ui.WaitUntilAsync(() => !model.IsRefreshing && model.State == ListingState.Complete && model.TotalCount == 1);
            await ui.InvokeAsync(() =>
            {
                Assert.Equal(1, Assert.Single(model.GetSelection()).Ordinal);
                Assert.True(model.TryGetFocused(out var focused)); Assert.Equal(EntryKind.Parent, focused.Kind);
            });
        }
        finally { await ui.InvokeAsync(model.Dispose); }
    }

    [Fact]
    public void Names_are_bounded_exact_and_do_not_replace_malformed_native_bytes()
    {
        Assert.Equal(HiddenInventoryState.Failed, UnixHiddenData.ParseInventoryNames([0xff, 0]).State);
        Assert.Equal(HiddenInventoryState.Failed, UnixHiddenData.ParseInventoryNames("user.missing-terminator"u8).State);
        Assert.Equal(HiddenInventoryState.Changed, UnixHiddenData.ParseInventoryNames("user.x\0user.x\0"u8).State);
        var full = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 1025).Select(i => $"user.{i}\0")));
        var bounded = UnixHiddenData.ParseInventoryNames(full);
        Assert.Equal(HiddenInventoryState.Partial, bounded.State); Assert.Equal(1024, bounded.Items.Length);
    }

    [Fact]
    public void Growing_Unix_attribute_names_report_partial_after_two_retries_without_reading_lengths()
    {
        int probes = 0;
        int fills = 0;
        var result = UnixHiddenData.CollectInventory("fixture", (buffer, size) =>
        {
            if (buffer is null) return 16 + ++probes;
            Assert.Equal(16 + probes, size);
            fills++;
            Marshal.SetLastPInvokeError(34); // ERANGE: names outgrew the reported size
            return -1;
        }, _ => throw new InvalidOperationException("No complete name was returned."), TestContext.Current.CancellationToken);

        Assert.Equal(3, probes);
        Assert.Equal(3, fills);
        Assert.Equal(HiddenInventoryState.Partial, result.State);
        Assert.Empty(result.Items);
        Assert.Contains("kept growing", result.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shrinking_Unix_attribute_names_never_publish_an_exact_complete_count(bool retainOne)
    {
        byte[] probed = "user.one\0user.two\0"u8.ToArray();
        byte[] returned = retainOne ? "user.one\0"u8.ToArray() : [];
        int lengthQueries = 0;
        var result = UnixHiddenData.CollectInventory("fixture", (buffer, _) =>
        {
            if (buffer is null) return probed.Length;
            returned.CopyTo(buffer, 0);
            return returned.Length;
        }, _ => { lengthQueries++; return 4; }, TestContext.Current.CancellationToken);

        Assert.Equal(HiddenInventoryState.Changed, result.State);
        Assert.Equal(retainOne ? new[] { "user.one" } : Array.Empty<string>(), result.Items.Select(item => item.Name));
        Assert.Equal(0, lengthQueries);
        var inventory = new HiddenInventory("fixture", DateTimeOffset.UtcNow, result.Items,
            [new(HiddenKind.Attribute, result.State, result.Items.Length, "fixture")], "fixture");
        Assert.Null(inventory.ExactCount);
    }

    /// <summary>
    /// The Extra data column's text: exact where every category is, a lower bound where a category knows of more than it
    /// lists, and otherwise what kept the count unknown, never a bare "Not inspected" for an access or read problem.
    /// </summary>
    [Theory]
    [InlineData(HiddenInventoryState.Complete, 0, 0, "0")]
    [InlineData(HiddenInventoryState.Complete, 0, 2, "2")]
    [InlineData(HiddenInventoryState.NotInspected, 0, 0, "Not inspected")]
    [InlineData(HiddenInventoryState.NotInspected, 0, 2, "2+")]
    [InlineData(HiddenInventoryState.NotInspected, 1, 0, "1+")]
    [InlineData(HiddenInventoryState.NotInspected, 1, 2, "3+")]
    [InlineData(HiddenInventoryState.Denied, 0, 0, "no access")]
    [InlineData(HiddenInventoryState.Failed, 0, 0, "error")]
    [InlineData(HiddenInventoryState.Changed, 0, 0, "changed")]
    [InlineData(HiddenInventoryState.Unsupported, 0, 0, "—")]
    public void A_count_is_exact_a_lower_bound_or_says_what_kept_it_unknown(HiddenInventoryState eas, int eaCount, int streams, string text)
    {
        ImmutableArray<HiddenItem> items = [.. Enumerable.Range(0, streams).Select(i => new HiddenItem("stream" + i, HiddenKind.Stream, 1))];
        var inventory = new HiddenInventory("fixture", DateTimeOffset.UtcNow, items,
            [new(HiddenKind.Stream, HiddenInventoryState.Complete, streams, "fixture"), new(HiddenKind.NtfsAttribute, eas, eaCount, "fixture")], "fixture");
        Assert.Equal(text, inventory.CountText);
        Assert.Equal(eas == HiddenInventoryState.Complete ? streams : null, inventory.ExactCount);
    }

    [Fact]
    public void Mac_attribute_length_that_requires_materialization_refuses_the_whole_inventory()
    {
        byte[] name = "user.dataless\0"u8.ToArray();
        nint List(byte[]? buffer, nint size)
        {
            if (buffer is not null) name.CopyTo(buffer, 0);
            return name.Length;
        }

        var error = Assert.Throws<IOException>(() => UnixHiddenData.CollectInventory("dataless-folder", List,
            _ => UnixHiddenData.MacSizeWithoutMaterialization("dataless-folder", () =>
            {
                Marshal.SetLastPInvokeError(MacMaterializationPolicy.Required);
                return -1;
            }), TestContext.Current.CancellationToken));
        Assert.Equal(MacMaterializationPolicy.Required, error.HResult);

        var ordinaryFailure = UnixHiddenData.CollectInventory("dataless-folder", List,
            _ => UnixHiddenData.MacSizeWithoutMaterialization("dataless-folder", () =>
            {
                Marshal.SetLastPInvokeError(34); // ERANGE: changed length, not a materialization request
                return -1;
            }), TestContext.Current.CancellationToken);
        Assert.Equal(HiddenInventoryState.Partial, ordinaryFailure.State);
    }

    [Fact]
    public void Managed_roles_and_download_mark_exceptions_use_the_typed_category()
    {
        var ea = new HiddenItem("Zone.Identifier", HiddenKind.NtfsAttribute, 4) { NativeFlags = 0x80 };
        Assert.Equal(HiddenMetadataRole.RequiredMetadata, HiddenMetadataRoles.Classify(ea));
        Assert.NotNull(HiddenMetadataRoles.DeleteRefusal(ea with { Role = HiddenMetadataRoles.Classify(ea) }));
        Assert.False(DownloadMarks.IsDownloadMark(ea, windows: true, mac: false));
        Assert.True(DownloadMarks.IsDownloadMark(ea with { Kind = HiddenKind.Stream }, windows: true, mac: false));
        Assert.False(DownloadMarks.IsDownloadMark(new("user.xdg.origin.url", HiddenKind.Attribute, 0), windows: false, mac: true));
    }

    [Fact]
    public void Decmpfs_header_shorter_than_sixteen_bytes_never_declares_a_method_or_size()
    {
        byte[] header = new byte[16]; "fpmc"u8.CopyTo(header);
        BitConverter.TryWriteBytes(header.AsSpan(4), 11u); BitConverter.TryWriteBytes(header.AsSpan(8), 4096ul);
        Assert.StartsWith("Header declares LZFSE", HiddenDataDecoder.Describe(new("com.apple.decmpfs", HiddenKind.Attribute, 16), header).Summary);
        for (int length = 0; length < 16; length++)
        {
            var (summary, details) = HiddenDataDecoder.Describe(new("com.apple.decmpfs", HiddenKind.Attribute, length), header.AsSpan(0, length));
            Assert.DoesNotContain("declares", summary);
            Assert.DoesNotContain("LZFSE", summary);
            Assert.DoesNotContain("uncompressed", summary);
            Assert.DoesNotContain("Compressed by", summary);
            if (length < 4) continue; // not even a decmpfs magic: ordinary bounded text/binary evidence
            Assert.Equal("Truncated decmpfs header; representation not interpreted", summary);
            Assert.Equal([Convert.ToHexString(header, 0, length).ToLowerInvariant()], details);
        }
        // A short value with a different magic is not interpreted as decmpfs at all.
        Assert.DoesNotContain("decmpfs", HiddenDataDecoder.Describe(new("com.apple.decmpfs", HiddenKind.Attribute, 8), "cmpf\0\0\0\0"u8).Summary);
    }

    [Theory]
    [InlineData(2u, 1048576ul, "representation not interpreted")]
    [InlineData(99u, 1048576ul, "representation not interpreted")]
    [InlineData(12u, ulong.MaxValue, "unsigned size exceeds")]
    public void Decmpfs_never_infers_active_state_or_guesses_an_unknown_or_overflowed_size(uint method, ulong size, string expected)
    {
        byte[] header = new byte[16]; "fpmc"u8.CopyTo(header);
        BitConverter.TryWriteBytes(header.AsSpan(4), method); BitConverter.TryWriteBytes(header.AsSpan(8), size);
        string summary = HiddenDataDecoder.Describe(new("com.apple.decmpfs", HiddenKind.Attribute, 16), header).Summary;
        Assert.StartsWith("Header declares", summary); Assert.Contains(expected, summary);
        Assert.DoesNotContain("Compressed by", summary);
    }
}
