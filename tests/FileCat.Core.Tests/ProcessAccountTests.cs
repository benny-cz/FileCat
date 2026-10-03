using FileCat.Core.Platform;

namespace FileCat.Core.Tests;

/// <summary>The window title says who FileCat runs as and with which rights.</summary>
public sealed class ProcessAccountTests
{
    [Fact]
    public void The_title_names_the_place_the_account_and_its_rights()
    {
        var standard = new ProcessAccount("marek", AccountRights.Standard);
        var administrator = new ProcessAccount("marek", AccountRights.AdministratorNotElevated);
        var elevated = new ProcessAccount("marek", AccountRights.Elevated);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("FileCat — Downloads — marek (standard user)", standard.WindowTitle("Downloads"));
            Assert.Equal("FileCat — Downloads — marek (administrator, not elevated)", administrator.WindowTitle("Downloads"));
            Assert.Equal("FileCat — Downloads — marek (elevated)", elevated.WindowTitle("Downloads"));
            Assert.Equal("FileCat — CORP\\jane (standard user)", new ProcessAccount("CORP\\jane", AccountRights.Standard).WindowTitle(null));
        }
        else
        {
            // "root" says it on Linux and macOS; sudo names who started it.
            Assert.Equal("Downloads — FileCat — marek", standard.WindowTitle("Downloads"));
            Assert.Equal("Downloads — FileCat — root", new ProcessAccount("root", AccountRights.Elevated).WindowTitle("Downloads"));
            Assert.Equal("FileCat — root (sudo from marek)", new ProcessAccount("root", AccountRights.Elevated, "marek").WindowTitle(null));
        }
    }

    [Fact]
    public void The_environment_gives_the_user_and_whether_the_process_is_privileged()
    {
        var account = ProcessAccount.FromEnvironment();
        Assert.Equal(Environment.UserName, account.Name);
        Assert.Equal(Environment.IsPrivilegedProcess, account.Rights == AccountRights.Elevated);
    }
}
