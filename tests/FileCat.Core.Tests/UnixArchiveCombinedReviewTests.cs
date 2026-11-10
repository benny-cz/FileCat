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
    // Plan §11.1/§11.3: replacements and exclusions are reviewed together in one question, not one each.
    [Fact]
    public async Task One_combined_review_lists_replacements_and_exclusions_together()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string first = Path.Combine(root, "a.txt"); File.WriteAllText(first, "first"); File.SetLastWriteTimeUtc(first, DateTime.UnixEpoch.AddSeconds(1700000000));
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        var backend = UnixArchiveJobBackend.Create(profile.Format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
        var decisions = new List<DecisionRequest>();
        manager.DecisionRequested += pending => { lock (decisions) decisions.Add(pending.Request); pending.Resolve(new Decision(DecisionAction.Proceed)); };
        string target = Path.Combine(root, "saved.tar");
        await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null, [new(ArchiveChangeKind.AddFile, "a.txt", first)], Profile: profile), null));
        Assert.Empty(decisions);

        // An incoming a.txt replaces the stored one, and a folder brings a symbolic link that a TAR here leaves out.
        string incoming = Path.Combine(root, "incoming"); Directory.CreateDirectory(Path.Combine(incoming, "dir"));
        File.WriteAllText(Path.Combine(incoming, "a.txt"), "second"); File.SetLastWriteTimeUtc(Path.Combine(incoming, "a.txt"), DateTime.UnixEpoch.AddSeconds(1700000001));
        File.WriteAllText(Path.Combine(incoming, "dir", "kept.txt"), "kept"); File.SetLastWriteTimeUtc(Path.Combine(incoming, "dir", "kept.txt"), DateTime.UnixEpoch.AddSeconds(1700000002));
        File.CreateSymbolicLink(Path.Combine(incoming, "dir", "link"), "kept.txt");
        var assessor = new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var job = manager.SubmitArchive(reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "a.txt", Path.Combine(incoming, "a.txt")),
            new(ArchiveChangeKind.AddFolder, "dir", Path.Combine(incoming, "dir"))]), null);
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(120));
            while (!job.State.IsFinished()) await Task.Delay(10, deadline.Token);
        }
        // The accepted exclusion is a skipped item: the job completes with issues and its summary says what was left out.
        Assert.True(job.State == JobState.CompletedWithIssues, job.State + ": " + string.Join(" | ", job.Issues.Select(issue => issue.Message)));
        Assert.Equal(1, job.ItemsSkipped);
        Assert.StartsWith("Archive updated: 2 requested changes; ", job.Summary); Assert.EndsWith(" verified and saved; 1 unsupported source item excluded.", job.Summary);
        var review = Assert.Single(decisions);
        Assert.Equal("Review this archive change", review.Title);
        Assert.Contains("Replace 1 existing entry under 1 name.", review.Message);
        Assert.Contains("will be left out of the archive", review.Message);
        Assert.Equal("Replace 1 existing member and continue", Assert.IsType<ConfirmRequest>(review).ProceedLabel);
    }
}
