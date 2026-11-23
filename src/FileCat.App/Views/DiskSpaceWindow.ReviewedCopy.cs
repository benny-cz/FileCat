using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static string? CopyReviewRefusal(AnalysisIndexedAcquisition? runtime, int count)
    {
        if (runtime?.CanReviewContentReads != true)
            return "Reviewed copy requires a finished live local NTFS analysis on Windows.";
        return count > ReviewedAnalysisCopyOperation.MaximumEntries
            ? $"Review up to {ReviewedAnalysisCopyOperation.MaximumEntries} captured entries at a time. Narrow the marks or basket to continue."
            : null;
    }

    private async Task<string?> ChooseReviewedCopyAsync(AnalysisIndexedAcquisition runtime, SessionIdentity identity,
        ImmutableHashSet<ulong> paths, CancellationToken lifetime)
    {
        if (CopyReviewRefusal(runtime, paths.Count) is { } refusal) return refusal;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime, _indexedLifetime.Token);
        var token = cancellation.Token;
        token.ThrowIfCancellationRequested();
        var items = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Choose copy destination", AllowMultiple = false });
        using var picked = new PickedStorageItems(items);
        token.ThrowIfCancellationRequested();
        if (items.Count == 0) return null;
        string folder = items[0].TryGetLocalPath() ?? throw new NotSupportedException("Choose a directly accessible local NTFS folder.");

        var preparing = runtime.PrepareReviewedCopyAsync(identity, paths, folder, token);
        if (!preparing.IsCompleted)
        {
            var closer = new DialogCloser();
            using var closeOnExit = token.Register(() => Dispatcher.UIThread.Post(() => closer.Close(false)));
            try
            {
                var cancel = _dialogs.ShowCustomAsync("Checking reviewed copy",
                    CopyReviewText("Checking current file bindings, data streams and the selected output folder."),
                    [new("Cancel / Keep files", false, IsDefault: true, IsCancel: true)], closer: closer);
                if (await Task.WhenAny(preparing, cancel) == cancel) cancellation.Cancel();
            }
            catch
            {
                cancellation.Cancel();
                try { await using var abandoned = await preparing; }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                throw;
            }
            finally { closer.Close(null); }
        }
        await using var review = await preparing;
        if (token.IsCancellationRequested) return null;
        var included = await ReviewCopyChoicesAsync(_dialogs, review.Destination, review.Rows, token);
        if (included is null) return null;
        var job = await review.SubmitAsync(included, token);
        return $"Reviewed copy submitted for {included.Count:N0} {(included.Count == 1 ? "file" : "files")}. See Operations for progress and results (job {job.ShortId}).";
    }

    internal static async Task<ImmutableHashSet<ulong>?> ReviewCopyChoicesAsync(IDialogService dialogs, string destination,
        ImmutableArray<AnalysisReviewedCopyRow> rows, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var included = rows.Where(row => row.Refusal is null).Select(row => row.PathId).ToHashSet();
        var count = CopyReviewText("");
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);
        var conflict = CopyReviewText("");
        AutomationProperties.SetLiveSetting(conflict, AutomationLiveSetting.Polite);
        var confirm = new DialogButtonPresentation(new("Queue reviewed copy"));
        var list = new StackPanel { Spacing = 7 };
        foreach (var row in rows)
        {
            if (row.Refusal is { } problem)
            {
                list.Children.Add(CopyReviewText($"{row.DisplayPath}\nSkipped · {problem}"));
                continue;
            }
            var include = new CheckBox
            {
                IsChecked = true,
                Content = CopyReviewText($"{row.DisplayPath}\n{row.Bytes:N0} bytes · {row.Streams:N0} {(row.Streams == 1 ? "data stream" : "data streams")}")
            };
            AutomationProperties.SetName(include, "Include copy of " + row.DisplayPath);
            AutomationProperties.SetHelpText(include, "Clear to skip this file.");
            include.IsCheckedChanged += (_, _) =>
            {
                if (include.IsChecked == true) included.Add(row.PathId);
                else included.Remove(row.PathId);
                Refresh();
            };
            list.Children.Add(include);
        }
        Refresh();
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Dispatcher.UIThread.Post(() => closer.Close(false)));
        var body = new StackPanel { Spacing = 8, Children =
        {
            CopyReviewText("Destination folder · " + AnalysisNames.Display(destination)),
            CopyReviewText("FileCat will check each included file again and create its copy under the same filename. Existing destination files and all originals are kept."),
            CopyReviewText("Copies keep file attributes and data streams and use private permissions for your account."),
            count, conflict,
            new ScrollViewer { Content = list, MaxHeight = 330, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
        } };
        var choice = await dialogs.ShowCustomAsync("Confirm reviewed copy", body,
            [new("Cancel / Keep files", false, IsDefault: true, IsCancel: true),
             new("Queue reviewed copy", true) { Presentation = confirm, IsAvailable = CanQueue }],
            closer: closer, resizable: true, behavior: new DialogBehavior { SizeKey = "analysis.reviewed-copy" });
        return choice is true && CanQueue() ? included.ToImmutableHashSet() : null;

        bool HasDuplicateName() => rows.Where(row => included.Contains(row.PathId)).GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Skip(1).Any());
        bool CanQueue() => !cancellation.IsCancellationRequested && included.Count != 0 && !HasDuplicateName();
        void Refresh()
        {
            count.Text = $"{included.Count:N0} included · {rows.Length - included.Count:N0} skipped";
            conflict.Text = HasDuplicateName() ? "Included files share an output filename. Clear Include for all but one file with each name." : "";
            conflict.IsVisible = conflict.Text.Length > 0;
            confirm.Update(new(included.Count == 1 ? "Queue copy of 1 file" : $"Queue copy of {included.Count:N0} files"));
        }
    }

    private static TextBlock CopyReviewText(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}
