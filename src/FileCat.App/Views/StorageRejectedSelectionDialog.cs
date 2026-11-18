using System.Collections.Immutable;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Views;

/// <summary>A complete captured refusal, before any native review or pin allocation.</summary>
internal static class StorageRejectedSelectionDialog
{
    private sealed record Row(string Name, string DisplayLocation, string Locator, string[] Paths, string Provider, bool Hidden, string Reason);

    internal static async Task<bool> ShowIfRejectedAsync(IDialogService dialogs, IReadOnlyList<ItemRef> sources,
        IReadOnlyList<bool> hidden, bool efs = false)
    {
        if (sources.Count is < 1 or > 64 || sources.Count != hidden.Count) throw new ArgumentException("The captured selection is invalid.");
        string feature = efs ? "EFS protection" : "NTFS compression";
        var problems = sources.Select(item => StoragePaths.SourceProblem(item, feature)).ToArray();
        if (problems.All(problem => problem is null)) return false;
        var rows = ImmutableArray.CreateBuilder<Row>(sources.Count);
        long characters = 0;
        for (int i = 0; i < sources.Count; i++)
        {
            var item = sources[i];
            characters += item.Name.Length;
            int depth = 0;
            var paths = new List<string>();
            for (Location? location = item.Parent; location is not null; location = location.Container)
            {
                characters += (long)location.Path.Length + location.Scheme.Length + (location.Session?.Length ?? 0);
                if (++depth > 32 || characters > 2 * 1024 * 1024) throw TooLarge();
                paths.Add(location.Path);
            }
            string display = item.Parent.IsFileSystem ? item.Parent.Path.TrimEnd('\\') + "\\" + item.Name : item.Parent + "/" + item.Name;
            rows.Add(new(item.Name, display, item.Parent.Serialize(), paths.ToArray(), item.Parent.IsFileSystem ? "Local filesystem" : item.Parent.Scheme,
                hidden[i], problems[i]?.Message ?? "Not inspected because another selected item is ineligible. Nothing in this selection can be submitted."));
        }
        var captured = rows.MoveToImmutable();
        // Refuse an oversized complete disclosure; never silently omit or truncate a captured root.
        using (var bound = new MetadataBound()) JsonSerializer.Serialize(bound, captured);
        var search = new TextBox { PlaceholderText = "Find a selected name or original location" };
        var items = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 180 };
        AutomationProperties.SetName(search, "Search selected items by name or original location");
        AutomationProperties.SetName(items, "Selected items and original locations, select text to copy");
        void Render()
        {
            string filter = search.Text ?? string.Empty;
            var matches = captured.Where(row => row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                row.DisplayLocation.Contains(filter, StringComparison.OrdinalIgnoreCase) || row.Paths.Any(path => path.Contains(filter, StringComparison.OrdinalIgnoreCase)));
            items.Text = string.Join("\n\n", matches.Select(row =>
                $"Name: {JsonSerializer.Serialize(row.Name)}\nOriginal location: {JsonSerializer.Serialize(row.DisplayLocation)}\nCaptured locator: {row.Locator}\nProvider: {JsonSerializer.Serialize(row.Provider)}\nHidden mark: {(row.Hidden ? "Yes" : "No")}\n{(efs ? "EFS" : "Compression")}: Not inspected\nEligibility: {JsonSerializer.Serialize(row.Reason)}"));
            if (items.Text.Length == 0) items.Text = "No selected items match this search.";
        }
        search.TextChanged += (_, _) => Render(); Render();
        var disclosure = new Expander { Header = $"Selected items ({captured.Length})", IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch, Content = new StackPanel { Spacing = 6, Children = { search, items } } };
        AutomationProperties.SetName(disclosure, $"All {captured.Length} selected items, including hidden marks");
        var body = new StackPanel { Spacing = 10, Children =
        {
            new TextBlock { Text = $"{captured.Length} selected items. Includes {captured.Count(row => row.Hidden)} marked items hidden by the filter.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Text = $"Some selected items cannot use {feature}. Cancel and change the selection. The original locations below are captured; no selected item was inspected or changed.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            disclosure,
        } };
        await dialogs.ShowCustomAsync(feature + "…", body, [new DialogButton("Cancel", "cancel", IsCancel: true, IsDefault: true)],
            search, resizable: true, behavior: new DialogBehavior { KeepOperationsUsable = true });
        return true;
    }

    private static ArgumentException TooLarge() => new("The complete selection review exceeds its 2 MiB metadata bound. Select fewer items.");
    private sealed class MetadataBound : Stream
    {
        private long _bytes;
        public override void Write(ReadOnlySpan<byte> buffer) { if (buffer.Length > 2 * 1024 * 1024 - _bytes) throw TooLarge(); _bytes += buffer.Length; }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _bytes;
        public override long Position { get => _bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
