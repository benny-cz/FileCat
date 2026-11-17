using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisInventoryInputPathTests
{
    [Theory]
    [InlineData(".FCDSA", false)] [InlineData(".FcDsA", false)] [InlineData(".fCdSa", false)]
    [InlineData(".FCDSC", true)] [InlineData(".FcDsC", true)] [InlineData(".fCdSc", true)]
    public void External_suffix_case_does_not_change_the_chosen_path(string extension, bool comparison)
    {
        string path = Path.Combine(Path.GetTempPath(), "MiXeD-folder", "Chosen-Name" + extension);
        Assert.Equal(path, AnalysisInventoryInput.ValidateChosenPath(path, comparison));
    }
    [Theory]
    [InlineData(".FCDSA.tmp", false)] [InlineData(".FCDSC.tmp", true)]
    [InlineData(".FCDSC", false)] [InlineData(".FCDSA", true)]
    public void External_case_acceptance_keeps_the_format_boundary(string extension, bool comparison) =>
        Assert.Throws<ArgumentException>(() => AnalysisInventoryInput.ValidateChosenPath(Path.Combine(Path.GetTempPath(), "Chosen" + extension), comparison));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void External_case_acceptance_keeps_parent_traversal_refused(bool comparison)
    {
        string path = Path.Combine(Path.GetTempPath(), "folder", "..", comparison ? "Chosen.FCDSC" : "Chosen.FCDSA");
        Assert.Throws<ArgumentException>(() => AnalysisInventoryInput.ValidateChosenPath(path, comparison));
    }
}
