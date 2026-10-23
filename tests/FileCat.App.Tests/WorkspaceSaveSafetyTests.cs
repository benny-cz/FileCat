using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class WorkspaceSaveSafetyTests
{
    [AvaloniaFact]
    public async Task Save_as_suggests_an_unused_name_and_never_replaces_the_first_saved_workspace()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string oldPath = Path.Combine(services.Paths.WorkspacesDirectory, "Accounting.json");
            JsonFileStore.Save(oldPath, new WorkspaceState { Name = "Accounting" }, StateJsonContext.Default.WorkspaceState);
            byte[] old = File.ReadAllBytes(oldPath);
            var dialogs = new SaveDialogs(o => new PromptResult(o.Text, false));
            vm.Dialogs = dialogs;

            await vm.SaveNamedWorkspaceAsync();

            Assert.Equal("Project", dialogs.Prompts.Single().Text);
            Assert.Equal(0, dialogs.Confirmations);
            Assert.Equal(old, File.ReadAllBytes(oldPath));
            Assert.True(File.Exists(Path.Combine(services.Paths.WorkspacesDirectory, "Project.json")));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [Theory]
    [InlineData("Client/A")]
    [InlineData("Client\\A")]
    [InlineData("CON.txt")]
    [InlineData("Project.")]
    [InlineData(" Project")]
    [InlineData("A\u202Etxt")]
    public void Unsafe_or_ambiguous_names_are_refused_before_publication(string name) =>
        Assert.NotNull(MainViewModel.WorkspaceNameProblem(name));

    [Fact]
    public void Distinct_safe_name_is_accepted() =>
        Assert.Null(MainViewModel.WorkspaceNameProblem("Client_A"));

    [AvaloniaFact]
    public async Task Canceling_a_named_replacement_preserves_the_main_and_backup_bytes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string path = Path.Combine(services.Paths.WorkspacesDirectory, "Accounting.json");
            JsonFileStore.Save(path, new WorkspaceState { Name = "Accounting" }, StateJsonContext.Default.WorkspaceState);
            File.WriteAllText(path + ".bak", "prior backup");
            byte[] main = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");
            var dialogs = new SaveDialogs(_ => new PromptResult("Accounting", false)) { Confirm = _ => false };
            vm.Dialogs = dialogs;

            await vm.SaveNamedWorkspaceAsync();

            Assert.Single(dialogs.Prompts);
            Assert.Equal(1, dialogs.Confirmations);
            Assert.Contains("Accounting", dialogs.LastConfirmation!);
            Assert.Contains("Saved:", dialogs.LastConfirmation!);
            Assert.Contains("Current:", dialogs.LastConfirmation!);
            Assert.Equal(main, File.ReadAllBytes(path));
            Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Case_only_alias_on_Windows_requires_review_of_the_existing_display_name()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Case-only filename aliases depend on Windows filesystem behavior."); return; }
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string path = Path.Combine(services.Paths.WorkspacesDirectory, "Accounting.json");
            JsonFileStore.Save(path, new WorkspaceState { Name = "Accounting" }, StateJsonContext.Default.WorkspaceState);
            byte[] original = File.ReadAllBytes(path);
            var dialogs = new SaveDialogs(_ => new PromptResult("accounting", false)) { Confirm = _ => false };
            vm.Dialogs = dialogs;

            await vm.SaveNamedWorkspaceAsync();

            Assert.Equal(1, dialogs.Confirmations);
            Assert.Contains("Accounting", dialogs.LastConfirmation!);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Changed_workspace_after_confirmation_is_not_replaced_and_the_draft_is_retained()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string path = Path.Combine(services.Paths.WorkspacesDirectory, "Accounting.json");
            JsonFileStore.Save(path, new WorkspaceState { Name = "Accounting" }, StateJsonContext.Default.WorkspaceState);
            File.WriteAllText(path + ".bak", "prior backup");
            byte[] backup = File.ReadAllBytes(path + ".bak");
            int prompts = 0;
            var dialogs = new SaveDialogs(o => ++prompts == 1 ? new PromptResult("Accounting", false) : null)
            {
                Confirm = _ => { File.WriteAllText(path, "changed concurrently"); return true; },
            };
            vm.Dialogs = dialogs;

            await vm.SaveNamedWorkspaceAsync();

            Assert.Equal(2, dialogs.Prompts.Count);
            Assert.Equal("Accounting", dialogs.Prompts[1].Text);
            Assert.Equal("changed concurrently", File.ReadAllText(path));
            Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Approved_replacement_keeps_the_previous_version_as_backup()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string path = Path.Combine(services.Paths.WorkspacesDirectory, "Accounting.json");
            JsonFileStore.Save(path, new WorkspaceState { Name = "Accounting" }, StateJsonContext.Default.WorkspaceState);
            byte[] previous = File.ReadAllBytes(path);
            var dialogs = new SaveDialogs(_ => new PromptResult("Accounting", false)) { Confirm = _ => true };
            vm.Dialogs = dialogs;

            await vm.SaveNamedWorkspaceAsync();

            Assert.Equal(1, dialogs.Confirmations);
            Assert.Equal(previous, File.ReadAllBytes(path + ".bak"));
            Assert.NotEqual(previous, File.ReadAllBytes(path));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Publication_failure_reopens_the_same_name_for_correction()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            Directory.CreateDirectory(Path.Combine(services.Paths.WorkspacesDirectory, "Project.json"));
            int prompts = 0;
            var dialogs = new SaveDialogs(o => ++prompts == 1 ? new PromptResult(o.Text, false) : null);
            vm.Dialogs = dialogs;

            await vm.SaveNamedWorkspaceAsync();

            Assert.Equal(2, dialogs.Prompts.Count);
            Assert.Equal("Project", dialogs.Prompts[1].Text);
            Assert.Equal(0, dialogs.Confirmations);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private sealed class SaveDialogs(Func<PromptOptions, PromptResult?> prompt) : IDialogService
    {
        public List<PromptOptions> Prompts { get; } = [];
        public Func<string, bool> Confirm { get; init; } = _ => throw new Xunit.Sdk.XunitException("Unexpected replace review.");
        public int Confirmations { get; private set; }
        public string? LastConfirmation { get; private set; }
        public bool IsOpen => false;
        public Task<PromptResult?> PromptAsync(PromptOptions options)
        {
            Prompts.Add(options);
            return Task.FromResult(prompt(options));
        }
        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false, string cancelText = "Cancel")
        {
            Confirmations++;
            LastConfirmation = message;
            Assert.True(danger);
            Assert.Contains("Replace", confirmText);
            return Task.FromResult(Confirm(message));
        }
        public Task AlertAsync(string title, string message) => throw new NotSupportedException();
        public Task<ChoiceResult> ChooseAsync(ChoiceOptions options) => throw new NotSupportedException();
        public Task<KeyboardReferenceChoice?> KeyboardReferenceAsync(IReadOnlyList<KeyboardHelpEntry> commands, string? selectedId = null, bool remapOnly = false) => throw new NotSupportedException();
        public Task<object?> ShowCustomAsync(string title, Control content, IReadOnlyList<DialogButton> buttons, Control? initialFocus = null,
            Func<bool>? canConfirm = null, DialogCloser? closer = null, bool resizable = false,
            Control? pinnedReview = null) => throw new NotSupportedException();
    }
}
