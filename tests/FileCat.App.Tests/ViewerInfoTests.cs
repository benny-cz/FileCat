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
    public async Task An_executable_opens_on_its_structure_where_Find_searches_and_F4_shows_its_bytes()
    {
        var ct = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var bytes = File.ReadAllBytes(typeof(FileCat.Core.Inspect.PeInspector).Assembly.Location);
        var viewer = new ViewerWindow(services, new MemoryContentSource("FileCat.Core.dll", bytes), "FileCat.Core.dll", hex: false);
        viewer.Show();
        try
        {
            // F3 on a program opens on its structure (as Salamander's viewer opens its PE viewer), not on its bytes as text.
            for (int i = 0; i < 250 && !viewer.InfoText.StartsWith("PE32", StringComparison.Ordinal); i++) await Task.Delay(20, ct);
            Assert.True(viewer.IsInfoShown);
            Assert.Contains("Optional header", viewer.InfoText);
            Assert.Contains("Referenced assemblies", viewer.InfoText);

            // Find searches the report.
            var search = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer).OfType<Avalonia.Controls.TextBox>().First(t => t.PlaceholderText == "Find (Ctrl+F)");
            Assert.True(search.IsVisible);
            search.Text = "Referenced assemblies";
            viewer.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
            for (int i = 0; i < 250 && viewer.InfoTop == 0; i++) await Task.Delay(20, ct);
            Assert.True(viewer.InfoTop > 0, viewer.StatusText);
            // The report scrolled to the line that holds the match.
            int line = viewer.InfoText.LastIndexOf('\n', viewer.InfoText.IndexOf("Referenced assemblies", StringComparison.Ordinal)) + 1;
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(viewer.InfoText.AsSpan(0, line)), viewer.InfoTop);

            // F4: the bytes, in hex for a binary file.
            viewer.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
            Assert.False(viewer.IsInfoShown);
        }
        finally
        {
            viewer.Close();
            services.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

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
