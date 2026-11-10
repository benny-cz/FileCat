using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    // Debian/Ubuntu/Fedora: umask 002 with a per-user private group makes every folder a user creates 0775.
    [Fact]
    public async Task Native_jobs_write_in_a_user_private_group_folder_and_refuse_a_shared_group_folder()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string folder = Path.Combine(root, "group-writable"); Directory.CreateDirectory(folder);
        string source = Path.Combine(root, "source.txt"); File.WriteAllText(source, "group folder payload");
        File.SetLastWriteTimeUtc(source, DateTime.UnixEpoch.AddSeconds(1700000000));
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
        manager.DecisionRequested += pending => Assert.Fail("No decision is expected: " + pending.Request.Message);

        // The folder's group is the process's primary group (Linux) or inherited from the parent (macOS).
        File.SetUnixFileMode(folder, (UnixFileMode)0x1fd);
        bool privateGroup = OperatingSystem.IsLinux() && PrimaryGroupIsPrivate();
        var created = manager.SubmitArchive(new(Path.Combine(folder, "private.tar"), null, [new(ArchiveChangeKind.AddFile, "source.txt", source)], Profile: profile), null);
        await FinishJob(created, ct);
        Assert.Equal(privateGroup ? JobState.Completed : JobState.Failed, created.State);
        Assert.Equal(privateGroup, File.Exists(Path.Combine(folder, "private.tar")));
        Assert.Empty(Directory.GetDirectories(folder, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        if (!privateGroup) Assert.Contains(created.Issues, issue => issue.Message.Contains("no other user or shared group", StringComparison.Ordinal));
        else
        {
            // An update in the same folder goes through the private exchange and keeps the archive's own mode.
            var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
            var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(Path.Combine(folder, "private.tar"), profile, null, ct);
            var updated = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.CreateFolderEntry, "later/")]), null);
            await FinishJob(updated, ct);
            Assert.True(updated.State == JobState.Completed, string.Join(" | ", updated.Issues.Select(issue => issue.Message)));
            Assert.Empty(Directory.GetDirectories(folder, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        }

        // A group the user shares with others never qualifies, whatever the platform.
        if (SharedSupplementaryGroup() is not { } shared) return;
        Assert.Equal(0, Chown(folder, uint.MaxValue, shared)); File.SetUnixFileMode(folder, (UnixFileMode)0x1fd);
        var refused = manager.SubmitArchive(new(Path.Combine(folder, "shared.tar"), null, [new(ArchiveChangeKind.AddFile, "source.txt", source)], Profile: profile), null);
        await FinishJob(refused, ct);
        Assert.Equal(JobState.Failed, refused.State);
        Assert.False(File.Exists(Path.Combine(folder, "shared.tar")));
        Assert.Empty(Directory.GetDirectories(folder, ArchiveExchangeUpdateFacts.PrivateDirectoryPrefix + "*"));
        Assert.Contains(refused.Issues, issue => issue.Message.Contains("no other user or shared group", StringComparison.Ordinal));
    }

    private static async Task FinishJob(Job job, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(120));
        while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
    }

    private static bool PrimaryGroupIsPrivate()
    {
        // getent prints name:password:gid:members. Private: named after the user, no other listed member.
        string user = Environment.UserName, gid = RunOutput("id", "-g").Trim();
        string[] fields = RunOutput("getent", "group", gid).Trim().Split(':');
        return fields.Length == 4 && fields[0] == user && fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries).All(member => member == user);
    }

    private static uint? SharedSupplementaryGroup()
    {
        uint primary = uint.Parse(RunOutput("id", "-g").Trim(), System.Globalization.CultureInfo.InvariantCulture);
        foreach (string text in RunOutput("id", "-G").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (uint.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out uint gid) && gid != primary && gid != 0)
                return gid;
        return null;
    }

    private static string RunOutput(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; string output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
        return output;
    }

}
