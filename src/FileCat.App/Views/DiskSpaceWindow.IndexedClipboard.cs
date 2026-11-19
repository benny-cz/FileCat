using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class IndexedSessionView
    {
        private readonly Button _copyCapturedPath = new() { Content = "Copy path", IsEnabled = false };
        private string? _clipboardPath;

        private void SetClipboardInspection(AnalysisExplorerInspection? detail)
        {
            if (detail is null) _inspectionReading.IsVisible = false;
            (string? path, string refusal) = detail switch
            {
                null => (null, "Choose a captured entry with a literal path."),
                { AncestorsComplete: false } => (null, "The full captured ancestry exceeds the inspector limit. The inventory retains its raw names."),
                _ when _explorer.Descriptor.Roots.SingleOrDefault(root => root.Id == detail.Row.Path.RootId) is { } root =>
                    AnalysisClipboardPath.Format(root, ClipboardAncestry(detail)),
                _ => (null, "No location was recorded for this entry.")
            };
            _clipboardPath = path;
            string help = path is null ? refusal : "Copy the literal path from the captured inventory.";
            AutomationProperties.SetHelpText(_copyCapturedPath, help);
            ToolTip.SetTip(_copyCapturedPath, new RichTip("Copy captured path", detail: help));
            RefreshClipboardButton();
        }

        private void RefreshClipboardButton() => _copyCapturedPath.IsEnabled = !_disposed && _clipboardPath is not null &&
            _shownInspection?.Row.Path.Id == _focus;

        private async Task CopyCapturedPathAsync()
        {
            if (!_copyCapturedPath.IsEnabled || _shownInspection?.Row.Path.Id != _focus || _clipboardPath is not { } path) return;
            await CopyCapturedTextAsync(path);
        }

        private async Task CopyCapturedTextAsync(string? text)
        {
            if (_disposed || string.IsNullOrEmpty(text)) return;
            using var work = _actualWork.Enter();
            if (_window.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
            else _inspectorStatus.Text = "The clipboard is unavailable.";
        }

        private static IEnumerable<AnalysisClipboardPath.Part> ClipboardAncestry(AnalysisExplorerInspection detail)
        {
            var leaf = detail.Row.Path;
            yield return new(leaf.Name, leaf.NameEncoding, leaf.RawName, leaf.ParentId == 0);
            for (int index = detail.Ancestors.Length - 1; index >= 0; index--)
            {
                var part = detail.Ancestors[index];
                yield return new(part.Name, part.NameEncoding, part.RawName, part.ParentId == 0);
            }
        }
    }
}
