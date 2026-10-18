using System.IO.Compression;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Search;
using FileCat.Core.Selection;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>FCUX-071: the actual MainWindow drag admission path, before a native drag is published.</summary>
public sealed class DragExportTests
{
    public class StorageProxy : DispatchProxy
    {
        internal Uri PathValue = null!;
        internal bool Disposed;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Path" => PathValue,
            "get_Name" => Path.GetFileName(PathValue.LocalPath),
            "Dispose" => DisposeItem(),
            _ => throw new NotSupportedException("Unexpected storage API: " + method?.Name),
        };
        private object? DisposeItem() { Disposed = true; return null; }
    }

    private sealed class MarkingOps : PortableFileOperations
    {
        internal readonly Dictionary<string, string> Marks = new(StringComparer.OrdinalIgnoreCase);
        internal bool ThrowOnWrite;
        public override string? ReadOriginMark(string path) => Marks.GetValueOrDefault(path);
        public override bool WriteOriginMark(string path, string mark)
        {
            if (ThrowOnWrite) throw new ApplicationException("origin mark provider failed");
            Marks[path] = mark;
            return true;
        }
    }

    private sealed class Sink(List<EntryData> entries) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> batch) => entries.AddRange(batch.ToArray());
        public void ReportIssue(string message) { }
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "filecat-drag-export-tests", Guid.NewGuid().ToString("N"));
        internal readonly ProviderRegistry Providers = new();
        internal readonly MarkingOps Operations = new();
        internal readonly List<StorageProxy> Converted = [];
        internal int Conversions;
        internal string? RefusePath;
        internal string? RedirectPath;

        internal Fixture()
        {
            Directory.CreateDirectory(Root);
            Providers.Register(new LocalFileSystemProvider());
            Providers.Register(new ZipProvider(Path.Combine(Root, "spool")));
        }

        internal string File(string name, string text)
        {
            string path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, text);
            return path;
        }

        internal string Zip(params (string Name, string Content)[] members)
        {
            string path = Path.Combine(Root, "members.zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (name, content) in members)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
            return path;
        }

        internal async Task<List<ItemRef>> Members(string zip)
        {
            var location = ZipProvider.ForFile(zip);
            var entries = new List<EntryData>();
            var provider = Providers.Get(Schemes.Zip);
            await provider.EnumerateAsync(location, new Sink(entries), TestContext.Current.CancellationToken);
            return entries.Where(entry => !entry.IsContainer).Select(entry => provider.GetItemRef(location, entry)).ToList();
        }

        internal Task<IStorageItem?> Convert(string path, bool folder)
        {
            Conversions++;
            if (path == RefusePath) return Task.FromResult<IStorageItem?>(null);
            IStorageItem item = folder ? DispatchProxy.Create<IStorageFolder, StorageProxy>() : DispatchProxy.Create<IStorageFile, StorageProxy>();
            var proxy = (StorageProxy)(object)item;
            proxy.PathValue = new Uri(RedirectPath ?? path);
            Converted.Add(proxy);
            return Task.FromResult<IStorageItem?>(item);
        }

        internal Task<(DragExport.Prepared? Transfer, string? Refusal)> Prepare(params ItemRef[] items) =>
            DragExport.PrepareAsync(items, Providers, Operations, Root, Convert, TestContext.Current.CancellationToken);

        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Local_files_and_folders_export_every_original_path_and_content()
    {
        using var f = new Fixture();
        string first = f.File("a.txt", "alpha"), second = f.File("b.txt", "beta");
        string folder = Path.Combine(f.Root, "folder");
        Directory.CreateDirectory(folder);
        var selection = new[] { ItemRef.ForFileSystemPath(first, EntryKind.File),
            ItemRef.ForFileSystemPath(folder, EntryKind.Directory), ItemRef.ForFileSystemPath(second, EntryKind.File) };
        var (transfer, refusal) = await f.Prepare(selection);
        using (transfer)
        {
            Assert.Null(refusal);
            Assert.NotNull(transfer);
            Assert.False(transfer.IsStaged);
            Assert.Equal([first, folder, second], transfer.Paths);
            Assert.Equal(selection.Length, transfer.Items.Count);
            Assert.Equal([first, folder, second], transfer.Items.Select(item => item.TryGetLocalPath()));
            Assert.Equal("alpha", System.IO.File.ReadAllText(transfer.Paths[0]));
            Assert.Equal("beta", System.IO.File.ReadAllText(transfer.Paths[2]));
            Assert.IsAssignableFrom<IStorageFolder>(transfer.Items[1]);
        }
        Assert.All(f.Converted, proxy => Assert.True(proxy.Disposed));
    }

    [AvaloniaTheory]
    [InlineData(Schemes.Zip)]
    [InlineData(Schemes.Sftp)]
    public async Task Mixed_local_and_nonfilesystem_selection_refuses_all_before_conversion(string scheme)
    {
        using var f = new Fixture();
        string local = f.File("a.txt", "alpha");
        var other = new ItemRef(new Location(scheme, "other"), "remote.txt", EntryKind.File, 3);
        var (transfer, refusal) = await f.Prepare(ItemRef.ForFileSystemPath(local, EntryKind.File), other);
        Assert.Null(transfer);
        Assert.Contains("2 selected", refusal);
        Assert.Contains("1 local and 1 non-file-system", refusal);
        Assert.Equal(0, f.Conversions);
        Assert.Equal("alpha", System.IO.File.ReadAllText(local));
        Assert.False(Directory.Exists(Path.Combine(f.Root, "drag")));
    }

    [AvaloniaFact]
    public async Task Unsupported_only_selection_refuses_without_drag_or_staging()
    {
        using var f = new Fixture();
        var remote = new ItemRef(new Location(Schemes.Sftp, "/home"), "remote.txt", EntryKind.File, 3);
        var (transfer, refusal) = await f.Prepare(remote);
        Assert.Null(transfer);
        Assert.Contains("1 selected", refusal);
        Assert.Contains("F5", refusal);
        Assert.Equal(0, f.Conversions);
        Assert.False(Directory.Exists(Path.Combine(f.Root, "drag")));
    }

    [AvaloniaFact]
    public async Task Archive_only_selection_stages_every_file_with_contents_and_origin_then_cleans_unhanded_drag()
    {
        using var f = new Fixture();
        string zip = f.Zip(("a.txt", "alpha"), ("b.txt", "beta"));
        f.Operations.Marks[zip] = "[ZoneTransfer]\r\nZoneId=3\r\n";
        var members = await f.Members(zip);
        var (transfer, refusal) = await f.Prepare([.. members]);
        Assert.Null(refusal);
        Assert.NotNull(transfer);
        Assert.True(transfer.IsStaged);
        Assert.Equal(members.Count, transfer.Items.Count);
        string folder = Path.GetDirectoryName(transfer.Paths[0])!;
        Assert.Equal("alpha", System.IO.File.ReadAllText(transfer.Paths.Single(path => Path.GetFileName(path) == "a.txt")));
        Assert.Equal("beta", System.IO.File.ReadAllText(transfer.Paths.Single(path => Path.GetFileName(path) == "b.txt")));
        Assert.All(transfer.Paths, path => Assert.Equal(f.Operations.Marks[zip], f.Operations.Marks[path]));
        transfer.Dispose(); // canceled before another app received the files
        Assert.False(Directory.Exists(folder));
    }

    [AvaloniaFact]
    public async Task Completed_staged_handoff_keeps_files_until_the_existing_sweep_policy_retires_them()
    {
        using var f = new Fixture();
        string zip = f.Zip(("inside.txt", "content"));
        var member = Assert.Single(await f.Members(zip));
        var (transfer, refusal) = await f.Prepare(member);
        Assert.Null(refusal);
        Assert.NotNull(transfer);
        string file = Assert.Single(transfer.Paths);
        transfer.Handoff();
        transfer.Dispose();
        Assert.Equal("content", System.IO.File.ReadAllText(file));
        DragStaging.Discard([file], f.Root);
        Assert.False(System.IO.File.Exists(file));
    }

    [AvaloniaFact]
    public async Task Failed_storage_conversion_or_redirect_refuses_the_whole_scope_and_retires_staging()
    {
        using var f = new Fixture();
        string first = f.File("a.txt", "alpha"), second = f.File("b.txt", "beta");
        f.RefusePath = second;
        var (local, refused) = await f.Prepare(ItemRef.ForFileSystemPath(first, EntryKind.File), ItemRef.ForFileSystemPath(second, EntryKind.File));
        Assert.Null(local);
        Assert.Contains("2 selected", refused);
        Assert.True(f.Converted.Single().Disposed);
        f.RefusePath = null;
        f.RedirectPath = second;
        (local, refused) = await f.Prepare(ItemRef.ForFileSystemPath(first, EntryKind.File));
        Assert.Null(local);
        Assert.Contains("requested path", refused);

        string zip = f.Zip(("inside.txt", "content"));
        var member = Assert.Single(await f.Members(zip));
        f.RedirectPath = null;
        f.RefusePath = null;
        int previous = f.Conversions;
        (local, refused) = await DragExport.PrepareAsync([member], f.Providers, f.Operations, f.Root,
            (_, _) => { f.Conversions++; return Task.FromResult<IStorageItem?>(null); }, TestContext.Current.CancellationToken);
        Assert.Null(local);
        Assert.Contains("1 selected", refused);
        Assert.Equal(previous + 1, f.Conversions);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(f.Root, "drag")));
    }

    [AvaloniaFact]
    public async Task Storage_conversion_exception_refuses_the_complete_selection()
    {
        using var f = new Fixture();
        string file = f.File("a.txt", "alpha");
        var (transfer, refusal) = await DragExport.PrepareAsync([ItemRef.ForFileSystemPath(file, EntryKind.File)],
            f.Providers, f.Operations, f.Root,
            (_, _) => throw new IOException("storage conversion failed"), TestContext.Current.CancellationToken);
        Assert.Null(transfer);
        Assert.Contains("1 selected", refusal);
        Assert.Contains("storage conversion failed", refusal);
        Assert.Equal("alpha", System.IO.File.ReadAllText(file));
    }

    [AvaloniaFact]
    public async Task Wrong_storage_kind_and_unexpected_conversion_error_refuse_the_complete_scope()
    {
        using var f = new Fixture();
        string file = f.File("a.txt", "alpha");
        var wrong = DispatchProxy.Create<IStorageFolder, StorageProxy>();
        ((StorageProxy)(object)wrong).PathValue = new Uri(file);
        var (transfer, refusal) = await DragExport.PrepareAsync([ItemRef.ForFileSystemPath(file, EntryKind.File)],
            f.Providers, f.Operations, f.Root, (_, _) => Task.FromResult<IStorageItem?>(wrong),
            TestContext.Current.CancellationToken);
        Assert.Null(transfer);
        Assert.Contains("1 selected", refusal);
        Assert.True(((StorageProxy)(object)wrong).Disposed);

        (transfer, refusal) = await DragExport.PrepareAsync([ItemRef.ForFileSystemPath(file, EntryKind.File)],
            f.Providers, f.Operations, f.Root, (_, _) => throw new ApplicationException("conversion failed"),
            TestContext.Current.CancellationToken);
        Assert.Null(transfer);
        Assert.Contains("1 selected", refusal);
        Assert.Equal("alpha", System.IO.File.ReadAllText(file));
    }

    [AvaloniaFact]
    public async Task Unexpected_staging_error_refuses_all_and_cleans_private_files()
    {
        using var f = new Fixture();
        string zip = f.Zip(("inside.txt", "content"));
        f.Operations.Marks[zip] = "[ZoneTransfer]\r\nZoneId=3\r\n";
        f.Operations.ThrowOnWrite = true;
        var member = Assert.Single(await f.Members(zip));
        var (transfer, refusal) = await f.Prepare(member);
        Assert.Null(transfer);
        Assert.Contains("1 selected", refusal);
        Assert.Contains("origin mark provider failed", refusal);
        Assert.Equal(0, f.Conversions);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(f.Root, "drag")));
    }

    [AvaloniaFact]
    public async Task Cancellation_after_archive_staging_discards_the_private_copy()
    {
        using var f = new Fixture();
        string zip = f.Zip(("inside.txt", "content"));
        var member = Assert.Single(await f.Members(zip));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DragExport.PrepareAsync([member],
            f.Providers, f.Operations, f.Root,
            (_, _) => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }, cancellation.Token));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(f.Root, "drag")));
    }

    [AvaloniaFact]
    public async Task Drag_limit_refuses_the_captured_scope_before_any_conversion()
    {
        using var f = new Fixture();
        string path = f.File("a.txt", "alpha");
        var selection = Enumerable.Repeat(ItemRef.ForFileSystemPath(path, EntryKind.File), DragExport.MaxItems + 1).ToArray();
        var (transfer, refusal) = await f.Prepare(selection);
        Assert.Null(transfer);
        Assert.Contains(selection.Length.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " selected", refusal);
        Assert.Equal(0, f.Conversions);
    }

    [AvaloniaFact]
    public async Task Hidden_marks_remain_in_the_actual_listing_snapshot_for_local_export_and_mixed_refusal()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string first = Path.Combine(root, "first.txt"), hidden = Path.Combine(root, "hidden.txt");
            System.IO.File.WriteAllText(first, "first content");
            System.IO.File.WriteAllText(hidden, "hidden content");
            var set = services.ResultSets.Create("Drag scope", "Hidden mark coverage");
            set.Add(ItemRef.ForFileSystemPath(first, EntryKind.File), "first");
            set.Add(ItemRef.ForFileSystemPath(hidden, EntryKind.File), "hidden");
            set.IsComplete = true;
            var tab = vm.ActiveTab!;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 2);
            tab.Listing.MarkAll(true);
            tab.Listing.Filter = Mask.Parse("*|hidden.txt");
            await Until(() => tab.Listing.VisibleCount == 1);
            var captured = tab.Listing.GetSelection();
            try
            {
                Assert.Equal(2, captured.Count);
                using var f = new Fixture();
                var (transfer, refusal) = await DragExport.PrepareAsync(captured, f.Providers, f.Operations,
                    f.Root, f.Convert, TestContext.Current.CancellationToken);
                using (transfer)
                {
                    Assert.Null(refusal);
                    Assert.NotNull(transfer);
                    Assert.Equal([first, hidden], transfer.Paths);
                    Assert.Equal("hidden content", System.IO.File.ReadAllText(transfer.Paths[1]));
                }
            }
            finally { ItemSources.Release(captured); }

            var mixed = services.ResultSets.Create("Mixed drag", "Hidden archive mark");
            mixed.Add(ItemRef.ForFileSystemPath(first, EntryKind.File), "first");
            mixed.Add(new ItemRef(new Location(Schemes.Zip, "archive.zip"), "hidden.txt", EntryKind.File, 6), "archive");
            mixed.IsComplete = true;
            tab.Listing.Filter = null;
            tab.Navigate(ResultSetProvider.LocationOf(mixed));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 2);
            tab.Listing.MarkAll(true);
            tab.Listing.Filter = Mask.Parse("*|hidden.txt");
            await Until(() => tab.Listing.VisibleCount == 1);
            captured = tab.Listing.GetSelection();
            try
            {
                Assert.Equal(2, captured.Count);
                using var f = new Fixture();
                var (transfer, refusal) = await DragExport.PrepareAsync(captured, f.Providers, f.Operations,
                    f.Root, f.Convert, TestContext.Current.CancellationToken);
                Assert.Null(transfer);
                Assert.Contains("1 local and 1 non-file-system", refusal);
                Assert.Equal(0, f.Conversions);
            }
            finally { ItemSources.Release(captured); }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
