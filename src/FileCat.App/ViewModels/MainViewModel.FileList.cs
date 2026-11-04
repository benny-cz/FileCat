using Avalonia.Platform.Storage;
using FileCat.App.Views;
using FileCat.App.Services;
using FileCat.Core.Diagnostics;
using FileCat.Core.Listing;
using FileCat.Core.Operations;
using FileCat.Core.Resources;
using FileCat.Core.Selection;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task CreateFileListAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is not { } root) return;
        long navigation = tab.NavigationGeneration;
        var options = await FileListDialog.ShowAsync(this, tab);
        if (options is null) return;
        if (tab.Listing.IsDisposed || tab.NavigationGeneration != navigation || !root.Equals(tab.Location))
        {
            Notify("The folder changed while Create file list was open. Open it again in the current folder.", true);
            return;
        }
        ItemRef[]? selected = null;
        if (options.SelectedOnly)
        {
            var captured = tab.Listing.GetSelection();
            try { selected = [.. captured]; }
            finally { ItemSources.Release(captured); }
            if (selected.Length == 0) { Notify("Select or focus an item first."); return; }
        }
        var rule = options.Rule;
        Task<string> Generate(CancellationToken token) => FileListExport.GenerateAsync(Services.Providers, root, selected, rule, token);

        if (options.Destination == FileListDestination.Viewer)
        {
            new ReportWindow("File list", Services.Providers.Display(root), Generate).Show();
            return;
        }

        string? outputPath = null;
        try
        {
            if (options.Destination == FileListDestination.TextFile)
            {
                var top = View.TopLevel;
                if (top is null) throw new InvalidOperationException("The save dialog is not available.");
                var picked = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save file list as UTF-8 text",
                    SuggestedFileName = "file-list.txt",
                    DefaultExtension = "txt",
                    FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
                });
                if (picked is null) return;
                outputPath = picked.TryGetLocalPath() ?? throw new NotSupportedException(
                    "Choose a local destination for the file list.");
                if (File.Exists(outputPath) && !await Dialogs.ConfirmAsync("Replace file list?",
                    $"Replace {Formatters.SafeName(outputPath)} with this file list?", "Replace file")) return;
            }
            Notify("Creating file list…");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            string text = await Task.Run(() => Generate(timeout.Token), timeout.Token);
            if (options.Destination == FileListDestination.Clipboard)
            {
                if (View.Clipboard is null) throw new NotSupportedException("The clipboard is not available.");
                await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(View.Clipboard, text);
                Notify($"Copied {text.Count(c => c == '\n'):N0} file-list lines to the clipboard.");
                return;
            }
            if (outputPath is null) throw new InvalidOperationException("The file-list destination is missing.");
            await AdvancedTextFileExchange.WriteUtf8AtomicAsync(outputPath, text, timeout.Token);
            Notify($"Saved the file list to {Formatters.SafeName(outputPath)}.");
        }
        catch (OperationCanceledException) { Notify("Creating the file list timed out. No partial list was exported.", true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or NotSupportedException or InvalidOperationException)
        {
            AppLog.Error("Create file list failed", ex);
            Notify("Could not create the file list: " + ex.Message, true);
        }
    }
}
