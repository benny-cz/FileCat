using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.App.Views;

/// <summary>Reusable, nonexecuting argument-vector preview for a selected Settings tool draft.</summary>
internal sealed class ToolInvocationPreview
{
    private readonly TextBox _arguments;
    private readonly Func<(ToolDefinition? Tool, string? Error)> _snapshot;
    private readonly TextBox _sample;
    private readonly SelectableTextBlock _result;
    private int _generation;

    public ToolInvocationPreview(string kind, TextBox arguments, Func<(ToolDefinition? Tool, string? Error)> snapshot, string sampleFolder)
    {
        _arguments = arguments;
        _snapshot = snapshot;
        _sample = new TextBox { Text = Path.Combine(sampleFolder, "example file.txt") };
        AutomationProperties.SetName(_sample, kind + " preview file");
        _result = new SelectableTextBlock
        {
            Text = "Choose Preview invocation to inspect argument boundaries. Nothing is launched or written.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 520,
        };
        AutomationProperties.SetName(_result, kind + " invocation preview");
        var tokens = new ComboBox { ItemsSource = ToolLauncher.Placeholders, SelectedIndex = 0, MinWidth = 145 };
        AutomationProperties.SetName(tokens, kind + " argument token");
        var add = new Button { Content = "Add " + kind.ToLowerInvariant() + " token" };
        add.Click += (_, _) =>
        {
            if (tokens.SelectedItem is not string token) return;
            if (!ToolTextGrammar.TryParseArguments(_arguments.Text, out var values, out var error))
            {
                _result.Text = "Arguments: " + error;
                AutomationProperties.SetHelpText(_arguments, error);
                _arguments.Focus();
                return;
            }
            values.Add(token);
            _arguments.Text = ToolTextGrammar.FormatArguments(values);
            AutomationProperties.SetHelpText(_arguments, null);
            _arguments.Focus();
        };
        var preview = new Button { Content = "Preview " + kind.ToLowerInvariant() + " invocation" };
        preview.Click += async (_, _) =>
        {
            int request = ++_generation;
            var owner = TopLevel.GetTopLevel(preview);
            if (owner is null) return;
            var sample = _sample.Text ?? string.Empty;
            if (!Path.IsPathFullyQualified(sample))
            {
                _result.Text = "Preview file: enter a full local path.";
                return;
            }
            var (tool, error) = _snapshot();
            if (tool is null)
            {
                _result.Text = error ?? "Select a command first.";
                return;
            }
            _result.Text = "Checking invocation…";
            var output = await Task.Run(() => Format(tool, sample));
            if (request == _generation && ReferenceEquals(TopLevel.GetTopLevel(preview), owner)) _result.Text = output;
        };
        _sample.TextChanged += (_, _) => Retire();
        View = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                DialogLayout.Actions(tokens, add),
                new TextBlock { Text = "Preview file (sample only)" },
                _sample,
                preview,
                _result,
            },
        };
    }

    public Control View { get; }

    public void Retire()
    {
        _generation++;
        _result.Text = "Preview out of date. Choose Preview invocation again.";
    }

    private static string Format(ToolDefinition tool, string sample)
    {
        try
        {
            var plan = ToolLauncher.Preview(tool,
                new ToolContext([sample], Path.GetDirectoryName(sample) ?? sample),
                Path.GetTempPath(), out var warning);
            var text = new StringBuilder("Sample invocation; no program or list file was created.\n");
            foreach (var (program, arguments) in plan)
            {
                text.Append("Program: ").Append(program).Append("\nArguments (").Append(arguments.Count).Append("):\n");
                for (int i = 0; i < arguments.Count; i++)
                    text.Append(i + 1).Append(": ").Append(JsonSerializer.Serialize(arguments[i])).Append('\n');
            }
            if (tool.Arguments.Contains("{listfile}")) text.Append("A list file is created only when this tool is launched.\n");
            if (warning is not null) text.Append(warning);
            return text.Length <= 16384 ? text.ToString() : text.ToString(0, 16384) + "\nPreview truncated.";
        }
        catch (Exception ex) when (ex is ToolLaunchException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return "Preview could not resolve the invocation: " + ex.Message;
        }
    }
}
