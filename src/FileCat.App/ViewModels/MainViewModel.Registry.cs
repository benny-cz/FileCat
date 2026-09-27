using Avalonia.Controls;
using Avalonia.Media;
using Avalonia;
using Avalonia.Platform.Storage;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;

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
        var desired = await EditRegistryDialogAsync(named.Text, null);
        if (desired is null) return;
        SubmitRegistry(new RegistryChange(RegistryAction.SetValue, loc, named.Text, Desired: desired),
            $"Create Registry value {(named.Text.Length == 0 ? "(Default)" : named.Text)}", focus: named.Text);
    }

    private async Task EditRegistryValueAsync(ItemRef item)
    {
        try
        {
            var original = await Task.Run(() => ReadRegistrySnapshot(item));
            var desired = await EditRegistryDialogAsync(item.Name, original);
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

    private async Task<RegistryValueSnapshot?> EditRegistryDialogAsync(string name, RegistryValueSnapshot? original)
    {
        var current = original is null ? null : new RegistryValueData(original.Type, original.Data, original.Data.Length);
        bool rawOnly = false;
        string initial = current is null ? "" : RegistryValueCodec.Format(current, out rawOnly);
        var types = RegistryValueCodec.EditableTypes.Concat(current is null ? [] : [current.Type]).Distinct().ToArray();
        var names = types.Select(RegistryValueCodec.TypeName).ToArray();
        var type = new ComboBox { ItemsSource = names, SelectedIndex = current is null ? 0 : Array.IndexOf(types, current.Type), MinWidth = 180 };
        Avalonia.Automation.AutomationProperties.SetName(type, "Registry value type");
        var input = new TextBox { Text = initial, MinWidth = 500, MinHeight = 80, MaxHeight = 280,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "Stored data" };
        Avalonia.Automation.AutomationProperties.SetName(input, "Registry value data");
        var reinterpret = new CheckBox { Content = "Reinterpret original bytes as the selected type", IsVisible = original is not null };
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 650, Classes = { "muted" } };
        var issue = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "error" } };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = $"{(name.Length == 0 ? "(Default)" : name)} · choose a type and edit its stored data. Changes are not applied until you confirm.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(type);
        body.Children.Add(input);
        body.Children.Add(reinterpret);
        body.Children.Add(preview);
        body.Children.Add(issue);
        RegistryValueSnapshot? parsed = null;
        void Refresh()
        {
            uint selected = types[Math.Max(0, type.SelectedIndex)];
            bool retype = original is not null && selected != original.Type && reinterpret.IsChecked == true;
            reinterpret.IsVisible = original is not null && selected != original.Type;
            bool hex = selected == 3 || !RegistryValueCodec.EditableTypes.Contains(selected) || rawOnly && original?.Type == selected;
            input.PlaceholderText = hex ? "Hex bytes, e.g. 00 FF 2A" : selected is 4 or 11 ? "Unsigned decimal or 0x hexadecimal" : selected == 7 ? "One string per line" : "Stored text (not expanded)";
            if (retype) parsed = new RegistryValueSnapshot(selected, original!.Data);
            else if (RegistryValueCodec.TryParse(selected, input.Text ?? "", hex, out var bytes, out var error)) parsed = new RegistryValueSnapshot(selected, bytes);
            else { parsed = null; issue.Text = error; preview.Text = ""; return; }
            issue.Text = "";
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

    private async Task DeleteRegistryAsync()
    {
        if (!TryGetFocusedRegistryItem(out var item)) return;
        if (item.Kind == EntryKind.RegistryKey)
        {
            if (item.Parent.Path.Length == 0) { Notify("Registry roots cannot be deleted.", true); return; }
            RegistryTreeSnapshot scope;
            try
            {
                Notify("Inspecting the Registry subtree before deletion…");
                scope = await Task.Run(() => RegistryTree.Scan(item.Parent.WithPath(item.Parent.Path + "\\" + item.Name)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                Notify($"Cannot inspect the full key subtree: {ex.Message}. Nothing was deleted.", true);
                return;
            }
            if (!await Dialogs.ConfirmAsync("Delete Registry key permanently",
                $"Delete {Services.Providers.Display(item.Parent)}\\{item.Name} and all {scope.KeyCount:N0} keys and {scope.ValueCount:N0} values beneath it? " +
                $"The values contain {scope.DataBytes:N0} bytes. {scope.LinkCount:N0} Registry links will be deleted as links; their targets are never followed. " +
                "This cannot be sent to the Recycle Bin. FileCat checks the captured subtree again before starting and stops on detected changes, but deletion of multiple keys is not atomic.",
                "Delete subtree", danger: true)) return;
            SubmitRegistry(new RegistryChange(RegistryAction.DeleteKey, item.Parent, item.Name,
                TreeDigest: RegistryTree.Digest(scope)), $"Delete Registry subtree {item.Name}", item);
            return;
        }
        var original = await Task.Run(() => ReadRegistrySnapshot(item));
        if (!await Dialogs.ConfirmAsync("Delete Registry value permanently",
            $"Delete {(item.Name.Length == 0 ? "(Default)" : item.Name)} from {Services.Providers.Display(item.Parent)}?\n\n" +
            $"{RegistryValueCodec.TypeName(original.Type)}, {original.Data.Length:N0} bytes. Registry values do not go to the Recycle Bin.",
            "Delete value", danger: true)) return;
        SubmitRegistry(new RegistryChange(RegistryAction.DeleteValue, item.Parent, item.Name, original),
            $"Delete Registry value {(item.Name.Length == 0 ? "(Default)" : item.Name)}", item);
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

    private async Task CopyRegistryValueAsync()
    {
        if (!TryGetFocusedRegistryItem(out var item)) return;
        var target = Workspace.ActiveTarget?.ActiveTab?.Location;
        if (target?.Scheme != Schemes.Registry)
        {
            Notify("Choose a Registry key in the target panel. Export to a file is a separate named command.");
            return;
        }
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

    private async Task OpenRegistryLinkAsync(string target, TabViewModel tab)
    {
        string? path = target.StartsWith(@"\Registry\Machine\", StringComparison.OrdinalIgnoreCase)
            ? "HKLM\\" + target[@"\Registry\Machine\".Length..]
            : target.StartsWith(@"\Registry\User\", StringComparison.OrdinalIgnoreCase)
                ? "HKU\\" + target[@"\Registry\User\".Length..] : null;
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
