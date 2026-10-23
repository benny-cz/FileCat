using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>The report window (D-56 file-system records): opens at once, fills in, finds, reads again, and says why it could not read.</summary>
public sealed class ReportWindowTests
{
    [AvaloniaFact]
    public async Task Report_lines_have_bounded_spoken_navigation_without_losing_complete_paths()
    {
        string source = @"C:\work\active\approved-work-item-with-a-long-filename.txt";
        string target = @"C:\work\archive\approved-work-item-with-a-long-filename.txt";
        string result = $"Restored \"{target}\" to \"{source}\".";
        string longLine = new string('x', 4095) + "😀tail";
        string text = $"Undo: Move\r\n{result}\r\n{longLine}\nLast outcome\n";
        var window = new ReportWindow("Undo report", "owned fixture", _ => Task.FromResult(text));
        window.Show();
        try
        {
            await window.Reading!;
            var controls = window.GetVisualDescendants().ToArray();
            var previous = Assert.Single(controls.OfType<Button>(), b => b.Content as string == "Previous report line");
            var next = Assert.Single(controls.OfType<Button>(), b => b.Content as string == "Next report line");
            var readout = Assert.Single(controls.OfType<TextBlock>(), b => b.Text == window.SpokenLine);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(readout));
            Assert.Equal("Line 1 of 4. Undo: Move", window.SpokenLine);
            Assert.False(previous.IsEnabled);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal($"Line 2 of 4. {result}", window.SpokenLine);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.StartsWith("Line 3 of 4, part 1. ", window.SpokenLine, StringComparison.Ordinal);
            Assert.EndsWith("😀", window.SpokenLine, StringComparison.Ordinal);
            Assert.True(window.SpokenLine.Length < 4200);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Line 3 of 4, part 2. tail", window.SpokenLine);
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.EndsWith("😀", window.SpokenLine, StringComparison.Ordinal);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Line 4 of 4. Last outcome", window.SpokenLine);
            Assert.False(next.IsEnabled);
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Line 3 of 4, part 2. tail", window.SpokenLine);
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.EndsWith("😀", window.SpokenLine, StringComparison.Ordinal);
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal($"Line 2 of 4. {result}", window.SpokenLine);
            Assert.Equal(text, window.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Report_line_readout_escapes_hidden_name_characters()
    {
        var window = new ReportWindow("Undo report", "owned fixture",
            _ => Task.FromResult("Heading\nKept C:\\work\\bad\u202e.txt\n"));
        window.Show();
        try
        {
            await window.Reading!;
            var next = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                b => b.Content as string == "Next report line");
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(@"bad\u202E.txt", window.SpokenLine, StringComparison.Ordinal);
            Assert.DoesNotContain('\u202e', window.SpokenLine);
            Assert.Contains("bad\u202e.txt", window.Text, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    private static string Report(int read) =>
        $"File-system record · NTFS\n\nItem\n  Path  C:\\data\\file.bin\n  Read  {read}\n" +
        string.Concat(Enumerable.Range(0, 300).Select(i => $"  line {i}\n")) + "\nSecurity\n  Owner  someone\n";

    [AvaloniaFact]
    public async Task A_report_opens_at_once_fills_in_finds_reads_again_and_closes_with_Esc()
    {
        var ct = TestContext.Current.CancellationToken;
        int reads = 0;
        var first = new TaskCompletionSource();
        string subject = Path.Combine(Path.GetTempPath(), "data", "file.bin");
        var window = new ReportWindow("File-system record", subject, async token =>
        {
            int read = Interlocked.Increment(ref reads);
            if (read == 1) await first.Task.WaitAsync(token);
            return Report(read);
        });
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();
        try
        {
            // It opens at once and says it is reading; the report fills in when ready.
            Assert.Equal("Reading…", window.Text);
            Assert.StartsWith($"Reading {subject}", window.StatusText, StringComparison.Ordinal);
            Assert.Equal("file.bin · File-system record — FileCat", window.Title);
            first.SetResult();
            await window.Reading!;
            Assert.Contains("Read  1", window.Text, StringComparison.Ordinal);
            Assert.Contains("308 lines · F5 reads it again", window.StatusText, StringComparison.Ordinal);

            // Find (F3) scrolls to the match.
            var search = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBox>().Single(t => t.PlaceholderText == "Find (Ctrl+F)");
            search.Text = "Security";
            window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
            for (int i = 0; i < 250 && window.TopOffset == 0; i++) await Task.Delay(20, ct);
            int line = window.Text.IndexOf("\nSecurity", StringComparison.Ordinal) + 1;
            Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(window.Text.AsSpan(0, line)), window.TopOffset);

            // F5 reads it again, in place.
            window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.None);
            await window.Reading!;
            Assert.Contains("Read  2", window.Text, StringComparison.Ordinal);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(closed);
        }
        finally
        {
            if (!closed) window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_report_that_cannot_be_read_says_why()
    {
        var window = new ReportWindow("File-system record", @"C:\secret", _ => throw new UnauthorizedAccessException(@"C:\secret cannot be opened: access is denied."));
        window.Show();
        try
        {
            await window.Reading!;
            Assert.Equal(@"It could not be read: C:\secret cannot be opened: access is denied.", window.Text);
        }
        finally { window.Close(); }
    }
}
