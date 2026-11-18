using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsEfsReplacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bound_replacement_preserves_the_exact_destination_when_it_becomes_EFS_protected(bool afterRetirement)
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-bound-efs-target-").FullName;
        try
        {
            var files = new WindowsFileOperations();
            string target = Path.Join(root, "target.bin"), source = Path.Join(root, "incoming.bin");
            byte[] old = [1, 2, 3, 4], incoming = [4, 3, 2, 1];
            File.WriteAllBytes(target, old);
            File.WriteAllBytes(source, incoming);
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            string digest = Convert.ToHexString(SHA256.HashData(old));
            string recovery = Assert.IsType<string>(files.PrepareReviewedReplacement(source, target));
            if (!afterRetirement) File.Encrypt(target);
            bool changed = false;
            var result = files.ReplaceReviewedCore(source, target, identity, digest, old.Length, TestContext.Current.CancellationToken,
                afterRetire: afterRetirement ? () => { File.Encrypt(recovery); changed = true; } : null, recoveryPath: recovery);
            Assert.NotNull(result);
            Assert.False(result.Published, result.Reason);
            Assert.Contains("EFS", result.Reason);
            Assert.Equal(afterRetirement, changed);
            Assert.Null(result.RetainedPath);
            Assert.False(File.Exists(recovery));
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(identity, files.GetFileIdentity(target));
            Assert.Equal(old, File.ReadAllBytes(target));
            Assert.Equal(incoming, File.ReadAllBytes(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    public async Task An_existing_EFS_target_removes_replace_even_with_a_default_replace_policy(JobKind kind)
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-efs-conflict-").FullName;
        try
        {
            string destination = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
            string source = Path.Join(root, "file.bin"), target = Path.Join(destination, "file.bin");
            File.WriteAllBytes(source, [1, 2]);
            File.WriteAllBytes(target, [3, 4]);
            File.Encrypt(target);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var manager = new JobManager(new WindowsFileOperations(), providers, Path.Join(root, "journal"));
            var decision = new TaskCompletionSource<DecisionRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.DecisionRequested += pending => { decision.TrySetResult(pending.Request); pending.Resolve(new(DecisionAction.Skip)); };
            manager.JobFinished += _ => finished.TrySetResult();
            var job = manager.Submit(new JobRequest { Kind = kind, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(destination), Options = new TransferOptions { Conflicts = ConflictPolicy.Replace } });
            try
            {
                await finished.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                var request = Assert.IsType<ConflictRequest>(await decision.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
                Assert.Contains("EFS", request.Title);
                Assert.False(request.CanReplace);
                Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
                Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(target));
                Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(source));
                Assert.Equal(new[] { target }, Directory.GetFiles(destination));
            }
            finally { if (!job.State.IsFinished()) job.Cancel(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void RequireFixture() => Assert.SkipUnless(WindowsStorageReviewEvidence.IsSupportedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
        "Requires a disposable normal-user Windows VM with EFS enabled.");
}
