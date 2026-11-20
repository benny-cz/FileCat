using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private static Button PrimaryPreviewButton()
    {
        var button = new Button { Content = "Preview file", IsEnabled = false };
        ToolTip.SetShowOnDisabled(button, true);
        return button;
    }

    private static string? PreviewRefusal(ulong? path, AnalysisIndexedAcquisition? runtime)
    {
        if (!path.HasValue) return "Focus a regular file to preview its current primary contents.";
        if (!OperatingSystem.IsWindows()) return new AnalysisCapabilities().For(AnalysisVerb.Preview).Reason;
        return runtime?.CanReviewContentReads == true ? null : "Preview requires a finished live local NTFS analysis.";
    }

    private static void UpdatePreviewButton(Button button, string? refusal)
    {
        button.IsEnabled = refusal is null;
        string help = refusal ?? "Show a bound, read-only preview of the first 16 KiB of the selected file.";
        if (Equals(button.Tag, help)) return;
        button.Tag = help;
        ToolTip.SetTip(button, help);
        AutomationProperties.SetHelpText(button, help);
    }

    private async Task ShowPrimaryPreviewAsync(AnalysisIndexedAcquisition runtime, SessionIdentity identity, ulong path, CancellationToken lifetime)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var preview = await IndexedComparisonProgressAsync("Reading the selected file",
            token => runtime.PreviewPrimaryAsync(identity, path, token), cancel.Token,
            "Reading a bounded preview from the reviewed live file. Cancel waits for the active read to finish.");
        cancel.Token.ThrowIfCancellationRequested();
        string size = preview.Item.Target.Bytes == 0 ? "The reviewed file is empty." :
            preview.BytesShown == preview.Item.Target.Bytes ? $"{preview.BytesShown:N0} bytes shown · {preview.Format}" :
            $"First {preview.BytesShown:N0} of {preview.Item.Target.Bytes:N0} bytes · {preview.Format}";
        var content = new TextBox
        {
            Text = preview.Content, IsReadOnly = true, AcceptsReturn = true, MinHeight = 140, MaxHeight = 460,
            TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse("monospace"), IsVisible = preview.BytesShown != 0
        };
        AutomationProperties.SetName(content, "Live primary file preview");
        ScrollViewer.SetVerticalScrollBarVisibility(content, ScrollBarVisibility.Auto);
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = ExactDuplicatePath(preview.Item), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = size, TextWrapping = TextWrapping.Wrap },
                content
            }
        };
        var closer = new DialogCloser();
        using var close = cancel.Token.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        cancel.Token.ThrowIfCancellationRequested();
        try
        {
            await _dialogs.ShowCustomAsync("File preview", body,
                [new DialogButton("Close preview", false, IsDefault: true, IsCancel: true)], closer: closer, resizable: true);
        }
        finally { content.Text = ""; }
    }

    private sealed partial class SessionView
    {
        private readonly Button _previewFile = PrimaryPreviewButton();
        private ulong? FocusedPreviewPath => Model.Selection.Focus is { } key &&
            Model.Tree?.Items.GetValueOrDefault(key) is { Kind: AnalysisKind.File, Synthetic: false, NodeId: { } id } ? id : null;

        private Task PreviewFocusedAsync() => PreviewAsync(FocusedPreviewPath, Model.Tree?.Snapshot.Identity);

        private async Task PreviewAsync(ulong? path, SessionIdentity? identity)
        {
            if (_disposed || _window._dialogs.IsOpen || Model.Comparison is not null) return;
            var runtime = Model.BranchRuntime;
            if (PreviewRefusal(path, runtime) is { } reason) { Model.SetStatus(reason); return; }
            if (identity is null || identity != Model.Tree?.Snapshot.Identity)
            { Model.SetStatus("The displayed revision changed. Choose the file again."); return; }
            using var work = _actualWork.Enter();
            var lifetime = Model.HistoryLifetime;
            Model.Interaction(true);
            try { await _window.ShowPrimaryPreviewAsync(runtime!, identity, path!.Value, lifetime); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (IndexedExpectedError(error))
            {
                _actualWork.RecordUnconfirmedClose(error);
                if (AnalysisCloseUnconfirmedException.IsPresent(error)) _window.RecordIndexedCloseFailure(error);
                if (!_disposed) Model.SetStatus("Preview unavailable. " + AnalysisNames.Display(error.Message, 360));
            }
            finally { Model.Interaction(false); }
        }
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _previewFile = PrimaryPreviewButton();
        private ulong? FocusedPreviewPath => _focus != 0 &&
            _shownInspection?.Row.Path is { Kind: AnalysisKind.File } path && path.Id == _focus ? _focus : null;

        private async Task PreviewFocusedAsync()
        {
            if (_disposed || _window._dialogs.IsOpen) return;
            var path = FocusedPreviewPath;
            var runtime = _runtimeAcquisition;
            if (PreviewRefusal(path, runtime) is { } reason) { _status.Text = reason; return; }
            using var work = _actualWork.Enter();
            Interaction(true);
            try { await _window.ShowPrimaryPreviewAsync(runtime!, _source.Descriptor.Identity, path!.Value, _selectionLifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) when (Expected(error))
            {
                RememberViewCloseFailure(error);
                if (!_disposed) _status.Text = "Preview unavailable. " + AnalysisNames.Display(error.Message, 360);
            }
            finally { Interaction(false); }
        }
    }
}
