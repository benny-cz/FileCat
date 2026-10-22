using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

/// <summary>File exchange for Settings' advanced text. Imported text stays in the dialog draft until OK.</summary>
internal static class AdvancedTextFileExchange
{
    internal const int MaxBytes = 8 << 20;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static void AddTo(StackPanel panel, TextBox raw, string subject)
    {
        var import = new Button { Content = $"Import {subject} text…" };
        var export = new Button { Content = $"Export {subject} text…" };
        var status = new TextBlock { Classes = { "small", "muted" }, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(status, $"{subject} text file status");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        void Report(string message) { status.Text = message; status.IsVisible = true; }

        import.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(import) is not { } owner) return;
            try
            {
                var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Import {subject} definitions from UTF-8 text", AllowMultiple = false,
                    FileTypeFilter = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
                });
                if (picked.Count == 0 || !ReferenceEquals(TopLevel.GetTopLevel(import), owner)) return;
                if (picked[0].TryGetLocalPath() is not { } path)
                {
                    Report("Choose a local UTF-8 text file.");
                    return;
                }
                string text = await ReadUtf8Async(path);
                if (!ReferenceEquals(TopLevel.GetTopLevel(import), owner)) return;
                raw.Text = text;
                Report($"Imported {subject} text into this draft. Review it before applying Settings.");
                raw.Focus();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Report("Could not import the text: " + Formatters.SafeName(ex.Message));
            }
        };
        export.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(export) is not { } owner) return;
            try
            {
                string text = raw.Text ?? string.Empty;
                if (Utf8.GetByteCount(text) > MaxBytes)
                {
                    Report("The text exceeds the 8 MiB file-exchange limit.");
                    return;
                }
                var picked = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = $"Export {subject} definitions as UTF-8 text",
                    SuggestedFileName = $"filecat-{subject}.txt", DefaultExtension = "txt",
                    FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }],
                });
                if (picked is null || !ReferenceEquals(TopLevel.GetTopLevel(export), owner)) return;
                if (picked.TryGetLocalPath() is not { } path)
                {
                    Report("Choose a local file for export.");
                    return;
                }
                if (File.Exists(path))
                {
                    if (owner.DataContext is not MainViewModel vm ||
                        !await vm.Dialogs.ConfirmAsync("Replace text export?",
                            $"Replace {Formatters.SafeName(path)} with the current {subject} text?", "Replace file")) return;
                    if (!ReferenceEquals(TopLevel.GetTopLevel(export), owner)) return;
                }
                await WriteUtf8AtomicAsync(path, text);
                if (ReferenceEquals(TopLevel.GetTopLevel(export), owner))
                    Report($"Exported {subject} text to {Formatters.SafeName(path)}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Report("Could not export the text: " + Formatters.SafeName(ex.Message));
            }
        };
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { import, export },
        });
        panel.Children.Add(status);
    }

    internal static async Task<string> ReadUtf8Async(string path, CancellationToken cancellation = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaxBytes) throw new InvalidDataException("The file exceeds the 8 MiB file-exchange limit.");
        var bytes = new byte[MaxBytes + 1];
        int count = 0;
        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(count), cancellation);
            if (read == 0) break;
            count += read;
        }
        if (count > MaxBytes) throw new InvalidDataException("The file exceeds the 8 MiB file-exchange limit.");
        int offset = count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Utf8.GetString(bytes, offset, count - offset);
    }

    internal static async Task WriteUtf8AtomicAsync(string path, string text, CancellationToken cancellation = default)
    {
        byte[] bytes = Utf8.GetBytes(text);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The text exceeds the 8 MiB file-exchange limit.");
        string target = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temp, target, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
