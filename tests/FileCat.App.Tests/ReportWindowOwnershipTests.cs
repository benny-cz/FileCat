using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class ReportWindowOwnershipTests
{
    private sealed record Observed(ReportWindow Window, WeakReference Producer, WeakReference Text, int Count);
    private sealed class Producer(string text)
    {
        public string Text { get; } = text;
        public Task<string> Read(CancellationToken _) => Task.FromResult(Text);
    }

    [AvaloniaTheory]
    [InlineData(16384, false)]
    [InlineData(16384, true)]
    [InlineData(131072, false)]
    [InlineData(131072, true)]
    [InlineData(1048576, false)]
    [InlineData(1048576, true)]
    public async Task A_held_report_window_keeps_materialized_owners_only_while_open(int count, bool close)
    {
        var observed = Open(count);
        try
        {
            var clock = Stopwatch.StartNew();
            while (!Ready(observed) && clock.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(Ready(observed));
            var reader = ObserveReader(observed.Window);
            Finish(observed, close);
            for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            bool[] alive = [observed.Producer.IsAlive, observed.Text.IsAlive, reader.IsAlive];
            TestContext.Current.TestOutputHelper?.WriteLine("REPORT_WINDOW_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                count, close, alive, visible = observed.Window.IsVisible, textLength = observed.Window.Text.Length,
                closedWindowDeliberatelyHeld = close, realReportProducerAndMemoryReader = true,
                readOnlyReflectionObservesReader = true, noPrivateStateMutation = true,
                noFilesOrDevicesOrHostUI = true, notNativeInputOrMemoryPeakOrCandidate = true,
            }));
            Assert.All(alive, value => Assert.Equal(!close, value));
            Assert.Equal(close ? 0 : count, observed.Window.Text.Length);
            GC.KeepAlive(observed.Window);
        }
        finally { observed.Window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Observed Open(int count)
    {
        var chars = Enumerable.Repeat('r', count).ToArray();
        for (int i = 79; i < chars.Length; i += 80) chars[i] = '\n';
        var producer = new Producer(new string(chars));
        var window = new ReportWindow("Owned report", "synthetic report", producer.Read);
        window.Show();
        return new Observed(window, new WeakReference(producer), new WeakReference(producer.Text), count);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Ready(Observed value) => value.Window.Reading is { IsCompletedSuccessfully: true } && value.Window.Text.Length == value.Count;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ObserveReader(ReportWindow window) => new(typeof(ReportWindow).GetField("_reader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Finish(Observed observed, bool close)
    {
        Assert.Equal(observed.Count, observed.Window.Text.Length);
        Assert.True(observed.Window.IsVisible);
        if (close) observed.Window.Close();
    }
}
