using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>FCUX-033: the compact path keeps an honest root/gap/leaf map and complete safe inspection.</summary>
public sealed class PathLineElisionTests
{
    [AvaloniaFact]
    public void One_two_and_many_part_paths_fit_the_narrow_widths_with_the_leaf_tail_inspectable()
    {
        string leaf = "very-long-current-folder-" + new string('x', 85) + "-DISTINCT-A";
        string uncRoot = "\\\\very-long-server-name.example\\shared-volume\\";
        string remoteRoot = "sftp://profile/";
        string remoteHome = remoteRoot + "home/";
        var cases = new[]
        {
            (Text: leaf, Ends: new[] { leaf.Length }, Scheme: Schemes.FileSystem),
            (Text: "C:\\" + leaf, Ends: new[] { 3, 3 + leaf.Length }, Scheme: Schemes.FileSystem),
            (Text: "C:\\Users\\someone\\archive\\" + leaf,
                Ends: new[] { 3, 9, 17, 25, 25 + leaf.Length }, Scheme: Schemes.FileSystem),
            (Text: uncRoot + leaf, Ends: new[] { uncRoot.Length, uncRoot.Length + leaf.Length }, Scheme: Schemes.FileSystem),
            (Text: remoteHome + leaf,
                Ends: new[] { remoteRoot.Length, remoteHome.Length, remoteHome.Length + leaf.Length }, Scheme: Schemes.Sftp),
        };
        foreach (var (text, ends, scheme) in cases)
        foreach (int width in new[] { 120, 240, 480 })
        {
            var line = new PathLine { Width = width, Height = 28 };
            var window = new Window { Width = width, Height = 48, Content = line };
            try
            {
                line.SetPath(text, ends.Select(end => new PathLine.Segment(end,
                    new Location(scheme, text[..Math.Min(end, text.Length)], session: scheme == Schemes.Sftp ? "profile" : null))).ToArray());
                window.Show();
                window.CaptureRenderedFrame();
                string shown = line.ShownText;
                Assert.True(line.ShownWidth <= line.Bounds.Width + 0.5,
                    $"{width} DIP: {shown} measured {line.ShownWidth:0.0} in {line.Bounds.Width:0.0}");
                Assert.Contains("…", shown);
                Assert.EndsWith("A", shown);
                Assert.Equal(text, line.PathUpTo(ends.Length - 1));
                Assert.Contains(Formatters.SafeName(text), ToolTip.GetTip(line)?.ToString());
                Assert.Contains(Formatters.SafeName(text), AutomationProperties.GetName(line));
                if (line.GapPoint is { } gap) Assert.Equal(-1, line.SegmentAt(gap));
                if (line.PointOf(0) is { } first) Assert.Equal(0, line.SegmentAt(first));
                if (line.PointOf(ends.Length - 1) is { } last)
                    Assert.Equal(ends.Length - 1, line.SegmentAt(last));
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void Unicode_graphemes_and_escaped_invisible_characters_are_not_cut_into_fragments()
    {
        string text = "C:\\" + new string('p', 60) + "👩‍💻e\u0301" + '\u202E' + '\u0001' + "TAIL🐈";
        var line = new PathLine { Width = 240, Height = 28 };
        var window = new Window { Width = 240, Height = 48, Content = line };
        try
        {
            line.SetPath(text, [new PathLine.Segment(3, Location.FileSystem("C:\\")),
                new PathLine.Segment(text.Length, Location.FileSystem(text))]);
            window.Show();
            foreach (int width in new[] { 240, 480 })
            {
                line.Width = window.Width = width;
                window.CaptureRenderedFrame();
                string shown = line.ShownText;
                Assert.True(line.ShownWidth <= line.Bounds.Width + 0.5);
                Assert.EndsWith("TAIL🐈", shown);
                Assert.DoesNotContain('\u202E', shown);
                Assert.DoesNotContain('\u0001', shown);
                string withoutWholeEscapes = shown.Replace("\\u202E", "", StringComparison.Ordinal)
                    .Replace("\\u0001", "", StringComparison.Ordinal);
                Assert.DoesNotContain("\\u", withoutWholeEscapes);
                for (int i = 0; i < shown.Length; i++)
                {
                    if (char.IsHighSurrogate(shown[i])) Assert.True(i + 1 < shown.Length && char.IsLowSurrogate(shown[i + 1]));
                    if (char.IsLowSurrogate(shown[i])) Assert.True(i > 0 && char.IsHighSurrogate(shown[i - 1]));
                }
                if (width == 480)
                {
                    Assert.Contains("👩‍💻", shown);
                    Assert.Contains("e\u0301", shown);
                }
            }
            Assert.Contains(Formatters.SafeName(text), ToolTip.GetTip(line)?.ToString());
            Assert.Equal(text, line.PathUpTo(1));
        }
        finally { window.Close(); }
    }
}
