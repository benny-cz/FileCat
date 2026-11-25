using System.Diagnostics;

namespace FileCat.App.Tests;

/// <summary>
/// I658: the recovery census lists processes through Linux's /proc. Mounted with hidepid, /proc hides other accounts'
/// processes from an ordinary user (invisible, ptraceable) or withholds them (noaccess, which .NET then leaves out of its
/// list); a PID namespace of its own shows nothing outside it. Then a census that finds no FileCat does not know that
/// none runs: it is unknown, and the root census decides. A census that may inspect every process is unaffected.
/// </summary>
public sealed class RecoveryProcessListingTests
{
    private const string SystemPid = "pid:[4026531836]", SystemUser = "user:[4026531837]";
    private const string Root = "21 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw,errors=remount-ro\n";
    private const string User = "Name:\tFileCat\nUmask:\t0002\nUid:\t1000\t1000\t1000\t1000\nGid:\t1000\t1000\t1000\t1000\n" +
                                "Groups:\t4 24 27 1000 \nCapInh:\t0000000000000000\nCapEff:\t0000000000000000\n";
    private const string Administrator = "Name:\tFileCat\nUid:\t0\t0\t0\t0\nGid:\t0\t0\t0\t0\nGroups:\t0 \nCapEff:\t000001ffffffffff\n";

    private static string Proc(string superOptions, string optional = "shared:12") =>
        Root + $"23 21 0:22 / /proc rw,nosuid,nodev,noexec,relatime {optional} - proc proc {superOptions}\n" +
        "24 23 0:23 / /proc/sys/fs/binfmt_misc rw,relatime shared:13 - autofs systemd-1 rw,fd=32\n";

    [Theory]
    [InlineData("rw")]
    [InlineData("rw,hidepid=off")]
    [InlineData("rw,hidepid=0")]
    [InlineData("rw,subset=pid")]
    public void A_proc_that_hides_nothing_lists_every_process(string options) =>
        Assert.Null(SingleInstance.LinuxProcessListingLimit(Proc(options), User, SystemPid, SystemUser));

    [Theory]
    [InlineData("rw,hidepid=invisible", "hidepid=invisible")]
    [InlineData("rw,hidepid=2", "hidepid=invisible")] // Linux before 5.8 prints the number
    [InlineData("rw,hidepid=ptraceable", "hidepid=ptraceable")]
    [InlineData("rw,hidepid=4", "hidepid=ptraceable")]
    [InlineData("rw,hidepid=noaccess", "hidepid=noaccess")]
    [InlineData("rw,hidepid=1", "hidepid=noaccess")]
    [InlineData("rw,gid=999,hidepid=invisible", "hidepid=invisible")] // a group this user is not in
    public void A_proc_that_hides_other_accounts_processes_limits_an_ordinary_census(string options, string reason) =>
        Assert.Contains(reason, SingleInstance.LinuxProcessListingLimit(Proc(options), User, SystemPid, SystemUser));

    [Theory]
    [InlineData("rw,gid=27,hidepid=invisible")] // a supplementary group
    [InlineData("rw,hidepid=invisible,gid=1000")] // the file-system group
    [InlineData("rw,gid=4,hidepid=noaccess")]
    public void Its_gid_group_sees_every_process(string options) =>
        Assert.Null(SingleInstance.LinuxProcessListingLimit(Proc(options), User, SystemPid, SystemUser));

    [Fact]
    public void Its_gid_group_does_not_exempt_from_ptraceable_or_in_another_user_namespace()
    {
        Assert.Contains("hidepid=ptraceable",
            SingleInstance.LinuxProcessListingLimit(Proc("rw,gid=27,hidepid=ptraceable"), User, SystemPid, SystemUser));
        // In another user namespace, gid= (printed in the system's numbering) and this process's groups may differ.
        Assert.Contains("hidepid=invisible",
            SingleInstance.LinuxProcessListingLimit(Proc("rw,gid=27,hidepid=invisible"), User, SystemPid, "user:[4026532511]"));
    }

    [Theory]
    [InlineData("rw,hidepid=invisible")]
    [InlineData("rw,hidepid=noaccess")]
    [InlineData("rw,hidepid=ptraceable")]
    public void A_census_that_may_inspect_every_process_is_unaffected(string options)
    {
        Assert.Null(SingleInstance.LinuxProcessListingLimit(Proc(options), Administrator, SystemPid, SystemUser));
        // CAP_SYS_PTRACE alone, as root has it, decides; not the user ID.
        string traced = User.Replace("CapEff:\t0000000000000000", "CapEff:\t0000000000080000", StringComparison.Ordinal);
        Assert.Null(SingleInstance.LinuxProcessListingLimit(Proc(options), traced, SystemPid, SystemUser));
        // Root of another user namespace holds its capabilities only there.
        Assert.NotNull(SingleInstance.LinuxProcessListingLimit(Proc(options), Administrator, SystemPid, "user:[4026532511]"));
    }

    [Fact]
    public void A_PID_namespace_of_its_own_limits_every_census()
    {
        foreach (string status in new[] { User, Administrator })
            Assert.Contains("PID namespace of its own",
                SingleInstance.LinuxProcessListingLimit(Proc("rw"), status, "pid:[4026532513]", SystemUser));
        Assert.NotNull(SingleInstance.LinuxProcessListingLimit(Proc("rw"), Administrator, null, SystemUser));
    }

    [Fact]
    public void What_cannot_be_read_or_recognized_limits_the_census()
    {
        Assert.NotNull(SingleInstance.LinuxProcessListingLimit(null, User, SystemPid, SystemUser));
        Assert.Contains("no /proc mount", SingleInstance.LinuxProcessListingLimit(Root, User, SystemPid, SystemUser));
        Assert.Contains("not the proc file system", SingleInstance.LinuxProcessListingLimit(
            Root + "23 21 0:22 / /proc rw,relatime shared:12 - tmpfs tmpfs rw\n", Administrator, SystemPid, SystemUser));
        Assert.Contains("does not know", SingleInstance.LinuxProcessListingLimit(Proc("rw,hidepid=8"), User, SystemPid, SystemUser));
        Assert.NotNull(SingleInstance.LinuxProcessListingLimit(Root + "23 21 0:22 / /proc rw shared:12\n", User, SystemPid, SystemUser));
        Assert.NotNull(SingleInstance.LinuxProcessListingLimit(Root + "23 21 0:22 / /proc\n", User, SystemPid, SystemUser));
        Assert.NotNull(SingleInstance.LinuxProcessListingLimit(Root + "23 21 0:22 / /proc rw - proc\n", User, SystemPid, SystemUser));
        // Without its status, the census cannot show that it is exempt.
        Assert.NotNull(SingleInstance.LinuxProcessListingLimit(Proc("rw,gid=27,hidepid=invisible"), null, SystemPid, SystemUser));
    }

    [Theory]
    [InlineData("")]
    [InlineData("shared:12 master:3")]
    public void Optional_fields_and_stacked_mounts_are_read(string optional)
    {
        Assert.Null(SingleInstance.LinuxProcessListingLimit(Proc("rw", optional), User, SystemPid, SystemUser));
        // A second proc mounted over the first: the one on top is what /proc shows, and every one must list all.
        string stacked = Proc("rw", optional) + "31 23 0:41 / /proc rw,relatime - proc proc rw,hidepid=invisible\n";
        Assert.Contains("hidepid=invisible", SingleInstance.LinuxProcessListingLimit(stacked, User, SystemPid, SystemUser));
    }

    [Fact]
    public void A_census_that_finds_no_FileCat_is_unknown_when_its_listing_is_limited()
    {
        var reasons = new List<string>();
        Assert.Null(SingleInstance.OtherFileCatRunning(() => [], diagnostic: (pid, name, reason) => reasons.Add($"{pid} {name}: {reason}"),
            listingLimit: () => "hidden"));
        Assert.Equal(["0 /proc: hidden"], reasons);
        Assert.False(SingleInstance.OtherFileCatRunning(() => [], listingLimit: () => null));
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("ComSpec")! : "/bin/sh")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
        };
        if (OperatingSystem.IsWindows()) { start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("set /p filecat_test_wait="); }
        else { start.ArgumentList.Add("-c"); start.ArgumentList.Add("read filecat_test_wait"); }
        using var child = Process.Start(start)!;
        try
        {
            // A FileCat the census can see still answers; one it cannot tell is still unknown.
            Assert.True(SingleInstance.OtherFileCatRunning(() => [Process.GetProcessById(child.Id)], _ => true, listingLimit: () => "hidden"));
            Assert.Null(SingleInstance.OtherFileCatRunning(() => [Process.GetProcessById(child.Id)], _ => null, listingLimit: () => null));
            Assert.Null(SingleInstance.OtherFileCatRunning(() => [Process.GetProcessById(child.Id)], _ => false, listingLimit: () => "hidden"));
            Assert.False(SingleInstance.OtherFileCatRunning(() => [Process.GetProcessById(child.Id)], _ => false, listingLimit: () => null));
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.WaitForExit(5000)) { child.Kill(); child.WaitForExit(); }
        }
    }

    // On the machine the tests run on: when this process cannot read process 1's /proc entry, which every PID namespace
    // has, /proc hides or withholds processes from it and the check must say so; when it can, in the system's PID
    // namespace, the check must find nothing (a false limit would leave every census, the root census too, unknown).
    [Fact]
    public void The_check_agrees_with_what_this_process_can_read_of_process_1()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux /proc."); return; }
        string? limit = SingleInstance.LinuxProcessListingLimit();
        bool readsInit;
        try { readsInit = File.ReadAllText("/proc/1/stat").StartsWith("1 (", StringComparison.Ordinal); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { readsInit = false; }
        bool listsInit;
        var listed = Process.GetProcesses();
        try { listsInit = listed.Any(process => process.Id == 1); }
        finally { foreach (var process in listed) process.Dispose(); }
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Process 1 read: {readsInit}; listed: {listsInit}; limit: {limit ?? "none"}; PID namespace: {new FileInfo("/proc/self/ns/pid").LinkTarget}");
        if (!readsInit || !listsInit) Assert.NotNull(limit);
        else if (new FileInfo("/proc/self/ns/pid").LinkTarget == SystemPid) Assert.Null(limit);
    }
}
