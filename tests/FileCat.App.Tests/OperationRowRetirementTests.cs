using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class OperationRowRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<string, int> Cases => new()
    {
        { "remove", 1 }, { "remove", 128 }, { "remove", 4096 },
        { "clear", 1 }, { "clear", 128 }, { "clear", 4096 },
        { "live", 1 }, { "live", 128 }, { "live", 4096 },
    };

    private sealed record Rig(OperationCenterViewModel Held, WeakReference Row, WeakReference Job, WeakReference Sources,
        bool FrozenSourcesMatch, bool ButtonInvoked, int OriginalRows);

    private static byte[] Known() => Enumerable.Range(0, 32768).Select(i => (byte)(17 + i * 31)).ToArray();

    [AvaloniaTheory, MemberData(nameof(Cases))]
    public void Removed_operation_rows_release_their_job_graph_while_the_center_stays_live(string route, int count)
    {
        string root = Directory.CreateTempSubdirectory("fc-operation-row-retirement-").FullName;
        string path = Path.Join(root, "known.bin");
        File.WriteAllBytes(path, Known());
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Rig? rig = null;
        try
        {
            rig = Visit(root, path, route, count);
            Collect();
            bool rowAlive = rig.Row.IsAlive, jobAlive = rig.Job.IsAlive, sourcesAlive = rig.Sources.IsAlive;
            bool unchanged = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash;
            output.WriteLine("OPERATION_ROW_RETIREMENT " + JsonSerializer.Serialize(new
            {
                root, route, count, rowAlive, jobAlive, sourcesAlive, rig.FrozenSourcesMatch,
                rig.ButtonInvoked, rig.OriginalRows, currentRows = rig.Held.Jobs.Count,
                managerRows = rig.Held.Manager.Jobs.Count, selected = rig.Held.Selected is not null,
                unchanged, knownBytes = 32768, allKnownBytes = File.ReadAllBytes(path).SequenceEqual(Known()),
                queuedCancellationOnly = true, publicRoutedButtonEvent = true,
                managedReachabilityNotNativeInputOrProcessPeak = true,
                ownedVisualContextsClearedBeforeCollection = true,
            }));
            GC.KeepAlive(rig.Held);
            Assert.True(unchanged);
            Assert.True(rig.FrozenSourcesMatch);
            Assert.Equal(route == "live", rowAlive);
            Assert.Equal(route == "live", jobAlive);
            Assert.Equal(route == "live", sourcesAlive);
        }
        finally
        {
            rig?.Held.RemoveFinished();
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(root, true);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Visit(string root, string path, string route, int count)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var manager = new JobManager(new PortableFileOperations(), providers, Path.Join(root, "journal")) { MaxConcurrent = 0 };
        var center = new OperationCenterViewModel(manager) { IsOpen = true };
        var view = new OperationsView { DataContext = center };
        var window = new Window { Content = view, Width = 1100, Height = 750 };
        window.Show();
        JobViewModel? selectedRow = null;
        List<Control> ownedControls = [];
        try
        {
            var job = manager.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = Enumerable.Range(0, count).Select(_ => ItemRef.ForFileSystemPath(path, EntryKind.File)).ToArray(),
                Destination = Location.FileSystem(Path.Join(root, "destination")),
            });
            job.Cancel();
            Assert.Equal(JobState.Canceled, job.State);
            Assert.Null(job.StartedUtc);
            Dispatcher.UIThread.RunJobs();
            var row = Assert.Single(center.Jobs);
            center.Selected = row;
            center.UpdateSummary();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            selectedRow = row;
            ownedControls = view.GetVisualDescendants().OfType<Control>().ToList();
            bool match = job.Request.Sources.Count == count && job.Request.Sources.All(v => v.FileSystemPath == path);
            bool clicked = false;
            if (route != "live")
            {
                string label = route == "remove" ? "Remove" : "Clear finished";
                var button = Assert.Single(view.GetVisualDescendants().OfType<Button>(), v => Equals(v.Content, label));
                Assert.True(button.IsVisible);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                clicked = true;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Empty(center.Jobs);
                Assert.Empty(manager.Jobs);
            }
            return new Rig(center, new WeakReference(row), new WeakReference(job),
                new WeakReference(job.Request.Sources), match, clicked, 1);
        }
        finally
        {
            // Isolate the center's ownership from the owned headless focus/template contexts.
            // Do not alter its map, job rows, selection or manager to manufacture collection.
            foreach (var control in ownedControls)
                if (ReferenceEquals(control.DataContext, selectedRow)) control.DataContext = null;
            view.DataContext = null;
            window.Content = null;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect()
    {
        for (int i = 0; i < 4; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
