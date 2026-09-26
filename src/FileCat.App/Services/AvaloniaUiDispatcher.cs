using Avalonia.Threading;
using FileCat.Core.Threading;

namespace FileCat.App.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static readonly AvaloniaUiDispatcher Instance = new();

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
