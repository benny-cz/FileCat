using System.Diagnostics;
using System.Globalization;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed partial class SftpPermissionAdmissionTests
{
    [Theory]
    [InlineData("group-only")]
    [InlineData("group-denied")]
    [InlineData("compound")]
    [InlineData("drift-after-group")]
    [InlineData("reconnect-after-group")]
    [InlineData("clear-before-group")]
    [InlineData("explicit-set-id-restore")]
    [InlineData("keep-set-id")]
    [InlineData("lose-successful-group-reply")]
    [InlineData("lose-denied-group-reply")]
    public async Task The_SFTP_group_job_verifies_each_native_stage(string scenario)
    {
        bool losesReply = scenario is "lose-successful-group-reply" or "lose-denied-group-reply";
        using var fixture = new Fixture(dropGroupReply: losesReply);
        string path = Path.Combine(fixture.Server.Files, "group witness");
        File.WriteAllText(path, "witness");
        bool existingSetId = scenario is "clear-before-group" or "keep-set-id" or "explicit-set-id-restore";
        bool denied = scenario is "group-denied" or "lose-denied-group-reply";
        const ushort Initial = 0x1a0, Restricted = 0x180, Granted = 0x190, ExplicitSetGroupId = 0x400;
        File.SetUnixFileMode(path, (UnixFileMode)(Initial | (existingSetId ? ExplicitSetGroupId : 0)));
        var original = StatGroupMode(path);
        var groupIds = ReadTestProcess("/usr/bin/id", "-G").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => uint.Parse(id, CultureInfo.InvariantCulture)).ToArray();
        uint wantedGroup = denied ? 0 : groupIds.FirstOrDefault(group => group != original.Group);
        if (wantedGroup == 0 && (!denied || groupIds.Contains(0u)))
            Assert.Skip("Requires a supplementary group and an independently denied group for the ordinary fixture user.");
        var subject = fixture.Subject(fixture.Server.Files, Path.GetFileName(path));
        var item = new ItemRef(subject.OriginalLocation, subject.RawLocator, EntryKind.File);
        var change = new AssignPermissionPrincipal(false,
            new Principal(PrincipalKind.UnixGroup, wantedGroup.ToString(CultureInfo.InvariantCulture), fixture.Profile.Id));
        var draft = new PermissionDraft(PermissionScope.Objects, [change]);
        if (scenario is "compound" or "drift-after-group" or "reconnect-after-group" or "explicit-set-id-restore" or "lose-successful-group-reply")
            draft = draft with { Changes = draft.Changes.Add(new ChangePermissionMode(Granted | ExplicitSetGroupId, 0xfff)) };
        if (scenario == "clear-before-group")
            draft = draft with { Changes = draft.Changes.Add(new ChangePermissionMode(0, ExplicitSetGroupId)) };
        if (scenario == "keep-set-id")
        {
            var refusal = Assert.Throws<RefusedOperationException>(() =>
                fixture.Provider.PrepareSftpPermissionOperation(subject, item, draft, Cancellation));
            Assert.Contains("Review the existing set-ID bits explicitly", refusal.Message);
            Assert.Equal(original, StatGroupMode(path));
            return;
        }
        var modified = File.GetLastWriteTimeUtc(path);
        using var review = fixture.Provider.PrepareSftpPermissionOperation(subject, item, draft, Cancellation);
        var stages = new List<(uint User, uint Group, ushort Mode)>();
        string history = Directory.CreateTempSubdirectory("sftp-group-history-").FullName;
        try
        {
            var registry = new ProviderRegistry();
            registry.Register(fixture.Provider);
            var manager = new JobManager(new PortableFileOperations(), registry, history, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, ct) =>
                {
                    var file = UnixAnalysisPrivateDirectory.CreatePermissionJournal(history, leaf, ct);
                    return file with { Verify = () =>
                    {
                        file.Verify();
                        byte[] bytes = new byte[8192];
                        int read = RandomAccess.Read(file.Stream.SafeFileHandle, bytes, 0);
                        var outcomes = Encoding.UTF8.GetString(bytes, 0, read).Split('\n')
                            .Count(line => line.Contains("\"t\":\"permission-outcome\"", StringComparison.Ordinal));
                        if (outcomes <= stages.Count) return;
                        stages.Add(StatGroupMode(path));
                        if (outcomes != 2) return;
                        if (scenario == "drift-after-group") File.SetUnixFileMode(path, (UnixFileMode)(Restricted | 4));
                        if (scenario == "reconnect-after-group") fixture.Connector.Last!.Dispose();
                    } };
                });
            var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.JobFinished += job => done.TrySetResult(job);
            var job = manager.SubmitPermissions(review);
            Assert.Same(job, await done.Task.WaitAsync(TimeSpan.FromSeconds(20), Cancellation));
            var result = Assert.Single(job.PermissionResults);
            Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
            if (losesReply)
            {
                Assert.Equal(2, fixture.Connector.Connections);
                // These lines are the forwarded lsetstat fields and the server's actual status code.
                var trace = File.ReadAllLines(Path.Combine(fixture.Server.Directory, "group-reply.trace"));
                if (denied)
                {
                    Assert.Equal(PermissionVerdict.Uncertain, result.Verdict);
                    Assert.Equal(original, StatGroupMode(path));
                    var status = Assert.Single(trace).Split(' ');
                    Assert.Equal(new[] { "2", "4294967295", "0" }, status[..3]);
                    Assert.NotEqual("0", status[3]);
                    var step = Assert.Single(result.Components);
                    Assert.Equal(PermissionVerification.ServerPath, step.Verification);
                    Assert.Contains("connection was lost", step.Detail);
                }
                else
                {
                    Assert.Equal(PermissionVerdict.Partial, result.Verdict);
                    Assert.Equal((original.User, wantedGroup, Restricted), StatGroupMode(path));
                    Assert.Equal(new[] { "4 33152 0", $"2 4294967295 {wantedGroup} 0" }, trace);
                    Assert.Equal(PermissionVerdict.Verified, result.Components[1].Verdict);
                    Assert.Equal(PermissionVerification.ServerPath, result.Components[1].Verification);
                    Assert.Contains("connection was lost", result.Components[1].Detail);
                    Assert.Equal(PermissionVerdict.Conflict, result.Components[^1].Verdict);
                }
                return;
            }
            if (scenario == "group-denied")
            {
                Assert.Equal(PermissionVerdict.FailedBeforeChange, result.Verdict);
                var failure = Assert.Single(result.Components);
                Assert.NotNull(failure.NativeError);
                Assert.Contains("server could not apply this group change", failure.Detail);
                Assert.Equal(original, Assert.Single(stages));
                Assert.Equal(original, StatGroupMode(path));
                return;
            }
            if (scenario is "drift-after-group" or "reconnect-after-group")
            {
                Assert.Equal(PermissionVerdict.Partial, result.Verdict);
                Assert.Equal(scenario == "drift-after-group" ? PermissionVerdict.Conflict : PermissionVerdict.FailedBeforeChange,
                    result.Components[^1].Verdict);
                Assert.Equal(new[] { (original.User, original.Group, Restricted), (original.User, wantedGroup, Restricted) }, stages);
                ushort finalMode = (ushort)(Restricted | (scenario == "drift-after-group" ? 4 : 0));
                Assert.Equal((original.User, wantedGroup, finalMode), StatGroupMode(path));
                string receipt = Assert.Single(Directory.EnumerateFiles(Path.Combine(history, PermissionJournalNames.Directory), "*.fcj"));
                var rows = File.ReadAllLines(receipt);
                Assert.Equal(2, rows.Count(line => line.Contains("\"t\":\"permission-intent\"", StringComparison.Ordinal)));
                if (scenario == "reconnect-after-group") Assert.Equal(2, fixture.Connector.Connections);
                return;
            }
            Assert.Equal(PermissionVerdict.Verified, result.Verdict);
            Assert.All(result.Components, step => Assert.Equal(PermissionVerification.ServerPath, step.Verification));
            (uint User, uint Group, ushort Mode)[] expected = scenario switch
            {
                "compound" or "explicit-set-id-restore" => [(original.User, original.Group, Restricted), (original.User, wantedGroup, Restricted),
                    (original.User, wantedGroup, Granted), (original.User, wantedGroup, Granted | ExplicitSetGroupId)],
                "clear-before-group" => [(original.User, original.Group, Initial), (original.User, wantedGroup, Initial)],
                _ => [(original.User, wantedGroup, Initial)]
            };
            Assert.Equal(expected, stages);
            Assert.Equal(expected[^1], StatGroupMode(path));
        }
        finally { Directory.Delete(history, recursive: true); }
    }

    private static (uint User, uint Group, ushort Mode) StatGroupMode(string path)
    {
        var values = ReadTestProcess("/usr/bin/stat", "-c", "%u:%g:%a", "--", path).Split(':');
        return (uint.Parse(values[0], CultureInfo.InvariantCulture), uint.Parse(values[1], CultureInfo.InvariantCulture),
            Convert.ToUInt16(values[2], 8));
    }

    private static string ReadTestProcess(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(5_000));
        Assert.Equal(0, process.ExitCode);
        return output.Trim();
    }
}
