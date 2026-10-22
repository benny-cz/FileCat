using System.Text.RegularExpressions;

namespace FileCat.App.Tests;

/// <summary>
/// A screen reader speaks a live region's accessible name. With a fixed name, such as "Find match count", the name
/// never changes with the text, so the region was never heard: the viewer's match count, the transfer form's errors,
/// bulk rename's counter errors and others. Live text keeps its text as its name; tests find it by AutomationId.
/// </summary>
public sealed class LiveRegionNamingTests
{
    [Fact]
    public void No_control_that_can_be_live_has_a_fixed_accessible_name()
    {
        if (SourceRoot() is not { } app)
        {
            Assert.Skip("The FileCat.App sources are not beside this test build.");
            return;
        }
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            foreach (var live in Regex.Matches(text, @"SetLiveSetting\(\s*([\w.]+)\s*,").Select(m => m.Groups[1].Value).Distinct())
            {
                if (Regex.IsMatch(text, @"SetName\(\s*" + Regex.Escape(live) + @"\s*,\s*\$?"""))
                    problems.Add($"{Path.GetFileName(file)}: {live}");
            }
        }
        foreach (var file in Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match element in Regex.Matches(File.ReadAllText(file), @"<[^<>]*AutomationProperties\.LiveSetting=""(?!Off)[^<>]*>"))
            {
                // A bound name follows the announced text; a literal one does not.
                if (Regex.IsMatch(element.Value, @"AutomationProperties\.Name=""(?!\{)"))
                    problems.Add($"{Path.GetFileName(file)}: {element.Value[..Math.Min(80, element.Value.Length)]}");
            }
        }
        Assert.Empty(problems);
    }

    private static string? SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var app = Path.Combine(directory.FullName, "src", "FileCat.App");
            if (File.Exists(Path.Combine(app, "FileCat.App.csproj"))) return app;
        }
        return null;
    }
}
