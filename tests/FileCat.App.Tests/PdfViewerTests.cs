using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class PdfViewerTests
{
    private static byte[] OwnedPdf() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "TestData", "Viewer", "owned-two-page.pdf"));

    [AvaloniaFact]
    public async Task F3_pdf_renders_two_pages_and_Info_describes_the_document_without_losing_F4_bytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-pdf-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services, new MemoryContentSource("owned-two-page.pdf", OwnedPdf()),
            "owned-two-page.pdf", hex: false);
        viewer.Show();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++) await Task.Delay(20, ct);
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), ct);
            Assert.NotNull(viewer.Picture);
            Assert.Equal(2, viewer.Picture!.Frames);
            Assert.Contains("page 1 of 2", viewer.StatusLine, StringComparison.Ordinal);
            var next = viewer.GetVisualDescendants().OfType<Button>().Single(b =>
                Avalonia.Automation.AutomationProperties.GetName(b) == "Next PDF page");
            Assert.True(next.IsEnabled);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), ct);
            Assert.Contains("page 2 of 2", viewer.StatusLine, StringComparison.Ordinal);
            Assert.False(next.IsEnabled);
            await viewer.ShowInfoAsync();
            Assert.Contains("Pages: 2", viewer.InfoText, StringComparison.Ordinal);
            Assert.Contains("Current page: 2", viewer.InfoText, StringComparison.Ordinal);
            Assert.Contains("612 × 792 points", viewer.InfoText, StringComparison.Ordinal);
            viewer.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
            Assert.False(viewer.IsInfoShown);
        }
        finally
        {
            viewer.Close();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task A_malformed_pdf_reports_a_render_failure_and_keeps_original_bytes_available()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-pdf-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services, new MemoryContentSource("bad.pdf", "%PDF-1.4\nbroken"u8.ToArray()),
            "bad.pdf", hex: false);
        viewer.Show();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++) await Task.Delay(20, ct);
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), ct);
            Assert.Null(viewer.Picture);
            var message = viewer.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => t.Text?.Contains("PDF page cannot be shown", StringComparison.Ordinal) == true);
            Assert.NotNull(message);
            viewer.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
            Assert.False(viewer.IsInfoShown);
        }
        finally
        {
            viewer.Close();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task An_encrypted_pdf_explains_why_preview_is_unavailable_and_keeps_bytes_available()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-pdf-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "Viewer", "owned-encrypted.pdf"));
        var viewer = new ViewerWindow(services, new MemoryContentSource("owned-encrypted.pdf", bytes),
            "owned-encrypted.pdf", hex: false);
        viewer.Show();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++) await Task.Delay(20, ct);
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), ct);
            Assert.Null(viewer.Picture);
            Assert.Contains(viewer.GetVisualDescendants().OfType<TextBlock>(), t =>
                t.Text?.Contains("Password required", StringComparison.OrdinalIgnoreCase) == true);
            viewer.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.None);
            Assert.False(viewer.IsInfoShown);
        }
        finally
        {
            viewer.Close();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Refresh_clamps_the_page_when_a_PDF_now_has_fewer_pages()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-pdf-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        byte[] replacement = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", "Viewer", "owned-one-page.pdf"));
        Task<IContentSource?> Reopen(CancellationToken _) => Task.FromResult<IContentSource?>(
            new MemoryContentSource("owned.pdf", replacement));
        var viewer = new ViewerWindow(services, new MemoryContentSource("owned.pdf", OwnedPdf()),
            "owned.pdf", hex: false, deviceKey: "pdf-refresh", preparedReader: null, reopen: Reopen);
        viewer.Show();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++) await Task.Delay(20, ct);
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), ct);
            var next = viewer.GetVisualDescendants().OfType<Button>().Single(b =>
                Avalonia.Automation.AutomationProperties.GetName(b) == "Next PDF page");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), ct);
            Assert.Contains("page 2 of 2", viewer.StatusLine, StringComparison.Ordinal);
            for (int i = 0; i < 250 && !viewer.GetVisualDescendants().OfType<Button>()
                     .Any(b => b.Content?.ToString() == "Refresh" && b.IsEnabled); i++) await Task.Delay(20, ct);
            await viewer.RefreshAsync();
            Assert.Equal(1, viewer.Picture?.Frames);
            Assert.Contains("page 1 of 1", viewer.StatusLine, StringComparison.Ordinal);
        }
        finally
        {
            viewer.Close();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
