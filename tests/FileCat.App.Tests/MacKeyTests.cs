using FileCat.App.Services;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class MacKeyTests
{
    [Fact]
    public void Command_stands_for_Ctrl_on_macOS_and_Command_Q_quits()
    {
        Assert.Equal(new KeyChord("C", KeyMods.Ctrl), KeyMapper.MacCommandAlias(new KeyChord("C", KeyMods.Meta)));
        Assert.Equal(new KeyChord("T", KeyMods.Ctrl | KeyMods.Shift), KeyMapper.MacCommandAlias(new KeyChord("T", KeyMods.Meta | KeyMods.Shift)));
        Assert.Equal(KeyMapper.MacQuit, KeyMapper.MacCommandAlias(new KeyChord("Q", KeyMods.Meta)));
        Assert.Null(KeyMapper.MacCommandAlias(new KeyChord("C", KeyMods.Ctrl | KeyMods.Meta)));
        Assert.Null(KeyMapper.MacCommandAlias(new KeyChord("C", KeyMods.None)));
    }
}
