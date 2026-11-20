using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Services;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private string? _rescanReason;
        internal void RefreshRescanAvailability()
        {
            if (_disposed) return;
            RefreshRecordedResultAvailability();
            RefreshBranchAvailability();
            string? refusal = _branchRefresh is not null ? "A branch refresh is already running." :
                Model.RescanUnavailableReason ?? (!_window._admissionPool.HasCapacity ? _window._admissionPool.Refusal : null);
            _rescan.IsEnabled = refusal is null;
            string detail = refusal ?? "Revalidate and scan the same captured locations with the same component policy. Open the new epoch in another tab; keep this result, marks and review basket. Focus is retained only where entry identity can be verified.";
            if (_rescanReason == detail) return;
            _rescanReason = detail;
            AutomationProperties.SetHelpText(_rescanHelp, detail);
            ToolTip.SetTip(_rescanHelp, new RichTip("Rescan all locations", detail: detail));
        }

        private async Task RescanAllAsync()
        {
            if (_disposed || _branchRefresh is not null || !Model.CanRescan) return;
            AnalysisSessionAdmission.Slot? admission = null; DiskSpaceViewModel? refreshed = null;
            using var actual = _actualWork.Enter();
            try
            {
                admission = _window._admissionPool.Acquire(); // Reserve before any private/source I/O.
                refreshed = await Model.CreateRescannedAsync();
                if (_disposed || _window._closed || refreshed is null) return;
                var transferred = admission; admission = null;
                _window.AddReservedSession(refreshed, [], transferred);
                refreshed = null;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
            { if (!_disposed) Model.SetStatus("Rescan unavailable: " + AnalysisNames.Display(error.Message, 400)); }
            finally
            {
                Task close = Task.CompletedTask;
                if (refreshed is not null) { refreshed.Dispose(); close = refreshed.CloseCompletion; }
                if (admission is not null) await admission.CloseAfterAsync(close);
                else await close;
            }
        }
    }
}
