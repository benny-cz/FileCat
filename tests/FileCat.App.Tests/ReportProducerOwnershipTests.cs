using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;

namespace FileCat.App.Tests;

public sealed class ReportProducerOwnershipTests
{
    private sealed class Producer(string text)
    {
        public string Text { get; } = text;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> Read(CancellationToken _)
        {
            Entered.TrySetResult();
            await Release.Task; // Deliberately ignores cancellation to exercise late completion.
            return Text;
        }
    }
    private sealed record Observed(ReportWindow Window, TaskCompletionSource Entered, TaskCompletionSource Release, WeakReference Producer, WeakReference Text);
    private sealed record Completion(bool ProducerHeldWhileActive, bool TextHeldWhileActive, string ClosedSnapshot);

    [AvaloniaTheory]
    [InlineData(16384)]
    [InlineData(131072)]
    [InlineData(1048576)]
    public async Task Closing_a_report_preserves_the_active_producer_then_retires_its_owners(int count)
    {
        var observed = Open(count);
        try
        {
            await observed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            var completion = await CloseAndFinish(observed);
            for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            bool producerAlive = observed.Producer.IsAlive, textAlive = observed.Text.IsAlive;
            bool latePublication = observed.Window.Text != completion.ClosedSnapshot;
            TestContext.Current.TestOutputHelper?.WriteLine("REPORT_PRODUCER_OWNERSHIP " + JsonSerializer.Serialize(new
            {
                count, completion.ProducerHeldWhileActive, completion.TextHeldWhileActive, producerAlive, textAlive, latePublication,
                textLength = observed.Window.Text.Length, closedWindowHeld = true, realTaskRunProducerIgnoringCancellation = true,
                inputGateOwnedByObserver = true, noFilesOrDevicesOrHostUI = true, notNativeInputOrPeakOrCandidate = true,
            }));
            Assert.True(completion.ProducerHeldWhileActive && completion.TextHeldWhileActive);
            Assert.False(latePublication);
            Assert.False(producerAlive); Assert.False(textAlive);
            Assert.Empty(observed.Window.Text);
            Assert.False(HasReader(observed.Window));
            GC.KeepAlive(observed.Window);
        }
        finally { observed.Release.TrySetResult(); observed.Window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Observed Open(int count)
    {
        var producer = new Producer(new string('r', count));
        var window = new ReportWindow("Owned delayed report", "synthetic report", producer.Read);
        window.Show();
        return new Observed(window, producer.Entered, producer.Release, new WeakReference(producer), new WeakReference(producer.Text));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Completion> CloseAndFinish(Observed observed)
    {
        Task? reading = observed.Window.Reading!;
        observed.Window.Close();
        for (int i = 0; i < 2; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        var result = new Completion(observed.Producer.IsAlive, observed.Text.IsAlive, observed.Window.Text);
        observed.Release.TrySetResult();
        await reading.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        reading = null;
        return result;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasReader(ReportWindow window) => typeof(ReportWindow).GetField("_reader", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is not null;
}
