using System.IO.Compression;
using Avalonia.Headless.XUnit;
using FileCat.Core.Archives;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>Plan §11.2 step 5: creating onto an existing ZIP never adds to it silently.</summary>
public sealed class ArchiveZipExistingTargetTests
{
    [AvaloniaFact]
    public async Task Packing_onto_an_existing_ZIP_asks_before_adding_and_Cancel_keeps_it()
    {
        using var f = await ArchiveCreationWorkflowTests.Fixture.Open();
        string existing = Path.Combine(f.Files, "a.zip");
        using (var archive = ZipFile.Open(existing, ZipArchiveMode.Create)) archive.CreateEntry("old.txt");
        byte[] before = File.ReadAllBytes(existing);
        var operation = f.Vm.ExecuteAsync(CommandIds.Pack); await f.Ready(); f.SelectFormat(ArchiveWriteFormat.Zip);
        f.Input("Archive path").Text = existing;
        await Until(() => f.Button("Pack")?.IsEffectivelyEnabled == true); f.Click("Pack");
        await Until(() => f.Button("Add") is not null);
        Assert.Contains("already exists", f.Text());
        f.Click("Cancel");
        await Until(() => f.Button("Pack") is not null); // Back to the creation dialog to choose another filename.
        Assert.Empty(f.Services.Jobs.Jobs); Assert.Equal(before, File.ReadAllBytes(existing));
        f.Click("Pack"); await Until(() => f.Button("Add") is not null); f.Click("Add"); await f.Complete(operation);
        Assert.Single(f.Services.Jobs.Jobs);
    }

    private static async Task Until(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); if (watch.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException(); await Task.Delay(10); }
    }
}
