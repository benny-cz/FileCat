using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.App.Services;

/// <summary>Lazy per-instance ownership. Construction performs no source enumeration.</summary>
public sealed class DiskSpaceWindowManager(AppServices services) : IDisposable
{
    private DiskSpaceWindow? _window;
    public void Open(IReadOnlyList<string> roots)
    {
        _window ??= Create();
        if (_window.SessionCount >= AnalysisLimits.Sessions) { _window.Show(); _window.Activate(); return; }
        _window.AddSession(new DiskSpaceViewModel(services), roots); _window.Show(); _window.Activate();
    }
    public void OpenObservations(AnalysisSnapshot snapshot)
    {
        _window ??= Create(); _window.AddSession(new DiskSpaceViewModel(snapshot)); _window.Show(); _window.Activate();
    }
    private DiskSpaceWindow Create()
    {
        var window = new DiskSpaceWindow(() => Open([]), () => services.Keymap); window.Closed += (_, _) => _window = null; return window;
    }
    public void Dispose() { _window?.ForceClose(); _window = null; }
}
