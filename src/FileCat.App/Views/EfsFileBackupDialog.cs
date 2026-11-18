using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

internal static class EfsFileBackupDialog
{
    internal static async Task<string?> ChooseKeyAsync(IDialogService dialogs, StorageEfsFileBackupReview review,
        IReadOnlyList<bool> hidden, IReadOnlyDictionary<string, StorageEfsFileBackupResult> results, string? lastKey, bool hasOwner)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Text($"{review.Items.Length} selected items. Includes {hidden.Count(value => value)} hidden marks."));
        body.Children.Add(Text("Windows account " + review.InvokerSid));
        body.Children.Add(Text("Keep each backup somewhere you can access without this Windows account or EFS key. Keep its password separately."));
        if (!hasOwner) body.Children.Add(Text("Open the Windows backup wizard from a visible FileCat window."));

        var status = Text("");
        AutomationProperties.SetAutomationId(status, "EFS key status");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var files = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180 };
        AutomationProperties.SetName(files, "Selected items, original locations and EFS certificate associations");
        int previous = -1;
        for (int i = 0; i < review.Keys.Length; i++)
            if (review.Keys[i].CertificateSha1 == lastKey) { previous = i; break; }
        var choose = new ComboBox
        {
            ItemsSource = review.Keys.Select(key =>
                (key.Subject is { Length: > 0 } subject ? Formatters.SafeName(subject) + " · " : "") + key.CertificateSha1 +
                (results.TryGetValue(key.CertificateSha1, out var result) && result.WindowsReportedExport ? " · Export completed" : "")).ToArray(),
            PlaceholderText = "Choose a certificate",
            SelectedIndex = previous,
        };
        AutomationProperties.SetName(choose, "Selected file EFS certificate");
        if (!review.Keys.IsEmpty) body.Children.Add(choose);
        body.Children.Add(status);
        body.Children.Add(new Expander { Header = $"Selected items ({review.Items.Length})", Content = files, IsExpanded = true });

        var backupLabel = new DialogButtonPresentation(new("Back up selected key…"));
        StorageEfsFileBackupKey? Selected() => (uint)choose.SelectedIndex < (uint)review.Keys.Length ? review.Keys[choose.SelectedIndex] : null;
        bool CanBackUp() => hasOwner && Selected() is { State: StorageEfsKeyState.MatchedAvailable };
        void Render()
        {
            var selected = Selected();
            StorageEfsFileBackupResult? result = null;
            if (selected is not null) results.TryGetValue(selected.CertificateSha1, out result);
            status.Text = review.Keys.IsEmpty ? "No EFS certificate for this account was found on the selected files."
                : selected is null ? "Choose the key to export. Files can use different EFS keys."
                : selected.Problem?.Message ?? (result?.WindowsReportedExport == true
                    ? $"Windows reported private-key export completed. {result.CoveredItems.Length} of the {review.Items.Length} selected items still matched this key when checked."
                    : result?.Canceled == true ? "Windows canceled this key export."
                    : result?.Problem?.Message ?? "Windows will ask where to save the backup and how to protect it.");
            if (result is { WindowsReportedExport: true, Problem: not null }) status.Text += " " + result.Problem.Message;
            files.Text = string.Join("\n\n", review.Items.Select((item, index) =>
            {
                string state = item.Problem?.Message ?? (selected is null
                    ? $"Associated keys for this account ({item.Certificates.Length})"
                    : result?.CoveredItems.Contains(index) == true ? "Matched the exported key when checked"
                    : item.Certificates.Contains(selected.CertificateSha1, StringComparer.Ordinal) ? "Associated with the selected key in this review"
                    : "Uses a different key");
                return JsonSerializer.Serialize(item.Target.Path) + (hidden[index] ? " · Hidden mark" : "") + "\n" + state;
            }));
            backupLabel.Update(new("Back up selected key…", "Back up the selected EFS certificate and private key"));
        }

        choose.SelectionChanged += (_, _) => Render();
        Render();
        var answer = await dialogs.ShowCustomAsync("Back up EFS key…", body,
            [new DialogButton("Back up selected key…", true) { IsAvailable = CanBackUp, Validate = CanBackUp, Presentation = backupLabel },
             new DialogButton("Done", false, IsDefault: true, IsCancel: true)],
            initialFocus: review.Keys.IsEmpty ? files : choose, resizable: true,
            behavior: new DialogBehavior { KeepOperationsUsable = true });
        return answer is true && CanBackUp() ? Selected()!.CertificateSha1 : null;
    }

    private static TextBlock Text(string content) => new() { Text = content, TextWrapping = TextWrapping.Wrap };
}
