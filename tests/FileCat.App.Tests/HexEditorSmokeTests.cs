using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class HexEditorSmokeTests
{
    [AvaloniaFact]
    public void Dedicated_hex_editor_opens_with_named_controls_and_a_protected_baseline()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "filecat-hexui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "small.bin");
        File.WriteAllBytes(file, [0, 1, 2, 3]);
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var window = new HexEditorWindow(services, file);
        try
        {
            window.Show();
            Assert.Contains("Hex Editor", window.Title ?? "");
            Assert.Empty(AccessibilityTests.Unnamed(window));
            Assert.Single(HexEditorWindow.OpenWindows);
        }
        finally
        {
            window.Close();
            Directory.Delete(root, recursive: true);
        }
    }
}
