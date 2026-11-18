using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    internal static async Task<bool> ConfirmFastNtfsAsync(IDialogService dialogs, FastNtfsScopeReview scope, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var closer = new DialogCloser();
        using var close = cancellation.Register(() => Dispatcher.UIThread.Post(() => closer.Close(false)));
        var selection = AnalysisNames.Display(scope.Selection, 80);
        var volume = AnalysisNames.Display(scope.Volume, 80);
        var components = scope.Components == AnalysisComponentPreset.DetailedAllocation ? "Detailed allocation" : "Standard allocation";
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Read the whole local NTFS volume {selection}.\n\nVolume identifier {volume}\n{components}\n\n" +
                "Administrator approval is required. The helper reads names and metadata, including entries your account normally cannot see. " +
                "The scan cannot grant cleanup rights. Sizes, allocation and alias coverage can be incomplete."
        };
        var answer = await dialogs.ShowCustomAsync("Review Fast NTFS scope", body,
            [new DialogButton("Cancel", false, IsDefault: true, IsCancel: true), new DialogButton("Continue to authorization", true)], closer: closer);
        return answer is true && !cancellation.IsCancellationRequested;
    }

    private sealed partial class SessionView
    {
        private readonly Button _fastNtfsScan = new() { Content = "Fast NTFS scan…" };
        private readonly TextBlock _fastNtfsHelp = new() { TextWrapping = TextWrapping.Wrap };

        private void AddFastNtfsSetup()
        {
            if (Model.HistoryServices is null) return;
            AutomationProperties.SetAutomationId(_fastNtfsScan, "OpenFastNtfsScopeReview");
            AutomationProperties.SetAutomationId(_fastNtfsHelp, "FastNtfsAvailability");
            ToolTip.SetTip(_fastNtfsScan, new RichTip("Fast NTFS scan", detail:
                "Read one whole local NTFS volume with an installed read-only helper. Review the exact volume before requesting authorization."));
            _fastNtfsScan.Click += async (_, _) =>
            {
                nint owner = _window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle ? handle.Handle : 0;
                await Model.StartFastNtfsAsync(_rootEditors.Select(e => e.Text ?? "").ToArray(), owner,
                    (scope, token) => ConfirmFastNtfsAsync(_window._dialogs, scope, token), CaptureFolderExclusions(), _usePanelFilter.IsChecked == true);
            };
            _setup.Children.Add(_fastNtfsScan);
            _setup.Children.Add(_fastNtfsHelp);
            AttachedToVisualTree += async (_, _) => await Model.CheckFastNtfsHelperAsync();
        }

        private void RefreshFastNtfsSetup()
        {
            if (Model.HistoryServices is null) return;
            var reason = Model.FastNtfsUnavailableReason;
            bool scoped = _folderExclusionEditors.Values.Any(editor => editor.Paths.Text?.Any(c => c is not ('\r' or '\n')) == true);
            _fastNtfsScan.IsVisible = reason is null;
            _fastNtfsScan.IsEnabled = reason is null && !scoped && !Model.IsPreparing;
            _fastNtfsHelp.Text = reason ?? (scoped ? DiskSpaceViewModel.FastNtfsFolderExclusionsReason :
                "For one whole local NTFS volume. Authorization and a scope review are required.");
        }
    }
}
