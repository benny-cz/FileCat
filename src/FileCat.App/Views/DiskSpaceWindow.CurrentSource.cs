using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed record RecordedSourceChoice(string Name, AnalysisKind Kind);
    private bool _choosingCurrentSource;

    private async Task BindCurrentSourceAsync(AppServices services, RecordedSourceChoice recorded, Action<string> status, CancellationToken lifetime)
    {
        if (_closed || _choosingCurrentSource || _dialogs.IsOpen || lifetime.IsCancellationRequested) return;
        if (!CanAddSession) { ExplainSessionLimit(); return; }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime, _indexedLifetime.Token);
        _choosingCurrentSource = true;
        try
        {
            if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
            // The chooser receives only a title and type. Recorded locators and names
            // must never seed the current picker, be resolved, or open a source.
            IReadOnlyList<IStorageItem> picked = recorded.Kind == AnalysisKind.Directory
                ? await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the current folder", AllowMultiple = false })
                : await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose the current file", AllowMultiple = false });
            using var items = new PickedStorageItems(picked);
            if (picked.Count != 1) return;
            cancel.Token.ThrowIfCancellationRequested();
            string path = picked[0].TryGetLocalPath() ?? throw new NotSupportedException("Choose a directly accessible local file or folder.");
            ValidateCurrentSourceChoice(path);
            if (!await ReviewCurrentSourceAsync(_dialogs, recorded.Name, path, cancel.Token)) return;
            cancel.Token.ThrowIfCancellationRequested();
            bool network = await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(path), _ => PathUtil.IsOnNetwork(path), cancel.Token);
            if (network) throw new NotSupportedException("Choose a file or folder on a local disk.");
            cancel.Token.ThrowIfCancellationRequested();
            if (!CanAddSession) { ExplainSessionLimit(); return; }
            var model = DiskSpaceViewModel.ForCurrentSource(services, recorded.Kind);
            try { AddSession(model, [path]); }
            catch { model.Dispose(); throw; }
            await model.StartAsync([path]);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        { if (!_closed && !cancel.IsCancellationRequested) status(AnalysisNames.Display(error.Message, 400)); }
        finally { _choosingCurrentSource = false; }
    }

    private static void ValidateCurrentSourceChoice(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0') || path.Contains('\ufffd') ||
            !AnalysisPrivateInventoryStore.WellFormedText(path) || System.Text.Encoding.UTF8.GetByteCount(path) > AnalysisLimits.LocatorBytes ||
            path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(component => component is "." or ".."))
            throw new NotSupportedException("Choose an absolute local path from the file chooser.");
        if (OperatingSystem.IsWindows() && Path.GetPathRoot(path) is not [>= 'A' and <= 'Z' or >= 'a' and <= 'z', ':', '\\' or '/'])
            throw new NotSupportedException("Choose a file or folder on a local drive.");
    }

    internal static async Task<bool> ReviewCurrentSourceAsync(IDialogService dialogs, string recorded, string current, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Dispatcher.UIThread.Post(() => closer.Close(false)));
        string currentDisplay = AnalysisNames.DisplayPath(current, OperatingSystem.IsWindows() ? AnalysisNameRules.Windows : AnalysisNameRules.Unix);
        var review = new TextBlock { TextWrapping = TextWrapping.Wrap,
            Text = "Recorded entry\n" + AnalysisNames.Display(recorded, 200) + "\n\nCurrent item you chose\n" + currentDisplay +
                "\n\nRead current metadata in a new Standard analysis. The recorded analysis stays offline. Review the new observations before using live actions." };
        var choice = await dialogs.ShowCustomAsync("Review current source", review,
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Analyze current selection", true)], closer: closer);
        return choice is true && !cancellation.IsCancellationRequested;
    }

    private sealed partial class SessionView
    {
        private readonly Button _bindCurrentSource = new() { Content = "Bind current source…", IsVisible = false };

        private RecordedSourceChoice? RecordedChoice()
        {
            var tree = Model.Tree;
            if (tree is null || tree.Snapshot.Origin == AnalysisOrigin.LiveSession || Model.HistoryServices is null ||
                tree.Items.GetValueOrDefault(Model.Selection.Focus ?? "")?.CapturedNode is not { Kind: AnalysisKind.File or AnalysisKind.Directory } node ||
                !tree.Snapshot.Roots.Any(root => root.Id == node.RootId && root.Scheme == "file")) return null;
            return new(node.Name, node.Kind);
        }

        private async Task BindRecordedSourceAsync(RecordedSourceChoice choice)
        {
            if (_disposed || Model.HistoryServices is not { } services) return;
            using var work = _actualWork.Enter();
            await _window.BindCurrentSourceAsync(services, choice, Model.SetStatus, Model.HistoryLifetime);
        }
    }

    private sealed partial class IndexedSessionView
    {
        private readonly Button _bindIndexedSource = new() { Content = "Bind current source…", IsVisible = false };

        private RecordedSourceChoice? IndexedRecordedChoice()
        {
            if (_services is null || _explorer.Descriptor.Origin == AnalysisOrigin.LiveSession ||
                _shownInspection?.Row.Path is not { Kind: AnalysisKind.File or AnalysisKind.Directory } path || path.Id != _focus ||
                !_explorer.Descriptor.Roots.Any(root => root.Id == path.RootId && root.Scheme == "file")) return null;
            return new(path.Name, path.Kind);
        }

        private async Task BindIndexedSourceAsync()
        {
            if (_disposed || _services is not { } services || IndexedRecordedChoice() is not { } choice) return;
            using var work = _actualWork.Enter();
            await _window.BindCurrentSourceAsync(services, choice, text => _inspectorStatus.Text = text, _selectionLifetime.Token);
        }
    }
}
