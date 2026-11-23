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
    // A legacy Windows DACL stored in automatic-inheritance form exactly as Windows tools read it (WindowsInheritedReading):
    // the review lists the stored entries and the converted ones, and nothing else changes.
    private async Task ShowWindowsInheritanceConversionAsync(IDialogService dialogs, InspectedPermission item, WindowsPermissionService service,
        PermissionSelection? origin)
    {
        const string title = "Convert to automatic inheritance";
        var source = item.Source!;
        string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        var cancellation = Services.Io.StoppingToken;
        try
        {
            var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => service.PrepareInheritanceConversion(item.Inspection, source, ct), cancellation);
            using var operation = prepared.Review;
            cancellation.ThrowIfCancellationRequested();
            var preview = new StackPanel { Spacing = 8 };
            preview.Children.Add(Wrapped(Formatters.SafeName(item.Snapshot.Subject.DisplayPath)));
            bool folder = item.Snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory;
            bool protect = prepared.Expected.Inheritance.Value?.IsProtected == true;
            preview.Children.Add(Wrapped("This DACL is stored without the automatic-inheritance mark. The conversion stores it as Windows tools " +
                "read it against the parent folder: entries that the parent's inheritable rules produce are marked inherited and listed last" +
                (protect ? ", and the DACL is protected because those rules no longer match it" : string.Empty) +
                ". No other object changes." + (folder ? " Afterwards native propagation can be reviewed for this folder." : string.Empty)));
            var rows = DescribeWindowsConversion(item.Snapshot, prepared.Expected).ToArray();
            var list = new ListBox { MaxHeight = 300, ItemsSource = rows, ItemTemplate = new FuncDataTemplate<string>((row, _) => Wrapped(row ?? string.Empty)) };
            AutomationProperties.SetName(list, "Stored and converted access rules");
            preview.Children.Add(list);
            var approve = new CheckBox { Content = "Approve storing this DACL in automatic-inheritance form" };
            preview.Children.Add(approve);
            if (await dialogs.ShowCustomAsync(title, preview,
                    [new("Cancel", "cancel", IsCancel: true), new("Convert", "apply") { IsAvailable = () => approve.IsChecked == true }], resizable: true,
                    behavior: new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 600),
                        SizeKey = "permissions.windows-convert" })
                is not "apply") return;
            cancellation.ThrowIfCancellationRequested();
            if (origin is not null && !origin.HasOriginalMarks)
                throw new PermissionBindingConflictException("The original selection changed. Inspect it again.");
            Services.Jobs.SubmitPermissions(operation);
            Notify("Inheritance conversion queued. Follow progress in Operations.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested) await dialogs.AlertAsync("Conversion unavailable", Formatters.SafeName(error.Message));
        }
    }

    private static IEnumerable<string> DescribeWindowsConversion(PermissionSnapshot original, PermissionSnapshot expected)
    {
        if (original.Access.Value?.Acl is WindowsDacl stored)
            for (int index = 0; index < stored.Entries.Length; index++)
                yield return $"Stored now · {index + 1} · {PermissionInspectorView.WindowsRule(stored.Entries[index])}";
        if (expected.Access.Value?.Acl is WindowsDacl converted)
            for (int index = 0; index < converted.Entries.Length; index++)
                yield return $"After conversion · {index + 1} · {PermissionInspectorView.WindowsRule(converted.Entries[index])}";
        if (expected.Inheritance.Value?.IsProtected == true) yield return "After conversion · inheritance protected";
    }
}
