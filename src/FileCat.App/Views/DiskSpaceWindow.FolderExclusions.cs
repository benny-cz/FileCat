using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private readonly Expander _folderExclusionSetup = new() { Header = "Exclude folders…" };
        private readonly StackPanel _folderExclusionRows = new() { Spacing = 6 };
        private readonly Dictionary<TextBox, (StackPanel Row, TextBlock Label, TextBox Paths)> _folderExclusionEditors = [];

        private void AddFolderExclusionSetup()
        {
            _folderExclusionRows.Children.Insert(0, new TextBlock
            {
                Text = "Enter one folder path per line, relative to its location. Use exact spelling and / between folders. Excluded folders and their contents are outside the scan totals.",
                TextWrapping = TextWrapping.Wrap
            });
            _folderExclusionSetup.Content = _folderExclusionRows;
            _setup.Children.Add(_folderExclusionSetup);
        }

        private void AddFolderExclusionEditor(TextBox location)
        {
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var paths = new TextBox
            {
                AcceptsReturn = true, MaxHeight = 105, PlaceholderText = "Projects/Cache",
                MaxLength = AnalysisFolderExclusions.Maximum * (AnalysisFolderExclusions.PathCharacters + 1)
            };
            var row = new StackPanel { Spacing = 3, Children = { label, paths } };
            _folderExclusionRows.Children.Add(row);
            _folderExclusionEditors.Add(location, (row, label, paths));
            location.TextChanged += (_, _) => NameFolderExclusionEditor(location, _rootEditors.IndexOf(location));
            paths.TextChanged += (_, _) => RefreshFastNtfsSetup();
        }

        private void RemoveFolderExclusionEditor(TextBox location)
        {
            if (_folderExclusionEditors.Remove(location, out var editor)) _folderExclusionRows.Children.Remove(editor.Row);
            RefreshFastNtfsSetup();
        }

        private void NameFolderExclusionEditor(TextBox location, int index)
        {
            if (!_folderExclusionEditors.TryGetValue(location, out var editor)) return;
            string name = $"Excluded folders for location {index + 1}";
            editor.Label.Text = string.IsNullOrEmpty(location.Text) ? name : $"{name} ({AnalysisNames.Display(location.Text, 90, true)})";
            AutomationProperties.SetName(editor.Paths, name);
        }

        private ImmutableArray<AnalysisFolderExclusion> CaptureFolderExclusions()
        {
            var rules = ImmutableArray.CreateBuilder<AnalysisFolderExclusion>();
            for (int i = 0; i < _rootEditors.Count; i++)
            {
                var text = _folderExclusionEditors[_rootEditors[i]].Paths.Text ?? "";
                foreach (string path in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    rules.Add(new(i, path));
                    if (rules.Count > AnalysisFolderExclusions.Maximum) return rules.ToImmutable();
                }
            }
            return rules.ToImmutable();
        }
    }
}
