using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Permissions;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ShowWindowsAclResetAsync(IDialogService dialogs, InspectedPermission item, WindowsPermissionService service,
        PermissionSelection? origin)
    {
        const string title = "Reset Windows descendant ACLs";
        var source = item.Source!;
        string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        var cancellation = Services.Io.StoppingToken;
        try
        {
            var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => service.PrepareAclReset(item.Inspection, source, ct), cancellation);
            using var operation = prepared.Operation;
            cancellation.ThrowIfCancellationRequested();
            int changed = prepared.Rows.Count(row => row.Changed);
            var preview = new StackPanel { Spacing = 8 };
            foreach (string line in PermissionOperationReport.TreeScopeLines(operation.Tree!.Summary, operation.Scope,
                         item.Snapshot.Subject.DisplayPath)) preview.Children.Add(Wrapped(line));
            preview.Children.Add(Wrapped($"{Formatters.Plural(changed, "ACL change", "ACL changes")} among " +
                $"{Formatters.Plural(prepared.Rows.Length, "captured descendant", "captured descendants")}. " +
                "This review covers the listed objects. Protected branches are excluded."));
            var targets = new ListBox { MaxHeight = 160, ItemsSource = prepared.Rows,
                ItemTemplate = new FuncDataTemplate<WindowsPermissionService.AclResetRow>((row, _) => row is null ? Wrapped(string.Empty) :
                    Wrapped($"{Formatters.SafeName(row.Path)} · {(row.Changed ? "Reset ACL" : "Already matches")}")) };
            var rules = new ListBox { MaxHeight = 230, ItemTemplate = new FuncDataTemplate<string>((rule, _) => Wrapped(rule ?? string.Empty)) };
            AutomationProperties.SetName(targets, "Captured Windows ACL reset targets");
            AutomationProperties.SetName(rules, "Removed and replacement access rules");
            targets.SelectionChanged += (_, _) =>
                rules.ItemsSource = targets.SelectedItem is WindowsPermissionService.AclResetRow row ? DescribeWindowsReset(row).ToArray() : [];
            preview.Children.Add(targets);
            preview.Children.Add(rules);
            targets.SelectedIndex = 0;
            var approve = new CheckBox { Content = "Approve the reviewed rule removals and inherited replacements", IsVisible = changed > 0 };
            preview.Children.Add(approve);
            if (await dialogs.ShowCustomAsync(title, preview,
                    [new("Cancel", "cancel", IsCancel: true), new("Reset captured ACLs", "apply", IsDanger: true)
                        { IsAvailable = () => changed > 0 && approve.IsChecked == true }], resizable: true,
                    behavior: new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 665) }) is not "apply") return;
            cancellation.ThrowIfCancellationRequested();
            if (origin is not null && !origin.HasOriginalMarks)
                throw new PermissionBindingConflictException("The original selection changed. Capture a new review.");
            Services.Jobs.SubmitPermissions(operation);
            Notify("Captured ACL reset queued. Follow progress in Operations.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested) await dialogs.AlertAsync("Windows ACL reset unavailable", Formatters.SafeName(error.Message));
        }
    }

    private static IEnumerable<string> DescribeWindowsReset(WindowsPermissionService.AclResetRow row)
    {
        for (int index = 0; index < row.Before.Length; index++)
        {
            var rule = row.Before[index];
            string label = (rule.Flags & 0x10) == 0 ? "Remove explicit" : "Previous inherited";
            yield return $"{label} · {index + 1} · {PermissionInspectorView.WindowsRule(rule)}";
        }
        for (int index = 0; index < row.After.Length; index++)
            yield return $"Replacement inherited · {index + 1} · {PermissionInspectorView.WindowsRule(row.After[index])}";
        if (row.After.IsEmpty) yield return "The resulting empty DACL grants no discretionary access.";
    }
}
