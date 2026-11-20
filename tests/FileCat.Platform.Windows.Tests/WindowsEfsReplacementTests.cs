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

    [Theory]
    [InlineData(JobKind.Copy, false)]
    [InlineData(JobKind.Move, false)]
    [InlineData(JobKind.Copy, true)]
    [InlineData(JobKind.Move, true)]
    public async Task Physical_usb_cross_volume_efs_destination_refuses_replace_without_changing_either_file(
        JobKind kind, bool sourceOnUsb)
    {
        RequireFixture();
        string? removableRoot = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_USB_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(removableRoot) && Directory.Exists(removableRoot),
            "Requires a prepared removable NTFS destination owned by the native fixture.");
        string removable = Path.GetFullPath(removableRoot!);
        var drive = new DriveInfo(Path.GetPathRoot(removable)!);
        Assert.Equal(DriveType.Removable, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        string root = Directory.CreateTempSubdirectory("filecat-efs-usb-replace-").FullName;
        Assert.Equal("NTFS", new DriveInfo(Path.GetPathRoot(root)!).DriveFormat);
        string usbFixture = Path.Combine(removable, "filecat-efs-usb-replace-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(removable, Path.GetDirectoryName(usbFixture));
        try
        {
            Directory.CreateDirectory(usbFixture);
            string destination = sourceOnUsb ? Directory.CreateDirectory(Path.Combine(root, "destination")).FullName : usbFixture;
            string source = Path.Combine(sourceOnUsb ? usbFixture : root, "secret.bin");
            string target = Path.Combine(destination, "secret.bin");
            byte[] incoming = [1, 2, 3, 4, 5], existing = [9, 8, 7];
            byte[] incomingNamed = [6, 5, 4], existingNamed = [3, 2, 1];
            File.WriteAllBytes(source, incoming);
            File.WriteAllBytes(source + ":proof", incomingNamed);
            File.Encrypt(source);
            File.WriteAllBytes(target, existing);
            File.WriteAllBytes(target + ":proof", existingNamed);
            File.Encrypt(target);
            var files = new WindowsFileOperations();
            string sourceId = Assert.IsType<string>(files.GetFileIdentity(source));
            string targetId = Assert.IsType<string>(files.GetFileIdentity(target));

            var (_, questions) = await WindowsEfsCopyTests.Run(root, files, kind, [source], destination,
                _ => new(DecisionAction.Skip), new TransferOptions { Conflicts = ConflictPolicy.Replace });
            var conflict = Assert.IsType<ConflictRequest>(Assert.Single(questions));
            Assert.Contains("EFS", conflict.Title);
            Assert.False(conflict.CanReplace);
            Assert.Equal(sourceId, files.GetFileIdentity(source));
            Assert.Equal(targetId, files.GetFileIdentity(target));
            Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            Assert.True(File.GetAttributes(target).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(incoming, File.ReadAllBytes(source));
            Assert.Equal(incomingNamed, File.ReadAllBytes(source + ":proof"));
            Assert.Equal(existing, File.ReadAllBytes(target));
            Assert.Equal(existingNamed, File.ReadAllBytes(target + ":proof"));
            Assert.Equal([target], Directory.GetFiles(destination));
        }
        finally
        {
            if (Directory.Exists(usbFixture)) Directory.Delete(usbFixture, recursive: true);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void RequireFixture() => WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
}
