using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.NativeAcceptance;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsEfsBackupWizardTests
{
    private const string FixturePassword = "FileCat-disposable-key-backup-2026!";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parented_private_key_backup_distinguishes_cancellation_from_completed_export(bool complete)
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_BACKUP_WIZARD") == "1",
            "Requires an interactive disposable Windows VM with an exportable EFS key.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-efs-export-test-").FullName;
        string backupPath = Path.Join(root, "synthetic.pfx");
        var finished = new TaskCompletionSource<(WindowsEfsKeys.BackupResult Result, string Hash)>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var ui = new Thread(() =>
            {
                try
                {
                    using var native = StorageReviewResources.EnterNative(ct);
                    using var identity = WindowsIdentity.GetCurrent();
                    var consent = StorageEfsConsent.Capture(Guid.NewGuid(), StorageAction.EncryptEfs, StorageScope.SelectedItems,
                        [new StorageObjectIdentity(1, new string('A', 32)) { VolumeBinding = "11111111-1111-1111-1111-111111111111" }], new(true, true));
                    var setup = new WindowsStorageTransforms().InspectEfsSetup(identity.User!.Value, consent, true, ct);
                    Assert.Null(setup.Problem);
                    Assert.NotNull(setup.Association);
                    string hash = Assert.Single(setup.Association.CurrentUserKeys).CertificateSha1;
                    nint owner = CreateWindow(0, "STATIC", "FileCat EFS backup test owner", 0x00CF0000, 80, 80, 480, 360, 0, 0, 0, 0);
                    Assert.NotEqual(0, owner);
                    try
                    {
                        ShowWindow(owner, 5);
                        var keys = new WindowsEfsKeys();
                        Assert.Equal("efs-backup-choice", keys.BackUpAssociatedCertificate(setup.Association, hash, owner, false, ct).Problem?.Code);
                        Assert.Equal("efs-backup-owner", keys.BackUpAssociatedCertificate(setup.Association, hash, 0, true, ct).Problem?.Code);
                        var controller = Task.Run(() => WindowsEfsBackupWizardDriver.Drive(owner, complete, backupPath, FixturePassword), ct);
                        var result = keys.BackUpAssociatedCertificate(setup.Association, hash, owner, true, ct);
                        controller.GetAwaiter().GetResult();
                        Assert.Equal(StorageEfsKeyState.MatchedAvailable, keys.ProbeAssociatedCertificate(identity.User!.Value, hash, ct).State);
                        if (complete)
                        {
                            Assert.NotNull(result.Completion);
                            var approval = StorageEfsRecoveryApproval.RecordWindowsExport(consent, setup, result.Completion);
                            Assert.Equal(StorageEfsRecoveryChoice.WindowsReportedExport, approval.Evidence.Choice);
                            Assert.True(StorageEfsResultKeys.FromAssociation(setup.Association, approval.InvokerSid).Matches(approval.Evidence));
                            Assert.Throws<ArgumentException>(() => StorageEfsRecoveryApproval.RecordWindowsExport(consent, setup,
                                result.Completion with { ObservedIdentity = consent.RootIdentities[0] }));
                        }
                        else Assert.Null(result.Completion);
                        finished.TrySetResult((result, hash));
                    }
                    finally { DestroyWindow(owner); }
                }
                catch (Exception error) { finished.TrySetException(error); }
            }) { IsBackground = true };
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            var (result, hash) = await finished.Task.WaitAsync(TimeSpan.FromSeconds(35), ct);
            Assert.Null(result.Problem);
            if (complete)
            {
                Assert.Equal(WindowsEfsKeys.BackupState.WindowsReportedComplete, result.State);
                using var restored = X509CertificateLoader.LoadPkcs12FromFile(backupPath, FixturePassword, X509KeyStorageFlags.EphemeralKeySet);
                Assert.Equal(hash, restored.Thumbprint);
                Assert.True(restored.HasPrivateKey);
            }
            else
            {
                Assert.Equal(WindowsEfsKeys.BackupState.Canceled, result.State);
                Assert.Equal(1223, result.NativeError);
                Assert.False(File.Exists(backupPath));
            }
            TestContext.Current.TestOutputHelper?.WriteLine($"EFS_BACKUP_WIZARD complete={complete} result={result.State} error={result.NativeError} artifact={File.Exists(backupPath)}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindow(uint exStyle, string className, string caption, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int show);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
