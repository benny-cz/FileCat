using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private sealed class NativeArchiveCommand(ArchiveTargetReview target, ArchiveMemberCommandReview members,
        ArchiveJobPassword? password) : IDisposable
    {
        internal ArchiveTargetReview Target { get; } = target;
        internal ArchiveMemberCommandReview Members { get; } = members;
        private ArchiveJobPassword? secret = password;
        internal ArchiveJobPassword? Password => secret;
        internal ArchiveJobPassword? TakePassword() { var value = secret; secret = null; return value; }
        public void Dispose() { secret?.Dispose(); secret = null; }
    }

    private async Task<NativeArchiveCommand?> PrepareNativeArchiveCommandAsync(Location folder, bool moveSources = false, string? expectedRevision = null,
        ArchiveWriteProfile? requestedProfile = null)
    {
        if (!Services.Archives.TryGetWriteTarget(folder, out string path, out var profile))
        { Notify(Services.Archives.ExplainUnavailable(folder, LocationCapabilities.TransferTarget), true); return null; }
        if (requestedProfile is not null)
        {
            if (requestedProfile.Format != profile.Format) throw new NotSupportedException("The saved edit profile does not match this archive.");
            // Re-discover protection with the current password; retain the explicitly saved codec options.
            profile = new(requestedProfile.Format, requestedProfile.Version, requestedProfile.CompressionLevel,
                requestedProfile.DictionaryBytes, requestedProfile.SevenZip, requestedProfile.Rar5);
        }
        var probe = new ArchivePlan(path, null, [], Profile: profile) { MoveSources = moveSources };
        if (!Services.Jobs.CanReviewArchive(probe))
        { Notify(Services.Jobs.ArchiveAvailability(probe).Reason ?? "Updating this archive is unavailable here.", true); return null; }
        ArchiveJobPassword? password = null;
        try
        {
            var info = await MutationIoAsync(path, () => Services.Platform.FileOperations.TryGetInfo(path),
                () => { if (Services.Io.IsStopped) throw new OperationCanceledException(); });
            if (info is null || info.IsDirectory) { Notify("The archive file is no longer available. Refresh the folder.", true); return null; }
            if (info.IsReadOnly) { Notify("The archive file is read-only. Clear its read-only attribute to change it.", true); return null; }
            while (!Services.Io.IsStopped)
            {
                try
                {
                    var target = await ReviewArchiveTargetAsync(path, profile, password, expectedRevision);
                    if (target is null) return null;
                    var members = await RunArchivePreparationAsync(path, "Review archive members",
                        "Checking exact member identities and recursive folder scopes.", ct => new ArchiveMemberCommandReview(target.Assessment, ct));
                    if (members is null) return null;
                    if (target.Profile.Encryption.Mode == ArchiveEncryptionMode.None) { password?.Dispose(); password = null; }
                    var result = new NativeArchiveCommand(target, members, password); password = null; return result;
                }
                catch (ArchiveWriteException error) when ((profile.Format is ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip) &&
                    (error.Reason == ArchiveWriteReason.PasswordRequired || password is not null && error.Reason == ArchiveWriteReason.InvalidArchive))
                {
                    password?.Dispose(); password = null;
                    password = await AskArchivePasswordAsync(path, profile.Format, error.Reason != ArchiveWriteReason.PasswordRequired);
                    if (password is null) return null;
                    profile = new(profile.Format, profile.Version, profile.CompressionLevel, profile.DictionaryBytes,
                        profile.SevenZip, profile.Rar5, new(ArchiveEncryptionMode.Data));
                }
            }
            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.ComponentModel.Win32Exception)
        { if (!Services.Io.IsStopped) Notify("Cannot update this archive: " + Formatters.SafeName(error.Message), true); return null; }
        finally { password?.Dispose(); }
    }

    private async Task<ArchiveJobPassword?> AskArchivePasswordAsync(string path, ArchiveWriteFormat format, bool retry,
        CancellationToken ct = default, bool reading = false)
    {
        var password = new TextBox { PasswordChar = '•', MinWidth = 260 };
        AutomationProperties.SetName(password, "Existing archive password");
        var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 620 };
        bool valid = false;
        void Validate()
        {
            valid = false;
            try
            {
                if (string.IsNullOrEmpty(password.Text)) { problem.Text = "Enter the archive's existing password."; return; }
                ArchiveJobPassword.ValidateEncoding(format, password.Text.AsSpan()); valid = true; problem.Text = "";
            }
            catch (ArgumentException error) { problem.Text = error.Message; }
        }
        password.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) Validate(); };
        Validate();
        var body = new StackPanel { Spacing = 10, Children = {
            new TextBlock { Text = System.IO.Path.GetFileName(path), TextWrapping = TextWrapping.Wrap, MaxWidth = 620 },
            new TextBlock { Text = retry ? "The password did not unlock a verifiable archive. Try again, or cancel to leave it unchanged."
                : reading ? "Enter the password to browse and extract this archive. It is kept only while its tabs or borrowed content remain open."
                : "Enter the existing password to check this archive. Its protection will be preserved.", TextWrapping = TextWrapping.Wrap, MaxWidth = 620 },
            password, problem,
        } };
        try
        {
            var closer = new DialogCloser();
            using var cancellation = ct.Register(() => Services.Ui.Post(() => closer.Close("cancel")));
            ct.ThrowIfCancellationRequested();
            var answer = await Dialogs.ShowCustomAsync("Archive password", body,
                [new("Cancel", "cancel", IsCancel: true), new("Unlock", "unlock", IsDefault: true)], password, canConfirm: () => valid, closer: closer);
            return answer as string == "unlock" && valid && !ct.IsCancellationRequested && !Services.Io.IsStopped ? new(password.Text.AsSpan()) : null;
        }
        finally { password.Text = ""; }
    }

    private sealed record NativeMemberPlanResult(ArchivePlan? Plan, string? Problem);

    private async Task<NativeMemberPlanResult?> ReviewNativeMemberChangesAsync(NativeArchiveCommand command, IReadOnlyList<ArchiveChange> changes)
    {
        var result = await RunArchivePreparationAsync(command.Target.Path, "Check archive change",
            "Checking names, metadata preservation and the complete resulting member scope.", ct =>
        {
            try { return new NativeMemberPlanResult(command.Target.ReviewMemberChanges(changes, ct), null); }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
            { return new NativeMemberPlanResult(null, Formatters.SafeName(error.Message)); }
        });
        if (result?.Problem is { } problem && !Services.Io.IsStopped) Notify("Cannot make this archive change: " + problem, true);
        return result;
    }

    private bool SubmitNativeArchiveCommand(NativeArchiveCommand command, ArchivePlan plan, string description,
        TabViewModel? origin, Location? originLocation, QueueMode mode = QueueMode.Start, string? focus = null)
    {
        if (Services.Io.IsStopped) return false;
        try
        {
            Services.Jobs.SubmitArchive(plan, command.TakePassword(), mode, description, register: job =>
            {
                if (origin is not null && originLocation is not null) _jobOrigins[job] = (origin, originLocation);
                if (focus is not null) _focusAfter[job] = focus;
            });
            return true;
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        { Notify("Cannot queue this archive change: " + Formatters.SafeName(error.Message), true); return false; }
    }

    private sealed record NativeMemberScopes(ArchiveMemberCommandScope[] Values);

    private async Task DeleteNativeArchiveMembersAsync(Location folder, IReadOnlyList<ItemRef> items)
    {
        if (items.Count == 0) return;
        if (items.Count > MaxArchiveBatch || items.Any(item => !item.Parent.Equals(folder) || item.Kind is not (EntryKind.File or EntryKind.Directory)))
        { Notify("Select members in one archive folder before removing them.", true); return; }
        var origin = ActiveTab;
        string? revision = items[0].ArchiveRevision;
        if (revision is null || items.Any(item => !string.Equals(item.ArchiveRevision, revision, StringComparison.OrdinalIgnoreCase)))
        { Notify(items.Select(item => item.ArchiveMutationProblem).FirstOrDefault(problem => problem is not null) ?? "Refresh this archive before changing its members.", true); return; }
        var frozen = items.Select(item => (item.Name, Directory: item.IsContainer, item.Ordinal)).ToArray();
        using var command = await PrepareNativeArchiveCommandAsync(folder, expectedRevision: revision);
        if (command is null) return;
        try
        {
            var scopes = await RunArchivePreparationAsync(command.Target.Path, "Review removal scope", "Counting the exact stored members selected for removal.", ct =>
            {
                var result = new ArchiveMemberCommandScope[frozen.Length]; var unique = new HashSet<(string, bool, int)>();
                var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, checked(command.Members.RetainedHeapBytes + frozen.Length * 64L));
                for (int i = 0; i < frozen.Length; i++)
                {
                    ct.ThrowIfCancellationRequested(); var item = frozen[i];
                    if (!unique.Add((item.Name, item.Directory, item.Ordinal))) throw new ArgumentException("A member was selected more than once.");
                    long length = checked((long)folder.Path.Length + (folder.Path.Length == 0 ? 0 : 1) + item.Name.Length);
                    if (length > ArchiveWritePolicy.MaxNameChars) throw ArchiveWriteBudget.Limit();
                    budget.AddNameChars(length); budget.AddMetadata(0, checked(256 + length * 2));
                    result[i] = command.Members.Select(InArchive(folder, item.Name), item.Directory, item.Ordinal);
                }
                return new NativeMemberScopes(result);
            });
            if (scopes is null) return;
            long total = scopes.Values.Sum(scope => (long)scope.PhysicalEntries);
            string samples = ArchiveJobReviewText.Samples(scopes.Values.Take(8).Select(NativeScopeText));
            if (!await Dialogs.ConfirmAsync("Remove from archive", $"Remove {total:N0} stored entries from {System.IO.Path.GetFileName(command.Target.Path)}? " +
                "Selected folders include all their descendants and stored directory records. Removed members do not go to the Recycle Bin. " +
                "The whole archive will be rebuilt and checked.\n" + samples, "Remove", danger: true)) return;
            var reviewed = await ReviewNativeMemberChangesAsync(command, scopes.Values.Select(scope => scope.Change(ArchiveChangeKind.Delete)).ToArray());
            if (reviewed?.Plan is { } plan) SubmitNativeArchiveCommand(command, plan, $"Remove {total:N0} entries from “{System.IO.Path.GetFileName(command.Target.Path)}”", origin, folder);
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        { if (!Services.Io.IsStopped) Notify("Cannot remove these archive members: " + Formatters.SafeName(error.Message), true); }
    }

    private async Task CreateNativeArchiveFolderAsync(TabViewModel tab, Location folder)
    {
        using var command = await PrepareNativeArchiveCommandAsync(folder);
        if (command is null) return;
        if (!command.Members.DirectoryExists(folder.Path)) { Notify("The archive folder changed. Refresh it before creating a folder.", true); return; }
        string draft = "";
        while (!Services.Io.IsStopped)
        {
            var answer = await Dialogs.PromptAsync(new("Create folder in archive", "Folder name (use / for nested folders):")
            {
                Text = draft, ConfirmText = "Create", Hint = "The whole archive will be rebuilt and checked.",
                Validate = name => name.TrimEnd('/').Length == 0 ? "Enter a folder name." : ArchivePaths.Problem(InArchive(folder, name.TrimEnd('/'))) ??
                    command.Members.FolderProblem(InArchive(folder, name.TrimEnd('/'))),
            });
            if (answer is null) return;
            draft = answer.Text; string path = InArchive(folder, draft.TrimEnd('/')) + "/";
            var reviewed = await ReviewNativeMemberChangesAsync(command, [new(ArchiveChangeKind.CreateFolderEntry, path)]);
            if (reviewed is null) return;
            if (reviewed.Plan is not { } plan) continue;
            SubmitNativeArchiveCommand(command, plan, $"Create folder “{Formatters.SafeName(draft)}” in archive", tab, folder,
                focus: draft.TrimEnd('/').Split('/')[0]);
            return;
        }
    }

    private async Task RenameNativeArchiveMemberAsync(TabViewModel tab, Location folder)
    {
        if (!tab.Listing.TryGetFocused(out var row) || row.Kind == EntryKind.Parent) return;
        var item = tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex);
        if (item.ArchiveRevision is null) { Notify(item.ArchiveMutationProblem ?? "Refresh this archive before changing its members.", true); return; }
        string path = MemberPath(item), draft = item.Name; bool directory = item.IsContainer; int occurrence = item.Ordinal;
        using var command = await PrepareNativeArchiveCommandAsync(folder, expectedRevision: item.ArchiveRevision);
        if (command is null) return;
        try
        {
            var scope = command.Members.Select(path, directory, occurrence);
            while (!Services.Io.IsStopped)
            {
                var prompt = new PromptOptions("Rename in archive", "New name for " + NativeScopeText(scope) + ":")
                {
                    Text = draft, SelectStem = !directory, ConfirmText = "Rename",
                    Validate = name => name.IndexOfAny(['/', '\\']) >= 0 ? "Rename within the same folder." :
                        ArchivePaths.Problem(InArchive(folder, name)) ?? command.Members.RenameProblem(scope, InArchive(folder, name)),
                };
                bool sameRow = ReferenceEquals(ActiveTab, tab) && tab.Location?.Equals(folder) == true &&
                    tab.Listing.TryGetFocused(out var focused) && focused.Kind != EntryKind.Parent &&
                    tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).Equals(item);
                string? answer = sameRow ? await View.RenameInlineAsync(prompt) : (await Dialogs.PromptAsync(prompt))?.Text;
                if (answer is null) return;
                draft = answer;
                if ((directory || scope.Copies > 1) && !await Dialogs.ConfirmAsync("Rename in archive",
                    $"Rename {NativeScopeText(scope)} to “{Formatters.SafeName(answer)}”? The whole archive will be rebuilt and checked.", "Rename")) return;
                string destination = InArchive(folder, answer) + (directory ? "/" : "");
                var reviewed = await ReviewNativeMemberChangesAsync(command, [scope.Change(ArchiveChangeKind.Rename, destination)]);
                if (reviewed is null) return;
                if (reviewed.Plan is not { } plan) continue;
                SubmitNativeArchiveCommand(command, plan, $"Rename {NativeScopeText(scope)} to “{Formatters.SafeName(answer)}”", tab, folder, focus: answer);
                return;
            }
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        { if (!Services.Io.IsStopped) Notify("Cannot rename this archive member: " + Formatters.SafeName(error.Message), true); }
    }

    private static string NativeScopeText(ArchiveMemberCommandScope scope) => scope.Directory
        ? $"folder “{Formatters.SafeName(scope.Path.TrimEnd('/'))}” ({scope.PhysicalEntries:N0} stored entries, including {scope.DirectoryRecords:N0} directory records)"
        : $"“{Formatters.SafeName(scope.Path)}”" + (scope.Copies > 1 ? $" (copy {scope.Occurrence + 1:N0} of {scope.Copies:N0})" : "");

    private async Task AddToNativeArchiveAsync(JobKind kind, IReadOnlyList<ItemRef> items, Location destination, TransferOptions options,
        string? newName, QueueMode mode, TabViewModel? sourceTab, Location? sourceContext)
    {
        if (options.Conflicts is not (ConflictPolicy.Ask or ConflictPolicy.Skip or ConflictPolicy.Replace))
        { Notify("For this archive choose Ask, Skip or Replace existing names. Automatic renaming and replacement by time are unavailable.", true); return; }
        var origin = sourceTab ?? ActiveTab; var originLocation = sourceContext ?? origin?.Location;
        // These are captured before password/review dialogs; navigation cannot replace the selection or destination.
        var frozen = items.ToArray(); var folder = destination; string? incomingName = newName;
        if (frozen.Select(item => incomingName ?? item.Name).Distinct(StringComparer.Ordinal).Count() != frozen.Length)
        { Notify("Two selected items have the same archive name. Add them from their own folders.", true); return; }
        using var command = await PrepareNativeArchiveCommandAsync(folder, moveSources: kind == JobKind.Move);
        if (command is null) return;
        if (incomingName is not null && command.Members.DirectoryExists(InArchive(folder, incomingName)))
        {
            var resolved = folder.WithPath(InArchive(folder, incomingName));
            if (!await Dialogs.ConfirmAsync("Existing archive folder", $"The destination is an existing archive folder. " +
                $"{(kind == JobKind.Move ? "Move" : "Copy")} the selected item into “{Formatters.SafeName(resolved.Path)}”?", kind == JobKind.Move ? "Move" : "Copy")) return;
            folder = resolved; incomingName = null;
        }
        if (command.Members.DestinationFolderProblem(folder.Path) is { } problem) { Notify(problem, true); return; }
        var changes = frozen.Select(item => new ArchiveChange(item.IsContainer ? ArchiveChangeKind.AddFolder : ArchiveChangeKind.AddFile,
            InArchive(folder, incomingName ?? item.Name), item.FileSystemPath)).ToArray();
        try
        {
            var plan = command.Target.CreatePlan(changes, moveSources: kind == JobKind.Move) with
            { AddConflictPolicy = options.Conflicts switch { ConflictPolicy.Skip => ArchiveAddConflictPolicy.Skip, ConflictPolicy.Replace => ArchiveAddConflictPolicy.Replace, _ => ArchiveAddConflictPolicy.Ask } };
            SubmitNativeArchiveCommand(command, plan,
                $"{(kind == JobKind.Move ? "Move" : "Add")} {frozen.Length:N0} items to “{System.IO.Path.GetFileName(command.Target.Path)}”", origin, originLocation, mode);
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        { if (!Services.Io.IsStopped) Notify("Cannot add these archive inputs: " + Formatters.SafeName(error.Message), true); }
    }
}
