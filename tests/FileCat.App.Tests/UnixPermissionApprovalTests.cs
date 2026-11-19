using FileCat.App.Services;
using FileCat.Core.FileSystem;

namespace FileCat.App.Tests;

public sealed class UnixPermissionApprovalTests
{
    [Fact]
    public void Scoped_helper_changes_only_the_reviewed_mode_and_rejects_a_link_replacement()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || !Environment.IsPrivilegedProcess)
            Assert.Skip("Run this native check as root on Linux or macOS.");
        var root = Directory.CreateTempSubdirectory("filecat-permission-approval-");
        try
        {
            string target = Path.Combine(root.FullName, "reviewed ' file");
            string outside = Path.Combine(root.FullName, "other file");
            string folder = Directory.CreateDirectory(Path.Combine(root.FullName, "reviewed folder")).FullName;
            string nested = Path.Combine(folder, "unselected.txt");
            File.WriteAllText(target, "selected");
            File.WriteAllText(outside, "untouched");
            File.WriteAllText(nested, "untouched");
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(outside, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(nested, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var agreed = UnixPermissionApproval.Capture([target, folder], UnixFileMode.GroupRead, UnixFileMode.None);
            Assert.Equal(0, Run(agreed));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(target));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead, File.GetUnixFileMode(folder));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(nested));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(outside));

            var stale = UnixPermissionApproval.Capture([target], UnixFileMode.OtherRead, UnixFileMode.None);
            File.Delete(target);
            File.CreateSymbolicLink(target, outside);
            Assert.Equal(0, Run(stale)); // a structured refusal is returned without changing the link target
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(outside));
            Assert.True(UnixFiles.Stat(target)!.Value.IsLink);
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public void Ordinary_helper_invocation_refuses_and_system_launcher_carries_only_the_mode_request()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || Environment.IsPrivilegedProcess)
            Assert.Skip("Run this check as an ordinary Linux or macOS user.");
        Assert.Equal(2, Run([]));
        var launcher = UnixPermissionApproval.StartInfo(UnixPermissionApproval.Encode([]));
        Assert.Equal(OperatingSystem.IsLinux() ? "/usr/bin/pkexec" : "/usr/bin/osascript", launcher.FileName);
        Assert.False(launcher.UseShellExecute);
        Assert.Contains(UnixPermissionApproval.Argument, launcher.ArgumentList);
    }

    private static int Run(UnixPermissionApproval.Entry[] entries) =>
        UnixPermissionApproval.RunHelper([UnixPermissionApproval.Argument, UnixPermissionApproval.Encode(entries)]);
}
