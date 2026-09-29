using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Tools;

namespace FileCat.App.ViewModels;

/// <summary>
/// Apply command (Ctrl+G, FAR; plan §14.2): one command per marked item, previewed exactly as it will run, then run as a
/// job that records each item's exit code.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxApplyItems = 10_000;

    private async Task ApplyCommandAsync()
    {
        var sel = SourceSelection();
        if (sel is null) return;
        var items = sel.Value.Items.ToList();
        if (items.Any(i => i.FileSystemPath is null))
        {
            Notify("Commands run only for files and folders on disk.", true);
            return;
        }
        if (items.Count > MaxApplyItems) { Notify($"Run a command for at most {MaxApplyItems:N0} items at a time.", true); return; }
        string? target = Workspace.ActiveTarget?.ActiveTab?.Location is { IsFileSystem: true } t ? t.Path : null;

        var history = Services.History.ApplyCommands;
        var command = new TextBox { Text = history.FirstOrDefault() ?? "", MinWidth = 640 };
        Avalonia.Automation.AutomationProperties.SetName(command, "Command");
        int historyIndex = history.Count > 0 ? 0 : -1;
        command.KeyDown += (_, e) =>
        {
            // Up and Down recall earlier commands, as in the copy dialog.
            if (history.Count == 0 || e.Key is not (Avalonia.Input.Key.Up or Avalonia.Input.Key.Down)) return;
            if (e.Key == Avalonia.Input.Key.Up && historyIndex < history.Count - 1) historyIndex++;
            else if (e.Key == Avalonia.Input.Key.Down && historyIndex > 0) historyIndex--;
            else return;
            command.Text = history[historyIndex];
            command.CaretIndex = command.Text.Length;
            e.Handled = true;
        };
        string shellName = ApplyCommandPlanner.DefaultShell == "cmd" ? "cmd.exe" : "/bin/sh";
        var shell = new CheckBox { Content = $"Run through the shell ({shellName}: pipes, redirection, built-in commands; names are quoted for it)" };
        var preview = new ListBox { Height = 220, MinWidth = 640 };
        Avalonia.Automation.AutomationProperties.SetName(preview, "Commands to run");
        var summary = new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        IReadOnlyList<ApplyInvocation> rows = [];
        ApplyCommandSpec? plannedFor = null;
        int generation = 0;
        ApplyCommandSpec Current() => new(command.Text ?? "", shell.IsChecked == true);

        async void Refresh()
        {
            int mine = ++generation;
            var spec = Current();
            var planned = await Task.Run(() => ApplyCommandPlanner.Plan(spec, items, target, ToolLauncher.ResolveExecutable));
            if (mine != generation) return;
            rows = planned;
            plannedFor = spec;
            if (spec.CommandLine.Trim().Length == 0)
            {
                // Nothing typed yet: the first step, not a problem with every item.
                preview.ItemsSource = items.Take(500).Select(i => i.Name).ToList();
                summary.Text = $"Type the command to run for {Formatters.Plural(items.Count, "item", "items")}; each one's command line is shown here before anything runs.";
                summary.Classes.Set("error", false);
                return;
            }
            preview.ItemsSource = rows.Take(500).Select(r => r.Problem is null ? r.Display : $"⚠ {r.Item.Name}: {r.Problem}").ToList();
            int problems = rows.Count(r => r.Problem is not null);
            summary.Text = problems > 0
                ? $"{Formatters.Plural(problems, "item has a problem", "items have problems")}; nothing runs until every command can."
                : $"{Formatters.Plural(rows.Count, "command runs", "commands run")} one after another, each in its item's folder." + (rows.Count > 500 ? " The first 500 are shown." : "");
            summary.Classes.Set("error", problems > 0);
        }
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        command.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        shell.IsCheckedChanged += (_, _) => Refresh();

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Command (program and arguments):" },
                command,
                new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 640,
                    Text = "{file} full path · {name} name · {stem} name without extension · {ext} extension · {dir} item's folder · {target} target panel folder · {index} 1, 2, 3… · Up and Down recall earlier commands",
                },
                shell,
                preview,
                summary,
            },
        };
        Refresh();
        var answer = await Dialogs.ShowCustomAsync(items.Count == 1 ? $"Run a command for \"{Formatters.SafeName(items[0].Name)}\"" : $"Run a command for {items.Count:N0} items", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Run", "run", IsDefault: true)], command,
            // Only a preview of exactly what is typed can be run.
            () => plannedFor == Current() && rows.Count > 0 && rows.All(r => r.Problem is null));
        debounce.Stop();
        if (answer as string != "run" || plannedFor != Current() || rows.Count == 0 || rows.Any(r => r.Problem is not null)) return;
        AppServices.RememberText(Services.History.ApplyCommands, command.Text ?? "");
        string program = Path.GetFileName(rows[0].Executable);
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.ApplyCommand,
            Sources = items,
            Invocations = rows,
            Description = items.Count == 1 ? $"Run {program} for \"{items[0].Name}\"" : $"Run {program} for {items.Count:N0} items",
        });
        if (ActiveTab is { } tab) Track(job, tab);
    }
}
