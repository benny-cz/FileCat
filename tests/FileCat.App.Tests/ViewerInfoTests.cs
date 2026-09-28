using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class ViewerInfoTests
{
    [AvaloniaFact]
    public async Task The_viewer_shows_an_executables_structure_in_Info_mode_and_Esc_still_closes_it()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var bytes = File.ReadAllBytes(typeof(FileCat.Core.Inspect.PeInspector).Assembly.Location);
        var viewer = new ViewerWindow(services, new MemoryContentSource("FileCat.Core.dll", bytes), "FileCat.Core.dll", hex: false);
        bool closed = false;
        viewer.Closed += (_, _) => closed = true;
        viewer.Show();
        try
        {
            await viewer.ShowInfoAsync();
            Assert.StartsWith("PE32", viewer.InfoText);
            Assert.Contains("IL only", viewer.InfoText);
            viewer.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(closed);
        }
        finally
        {
            if (!closed) viewer.Close();
            services.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
