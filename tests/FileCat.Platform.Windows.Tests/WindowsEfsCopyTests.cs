using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsEfsCopyTests
{
    [Fact]
    public async Task Native_copy_preserves_efs_on_ntfs_without_a_question()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-copy-").FullName;
        try
        {
            string destination = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
            string source = Encrypt(root, "secret.bin", [9, 4, 1]);
            var (_, questions) = await Run(root, new WindowsFileOperations(), JobKind.Copy, [source], destination, _ => new(DecisionAction.CancelJob));
            Assert.Empty(questions);
            string target = Path.Join(destination, Path.GetFileName(source));
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_staged_clear_copy_reviews_the_temporary_and_final_names()
    {
        RequireEfs();
        string? clearRoot = Environment.GetEnvironmentVariable("FILECAT_STORAGE_EFS_CLEAR_TEST_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(clearRoot) && Directory.Exists(clearRoot),
            "Requires a prepared destination where CopyFile2 cannot preserve EFS.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-copy-stage-").FullName;
        string destination = Path.Join(clearRoot!, "efs-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(destination);
            string previous = Path.Join(destination, "secret.bin");
            File.WriteAllBytes(previous, [1]);
            byte[] payload = [3, 2, 1];
            string source = Encrypt(root, "secret.bin", payload);
            string[] existingAtQuestion = [];
            var (_, questions) = await Run(root, new WindowsFileOperations(), JobKind.Copy, [source], destination, request =>
            {
                if (request is not ConfirmRequest { ConfirmClass: "decrypt" }) return new(DecisionAction.CancelJob);
                existingAtQuestion = Directory.GetFiles(destination);
                return new(DecisionAction.Proceed);
            }, new TransferOptions { Conflicts = ConflictPolicy.KeepBothRenameIncoming });
            Assert.Equal([previous], existingAtQuestion);
            var prompt = Assert.IsType<ConfirmRequest>(Assert.Single(questions));
            string published = Assert.Single(Directory.EnumerateFiles(destination), path => path != previous);
            Assert.Contains("Destination: " + published, prompt.Message);
            string stage = Assert.Single(prompt.Message.Split('\n'), line => line.StartsWith("Temporary copy:", StringComparison.Ordinal));
            Assert.Contains(Path.Join(destination, JournalRecovery.StagedPrefix), stage);
            Assert.DoesNotContain(JournalRecovery.StagedPrefix, published);
            Assert.Equal(payload, File.ReadAllBytes(published));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(previous));
            Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(JobKind.Copy)]
    [InlineData(JobKind.Move)]
    public async Task Copy_and_cross_volume_move_preserve_efs_on_a_capable_fat32_volume(JobKind kind)
    {
        RequireEfs();
        string? fat = Environment.GetEnvironmentVariable("FILECAT_STORAGE_EXPORT_TEST_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(fat) && Directory.Exists(fat), "Requires the prepared FAT32 or exFAT export fixture.");
        Assert.Contains(new DriveInfo(Path.GetPathRoot(fat!)!).DriveFormat, new[] { "FAT32", "exFAT" });
        string root = Directory.CreateTempSubdirectory("filecat-efs-fat-preserve-").FullName;
        string destination = Path.Join(fat!, "efs-preserve-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(destination);
            string capability = Path.Join(destination, "canary.bin");
            File.WriteAllBytes(capability, []);
            bool supported = false;
            try
            {
                File.Encrypt(capability);
                supported = File.GetAttributes(capability).HasFlag(FileAttributes.Encrypted);
            }
            catch (IOException) { }
            File.Delete(capability);
            Assert.SkipUnless(supported, "The prepared non-NTFS volume must support EFS for this preservation fixture.");
            string source = Encrypt(root, "secret.bin", [5, 4, 8]);
            var (_, questions) = await Run(root, new WindowsFileOperations(), kind, [source], destination, _ => new(DecisionAction.CancelJob));
            Assert.Empty(questions);
            string target = Assert.Single(Directory.GetFiles(destination));
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(new byte[] { 5, 4, 8 }, File.ReadAllBytes(target));
            Assert.Equal(kind == JobKind.Copy, File.Exists(source));
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Copy, true)]
    [InlineData(JobKind.Move, false)]
    public async Task A_destination_that_cannot_encrypt_needs_a_fresh_choice_for_each_file(JobKind kind, bool empty)
    {
        RequireEfs();
        string? clearRoot = Environment.GetEnvironmentVariable("FILECAT_STORAGE_EFS_CLEAR_TEST_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(clearRoot) && Directory.Exists(clearRoot),
            "Requires a prepared destination where CopyFile2 cannot preserve EFS.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-clear-copy-").FullName;
        string destination = Path.Join(clearRoot!, "efs-copy-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(destination);
            byte[] expected = empty ? [] : [7, 6, 5];
            string approve = Encrypt(root, "approved.bin", expected);
            string deny = Encrypt(root, "declined.bin", [1, 2, 3, 4]);
            List<string> alreadyWritten = [];
            var (job, questions) = await Run(root, new WindowsFileOperations(), kind, [approve, deny], destination, request =>
            {
                if (request is not ConfirmRequest { ConfirmClass: "decrypt" } confirm) return new(DecisionAction.CancelJob);
                string target = Path.Join(destination, Path.GetFileName(confirm.Path));
                if (File.Exists(target)) alreadyWritten.Add(target);
                return confirm.Path == approve ? new(DecisionAction.Proceed, ApplyToAll: true) : new(DecisionAction.Skip);
            });
            Assert.Empty(alreadyWritten);
            Assert.Equal(2, questions.Length);
            Assert.True(questions.All(request => request is ConfirmRequest),
                string.Join("\n", questions.Select(request => request.Title + " " + request.Message)));
            foreach (var request in questions.Select(Assert.IsType<ConfirmRequest>))
            {
                Assert.Equal("decrypt", request.ConfirmClass);
                Assert.False(request.OffersApplyToAll);
                Assert.Equal(DecisionAction.Skip, request.InitialAction);
                Assert.Contains(request.Path, request.Message);
                Assert.Contains(Path.Join(destination, Path.GetFileName(request.Path)), request.Message);
            }
            string published = Assert.Single(Directory.GetFiles(destination));
            Assert.Equal(Path.Join(destination, "approved.bin"), published);
            Assert.Equal(expected, File.ReadAllBytes(published));
            Assert.False(File.GetAttributes(published).HasFlag(FileAttributes.Encrypted));
            Assert.True(File.GetAttributes(deny).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(deny));
            Assert.Equal(kind == JobKind.Copy, File.Exists(approve));
            Assert.Single(job.Issues, i => i.Message.Contains("Copied decrypted with approval", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Replacing_the_output_folder_during_approval_produces_no_clear_payload()
    {
        RequireEfs();
        string? clearRoot = Environment.GetEnvironmentVariable("FILECAT_STORAGE_EFS_CLEAR_TEST_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(clearRoot) && Directory.Exists(clearRoot),
            "Requires a prepared destination where CopyFile2 cannot preserve EFS.");
        string root = Directory.CreateTempSubdirectory("filecat-efs-copy-parent-").FullName;
        string destination = Path.Join(clearRoot!, "efs-parent-" + Guid.NewGuid().ToString("N"));
        string originalDirectory = destination + "-before";
        try
        {
            Directory.CreateDirectory(destination);
            byte[] payload = [5, 7, 9];
            string source = Encrypt(root, "protected.bin", payload);
            var (_, questions) = await Run(root, new WindowsFileOperations(), JobKind.Move, [source], destination, request =>
            {
                if (request is not ConfirmRequest { ConfirmClass: "decrypt" }) return new(DecisionAction.Skip);
                Directory.Move(destination, originalDirectory);
                Directory.CreateDirectory(destination);
                return new(DecisionAction.Proceed);
            });
            Assert.Equal(2, questions.Length);
            Assert.IsType<ConfirmRequest>(questions[0]);
            Assert.Contains("reviewed folder", Assert.IsType<ErrorRequest>(questions[1]).Message);
            Assert.Empty(Directory.GetFiles(destination));
            Assert.Empty(Directory.GetFiles(originalDirectory));
            Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(payload, File.ReadAllBytes(source));
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            if (Directory.Exists(originalDirectory)) Directory.Delete(originalDirectory, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Copy_then_delete_keeps_the_efs_source_when_the_output_is_decrypted_before_retirement()
    {
        RequireEfs();
        string root = Directory.CreateTempSubdirectory("filecat-efs-retain-").FullName;
        try
        {
            string destination = Directory.CreateDirectory(Path.Join(root, "destination")).FullName;
            string source = Encrypt(root, "moved.bin", [9, 9, 2]);
            string target = Path.Join(destination, "moved.bin");
            var files = new ProtectionChangedAfterCopy(source, destination);
            var (job, questions) = await Run(root, files, JobKind.Move, [source], destination, _ => new(DecisionAction.CancelJob));
            Assert.Empty(questions);
            Assert.True(files.Decrypted);
            Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            Assert.False(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
            var refusal = Assert.Single(job.Issues, issue => issue.Message.Contains("could not be verified as EFS-encrypted", StringComparison.Ordinal));
            Assert.Equal(StepOutcome.PartiallyApplied, refusal.Outcome);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ProtectionChangedAfterCopy(string source, string destination) : WindowsFileOperations
    {
        public bool Decrypted { get; private set; }

        // Select the copy-then-delete route with two NTFS directories so both files can start EFS-encrypted.
        public override string GetVolumeRoot(string path) =>
            PathUtil.IsSameOrUnder(path, destination) ? destination : base.GetVolumeRoot(path);

        public override string? GetFileIdentity(string path)
        {
            string target = Path.Join(destination, Path.GetFileName(source));
            if (!Decrypted && PathUtil.SafetyComparer.Equals(path, source) && File.Exists(target))
            {
                if (!File.GetAttributes(target).HasFlag(FileAttributes.Encrypted))
                    throw new InvalidOperationException("The native copy did not preserve EFS.");
                File.Decrypt(target);
                Decrypted = true;
            }
            return base.GetFileIdentity(path);
        }
    }

    private static async Task<(Job Job, DecisionRequest[] Questions)> Run(string root, WindowsFileOperations files,
        JobKind kind, string[] sources, string destination, Func<DecisionRequest, Decision> decide, TransferOptions? options = null)
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var manager = new JobManager(files, providers, Path.Join(root, "journal"));
        List<DecisionRequest> questions = [];
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.DecisionRequested += pending =>
        {
            questions.Add(pending.Request);
            pending.Resolve(decide(pending.Request));
        };
        manager.JobFinished += _ => finished.TrySetResult();
        var job = manager.Submit(new JobRequest
        {
            Kind = kind,
            Sources = sources.Select(path => ItemRef.ForFileSystemPath(path, EntryKind.File)).ToArray(),
            Destination = Location.FileSystem(destination),
            Options = options ?? new(),
        });
        try
        {
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(25), TestContext.Current.CancellationToken);
            return (job, questions.ToArray());
        }
        finally { if (!job.State.IsFinished()) job.Cancel(); }
    }

    private static string Encrypt(string parent, string name, byte[] bytes)
    {
        string path = Path.Join(parent, name);
        File.WriteAllBytes(path, bytes);
        File.Encrypt(path);
        return path;
    }

    private static void RequireEfs() => Assert.SkipUnless(WindowsStorageReviewEvidence.IsSupportedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
        "Requires a disposable normal-user Windows VM with EFS enabled.");
}
