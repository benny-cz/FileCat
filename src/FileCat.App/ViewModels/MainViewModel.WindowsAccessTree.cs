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
    private async Task ShowWindowsAccessTreeAsync(IDialogService dialogs, InspectedPermission item, WindowsPermissionService service,
        PermissionObjectKind targetKind, PermissionSelection? origin)
    {
        bool folders = targetKind == PermissionObjectKind.Directory;
        string kind = folders ? "folder" : "file";
        string title = $"Add access rule to descendant {kind}s";
        var source = item.Source!;
        var subject = item.Snapshot.Subject;
        var editor = new WindowsAccessEditor(null, subject.Authority.Id, copy: null, directory: folders, allowInheritance: false);
        var problem = PermissionNotice();
        void Changed()
        {
            string? error = editor.ReadChange().Error;
            problem.Text = error;
            problem.IsVisible = error is not null;
        }
        editor.Changed += Changed;
        Changed();
        var behavior = new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 660),
            SizeKey = "permissions.windows-access-tree" };
        var options = new StackPanel { Spacing = 8, Children =
        {
            Wrapped(Formatters.SafeName(subject.DisplayPath)),
            Wrapped($"Choose one explicit access rule for the {kind}s under this folder. FileCat will show the captured paths and rule positions for approval."),
            editor, problem
        } };
        if (await dialogs.ShowCustomAsync(title, options,
                [new("Cancel", "cancel", IsCancel: true), new($"Capture {kind} review", "review", IsDefault: true)
                    { IsAvailable = () => editor.ReadChange().Change is AddWindowsAce }], resizable: true, behavior: behavior) is not "review" ||
            editor.ReadChange().Change is not AddWindowsAce change || Services.Io.IsStopped) return;

        string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        var cancellation = Services.Io.StoppingToken;
        try
        {
            var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => service.PrepareAccessTree(item.Snapshot, source, change, targetKind, ct), cancellation);
            using var operation = prepared.Operation;
            cancellation.ThrowIfCancellationRequested();
            int changes = prepared.Rows.Count(row => !row.AlreadyPresent);
            var targets = new ListBox { MaxHeight = 290, ItemsSource = prepared.Rows,
                ItemTemplate = new FuncDataTemplate<WindowsPermissionService.AccessTreeRow>((row, _) => row is null ? Wrapped(string.Empty) :
                    Wrapped($"{Formatters.SafeName(row.Path)} · {(row.AlreadyPresent ? "Already present" : $"Insert at rule {row.RulePosition}")}")) };
            AutomationProperties.SetName(targets, $"Captured Windows {kind}s and rule positions");
            string scope = folders
                ? "The rule applies to the listed folders. Files, future children and the selected source folder keep their existing rules."
                : $"FileCat visits {Formatters.Plural(prepared.Directories, "folder", "folders")} to locate those files.";
            var preview = new StackPanel { Spacing = 8, Children =
            {
                Wrapped(Formatters.SafeName(subject.DisplayPath)),
                Wrapped(WindowsAccessEditor.Describe(change.Entry)),
                Wrapped($"{Formatters.Plural(changes, "rule addition", "rule additions")} among " +
                    $"{Formatters.Plural(prepared.Rows.Length, $"captured {kind}", $"captured {kind}s")}. {scope} Later arrivals are outside this review."),
                targets
            } };
            if (await dialogs.ShowCustomAsync(title, preview,
                    [new("Cancel", "cancel", IsCancel: true), new($"Apply to captured {kind}s", "apply")
                        { IsAvailable = () => changes > 0 }], resizable: true,
                    behavior: behavior with { SizeKey = "permissions.windows-access-tree-review" }) is not "apply") return;
            cancellation.ThrowIfCancellationRequested();
            if (origin is not null && !origin.HasOriginalMarks)
                throw new PermissionBindingConflictException("The original selection changed. Capture a new review.");
            Services.Jobs.SubmitPermissions(operation);
            Notify($"Captured {kind} access changes queued. Follow progress in Operations.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested) await dialogs.AlertAsync("Windows ACL review unavailable", Formatters.SafeName(error.Message));
        }
    }
}
