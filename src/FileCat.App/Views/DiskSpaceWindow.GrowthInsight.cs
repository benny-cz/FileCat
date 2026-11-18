using Avalonia.Automation;
using Avalonia.Controls;
using FileCat.App.Controls;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private const string GrowthRecipeQuery = "change:growing AND delta>0";
    private static readonly AnalysisRecipeDefinition GrowthRecipe = new(AnalysisMetadataRecipe.GrowthSinceAnalysis, "growth-since-analysis", 1,
        "Growth since saved analysis", "Show observed increases with known differences in the same recorded scope and measure.",
        "Attribution changes and unknown baselines stay outside this view.");

    private static Border CreateGrowthInsight()
    {
        var action = new Button { Content = "View observed growth" };
        var host = new Border { Child = action };
        AutomationProperties.SetName(host, GrowthRecipe.Title);
        return host;
    }

    private static void SetGrowthInsightAvailability(Border host, AnalysisComparisonCompatibility? compatibility, bool ready)
    {
        bool comparable = compatibility?.ComparableValues == true;
        ((Button)host.Child!).IsEnabled = comparable && ready;
        host.Focusable = !comparable || !ready;
        string detail = !ready ? "Wait for the captured comparison to finish." : !comparable
            ? "Growth needs compatible recorded scopes, policies, and measurement bases. Open comparison details for the reason."
            : GrowthRecipe.Rule + " " + GrowthRecipe.Limitations;
        if (AutomationProperties.GetHelpText(host) == detail) return;
        AutomationProperties.SetHelpText(host, detail);
        AutomationProperties.SetHelpText((Button)host.Child, detail);
        ToolTip.SetTip(host, new RichTip(GrowthRecipe.Title, detail: detail));
    }
}
