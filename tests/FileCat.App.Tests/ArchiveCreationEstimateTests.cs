using Avalonia.Headless.XUnit;
using FileCat.Core.Archives;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>Plan §11.2 step 6: the Create dialog shows the checked count and the space needed before submission.</summary>
public sealed class ArchiveCreationEstimateTests
{
    [AvaloniaFact]
    public async Task The_create_dialog_shows_the_checked_count_and_space_before_packing()
    {
        using var f = await ArchiveCreationWorkflowTests.Fixture.Open();
        var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Tar);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!f.Text().Contains("to pack.", StringComparison.Ordinal))
        { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(f.Text()); await Task.Delay(10); }
        string text = f.Text();
        Assert.Contains("1 file (", text); // a.txt is the focused selection.
        Assert.Contains("Saving needs up to about", text);
        Assert.Empty(f.Services.Jobs.Jobs); // Nothing is submitted by the estimate.
        f.Click("Cancel"); await f.Complete(operation);
    }
}
