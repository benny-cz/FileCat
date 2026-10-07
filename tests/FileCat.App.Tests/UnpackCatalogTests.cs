using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class UnpackCatalogTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData("duplicate.zip", false)]
    [InlineData("duplicate.zip", true)]
    [InlineData("duplicate.tar", false)]
    [InlineData("duplicate.tar", true)]
    [InlineData("damaged.tar", false)]
    [InlineData("damaged.tar", true)]
    [InlineData("damaged.tar.gz", false)]
    [InlineData("damaged.tar.gz", true)]
    public async Task Real_catalog_warning_is_shown_before_any_extraction_and_requires_an_explicit_choice(string name, bool approve)
    {
        using var f = new Fixture(name); await f.Load(); var task = await f.Start();
        await Wait(() => task.IsCompleted || f.Button("Extract listed members") is not null);
        bool warned = f.Button("Extract listed members") is not null; int before = f.Services.Jobs.Jobs.Count;
        Emit("warning-before-choice", name, approve, f, warned, before);
        Assert.True(warned); Assert.Equal(0, before);
        Assert.Contains(f.Window.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains(name.StartsWith("duplicate", StringComparison.Ordinal) ? "duplicate names" : "damaged after", StringComparison.Ordinal));
        f.Click(approve ? "Extract listed members" : "Cancel"); await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        if (approve)
        {
            var job = Assert.Single(f.Services.Jobs.Jobs); Assert.Equal(JobKind.Extract, job.Kind); Assert.Equal(f.Target, job.Request.Destination!.Path);
            Assert.Equal(name.StartsWith("duplicate", StringComparison.Ordinal) ? new[] { 0, 1 } : new[] { 0 }, job.Request.Sources.Select(s => s.Ordinal));
        }
        else { Assert.Empty(f.Services.Jobs.Jobs); Assert.False(Directory.Exists(f.Target)); }
        Emit("warning-after-choice", name, approve, f, warned, before); f.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("empty.zip", false)]
    [InlineData("empty.zip", true)]
    [InlineData("empty.tar", false)]
    [InlineData("empty.tar", true)]
    public async Task Empty_archives_report_no_members_without_creating_an_extraction_job(string name, bool separate)
    {
        using var f = new Fixture(name); await f.Load(); var task = await f.Start(separate: separate);
        await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Emit("empty", name, separate, f, false, 0);
        Assert.Empty(f.Services.Jobs.Jobs); Assert.Contains("no members", f.Vm.Notification, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(f.Target)); f.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("valid.zip", "selection")]
    [InlineData("valid.zip", "source")]
    [InlineData("valid.zip", "target")]
    [InlineData("valid.tar", "selection")]
    [InlineData("valid.tar", "source")]
    [InlineData("valid.tar", "target")]
    public async Task Approved_archive_and_destination_remain_frozen_when_panel_state_changes(string name, string change)
    {
        using var f = new Fixture(name); await f.Load(); var task = await f.Start(change);
        await task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var job = Assert.Single(f.Services.Jobs.Jobs);
        await Wait(() => job.State.IsFinished());
        Emit("frozen-positive", name, false, f, false, 0);
        Assert.Equal(f.Target, job.Request.Destination!.Path); Assert.Equal(JobState.Completed, job.State);
        Assert.Equal("owned member bytes", File.ReadAllText(Path.Join(f.Target, "kept.txt")));
        Assert.Equal(f.Archive, job.Request.Sources[0].Parent.Container!.Path); f.AssertUnchanged();
    }

    private void Emit(string control, string name, bool choice, Fixture f, bool warned, int jobsBefore)
        => output.WriteLine("UNPACK_CATALOG " + JsonSerializer.Serialize(new { control, name, choice, warned, jobsBefore,
            Notification = f.Vm.Notification, SourceHashBefore = f.Before, SourceHashAfter = f.Hash(),
            Jobs = f.Services.Jobs.Jobs.Select(j => new { Kind = j.Kind.ToString(), State = j.State.ToString(), Destination = j.Request.Destination?.Path,
                Sources = j.Request.Sources.Select(s => new { s.Name, s.Ordinal, Parent = s.Parent.ToString() }) }) }));
    internal static async Task Wait(Func<bool> done)
    {
        var clock = Stopwatch.StartNew(); while (!done()) { Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "Owned unpack checkpoint timed out."); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    internal sealed class Fixture : IDisposable
    {
        private readonly string _root, _folder; public AppServices Services { get; } public MainViewModel Vm { get; } public MainWindow Window { get; }
        public string Archive { get; } public string Target { get; } public string Before { get; } public TabViewModel Source { get; } public TabViewModel TargetTab { get; }
        public Fixture(string name)
        {
            (Services, Vm, Window, _root) = AccessibilityTests.OpenMainWindow(); _folder = Directory.CreateDirectory(Path.Join(_root, "archives")).FullName;
            Archive = Path.Join(_folder, name); Target = Path.Join(_root, "approved-output");
            if (name.EndsWith(".zip", StringComparison.Ordinal))
            {
                using var zip = new ZipArchive(File.Create(Archive), ZipArchiveMode.Create);
                if (!name.StartsWith("empty", StringComparison.Ordinal))
                    foreach (string member in name.StartsWith("duplicate", StringComparison.Ordinal) ? new[] { "kept.txt", "kept.txt" } : new[] { "kept.txt" })
                    { using var stream = zip.CreateEntry(member).Open(); stream.Write("owned member bytes"u8); }
            }
            else
            {
                using var buffer = new MemoryStream();
                using (var tar = new TarWriter(buffer, TarEntryFormat.Ustar, true))
                    if (!name.StartsWith("empty", StringComparison.Ordinal))
                        foreach (string member in name.StartsWith("duplicate", StringComparison.Ordinal) ? new[] { "kept.txt", "kept.txt" } : name.StartsWith("damaged", StringComparison.Ordinal) ? new[] { "kept.txt", "lost.txt" } : new[] { "kept.txt" })
                            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, member) { DataStream = new MemoryStream("owned member bytes"u8.ToArray()) });
                byte[] raw = buffer.ToArray();
                if (name.StartsWith("damaged", StringComparison.Ordinal))
                {
                    raw[1024 + 124] = (byte)'x'; using var oracle = new TarReader(new MemoryStream(raw));
                    Assert.Equal("kept.txt", oracle.GetNextEntry()!.Name); Assert.Throws<InvalidDataException>(() => oracle.GetNextEntry());
                }
                if (name.EndsWith(".gz", StringComparison.Ordinal)) { using var gz = new GZipStream(File.Create(Archive), CompressionLevel.Fastest); gz.Write(raw); }
                else File.WriteAllBytes(Archive, raw);
            }
            Before = Hash(); Source = Vm.Workspace.Panels[0].ActiveTab!; TargetTab = Vm.Workspace.Panels[1].ActiveTab!;
        }
        public string Hash() => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Archive)));
        public void AssertUnchanged() => Assert.Equal(Before, Hash());
        public async Task Load()
        {
            Source.Navigate(Location.FileSystem(_folder)); TargetTab.Navigate(Location.FileSystem(_root)); Vm.Workspace.Activate(Source.Panel);
            await Wait(() => Source.Listing.State == ListingState.Complete && TargetTab.Listing.State == ListingState.Complete);
            Assert.True(Source.Listing.FocusName(Path.GetFileName(Archive)));
        }
        public Button? Button(string text) => Window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.Content as string == text &&
            b.GetVisualAncestors().OfType<Border>().Any(a => a.Classes.Contains("backdrop")));
        public void Click(string text) => Assert.IsType<Button>(Button(text)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public async Task<Task> Start(string? change = null, bool separate = false)
        {
            var task = (Task)typeof(MainViewModel).GetMethod("UnpackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Vm, null)!;
            await Wait(() => Button("Unpack") is not null);
            Window.GetVisualDescendants().OfType<TextBox>().Single(t => (Avalonia.Automation.AutomationProperties.GetName(t) ?? "").StartsWith("Unpack ", StringComparison.Ordinal)).Text = Target;
            Window.GetVisualDescendants().OfType<CheckBox>().Single(c => (c.Content as string)?.StartsWith("Separate folder", StringComparison.Ordinal) == true).IsChecked = separate;
            if (change == "selection") Source.Listing.FocusName("..");
            if (change == "source") Source.Navigate(Location.FileSystem(_root));
            if (change == "target") TargetTab.Navigate(Location.FileSystem(_folder));
            Click("Unpack"); return task;
        }
        public void Dispose() => AccessibilityTests.Close(Services, Window, _root);
    }
}
