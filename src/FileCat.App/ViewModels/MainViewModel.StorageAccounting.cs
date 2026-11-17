using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.FileSystem;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private StorageHistoryAccountingRunner? _storageAccounting;
    private StorageHistoryAccountingRunner StorageAccounting
    {
        get
        {
            if (_storageAccounting is not null) return _storageAccounting;
            var runner = _storageAccounting = new(Services.Io, PathUtil.GetDeviceKey(Services.Paths.JournalDirectory), Services.Jobs.AccountStorageHistory);
            runner.Changed += () => Dispatcher.UIThread.Post(UpdateStorageAccounting);
            return runner;
        }
    }

    private void StartStorageAccounting()
    {
        if (EditSessionsActive && Services.Jobs.SupportsStorageHistory) StorageAccounting.Start();
    }

    private void UpdateStorageAccounting()
    {
        if (!EditSessionsActive || _storageAccounting is null) return;
        var state = _storageAccounting.State;
        Operations.IsStorageHistoryAccounting = state.Running;
        Operations.StorageAccounting = state.Result;
        Operations.StorageAccountingStatus = state.Running
            ? state.Stopping ? "Stopping history accounting; waiting for its current read to retire…" : "History accounting/verification still in progress…"
            : state.UserStopped ? "History accounting stopped. Refresh checks it again; mutation admission still performs a fresh census."
            : state.Error is { } error ? "History accounting could not finish: " + Formatters.SafeName(error)
            : state.Result is { } result
                ? !result.Complete ? "History accounting is incomplete: " + Formatters.SafeName(result.Problem ?? "Some evidence could not be verified.")
                : result.Journals + result.Artifacts == 0 ? string.Empty
                : $"Observed history at {state.ObservedUtc?.ToLocalTime():T}: {result.Journals:N0} journals, {result.PendingHistories:N0} pending histories; " +
                  $"{Formatters.SizeWithUnit(result.EvidenceBytes)} evidence + {Formatters.SizeWithUnit(result.ReservedReviewBytes)} reserved for review." +
                  (result.ChargedBytes > 256L * 1024 * 1024 ? " The observed 256 MiB process allowance is exceeded; new transforms are refused." : "")
            : string.Empty;
        if (Services.Jobs.SupportsStorageHistory && !state.Running && !state.Interactive && state.NeedsScan && !state.UserStopped) _storageAccounting.Start();
    }

    private void InvalidateStorageAccounting()
    {
        if (!Services.Jobs.SupportsStorageHistory) return;
        StorageAccounting.Invalidate(); StartStorageAccounting();
    }
}
