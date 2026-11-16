using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed partial class OwnerColumnTests
{
    [AvaloniaFact]
    public async Task Absent_owner_column_scroll_inspection_accessibility_and_cached_sort_do_not_read_owners()
    {
        using var fixture = new OwnerFixture();
        await fixture.Load();
        fixture.Tab.Listing.SetFocus(90);
        fixture.List.EnsureFocusVisible();
        fixture.Frame();
        fixture.Window.MouseMove(new Point(50, 80));
        Assert.DoesNotContain("Owner:", fixture.List.FocusedInspectionText());
        var peer = ControlAutomationPeer.CreatePeerForElement(fixture.List)!;
        _ = peer.GetName();
        _ = peer.GetHelpText();
        _ = fixture.List.DescribeFocus();
        fixture.Tab.SortByMetadata("owner");
        await FileListFixture.Wait(() => fixture.Tab.Listing.AppliedViewSpecVersion == fixture.Tab.Listing.ViewSpecVersion);
        Assert.Empty(fixture.Source.ReadPaths);
        Assert.Equal(0, fixture.Source.NameCalls);
    }

    [AvaloniaFact]
    public async Task Shown_owner_column_reads_visible_rows_only_and_resolved_names_are_safe_in_cells()
    {
        using var fixture = new OwnerFixture();
        await fixture.Load();
        fixture.Tab.ColumnProfile = 1;
        fixture.Frame();
        await FileListFixture.Wait(() => fixture.Source.ReadPaths.Count > 0);
        await fixture.Names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.Frame();
        Assert.InRange(fixture.Source.ReadPaths.Count, 1, fixture.List.VisibleRowCapacity + 1);
        Assert.Equal(1, fixture.Source.NameCalls);
        Assert.All(fixture.Source.ReadPaths, path => Assert.StartsWith(Path.Combine(fixture.Root, "synthetic"), path));
        string report = fixture.List.FocusedInspectionText()!;
        Assert.Contains("Owner: fixture\\u202Euser", report);
        Assert.DoesNotContain('\u202E', report);
        Assert.DoesNotContain("fixture", fixture.List.DescribeFocus()); // ownership never enters the spoken row summary
    }

    private sealed class OwnerFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "filecat-owner-columns-" + Guid.NewGuid().ToString("N"));
        public AppServices Services { get; }
        public OwnerSource Source { get; }
        public CountingOwnerProvider Provider { get; }
        public PrincipalNames Names { get; }
        public TabViewModel Tab { get; }
        public FileListControl List { get; }
        public Window Window { get; }

        public OwnerFixture(OwnerSource? source = null)
        {
            Source = source ?? new();
            Directory.CreateDirectory(Root);
            Services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(Root, "state")));
            Names = new PrincipalNames(Source);
            Names.Changed += Services.Metadata.NotifyValuesChanged;
            Services.Metadata.Register(OwnershipFields.CreateOwner(Source, Names));
            var inner = Services.Providers.Get(Schemes.FileSystem);
            Provider = new CountingOwnerProvider(new SyntheticListingProvider(inner, Root, 120));
            Services.Providers.Register(Provider);
            ColumnSpec name = new(ColumnField.Name, "Name", 200, Star: true);
            Services.Columns.Replace(new[]
            {
                ("Name", new[] { name }),
                ("Owner", new[] { name, new ColumnSpec(ColumnField.Metadata, "Owner", 170) { MetadataId = "owner" } }),
            });
            var workspace = new WorkspaceViewModel(Services);
            var panel = new PanelViewModel(workspace, Services);
            workspace.Panels.Add(panel);
            Tab = panel.OpenTab(Location.FileSystem(Path.Combine(Root, "synthetic")));
            Tab.ColumnProfile = 0;
            List = new FileListControl { Tab = Tab };
            Window = new Window { Width = 640, Height = 280, Content = List };
            Window.Show();
        }

        public Task Load() => FileListFixture.Wait(() => Tab.Listing.State == ListingState.Complete);
        public EntryData FocusedEntry => Tab.Listing.GetVisible(Tab.Listing.FocusedIndex);
        public MetadataValue FocusedValue => Services.Metadata.Peek("owner", Path.Combine(Root, "synthetic", FocusedEntry.Name), FocusedEntry, false);
        public async Task ShowOwner()
        {
            Tab.ColumnProfile = 1;
            Frame();
            await FileListFixture.Wait(() => FocusedValue.State is not (MetadataState.NotRequested or MetadataState.Pending));
            await Names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Frame();
        }
        public void Frame() { List.InvalidateVisual(); Window.CaptureRenderedFrame()?.Dispose(); }
        public void Dispose()
        {
            Window.Close();
            Tab.Dispose();
            Names.Changed -= Services.Metadata.NotifyValuesChanged;
            Names.Dispose();
            Services.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class OwnerSource : IOwnership
    {
        public bool IsSupported => true;
        public ConcurrentQueue<string> ReadPaths { get; } = new();
        public int NameCalls;
        public Func<string, OwnerRead>? ReadOverride { get; set; }
        public PrincipalName NameAnswer { get; set; } = new(NameStatus.Resolved, "fixture\u202Euser", "User");
        public OwnerRead ReadOwner(string path) { ReadPaths.Enqueue(path); return ReadOverride?.Invoke(path) ?? new(OwnerReadStatus.Ok, new Principal(PrincipalKind.UnixUser, "42")); }
        public OwnerRead ReadGroup(string path) => new(OwnerReadStatus.Unsupported);
        public PrincipalName LookUp(Principal principal) { Interlocked.Increment(ref NameCalls); return NameAnswer; }
    }

    private sealed class CountingOwnerProvider(ResourceProvider inner) : ResourceProvider
    {
        public int References;
        public override string Scheme => inner.Scheme;
        public override string GetDisplayPath(Location location) => inner.GetDisplayPath(location);
        public override Location? GetParent(Location location) => inner.GetParent(location);
        public override LocationCapabilities GetCapabilities(Location location) => inner.GetCapabilities(location);
        public override Location? GetChildLocation(Location parent, in EntryData entry) => inner.GetChildLocation(parent, entry);
        public override string GetDeviceKey(Location location) => inner.GetDeviceKey(location);
        public override ItemRef GetItemRef(Location location, in EntryData entry) { References++; return inner.GetItemRef(location, entry); }
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(location, sink, ct);
    }
}
