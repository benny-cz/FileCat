using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task ShowReviewedAclResetAsync(IDialogService dialogs, InspectedPermission item, LinuxPermissionService service, PermissionSelection? origin)
    {
        var snapshot = item.Snapshot;
        var source = item.Source!;
        var exact = new CheckBox { Content = "Allow execute on regular files that have no execute bit" };
        var options = new StackPanel { Spacing = 8 };
        options.Children.Add(Wrapped($"Use the default ACL of {Formatters.SafeName(snapshot.Subject.DisplayPath)} as the template."));
        options.Children.Add(Wrapped("The reset replaces access rules on captured descendants. By default, regular files without execute stay non-executable."));
        options.Children.Add(exact);
        var behavior = new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 665) };
        if (await dialogs.ShowCustomAsync("Reset descendant access ACLs", options,
                [new("Cancel", "cancel", IsCancel: true), new("Capture reset review", "review", IsDefault: true)],
                resizable: true, behavior: behavior) is not "review" || Services.Io.IsStopped) return;

        var subject = snapshot.Subject with { ExpectedIdentity = snapshot.Binding!.Identity };
        string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        bool exactExecute = exact.IsChecked == true;
        CancellationToken cancellation = Services.Io.StoppingToken;
        try
        {
            var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => service.PrepareAclResetTree(source, subject, exactExecute, acceptSetIdClear: true, ct, retainedInspections: 1), cancellation);
            using var operation = prepared.Operation;
            cancellation.ThrowIfCancellationRequested();
            var rows = prepared.Rows.Where(row => row.Changed).ToArray();
            var preview = new StackPanel { Spacing = 8 };
            foreach (string text in PermissionOperationReport.TreeScopeLines(operation.Tree!.Summary, operation.Scope, subject.DisplayPath))
                preview.Children.Add(Wrapped(text));
            preview.Children.Add(Wrapped($"{Formatters.Plural(rows.Length, "access ACL change", "access ACL changes")} among " +
                $"{Formatters.Plural(prepared.Rows.Length, "captured descendant", "captured descendants")}. Later arrivals are outside this review."));
            byte templateMask = PosixAccessDraft.Mask(prepared.Template);
            var template = new ListBox { MaxHeight = 108, ItemsSource = prepared.Template.Select(row =>
                $"{PosixAccessEditor.PrincipalLabel(row)} · {PosixAccessEditor.DescribeRights(row, templateMask)}").ToArray() };
            AutomationProperties.SetName(template, "Captured default ACL template");
            preview.Children.Add(template);
            preview.Children.Add(Wrapped("Removing a named rule can change which other entries apply. Check the configured and effective rights for each descendant."));
            if (rows.Any(row => row.Subject.Kind == PermissionObjectKind.File && row.LinkCount > 1))
                preview.Children.Add(Wrapped("Changes to a hard-linked file also affect its other names, including names outside this tree."));
            var targets = new ListBox { MaxHeight = 170, ItemsSource = rows,
                ItemTemplate = new FuncDataTemplate<PermissionTreeRow>((row, _) => Wrapped(Formatters.SafeName(row?.Subject.DisplayPath ?? string.Empty))) };
            var rights = new ListBox { MaxHeight = 170 };
            AutomationProperties.SetName(targets, "Descendants whose access ACL will change");
            AutomationProperties.SetName(rights, "Access ACL changes for the selected descendant");
            targets.SelectionChanged += (_, _) => rights.ItemsSource = targets.SelectedItem is PermissionTreeRow row
                ? DescribePermissionDiff(row.Before, row.After, row.ResetBefore, row.ResetAfter) : [];
            preview.Children.Add(targets);
            preview.Children.Add(rights);
            if (rows.Length > 0) targets.SelectedIndex = 0;

            var allowAccess = new CheckBox { Content = "Approve the reviewed rule replacements and any access they grant", IsVisible = rows.Length > 0 };
            var acceptSetId = new CheckBox { Content = "Accept clearing the reviewed set-ID bits",
                IsVisible = rows.Any(row => (row.Before & 0xc00 & ~row.After) != 0) };
            preview.Children.Add(allowAccess);
            preview.Children.Add(acceptSetId);
            var confirmed = await dialogs.ShowCustomAsync("Review descendant access ACL reset", preview,
                [new("Cancel", "cancel", IsCancel: true), new("Reset captured access ACLs", "reset", IsDanger: true)
                    { IsAvailable = () => rows.Length > 0 && allowAccess.IsChecked == true && (!acceptSetId.IsVisible || acceptSetId.IsChecked == true) }],
                resizable: true, behavior: behavior);
            if (confirmed is not "reset") return;
            cancellation.ThrowIfCancellationRequested();
            if (origin is not null && !origin.HasOriginalMarks)
                throw new PermissionBindingConflictException("The original selection changed. Capture a new review.");
            Services.Jobs.SubmitPermissions(operation);
            Notify("Captured ACL reset queued. Follow progress in Operations.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested) await dialogs.AlertAsync("ACL reset review unavailable", Formatters.SafeName(error.Message));
        }
    }

    private bool CanReviewModeTree(PermissionSnapshot snapshot) => Services.Permissions switch
    {
        LinuxPermissionService linux => linux.CanEditModeTree(snapshot),
        DarwinPermissionService darwin => darwin.CanEditModeTree(snapshot),
        _ => false
    };

    private PermissionTreeReview PrepareLocalModeTree(IEnumerable<(ItemRef Item, PermissionSubject Subject)> roots,
        ChangePermissionMode change, bool exactExecute, CancellationToken ct, int retainedInspections) => Services.Permissions switch
    {
        LinuxPermissionService linux => linux.PrepareModeTree(roots, change, exactExecute, acceptSetIdClear: true, ct, retainedInspections),
        DarwinPermissionService darwin => darwin.PrepareModeTree(roots, change, exactExecute, acceptSetIdClear: true, ct, retainedInspections),
        _ => throw new NotSupportedException("The selected filesystem has no reviewed tree writer.")
    };

    private async Task ShowReviewedModeTreeAsync(IDialogService dialogs, InspectedPermission item, PermissionSelection? origin)
    {
        var snapshot = item.Snapshot;
        var source = item.Source!;
        var original = snapshot.Mode.Value!.Value;
        var editor = new UnixPermissionModeEditor(bit => (original & (ushort)bit) != 0,
            snapshot.Access.Value?.Acl is PosixAcl acl && acl.Entries.Any(row => row.Tag == PosixAclTag.Mask));
        var exact = new CheckBox { Content = "Set requested execute bits on regular files that have no execute bit" };
        var draft = new StackPanel { Spacing = 8 };
        draft.Children.Add(Wrapped(Formatters.SafeName(snapshot.Subject.DisplayPath)));
        draft.Children.Add(editor);
        draft.Children.Add(Wrapped("By default, new execute bits reach directories and descendant files that already have an execute bit."));
        draft.Children.Add(exact);
        var behavior = new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 640) };
        var choice = await dialogs.ShowCustomAsync("Change mode in a folder tree", draft,
            [new("Cancel", "cancel", IsCancel: true), new("Capture review", "review", IsDefault: true)
                { IsAvailable = () => editor.TryGetChange(out var candidate) && candidate.TouchedBits != 0 }], resizable: true, behavior: behavior);
        if (choice is not "review" || !editor.TryGetChange(out var change) || change.TouchedBits == 0 || Services.Io.IsStopped) return;

        var subject = snapshot.Subject with { ExpectedIdentity = snapshot.Binding!.Identity };
        string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        bool exactExecute = exact.IsChecked == true;
        CancellationToken cancellation = Services.Io.StoppingToken;
        try
        {
            var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => PrepareLocalModeTree([(source, subject)], change, exactExecute, ct, retainedInspections: 1), cancellation);
            using var operation = prepared.Operation;
            cancellation.ThrowIfCancellationRequested();
            if (!await ConfirmModeTreeAsync(dialogs, prepared)) return;
            cancellation.ThrowIfCancellationRequested();
            if (origin is not null && !origin.HasOriginalMarks)
                throw new PermissionBindingConflictException("The original selection changed. Capture a new review.");
            Services.Jobs.SubmitPermissions(operation);
            Notify("Captured mode changes queued. Follow progress in Operations.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested) await dialogs.AlertAsync("Folder tree review unavailable", Formatters.SafeName(error.Message));
        }
    }

    private async Task<ReviewedPermissionOperation?> CaptureAttributeModeTreeAsync(
        IReadOnlyList<AttributePermissionTarget> targets, ChangePermissionMode change, bool exactExecute, int retainedInspections,
        IDialogService dialogs, CancellationToken cancellation)
    {
        var roots = targets.Select(target =>
        {
            var snapshot = target.Inspection.Snapshot;
            // The original inode is still pinned. A separately applied timestamp may have changed the listing facts.
            return (target.Source, snapshot.Subject with
            {
                ExpectedIdentity = snapshot.Binding!.Identity, ListedSize = null, ListedModifiedUtcTicks = null
            });
        }).ToArray();
        var location = roots[0].Source.Parent;
        string device = Services.Providers.For(location).GetDeviceKey(location);
        var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
            ct => PrepareLocalModeTree(roots, change, exactExecute, ct, retainedInspections), cancellation);
        bool accepted = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!await ConfirmModeTreeAsync(dialogs, prepared)) return null;
            cancellation.ThrowIfCancellationRequested();
            accepted = true;
            return prepared.Operation;
        }
        finally
        {
            if (!accepted) prepared.Operation.Dispose();
        }
    }

    private static async Task<bool> ConfirmModeTreeAsync(IDialogService dialogs, PermissionTreeReview prepared)
    {
        var operation = prepared.Operation;
        var rows = prepared.Rows.Select((row, index) => (Row: row, Item: operation.Targets[index].Source))
            .Where(entry => entry.Row.Changed).ToArray();
        var preview = new StackPanel { Spacing = 8 };
        foreach (string text in PermissionOperationReport.TreeScopeLines(operation.Tree!.Summary)) preview.Children.Add(Wrapped(text));
        if (operation.Targets.Any(target => target.Binding?.DarwinRoute == DarwinBindingRoute.ParentRelative))
            preview.Children.Add(Wrapped(DarwinPermissionService.ParentRelativeReviewNotice));
        if (operation.Targets.Any(target => target.Binding?.DarwinRoute == DarwinBindingRoute.Descriptor))
            preview.Children.Add(Wrapped(DarwinPermissionService.HeldDescriptorReviewNotice));
        preview.Children.Add(Wrapped($"{Formatters.Plural(rows.Length, "mode change", "mode changes")} among " +
            $"{Formatters.Plural(prepared.Rows.Length, "captured object", "captured objects")}. Later arrivals are outside this review."));
        if (rows.Any(entry => !entry.Item.IsContainer && entry.Row.LinkCount > 1))
            preview.Children.Add(Wrapped("Changing a hard-linked file changes its mode through every name, including names outside this tree."));

        var changes = new ListBox
        {
            MaxHeight = 280,
            ItemsSource = rows.Select(entry => new ModeChangePreview(entry.Item, entry.Row.Access, entry.Row.Before, entry.Row.After,
                DarwinBindingRoute.None) { DisplayPath = entry.Row.Subject.DisplayPath }).ToArray(),
            ItemTemplate = new FuncDataTemplate<ModeChangePreview>((row, _) => Wrapped(row?.ToString() ?? string.Empty))
        };
        AutomationProperties.SetName(changes, "Captured mode changes");
        var rights = new ListBox { MaxHeight = 125, IsVisible = false };
        AutomationProperties.SetName(rights, "Affected ACL entries and effective rights");
        changes.SelectionChanged += (_, _) =>
        {
            var affected = (changes.SelectedItem as ModeChangePreview)?.AffectedMaskEntries() ?? [];
            rights.ItemsSource = affected;
            rights.IsVisible = affected.Length > 0;
        };
        if (rows.Length > 0)
        {
            preview.Children.Add(changes);
            preview.Children.Add(rights);
            changes.SelectedIndex = 0;
        }
        var accept = new CheckBox { Content = "Accept clearing the reviewed set-ID bits",
            IsVisible = rows.Any(entry => (entry.Row.Before & 0xc00 & ~entry.Row.After) != 0) };
        preview.Children.Add(accept);

        return await dialogs.ShowCustomAsync("Review captured mode changes", preview,
            [new("Cancel", "cancel", IsCancel: true), new("Apply to captured objects", "apply")
                { IsAvailable = () => rows.Length > 0 && (!accept.IsVisible || accept.IsChecked == true) }], resizable: true,
            behavior: new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 640) }) is "apply";
    }
}
