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
    private async Task ShowWindowsFileTreeAsync(IDialogService dialogs, InspectedPermission item, WindowsPermissionService service,
        PermissionSelection? origin)
    {
        const string title = "Add access rule to descendant files";
        var source = item.Source!;
        var subject = item.Snapshot.Subject;
        var editor = new WindowsAccessEditor(null, subject.Authority.Id, copy: null);
        var problem = PermissionNotice();
        void Changed()
        {
            string? error = editor.ReadChange().Error;
            problem.Text = error;
            problem.IsVisible = error is not null;
        }
        editor.Changed += Changed;
        Changed();
        var behavior = new DialogBehavior { DismissOnBackdrop = false, ConfirmOnListItemEnter = false, PreferredSize = new Avalonia.Size(760, 660) };
        var options = new StackPanel { Spacing = 8, Children =
        {
            Wrapped(Formatters.SafeName(subject.DisplayPath)),
            Wrapped("Choose one explicit access rule for the files under this folder. FileCat will show the captured file paths and rule positions for approval."),
            editor, problem
        } };
        if (await dialogs.ShowCustomAsync(title, options,
                [new("Cancel", "cancel", IsCancel: true), new("Capture file review", "review", IsDefault: true)
                    { IsAvailable = () => editor.ReadChange().Change is AddWindowsAce }], resizable: true, behavior: behavior) is not "review" ||
            editor.ReadChange().Change is not AddWindowsAce change || Services.Io.IsStopped) return;

        string device = Services.Providers.For(source.Parent).GetDeviceKey(source.Parent);
        var cancellation = Services.Io.StoppingToken;
        try
        {
            var prepared = await Services.Io.RunOwned(device, IoPriority.Interactive,
                ct => service.PrepareFileTree(item.Snapshot, source, change, ct), cancellation);
            using var operation = prepared.Operation;
            cancellation.ThrowIfCancellationRequested();
            int changes = prepared.Rows.Count(row => !row.AlreadyPresent);
            var files = new ListBox { MaxHeight = 290, ItemsSource = prepared.Rows,
                ItemTemplate = new FuncDataTemplate<WindowsPermissionService.FileTreeRow>((row, _) => row is null ? Wrapped(string.Empty) :
                    Wrapped($"{Formatters.SafeName(row.Path)} · {(row.AlreadyPresent ? "Already present" : $"Insert at rule {row.RulePosition}")}")) };
            AutomationProperties.SetName(files, "Captured Windows files and rule positions");
            var preview = new StackPanel { Spacing = 8, Children =
            {
                Wrapped(Formatters.SafeName(subject.DisplayPath)),
                Wrapped(WindowsAccessEditor.Describe(change.Entry)),
                Wrapped($"{Formatters.Plural(changes, "rule addition", "rule additions")} among " +
                    $"{Formatters.Plural(prepared.Rows.Length, "captured file", "captured files")}. " +
                    $"FileCat visits {Formatters.Plural(prepared.Directories, "folder", "folders")} to locate those files. Later arrivals are outside this review."),
                files
            } };
            if (await dialogs.ShowCustomAsync(title, preview,
                    [new("Cancel", "cancel", IsCancel: true), new("Apply to captured files", "apply")
                        { IsAvailable = () => changes > 0 }], resizable: true, behavior: behavior) is not "apply") return;
            cancellation.ThrowIfCancellationRequested();
            if (origin is not null && !origin.HasOriginalMarks)
                throw new PermissionBindingConflictException("The original selection changed. Capture a new review.");
            Services.Jobs.SubmitPermissions(operation);
            Notify("Captured file access changes queued. Follow progress in Operations.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            if (!cancellation.IsCancellationRequested) await dialogs.AlertAsync("File ACL review unavailable", Formatters.SafeName(error.Message));
        }
    }
}
