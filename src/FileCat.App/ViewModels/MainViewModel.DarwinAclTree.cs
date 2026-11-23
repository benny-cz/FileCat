using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Permissions;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ShowReviewedDarwinAclTreeAsync(IDialogService dialogs, InspectedPermission item,
        DarwinPermissionService service, bool reset, PermissionSelection? origin)
    {
        var snapshot = item.Snapshot;
        var source = item.Source!;
        var subject = snapshot.Subject with { ExpectedIdentity = snapshot.Binding!.Identity };
        var behavior = new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 665),
            SizeKey = "permissions.darwin-acl-tree" };
        string title = reset ? "Reset descendant access ACLs" : "Refresh inherited access rules";
        var options = new StackPanel { Spacing = 8, Children =
        {
            Wrapped($"ACL source · {Formatters.SafeName(subject.DisplayPath)}"),
            Wrapped(reset ? "Replace the access rules on captured descendants with ordered rules derived from the source. The review lists each explicit rule that would be removed."
                : "Derive inherited rules from the source for each captured descendant and retain its explicit rules.")
        } };
        if (await dialogs.ShowCustomAsync(title, options,
                [new("Cancel", "cancel", IsCancel: true), new("Capture ACL review", "review", IsDefault: true)],
                resizable: true, behavior: behavior) is not "review" || Services.Io.IsStopped) return;

        string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        CancellationToken cancellation = Services.Io.StoppingToken;
        try
        {
            var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => service.PrepareAclTree(source, subject, reset, ct, retainedInspections: 1), cancellation);
            using var operation = prepared.Operation;
            cancellation.ThrowIfCancellationRequested();
            var rows = prepared.Rows.Where(row => row.Changed).ToArray();
            var preview = new StackPanel { Spacing = 8 };
            foreach (string text in PermissionOperationReport.TreeScopeLines(operation.Tree!.Summary, operation.Scope, subject.DisplayPath))
                preview.Children.Add(Wrapped(text));
            preview.Children.Add(Wrapped($"{Formatters.Plural(rows.Length, "ACL change", "ACL changes")} among " +
                $"{Formatters.Plural(prepared.Rows.Length, "captured descendant", "captured descendants")}. Later arrivals are outside this review."));
            var template = new ListBox { MaxHeight = 105, ItemsSource = DescribeDarwinTreeAcl(prepared.DarwinTemplate, directory: true, "Source"),
                ItemTemplate = new FuncDataTemplate<string>((row, _) => Wrapped(row ?? string.Empty)) };
            AutomationProperties.SetName(template, "Captured source ACL");
            preview.Children.Add(template);
            if (rows.Any(row => row.Subject.Kind == PermissionObjectKind.File && row.LinkCount > 1))
                preview.Children.Add(Wrapped("Changes to a hard-linked file affect its other names, including names outside this tree."));

            var targets = new ListBox { MaxHeight = 155, ItemsSource = rows,
                ItemTemplate = new FuncDataTemplate<PermissionTreeRow>((row, _) => Wrapped(Formatters.SafeName(row?.Subject.DisplayPath ?? string.Empty))) };
            var rules = new ListBox { MaxHeight = 185, ItemTemplate = new FuncDataTemplate<string>((row, _) => Wrapped(row ?? string.Empty)) };
            AutomationProperties.SetName(targets, "Descendants whose ordered ACL will change");
            AutomationProperties.SetName(rules, "Ordered ACL changes for the selected descendant");
            targets.SelectionChanged += (_, _) =>
            {
                if (targets.SelectedItem is not PermissionTreeRow row) { rules.ItemsSource = Array.Empty<string>(); return; }
                bool directory = row.Subject.Kind == PermissionObjectKind.Directory;
                rules.ItemsSource = DescribeDarwinTreeAcl(row.DarwinBefore, directory, "Before", reset)
                    .Concat(DescribeDarwinTreeAcl(row.DarwinAfter, directory, "After")).ToArray();
            };
            preview.Children.Add(targets);
            preview.Children.Add(rules);
            if (rows.Length > 0) targets.SelectedIndex = 0;

            var accept = new CheckBox { Content = "Approve the reviewed ACL changes and any access they grant", IsVisible = rows.Length > 0 };
            preview.Children.Add(accept);
            string applyLabel = reset ? "Reset captured access ACLs" : "Refresh captured inherited rules";
            if (await dialogs.ShowCustomAsync(title, preview,
                    [new("Cancel", "cancel", IsCancel: true), new(applyLabel, "apply", IsDanger: reset)
                        { IsAvailable = () => rows.Length > 0 && accept.IsChecked == true }],
                    resizable: true, behavior: behavior with { SizeKey = "permissions.darwin-acl-tree-review" }) is not "apply") return;
            cancellation.ThrowIfCancellationRequested();
            if (origin is not null && !origin.HasOriginalMarks)
                throw new PermissionBindingConflictException("The original selection changed. Capture a new review.");
            Services.Jobs.SubmitPermissions(operation);
            Notify("Captured ACL changes queued. Follow progress in Operations.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested) await dialogs.AlertAsync("ACL tree review unavailable", Formatters.SafeName(error.Message));
        }
    }

    private static IEnumerable<string> DescribeDarwinTreeAcl(DarwinAcl? acl, bool directory, string stage, bool reset = false)
    {
        if (acl is null) { yield return $"{stage} · No extended ACL"; yield break; }
        yield return $"{stage} · {Formatters.Plural(acl.Entries.Length, "entry", "entries")} · ACL flags 0x{acl.Flags:X8}";
        for (int index = 0; index < acl.Entries.Length; index++)
        {
            var entry = acl.Entries[index];
            bool removesExplicit = reset && (entry.Flags & DarwinPermissionAcl.Inherited) == 0;
            yield return $"{(removesExplicit ? "Remove explicit" : stage)} · {index + 1} · {PermissionInspectorView.DarwinRule(entry, directory)}";
        }
    }
}
