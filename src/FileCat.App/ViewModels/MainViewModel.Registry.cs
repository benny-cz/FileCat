using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private void ViewRegistryValue(ItemRef item, bool raw) => _ = ViewRegistryValueAsync(item, raw);

    private async Task ViewRegistryValueAsync(ItemRef item, bool raw)
    {
        try
        {
            var value = await Task.Run(() =>
            {
                using var key = WindowsRegistryProvider.Open(item.Parent, false);
                return RegistryRaw.Read(key, item.Name);
            });
            if (value.Data.Length != value.Length)
            {
                Notify($"This Registry value is {value.Length:N0} bytes; the editor/viewer limit is {RegistryRaw.EditLimit:N0} bytes.", true);
                return;
            }
            var display = $"{item.Parent.Path}\\{(item.Name.Length == 0 ? "(Default)" : item.Name)}";
            if (raw)
            {
                new ViewerWindow(Services, new MemoryContentSource(display + " · " + value.TypeName, value.Data), display, hex: true).Show();
                return;
            }
            var result = await Dialogs.ShowCustomAsync("Registry value · read only",
                new TextBlock
                {
                    Text = $"{display}\n{value.TypeName} · {value.Length:N0} bytes\n\n{RegistryRaw.Preview(value)}\n\nStored data is shown without expanding variables or executing it.",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 640,
                },
                [new DialogButton("Close", "close", IsDefault: true, IsCancel: true), new DialogButton("View raw bytes", "raw")]);
            if (result as string == "raw")
                new ViewerWindow(Services, new MemoryContentSource(display + " · " + value.TypeName, value.Data), display, hex: true).Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Notify($"Cannot read Registry value: {ex.Message}", true);
        }
    }
}
