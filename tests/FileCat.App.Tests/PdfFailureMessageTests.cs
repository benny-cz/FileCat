using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// I654: the viewer appended ". Text and Hex (F4)…" to the PDF library's message, which ends with a period of its own:
/// "This PDF page cannot be shown: File not in PDF format or corrupted.. Text and Hex (F4)…".
/// </summary>
public sealed class PdfFailureMessageTests
{
    private const string Prefix = "This PDF page cannot be shown: ";
    private const string Suffix = ". Text and Hex (F4) show its bytes; Info (Ctrl+I) describes the file.";

    private static async Task<string> FailureMessage(string name, byte[] bytes)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-pdf-tests", Guid.NewGuid().ToString("N"));
        using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services, new MemoryContentSource(name, bytes), name, hex: false);
        viewer.Show();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++) await Task.Delay(20, ct);
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad!.WaitAsync(TimeSpan.FromSeconds(40), ct);
            Assert.Null(viewer.Picture);
            return Assert.Single(viewer.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)
                .Where(text => text?.StartsWith(Prefix, StringComparison.Ordinal) == true).Distinct())!;
        }
        finally
        {
            viewer.Close();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task A_pdf_the_library_cannot_read_is_reported_with_its_message_and_one_period()
    {
        string message = await FailureMessage("bad.pdf", "%PDF-1.4\nbroken"u8.ToArray());
        Assert.DoesNotContain("..", message, StringComparison.Ordinal);
        // The library's words as it gave them, but for the period FileCat's own sentence now ends.
        Assert.Equal(Prefix + "File not in PDF format or corrupted" + Suffix, message);
    }

    [AvaloniaFact]
    public async Task An_encrypted_pdf_is_reported_with_its_message_and_one_period()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Viewer", "owned-encrypted.pdf"));
        string message = await FailureMessage("owned-encrypted.pdf", bytes);
        Assert.DoesNotContain("..", message, StringComparison.Ordinal);
        Assert.Equal(Prefix + "Password required or incorrect password" + Suffix, message);
    }
}
