using Avalonia.Controls;
using Avalonia.Media;
using Avalonia;
using Avalonia.Platform.Storage;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool TryGetFocusedRegistryItem(out ItemRef item)
    {
        item = null!;
        var listing = ActiveTab?.Listing;
        if (listing is null || !listing.TryGetFocused(out var row) || row.Kind is not (EntryKind.RegistryKey or EntryKind.RegistryValue))
            return false;
        item = listing.GetItemRef(listing.FocusedStoreIndex);
        return item.Parent.Scheme == Schemes.Registry;
    }

    private async Task ExportRegistryAsync()
    {
        var location = ActiveTab?.Location;
        ItemRef? selected = TryGetFocusedRegistryItem(out var item) ? item : null;
        if (selected is null && (location?.Scheme != Schemes.Registry || location.Path.Length == 0)) return;
        var choices = new List<ChoiceItem>();
        if (location?.Scheme == Schemes.Registry && location.Path.Length > 0)
            choices.Add(new ChoiceItem("Current key and subtree", Services.Providers.Display(location)));
        if (selected is not null)
            choices.Add(new ChoiceItem(selected.Kind == EntryKind.RegistryValue ? "Selected value" : "Selected key and subtree",
                Services.Providers.Display(selected.Parent) + "\\" + (selected.Name.Length == 0 ? "(Default)" : selected.Name)));
        var pick = await Dialogs.ChooseAsync(new ChoiceOptions("Export Registry", choices.ToArray())
        { Hint = "Exports raw types and bytes. .reg files do not contain ACLs or the 32/64-bit view." });
        if (pick.Index < 0) return;
        bool current = location?.Scheme == Schemes.Registry && location.Path.Length > 0 && pick.Index == 0;
        var key = current ? location! : selected!.Kind == EntryKind.RegistryKey
            ? selected.Parent.WithPath(selected.Parent.Path.Length == 0 ? selected.Name : selected.Parent.Path + "\\" + selected.Name)
            : selected.Parent;
        string? valueName = current || selected?.Kind != EntryKind.RegistryValue ? null : selected.Name;
        string suggested = (valueName ?? key.Path[(key.Path.LastIndexOf('\\') + 1)..]).Replace(' ', '_') + ".reg";
        var top = View.TopLevel;
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Registry to .reg",
            SuggestedFileName = suggested,
            DefaultExtension = "reg",
            FileTypeChoices = [new FilePickerFileType("Registry files") { Patterns = ["*.reg"] }],
        });
        string? path = file?.TryGetLocalPath();
        if (path is null) { if (file is not null) Notify("Choose a local file path for Registry export.", true); return; }
        if (!path.EndsWith(".reg", StringComparison.OrdinalIgnoreCase)) path += ".reg";
        if (File.Exists(path) && !await Dialogs.ConfirmAsync("Replace export file?",
            $"Replace {path} with an export of {Services.Providers.Display(key)}?", "Replace file", danger: true)) return;
        try
        {
            Notify("Exporting Registry selection…");
            await Task.Run(() => RegistryInterchange.Export(key, valueName, path));
            Notify($"Exported {Services.Providers.Display(key)} to {path}. Import must use the intended Registry view explicitly.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
        { Notify($"Registry export failed: {ex.Message}", true); }
    }

    private async Task ImportRegistryAsync()
    {
        var tab = ActiveTab;
        var scope = tab?.Location;
        if (scope?.Scheme != Schemes.Registry || scope.Path.Length == 0) return;
        var top = View.TopLevel;
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Import .reg within {scope.Path}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Registry files") { Patterns = ["*.reg"] }],
        });
        var file = files.FirstOrDefault();
        string? path = file?.TryGetLocalPath();
        if (path is null) { if (file is not null) Notify("Choose a local .reg file to import.", true); return; }
        RegistryImportPlan plan;
        try
        {
            Notify("Reading and checking Registry import…");
            plan = await Task.Run(() => RegistryImport.Preview(path, scope));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException or System.ComponentModel.Win32Exception)
        { Notify($"Cannot preview Registry import: {ex.Message}", true); return; }
        if (plan.Changes.Count == 0) { Notify("This .reg file makes no changes within the selected Registry scope."); return; }
        string Describe(RegistryChange c) => c.Action switch
        {
            RegistryAction.CreateKey => $"+ key  {c.Key.Path}\\{c.Name}",
            RegistryAction.DeleteKey => $"− tree {c.Key.Path}\\{c.Name}",
            RegistryAction.DeleteValue => $"− value {c.Key.Path}\\{(c.Name.Length == 0 ? "(Default)" : c.Name)}",
            RegistryAction.SetValue => $"{(c.Expected is null ? "+ value" : "↻ value")} {c.Key.Path}\\{(c.Name.Length == 0 ? "(Default)" : c.Name)} · {RegistryValueCodec.TypeName(c.Desired!.Type)}, {c.Desired.Data.Length:N0} bytes",
            _ => c.Action.ToString(),
        };
        var detail = new TextBox
        {
            Text = string.Join(Environment.NewLine, plan.Changes.Take(200).Select(Describe)) +
                   (plan.Changes.Count > 200 ? $"{Environment.NewLine}… {plan.Changes.Count - 200:N0} more changes" : ""),
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MinWidth = 620, MinHeight = 220, MaxHeight = 350,
        };
        var body = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = $"Scope: {Services.Providers.Display(scope)}\nFile: {path}\nAdd {plan.AddedKeys:N0} keys and {plan.AddedValues:N0} values; overwrite {plan.OverwrittenValues:N0} values; delete {plan.DeletedValues:N0} values and {plan.DeletedTrees:N0} subtrees. {plan.DataBytes:N0} incoming bytes.", TextWrapping = TextWrapping.Wrap, MaxWidth = 690 },
            new TextBlock { Text = "The file's paths must stay inside this scope. The selected Registry view applies to every change. Import is not atomic: completed steps remain if a later step fails. Existing values and subtrees are checked again before mutation.", TextWrapping = TextWrapping.Wrap, MaxWidth = 690 },
            detail,
        } };
        var result = await Dialogs.ShowCustomAsync("Review Registry import", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Import changes", "import", IsDefault: true)]);
        if (result as string != "import") return;
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Registry,
            RegistryChanges = plan.Changes,
            Destination = scope,
            Description = $"Import {plan.Changes.Count:N0} Registry changes from {Path.GetFileName(path)}",
        });
        Track(job, tab!);
    }

    private static string? RegistryNameError(string name, bool key) =>
        name.Contains('\0') || name.Length > 16383 || key && (name.Length == 0 || name.Contains('\\'))
            ? "Enter a valid Registry name (keys cannot contain \\)." : null;

    private RegistryValueSnapshot ReadRegistrySnapshot(ItemRef item)
    {
        using var key = WindowsRegistryProvider.Open(item.Parent, false);
        var raw = RegistryRaw.Read(key, item.Name);
        if (raw.Data.Length != raw.Length) throw new IOException("This value exceeds the 64 MiB edit limit.");
        return new RegistryValueSnapshot(raw.Type, raw.Data);
    }

    private void SubmitRegistry(RegistryChange change, string title, ItemRef? source = null, string? focus = null)
    {
        var tab = ActiveTab;
        if (tab is null) return;
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Registry,
            Registry = change,
            Sources = source is null ? [] : [source],
            Destination = change.TargetKey ?? change.Key,
            Description = title,
        });
        Track(job, tab);
        if (focus is not null) _focusAfter[job] = focus;
    }

    private async Task CreateRegistryAsync()
    {
        var loc = ActiveTab?.Location;
        if (loc is null || (Services.Providers.For(loc).GetCapabilities(loc) & LocationCapabilities.CreateDirectory) == 0)
        {
            Notify("Select a writable Registry key. HKCR and HKCC are merged or alias views; choose an explicit HKCU or HKLM target.", true);
            return;
        }
        var choice = await Dialogs.ChooseAsync(new ChoiceOptions("Create in Registry", [
            new ChoiceItem("Key", "A navigable subkey"), new ChoiceItem("Value", "A typed value; an empty name means (Default)"),
        ]) { Hint = "F7 creates a key or value in this location" });
        if (choice.Index < 0) return;
        bool key = choice.Index == 0;
        var named = await Dialogs.PromptAsync(new PromptOptions(key ? "Create key" : "Create value", key ? "New key name:" : "Value name (empty for the default value):")
        {
            Validate = n => RegistryNameError(n, key),
            ConfirmText = "Next",
        });
        if (named is null) return;
        if (key)
        {
            SubmitRegistry(new RegistryChange(RegistryAction.CreateKey, loc, named.Text), $"Create Registry key {named.Text}", focus: named.Text);
            return;
        }
        using (var opened = WindowsRegistryProvider.Open(loc, false))
            if (RegistryRaw.ReadIfPresent(opened, named.Text, RegistryRaw.PreviewLimit) is not null)
            {
                Notify("That value already exists. Select it and use F4 to edit it.", true);
                return;
            }
        var desired = await EditRegistryDialogAsync(named.Text, null, loc);
        if (desired is null) return;
        SubmitRegistry(new RegistryChange(RegistryAction.SetValue, loc, named.Text, Desired: desired),
            $"Create Registry value {(named.Text.Length == 0 ? "(Default)" : named.Text)}", focus: named.Text);
    }

    private async Task EditRegistryValueAsync(ItemRef item)
    {
        try
        {
            var original = await Task.Run(() => ReadRegistrySnapshot(item));
            var desired = await EditRegistryDialogAsync(item.Name, original, item.Parent);
            if (desired is null || desired.Type == original.Type && desired.Data.AsSpan().SequenceEqual(original.Data)) return;
            if (!await Dialogs.ConfirmAsync("Save Registry value",
                $"Write {(item.Name.Length == 0 ? "(Default)" : item.Name)} in {Services.Providers.Display(item.Parent)}?\n\n" +
                $"{RegistryValueCodec.TypeName(original.Type)} → {RegistryValueCodec.TypeName(desired.Type)}; {original.Data.Length:N0} → {desired.Data.Length:N0} bytes. " +
                "FileCat rereads the original before writing and verifies the result. Another program can still race this best-effort check.", "Save value")) return;
            SubmitRegistry(new RegistryChange(RegistryAction.SetValue, item.Parent, item.Name, original, desired),
                $"Edit Registry value {(item.Name.Length == 0 ? "(Default)" : item.Name)}", item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Notify($"Cannot edit Registry value: {ex.Message}", true);
        }
    }

    private async Task<RegistryValueSnapshot?> EditRegistryDialogAsync(string name, RegistryValueSnapshot? original, Location? watch = null)
    {
        var current = original is null ? null : new RegistryValueData(original.Type, original.Data, original.Data.Length);
        bool rawOnly = false;
        string initial = current is null ? "" : RegistryValueCodec.Format(current, out rawOnly);
        var types = RegistryValueCodec.EditableTypes.Concat(current is null ? [] : [current.Type]).Distinct().ToArray();
        var names = types.Select(RegistryValueCodec.TypeName).ToArray();
        var type = new ComboBox { ItemsSource = names, SelectedIndex = current is null ? 0 : Array.IndexOf(types, current.Type), MinWidth = 180 };
        Avalonia.Automation.AutomationProperties.SetName(type, "Registry value type");
        var input = new TextBox { Text = initial, MinWidth = 500, MaxHeight = 280, PlaceholderText = "Stored data" }; // lines: set per type below
        Avalonia.Automation.AutomationProperties.SetName(input, "Registry value data");
        var reinterpret = new CheckBox { Content = "Reinterpret original bytes as the selected type", IsVisible = original is not null };
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 650, Classes = { "muted" } };
        // Shown only while they say something (empty lines would still take the dialog's spacing).
        var issue = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "error" }, IsVisible = false };
        var changed = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "error" }, IsVisible = false };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = $"{(name.Length == 0 ? "(Default)" : name)} · choose a type and edit its stored data. Changes are not applied until you confirm.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(type);
        body.Children.Add(input);
        body.Children.Add(reinterpret);
        body.Children.Add(preview);
        body.Children.Add(issue);
        body.Children.Add(changed);
        using var monitor = watch is null ? null : new RegistryChangeMonitor(watch,
            () => Services.Ui.Post(() => (changed.Text, changed.IsVisible) = ("This Registry key changed while the editor was open. Your input is preserved; save will recheck the original and may report a conflict.", true)),
            _ => Services.Ui.Post(() => (changed.Text, changed.IsVisible) = ("Change notifications stopped. Save will still recheck the original value.", true)));
        RegistryValueSnapshot? parsed = null;
        void Refresh()
        {
            uint selected = types[Math.Max(0, type.SelectedIndex)];
            bool retype = original is not null && selected != original.Type && reinterpret.IsChecked == true;
            reinterpret.IsVisible = original is not null && selected != original.Type;
            bool hex = selected == 3 || !RegistryValueCodec.EditableTypes.Contains(selected) || rawOnly && original?.Type == selected;
            input.PlaceholderText = hex ? "Hex bytes, e.g. 00 FF 2A" : selected is 4 or 11 ? "Unsigned decimal or 0x hexadecimal" : selected == 7 ? "One string per line" : "Stored text (not expanded)";
            // Lines only where the data has them (strings of a multi-string, rows of bytes): a number or a string is one
            // line, and Enter saves it.
            bool lines = hex || selected == 7;
            input.AcceptsReturn = lines;
            input.MinHeight = lines ? 80 : 0;
            input.TextWrapping = selected is 4 or 11 ? TextWrapping.NoWrap : TextWrapping.Wrap;
            if (retype) parsed = new RegistryValueSnapshot(selected, original!.Data);
            else if (RegistryValueCodec.TryParse(selected, input.Text ?? "", hex, out var bytes, out var error)) parsed = new RegistryValueSnapshot(selected, bytes);
            else { parsed = null; issue.Text = error; issue.IsVisible = true; preview.Text = ""; return; }
            issue.Text = "";
            issue.IsVisible = false;
            preview.Text = $"Preview: {RegistryValueCodec.TypeName(selected)} · {parsed.Data.Length:N0} bytes · {RegistryRaw.Preview(new RegistryValueData(selected, parsed.Data, parsed.Data.Length))}";
        }
        type.SelectionChanged += (_, _) => Refresh();
        input.TextChanged += (_, _) => Refresh();
        reinterpret.IsCheckedChanged += (_, _) => Refresh();
        Refresh();
        var result = await Dialogs.ShowCustomAsync(original is null ? "Create Registry value" : "Edit Registry value", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Save", "save", IsDefault: true)], input,
            () => parsed is not null);
        return result as string == "save" ? parsed : null;
    }

    private const int MaxRegistryBatch = 1000;

    /// <summary>Marked Registry items, or the focused one when none are marked (plan §4.3); null with a reason otherwise.</summary>
    private IReadOnlyList<ItemRef>? RegistryBatch(string verb)
    {
        var sel = SourceSelection();
        if (sel is null) return null;
        var items = sel.Value.Items;
        if (items.Count > MaxRegistryBatch)
        {
            Notify($"{verb} at most {MaxRegistryBatch:N0} Registry items at a time; narrow the selection or use a key's subtree.", true);
            return null;
        }
        var list = items.ToList();
        if (list.Any(i => i.Parent.Scheme != Schemes.Registry || i.Kind is not (EntryKind.RegistryKey or EntryKind.RegistryValue))) return null;
        if (list.Any(i => i.Kind == EntryKind.RegistryKey && i.Parent.Path.Length == 0))
        {
            Notify("Registry roots are not items you can delete or copy; open one and work inside it.", true);
            return null;
        }
        return list;
    }

    private static Location KeyOf(ItemRef item) => item.Parent.WithPath(item.Parent.Path.Length == 0 ? item.Name : item.Parent.Path + "\\" + item.Name);

    private static string Label(ItemRef item) => item.Kind == EntryKind.RegistryValue && item.Name.Length == 0 ? "(Default)" : item.Name;

    private sealed record RegistryPreflight(List<RegistryChange> Changes, int Keys, int Values, int NestedKeys, int NestedValues, long Bytes, int Links);

    /// <summary>Captures what F8 will delete: each value's exact data, each subtree's fingerprint (guards in the job).</summary>
    private RegistryPreflight PreflightDelete(IReadOnlyList<ItemRef> items)
    {
        var changes = new List<RegistryChange>();
        int keys = 0, values = 0, nestedKeys = 0, nestedValues = 0, links = 0;
        long bytes = 0;
        foreach (var item in items)
        {
            if (item.Kind == EntryKind.RegistryValue)
            {
                var snapshot = ReadRegistrySnapshot(item);
                changes.Add(new RegistryChange(RegistryAction.DeleteValue, item.Parent, item.Name, snapshot));
                values++;
                bytes += snapshot.Data.Length;
                continue;
            }
            var scope = RegistryTree.Scan(KeyOf(item));
            changes.Add(new RegistryChange(RegistryAction.DeleteKey, item.Parent, item.Name, TreeDigest: RegistryTree.Digest(scope)));
            keys++;
            nestedKeys += scope.KeyCount - 1;
            nestedValues += scope.ValueCount;
            bytes += scope.DataBytes;
            links += scope.LinkCount;
        }
        return new RegistryPreflight(changes, keys, values, nestedKeys, nestedValues, bytes, links);
    }

    private async Task DeleteRegistryAsync()
    {
        var items = RegistryBatch("Delete");
        if (items is null) return;
        if (items.Any(i => RegistryAliases.IsAliasPath(i.Parent.Path))) { Notify(RegistryAliases.ReadOnlyReason, true); return; }
        RegistryPreflight plan;
        try
        {
            Notify("Inspecting the Registry items before deletion…");
            plan = await Task.Run(() => PreflightDelete(items));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            Notify($"Cannot inspect everything that would be deleted: {ex.Message}. Nothing was deleted.", true);
            return;
        }
        string what = items.Count == 1
            ? $"{Services.Providers.Display(items[0].Parent)}\\{Label(items[0])}" + (plan.Keys == 1 ? $" and everything beneath it ({plan.NestedKeys:N0} keys, {plan.NestedValues:N0} values)" : "")
            : $"{Formatters.Plural(plan.Values, "value", "values")} and {Formatters.Plural(plan.Keys, "key", "keys")}" +
              (plan.Keys > 0 ? $" with {plan.NestedKeys:N0} keys and {plan.NestedValues:N0} values beneath them" : "");
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Text = $"Delete {what} ({plan.Bytes:N0} bytes of data) permanently?" });
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Classes = { "muted" },
            Text = "Registry data does not go to the Recycle Bin. Undo (Ctrl+Z) restores deleted values while nothing else changed them; deleted keys can be restored only from a backup. " +
                   "FileCat checks everything again right before deleting and stops at the first change it finds; deleting several items is not atomic." +
                   (plan.Links > 0 ? $" {Formatters.Plural(plan.Links, "Registry link is", "Registry links are")} deleted as links; their targets are kept." : ""),
        });
        var backup = new CheckBox
        {
            Content = $"Save a .reg backup first (in {Services.Paths.RegistryBackupDirectory})",
            IsChecked = plan.Keys > 0 && plan.Links == 0,
            IsVisible = plan.Links == 0,
        };
        body.Children.Add(backup);
        if (plan.Links > 0)
            body.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 640, Classes = { "muted" }, Text = "No backup is offered: a .reg file cannot represent Registry links." });
        var answer = await Dialogs.ShowCustomAsync("Delete from the Registry", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Delete permanently", "delete", IsDefault: true, IsDanger: true)]);
        if (answer as string != "delete") return;
        string? backupPath = null;
        if (backup.IsChecked == true && backup.IsVisible)
        {
            string stem = string.Concat(Label(items[0]).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            backupPath = Path.Combine(Services.Paths.RegistryBackupDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss} {(stem.Length == 0 ? "Default" : stem)}.reg");
            try
            {
                Notify("Saving the .reg backup…");
                await Task.Run(() => RegistryInterchange.ExportMany(
                    items.Select(i => i.Kind == EntryKind.RegistryKey ? (KeyOf(i), (string?)null) : (i.Parent, (string?)i.Name)).ToList(), backupPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                if (!await Dialogs.ConfirmAsync("Backup failed", $"The .reg backup could not be saved: {ex.Message}\n\nDelete without a backup?", "Delete without backup", danger: true))
                    return;
                backupPath = null;
            }
        }
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Registry,
            RegistryChanges = plan.Changes,
            Sources = items,
            Destination = items[0].Parent,
            Description = items.Count == 1
                ? $"Delete Registry {(plan.Keys == 1 ? "subtree" : "value")} {Label(items[0])}"
                : $"Delete {items.Count:N0} Registry items",
        });
        if (ActiveTab is { } tab) Track(job, tab);
        if (backupPath is not null)
            Notify($"Backup saved to {backupPath}. To restore, open the parent key and use File → Import .reg.");
    }

    private async Task RenameRegistryAsync()
    {
        if (!TryGetFocusedRegistryItem(out var item)) return;
        var named = await Dialogs.PromptAsync(new PromptOptions("Rename Registry item", "New name in this key:")
        {
            Text = item.Name,
            Validate = n => RegistryNameError(n, item.Kind == EntryKind.RegistryKey) ?? (n == item.Name ? "The name is unchanged." : null),
            ConfirmText = "Rename",
        });
        if (named is null) return;
        if (item.Kind == EntryKind.RegistryKey)
            SubmitRegistry(new RegistryChange(RegistryAction.RenameKey, item.Parent, item.Name, TargetName: named.Text),
                $"Rename Registry key {item.Name} to {named.Text}", item, named.Text);
        else
        {
            var original = await Task.Run(() => ReadRegistrySnapshot(item));
            if (!await Dialogs.ConfirmAsync("Rename Registry value",
                "Value rename creates the new name and then deletes the old one. If deletion fails, both values remain; the operation report identifies that outcome.", "Rename value")) return;
            SubmitRegistry(new RegistryChange(RegistryAction.RenameValue, item.Parent, item.Name, original, TargetKey: item.Parent, TargetName: named.Text),
                $"Rename Registry value {item.Name} to {named.Text}", item, named.Text);
        }
    }

    private async Task CopyRegistryAsync()
    {
        var items = RegistryBatch("Copy");
        if (items is null) return;
        var target = Workspace.ActiveTarget?.ActiveTab?.Location;
        if (target is { IsFileSystem: true })
        {
            // Typed Registry data never becomes a byte file silently; the named export is offered instead (plan §12.1).
            await ExportRegistryToFolderAsync(items, target);
            return;
        }
        if (target?.Scheme != Schemes.Registry || target.Path.Length == 0)
        {
            Notify("Choose a Registry key (or a folder, to export a .reg file) in the target panel.");
            return;
        }
        if (RegistryAliases.IsAliasPath(target.Path)) { Notify(RegistryAliases.ReadOnlyReason, true); return; }
        if (items.Count == 1) await CopyOneRegistryItemAsync(items[0], target);
        else await CopyRegistryBatchAsync(items, target);
    }

    private async Task ExportRegistryToFolderAsync(IReadOnlyList<ItemRef> items, Location folder)
    {
        if (items.Select(i => i.Parent.Session ?? "default").Distinct().Count() > 1)
        {
            Notify("A .reg file holds one Registry view; export each view separately.", true);
            return;
        }
        string stem = items.Count == 1 ? Label(items[0]) : Path.GetFileName(items[0].Parent.Path);
        stem = string.Concat(stem.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var named = await Dialogs.PromptAsync(new PromptOptions("Export to a .reg file",
            $"Registry data is typed, so F5 does not copy it into a folder. Export {(items.Count == 1 ? Label(items[0]) : Formatters.Plural(items.Count, "item", "items"))} as a .reg file in {folder.Path}:")
        {
            Text = (stem.Length == 0 ? "registry" : stem) + ".reg",
            SelectStem = true,
            ConfirmText = "Export",
            Validate = n => n.EndsWith(".reg", StringComparison.OrdinalIgnoreCase) && n.Length > 4 && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                ? null : "Enter a file name ending in .reg.",
        });
        if (named is null) return;
        string path = Path.Combine(folder.Path, named.Text);
        if (File.Exists(path) && !await Dialogs.ConfirmAsync("Replace export file?", $"Replace {path} with the export?", "Replace file", danger: true)) return;
        try
        {
            Notify("Exporting to .reg…");
            await Task.Run(() => RegistryInterchange.ExportMany(
                items.Select(i => i.Kind == EntryKind.RegistryKey ? (KeyOf(i), (string?)null) : (i.Parent, (string?)i.Name)).ToList(), path));
            Notify($"Exported to {path}. It holds raw types and data, not permissions or the 32/64-bit view.");
            RefreshTabsShowing(folder.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            Notify($"Registry export failed: {ex.Message}", true);
        }
    }

    /// <summary>Several items keep their names; existing destination items are reported and never overwritten.</summary>
    private async Task CopyRegistryBatchAsync(IReadOnlyList<ItemRef> items, Location target)
    {
        (List<RegistryChange> Changes, List<ItemRef> Copied, List<string> Existing, int NestedKeys, int NestedValues, int Links) plan;
        try
        {
            Notify("Inspecting the Registry items before copying…");
            plan = await Task.Run(() =>
            {
                var changes = new List<RegistryChange>();
                var copied = new List<ItemRef>();
                var existing = new List<string>();
                int nestedKeys = 0, nestedValues = 0, links = 0;
                using var destination = WindowsRegistryProvider.Open(target, writable: false);
                foreach (var item in items)
                {
                    if (item.Kind == EntryKind.RegistryValue)
                    {
                        if (RegistryRaw.ReadIfPresent(destination, item.Name, RegistryRaw.PreviewLimit) is not null) { existing.Add(Label(item)); continue; }
                        changes.Add(new RegistryChange(RegistryAction.CopyValue, item.Parent, item.Name, ReadRegistrySnapshot(item), TargetKey: target, TargetName: item.Name));
                    }
                    else
                    {
                        if (RegistryRaw.SubKeyExists(destination, item.Name)) { existing.Add(item.Name); continue; }
                        var scope = RegistryTree.Scan(KeyOf(item));
                        if (scope.LinkCount > 0) { links += scope.LinkCount; continue; }
                        nestedKeys += scope.KeyCount - 1;
                        nestedValues += scope.ValueCount;
                        changes.Add(new RegistryChange(RegistryAction.CopyKey, item.Parent, item.Name, TargetKey: target, TargetName: item.Name,
                            TreeDigest: RegistryTree.Digest(scope)));
                    }
                    copied.Add(item);
                }
                return (changes, copied, existing, nestedKeys, nestedValues, links);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            Notify($"Cannot inspect everything that would be copied: {ex.Message}. Nothing was copied.", true);
            return;
        }
        if (plan.Changes.Count == 0)
        {
            Notify(plan.Existing.Count > 0 ? "Every selected name already exists at the destination; nothing was copied." :
                "The selected keys contain Registry links, which FileCat does not copy or follow; nothing was copied.", true);
            return;
        }
        var notes = new List<string>();
        if (plan.Existing.Count > 0) notes.Add($"{Formatters.Plural(plan.Existing.Count, "name already exists", "names already exist")} at the destination and {(plan.Existing.Count == 1 ? "is" : "are")} skipped: {string.Join(", ", plan.Existing.Take(5))}{(plan.Existing.Count > 5 ? ", …" : "")}.");
        if (plan.Links > 0) notes.Add("Keys containing Registry links are skipped; links are never copied or followed.");
        if (!await Dialogs.ConfirmAsync("Copy Registry items",
                $"Copy {Formatters.Plural(plan.Changes.Count, "item", "items")}" +
                (plan.NestedKeys + plan.NestedValues > 0 ? $" (with {plan.NestedKeys:N0} keys and {plan.NestedValues:N0} values beneath them)" : "") +
                $" to {Services.Providers.Display(target)}? The copies inherit the destination's permissions. " + string.Join(" ", notes), "Copy"))
            return;
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Registry,
            RegistryChanges = plan.Changes,
            Sources = plan.Copied,
            Destination = target,
            Description = $"Copy {plan.Changes.Count:N0} Registry items",
        });
        if (ActiveTab is { } tab) Track(job, tab);
    }

    private async Task CopyOneRegistryItemAsync(ItemRef item, Location target)
    {
        if (item.Kind == EntryKind.RegistryKey)
        {
            RegistryTreeSnapshot scope;
            try
            {
                Notify("Inspecting the Registry subtree before copying…");
                scope = await Task.Run(() => RegistryTree.Scan(item.Parent.WithPath(item.Parent.Path + "\\" + item.Name)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Notify($"Cannot inspect the full key subtree: {ex.Message}. Nothing was copied.", true);
                return;
            }
            if (scope.LinkCount > 0)
            {
                Notify($"This subtree contains {scope.LinkCount:N0} Registry links. FileCat will not copy links or follow their targets implicitly.", true);
                return;
            }
            var targetName = await Dialogs.PromptAsync(new PromptOptions("Copy Registry key", $"Destination: {Services.Providers.Display(target)}\nNew key name:")
            {
                Text = item.Name, Validate = n => RegistryNameError(n, true), ConfirmText = "Review copy",
            });
            if (targetName is null) return;
            if (!await Dialogs.ConfirmAsync("Copy Registry subtree",
                $"Copy {scope.KeyCount:N0} keys and {scope.ValueCount:N0} values ({scope.DataBytes:N0} bytes) to {Services.Providers.Display(target)}\\{targetName.Text}? " +
                "The destination inherits its parent's permissions. Existing keys are not merged or overwritten. A partial copy remains visible if work stops.", "Copy subtree")) return;
            SubmitRegistry(new RegistryChange(RegistryAction.CopyKey, item.Parent, item.Name,
                TargetKey: target, TargetName: targetName.Text, TreeDigest: RegistryTree.Digest(scope)),
                $"Copy Registry subtree {item.Name}", item);
            return;
        }
        var name = await Dialogs.PromptAsync(new PromptOptions("Copy Registry value", $"Destination: {Services.Providers.Display(target)}\nName:")
        {
            Text = item.Name, Validate = n => RegistryNameError(n, false), ConfirmText = "Copy",
        });
        if (name is null) return;
        var original = await Task.Run(() => ReadRegistrySnapshot(item));
        if (!await Dialogs.ConfirmAsync("Copy Registry value",
            $"Copy {RegistryValueCodec.TypeName(original.Type)} ({original.Data.Length:N0} bytes) to {Services.Providers.Display(target)}\\{(name.Text.Length == 0 ? "(Default)" : name.Text)}? Existing values are never overwritten silently.", "Copy value")) return;
        SubmitRegistry(new RegistryChange(RegistryAction.CopyValue, item.Parent, item.Name, original, TargetKey: target, TargetName: name.Text),
            $"Copy Registry value {item.Name}", item);
    }

    private void ViewRegistryValue(ItemRef item, bool raw) => _ = ViewRegistryValueAsync(item, raw);

    private async Task ViewRegistryKeyAsync(ItemRef item)
    {
        var location = item.Parent.WithPath(item.Parent.Path.Length == 0 ? item.Name : item.Parent.Path + "\\" + item.Name);
        try
        {
            if ((item.Flags & EntryFlags.Link) != 0)
            {
                using var parent = WindowsRegistryProvider.Open(item.Parent, false);
                string? target = await Task.Run(() => RegistryRaw.LinkTarget(parent, item.Name));
                await Dialogs.AlertAsync("Registry link", $"{Services.Providers.Display(location)}\nTarget: {target ?? "unavailable"}\n\nOpen the target explicitly with Enter. Subtree operations do not follow it.");
                return;
            }
            var details = await Task.Run(() =>
            {
                using var key = WindowsRegistryProvider.Open(location, false);
                return (key.SubKeyCount, key.ValueCount, Acl: RegistryAcl.Inspect(location));
            });
            await Dialogs.AlertAsync("Registry key · read only",
                $"{Services.Providers.Display(location)}\n{details.SubKeyCount:N0} direct subkeys · {details.ValueCount:N0} direct values\n\n" +
                $"Owner SID: {details.Acl.OwnerSid ?? "not available"}\nGroup SID: {details.Acl.GroupSid ?? "not available"}\n" +
                $"DACL: {details.Acl.AceCount:N0} access entries\nSDDL: {details.Acl.Sddl}\n\n" +
                "The system audit ACL is not read; it requires extra privilege. Key ACL editing is not available here.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Notify($"Cannot inspect Registry key: {ex.Message}", true); }
    }

    private async Task OpenRegistryLinkFromResultAsync(ItemRef item, TabViewModel tab)
    {
        try
        {
            var target = await Task.Run(() =>
            {
                using var parent = WindowsRegistryProvider.Open(item.Parent, false);
                return RegistryRaw.LinkTarget(parent, item.Name);
            });
            if (target is null) { Notify("This Registry key is no longer a link. Refresh the results."); return; }
            await OpenRegistryLinkAsync(target, tab);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Notify($"Cannot inspect Registry link: {ex.Message}", true); }
    }

    /// <summary>Explicit conversion (REG-003): a value's stored bytes, exactly, into a file. The type is not part of the file.</summary>
    private async Task SaveRegistryDataAsync()
    {
        if (!TryGetFocusedRegistryItem(out var item) || item.Kind != EntryKind.RegistryValue) return;
        RegistryValueSnapshot value;
        try { value = await Task.Run(() => ReadRegistrySnapshot(item)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Notify($"Cannot read the value: {ex.Message}", true);
            return;
        }
        if (View.TopLevel is not { } top) return;
        string stem = string.Concat(Label(item).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the value's raw data",
            SuggestedFileName = (stem.Length == 0 ? "value" : stem) + ".bin",
        });
        string? path = file?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            await Task.Run(() =>
            {
                string temp = path + ".filecat-" + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, value.Data);
                File.Move(temp, path, overwrite: true);
            });
            Notify($"Saved {value.Data.Length:N0} bytes of {RegistryValueCodec.TypeName(value.Type)} data to {path}. The file holds the stored bytes exactly; the value type is not part of it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify($"Cannot save the value data: {ex.Message}", true);
        }
    }

    /// <summary>
    /// Explicit conversion (REG-003): a file's bytes become a value's stored data, unchanged. An existing value keeps its
    /// type and is guarded by its captured data; a new value is REG_BINARY.
    /// </summary>
    private async Task LoadRegistryDataAsync()
    {
        var key = ActiveTab?.Location;
        if (key?.Scheme != Schemes.Registry || key.Path.Length == 0 || RegistryAliases.IsAliasPath(key.Path)) return;
        ItemRef? existing = TryGetFocusedRegistryItem(out var focused) && focused.Kind == EntryKind.RegistryValue && Equals(focused.Parent, key) ? focused : null;
        string name;
        RegistryValueSnapshot? expected = null;
        if (existing is not null)
        {
            name = existing.Name;
            try { expected = await Task.Run(() => ReadRegistrySnapshot(existing)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Notify($"Cannot read the value: {ex.Message}", true);
                return;
            }
        }
        else
        {
            var named = await Dialogs.PromptAsync(new PromptOptions("Load value data", "Name of the new REG_BINARY value (empty for the default value):")
            {
                Validate = n => RegistryNameError(n, false),
                ConfirmText = "Choose file",
            });
            if (named is null) return;
            name = named.Text;
        }
        if (View.TopLevel is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load value data from a file", AllowMultiple = false });
        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        byte[] data;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > RegistryRaw.EditLimit)
            {
                Notify($"The file is {info.Length:N0} bytes; Registry values are limited to {RegistryRaw.EditLimit:N0} bytes here.", true);
                return;
            }
            data = await Task.Run(() => File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify($"Cannot read the file: {ex.Message}", true);
            return;
        }
        uint type = expected?.Type ?? 3;
        string label = name.Length == 0 ? "(Default)" : name;
        if (!await Dialogs.ConfirmAsync("Load value data",
                $"Store the {data.Length:N0} bytes of {Path.GetFileName(path)} as the data of {label} ({RegistryValueCodec.TypeName(type)}) in {Services.Providers.Display(key)}?" +
                (expected is null ? " A new value is created." : $" This replaces its {expected.Data.Length:N0} bytes; FileCat checks they are unchanged first, and Undo restores them.") +
                (data.Length > 1024 * 1024 ? " Values over 1 MB slow down Windows; programs usually keep such data in files." : ""),
                expected is null ? "Create value" : "Replace data"))
            return;
        SubmitRegistry(new RegistryChange(RegistryAction.SetValue, key, name, expected, new RegistryValueSnapshot(type, data)),
            $"Load data into Registry value {label}", existing, name);
    }

    /// <summary>
    /// Switches the explicit WOW64 view while keeping the current path (plan §12.1): the view is part of the location,
    /// never a detour through Wow6432Node. A path missing in the other view opens its nearest existing parent.
    /// </summary>
    private async Task SwitchRegistryViewAsync()
    {
        var tab = ActiveTab;
        var current = tab?.Location;
        if (tab is null || current?.Scheme != Schemes.Registry) return;
        string[] views = ["default", "64", "32"];
        var items = views.Select(v => new ChoiceItem($"{WindowsRegistryProvider.ViewLabel(v)}{((current.Session ?? "default") == v ? " (current)" : "")}",
            v switch
            {
                "64" => "The 64-bit keys, as 64-bit programs see them.",
                "32" => "The 32-bit keys (WOW64), as 32-bit programs see them.",
                _ => "The view of FileCat's own process.",
            })).ToList();
        var pick = await Dialogs.ChooseAsync(new ChoiceOptions("Registry view", items)
        {
            SelectedIndex = Math.Max(0, Array.IndexOf(views, current.Session ?? "default")),
            Hint = "Keys such as HKLM\\SOFTWARE differ between the 32-bit and 64-bit views; the view is shown in the path.",
        });
        if (pick.Index < 0 || views[pick.Index] == (current.Session ?? "default")) return;
        var target = new Location(Schemes.Registry, current.Path, session: views[pick.Index]);
        var existing = await Task.Run(() =>
        {
            for (var probe = target; ; probe = probe.WithPath(probe.Path[..probe.Path.LastIndexOf('\\')]))
            {
                if (probe.Path.Length == 0 || !probe.Path.Contains('\\')) return probe;
                try
                {
                    using (WindowsRegistryProvider.Open(probe, false)) return probe;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            }
        });
        tab.Navigate(existing);
        if (!Equals(existing, target))
            Notify($"{current.Path} does not exist in the {WindowsRegistryProvider.ViewLabel(views[pick.Index])}; opened {existing.Path} instead.");
    }

    /// <summary>
    /// HKCR and HKCC are browsed read-only (plan §12.3); this names the concrete key behind the view (per-user or
    /// machine registration, or the resolved hardware profile) and opens it, so the write target is always visible.
    /// </summary>
    private async Task OpenWritableRegistryAsync()
    {
        var tab = ActiveTab;
        var alias = tab?.Location;
        if (tab is null || alias?.Scheme != Schemes.Registry || !RegistryAliases.IsAliasPath(alias.Path)) return;
        IReadOnlyList<RegistryWriteTarget> targets;
        try { targets = await Task.Run(() => RegistryAliases.WritableTargets(alias)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Notify($"Cannot resolve the key behind {Services.Providers.Display(alias)}: {ex.Message}", true);
            return;
        }
        if (targets.Count == 0)
        {
            Notify("This alias resolves outside the local machine and user hives, so FileCat does not open it for writing.", true);
            return;
        }
        var chosen = targets[0];
        if (targets.Count > 1)
        {
            var items = targets.Select(t => new ChoiceItem($"{t.Label} — {t.Target.Path}", (t.Exists ? "Exists. " : "Does not exist yet. ") + t.Note)).ToList();
            var pick = await Dialogs.ChooseAsync(new ChoiceOptions("Open writable Registry location", items)
            {
                Hint = "HKCR merges these keys; the per-user entry wins where both exist. Choose which one to edit.",
            });
            if (pick.Index < 0) return;
            chosen = targets[pick.Index];
        }
        tab.Navigate(chosen.Existing);
        Notify(chosen.Exists
            ? $"Opened {Services.Providers.Display(chosen.Target)}, the {chosen.Label.ToLowerInvariant()} key behind the merged view."
            : $"{chosen.Target.Path} does not exist yet; opened {chosen.Existing.Path}. Create the missing keys with F7.");
    }

    private async Task OpenRegistryLinkAsync(string target, TabViewModel tab)
    {
        string? path = RegistryAliases.MapNativePath(target);
        if (path is null || !Services.Providers.For(tab.Location!).TryParse(path, tab.Location, out var location) || location is null)
        {
            await Dialogs.AlertAsync("Registry link", $"Target: {target}\n\nThis target cannot be opened as a local Registry location. No target was followed.");
            return;
        }
        if (await Dialogs.ConfirmAsync("Follow Registry link?",
            $"This key is a Registry link to:\n{target}\n\nOpen {Services.Providers.Display(location)} explicitly? Subtree jobs never follow Registry links.", "Open target"))
            tab.Navigate(location);
    }

    private async Task ViewRegistryValueAsync(ItemRef item, bool raw)
    {
        try
        {
            var value = await Task.Run(() =>
            {
                using var key = WindowsRegistryProvider.Open(item.Parent, false);
                return RegistryRaw.Read(key, item.Name);
            });
            if (value.Data.Length != value.Length)
            {
                Notify($"This Registry value is {value.Length:N0} bytes; the editor/viewer limit is {RegistryRaw.EditLimit:N0} bytes.", true);
                return;
            }
            var display = $"{item.Parent.Path}\\{(item.Name.Length == 0 ? "(Default)" : item.Name)}";
            if (raw)
            {
                new ViewerWindow(Services, new MemoryContentSource(display + " · " + value.TypeName, value.Data), display, hex: true).Show();
                return;
            }
            var result = await Dialogs.ShowCustomAsync("Registry value · read only",
                new TextBlock
                {
                    Text = $"{display}\n{value.TypeName} · {value.Length:N0} bytes\n\n{RegistryRaw.Preview(value)}\n\nStored data is shown without expanding variables or executing it.",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 640,
                },
                [new DialogButton("Close", "close", IsDefault: true, IsCancel: true), new DialogButton("View raw bytes", "raw")]);
            if (result as string == "raw")
                new ViewerWindow(Services, new MemoryContentSource(display + " · " + value.TypeName, value.Data), display, hex: true).Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Notify($"Cannot read Registry value: {ex.Message}", true);
        }
    }
}
