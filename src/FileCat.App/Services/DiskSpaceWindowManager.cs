using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.App.Services;

/// <summary>Lazy per-instance ownership. Construction performs no source enumeration.</summary>
public sealed class DiskSpaceWindowManager(AppServices services) : IDisposable
{
    internal DiskSpaceWindow? Window { get; private set; }
    internal bool HasTemporaryWork => Window?.HasTemporaryWork == true;

    internal void OpenFromTools(IReadOnlyList<string> roots)
    {
        if (Window is { IsVisible: false, HasSessions: true }) BringForward();
        else Open(roots);
    }

    internal void BringForward()
    {
        if (Window is not { } window) return;
        if (!window.IsVisible) window.Show();
        window.Activate();
    }

    public void Open(IReadOnlyList<string> roots)
    {
        var window = Window ??= Create();
        if (window.CanAddSession) window.AddSession(new DiskSpaceViewModel(services), roots);
        else window.ExplainSessionLimit();
        BringForward();
    }
    public void OpenObservations(AnalysisSnapshot snapshot)
    {
        var window = Window ??= Create();
        if (window.CanAddSession) window.AddSession(new DiskSpaceViewModel(snapshot));
        else window.ExplainSessionLimit();
        BringForward();
    }
    internal void OpenIndexedPreview(IReadOnlyList<string> roots)
    {
        var window = Window ??= Create(indexedPreview: true);
        if (window.CanAddSession) window.AddSession(new DiskSpaceViewModel(services, indexedStorage: true), roots);
        else window.ExplainSessionLimit();
        BringForward();
    }
    private DiskSpaceWindow Create(bool indexedPreview = false)
    {
        var window = new DiskSpaceWindow(() => { if (indexedPreview) OpenIndexedPreview([]); else Open([]); }, () => services.Keymap);
        window.Closed += (_, _) => Window = null; return window;
    }
    public void Dispose() { Window?.ForceClose(); Window = null; }
}
