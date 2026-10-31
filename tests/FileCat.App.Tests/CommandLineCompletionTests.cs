using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

/// <summary>Tab in the command line completes names from the panel's folder, as shells do.</summary>
public sealed class CommandLineCompletionTests
{
    [Fact]
    public void Tab_completes_the_word_before_the_caret_and_steps_through_the_names()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-cmdline", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "alpha.txt"), "a");
            Directory.CreateDirectory(Path.Combine(root, "alpine"));
            File.WriteAllText(Path.Combine(root, "alpine", "inner.txt"), "i");
            Directory.CreateDirectory(Path.Combine(root, "My Documents"));
            char sep = Path.DirectorySeparatorChar;

            var first = CommandLineCompletion.Complete("type al", 7, root, backwards: false, cycle: null)!.Value;
            Assert.Equal(("type alpha.txt", 14), (first.Text, first.Caret));
            var second = CommandLineCompletion.Complete(first.Text, first.Caret, root, backwards: false, first.Cycle)!.Value;
            Assert.Equal($"type alpine{sep}", second.Text); // a folder keeps its separator, for the next Tab
            var wrapped = CommandLineCompletion.Complete(second.Text, second.Caret, root, backwards: false, second.Cycle)!.Value;
            Assert.Equal("type alpha.txt", wrapped.Text);
            var back = CommandLineCompletion.Complete("type al", 7, root, backwards: true, cycle: null)!.Value;
            Assert.Equal($"type alpine{sep}", back.Text); // Shift+Tab starts from the last

            // Inside a folder, and after the caret the rest of the line stays.
            var inner = CommandLineCompletion.Complete($"type alpine{sep}in > out.txt", 14, root, backwards: false, cycle: null)!.Value;
            Assert.Equal($"type alpine{sep}inner.txt > out.txt", inner.Text);

            // A name with spaces is quoted; a folder leaves the caret inside the quotes.
            var spaced = CommandLineCompletion.Complete("cd My", 5, root, backwards: false, cycle: null)!.Value;
            Assert.Equal($"cd \"My Documents{sep}\"", spaced.Text);
            Assert.Equal(spaced.Text.Length - 1, spaced.Caret);
            var typedQuote = CommandLineCompletion.Complete("cd \"My D", 8, root, backwards: false, cycle: null)!.Value;
            Assert.Equal($"cd \"My Documents{sep}\"", typedQuote.Text);

            // A full path; nothing to complete.
            var full = CommandLineCompletion.Complete($"dir {root}{sep}alp", root.Length + 8, Path.GetTempPath(), backwards: false, cycle: null)!.Value;
            Assert.Equal($"dir {root}{sep}alpha.txt", full.Text);
            Assert.Null(CommandLineCompletion.Complete("type zz", 7, root, backwards: false, cycle: null));
            Assert.Null(CommandLineCompletion.Complete("type ", 5, root, backwards: false, cycle: null));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task Tab_in_the_command_line_completes_a_name_from_the_panel()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && window.FocusManager?.GetFocusedElement() is not FileCat.App.Controls.FileListControl; i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.CommandLineFocus);
            var line = window.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single(t => t.Name == "CommandLine");
            for (int i = 0; i < 100 && !line.IsFocused; i++) await Task.Delay(20, ct);
            line.Text = "type b";
            line.CaretIndex = 6;
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            for (int i = 0; i < 250 && line.Text != "type b.txt"; i++) await Task.Delay(20, ct); // read off the UI thread
            Assert.Equal("type b.txt", line.Text);
            Assert.True(line.IsFocused); // Tab completed instead of leaving the command line
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Ctrl_Enter_inserts_the_focused_name_at_the_caret_or_selection()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && listing.VisibleCount < 3; i++) await Task.Delay(20, ct);
            listing.SetFocus(2); // b.txt, after the synthetic parent and a.txt
            vm.Execute(CommandIds.CommandLineFocus);
            var line = window.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single(t => t.Name == "CommandLine");
            for (int i = 0; i < 100 && !line.IsFocused; i++) await Task.Delay(20, ct);
            Assert.True(line.IsFocused);

            line.Text = "echo before after";
            line.CaretIndex = 12;
            window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
            for (int i = 0; i < 100 && line.Text != "echo before b.txt after"; i++) await Task.Delay(20, ct);
            Assert.Equal("echo before b.txt after", line.Text);
            Assert.Equal("echo before b.txt ".Length, line.CaretIndex);

            line.Text = "cat placeholder > out";
            line.SelectionStart = 4;
            line.SelectionEnd = 15;
            window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
            for (int i = 0; i < 100 && line.Text != "cat b.txt > out"; i++) await Task.Delay(20, ct);
            Assert.Equal("cat b.txt > out", line.Text);

            line.Text = "echo done";
            vm.Execute(CommandIds.InsertName); // shortcut from the panel still appends
            Assert.Equal("echo done b.txt ", line.Text);

            line.Text = "cat ";
            line.CaretIndex = 4;
            window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.Enter, null);
            string fullPath = Path.Combine(root, "files", "b.txt");
            string quotedPath = FileCat.Core.Tools.ShellQuoting.Quote(fullPath, services.Settings.Terminal.Shell);
            for (int i = 0; i < 100 && line.Text != $"cat {quotedPath} "; i++) await Task.Delay(20, ct);
            Assert.Equal($"cat {quotedPath} ", line.Text);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
