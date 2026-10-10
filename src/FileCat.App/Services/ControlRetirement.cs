using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace FileCat.App.Services;

internal static class ControlRetirement
{
    /// <summary>Retires replaced item contexts without waiting for another layout pass.</summary>
    internal static void ClearItems(ItemsControl list)
    {
        // Focus restoration and scroll anchoring can remember a realized row after ItemsSource is cleared.
        // Retire its contexts and template children while they are still discoverable through the list.
        foreach (var row in list.GetRealizedContainers().ToArray())
        {
            var controls = row.GetVisualDescendants().OfType<Control>().Prepend(row).ToArray();
            foreach (var control in controls) control.DataContext = null;
            if (row is ContentControl content) content.Content = null;
            foreach (var presenter in controls.OfType<ContentPresenter>())
            {
                presenter.Content = null;
                presenter.UpdateChild();
            }
        }
        list.ItemsSource = null;
    }
}
