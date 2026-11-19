using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
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
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipUnless(WindowsStorageTransforms.SupportsHost(qualifyNativeArm64) &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_BACKUP_WIZARD") == "1",
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
                    var setup = new WindowsStorageTransforms(efsSetupBaseDirectory: () => root, qualifyNativeArm64: qualifyNativeArm64)
                        .InspectEfsSetup(identity.User!.Value, consent, true, ct);
                    Assert.Null(setup.Problem);
                    Assert.NotNull(setup.Association);
                    string hash = Assert.Single(setup.Association.CurrentUserKeys).CertificateSha1;
                    nint owner = CreateWindow(0, "STATIC", "FileCat EFS backup test owner", 0x00CF0000, 80, 80, 480, 360, 0, 0, 0, 0);
                    Assert.NotEqual(0, owner);
                    try
                    {
                        ShowWindow(owner, 5);
                        var keys = new WindowsEfsKeys(qualifyNativeArm64);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_file_key_backup_reports_only_the_matching_files_after_export(bool complete)
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipUnless(WindowsStorageTransforms.SupportsHost(qualifyNativeArm64) &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_BACKUP_WIZARD") == "1",
            "Requires a disposable Windows VM with an exportable EFS key and a native wizard.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-selected-efs-export-test-").FullName;
        string backupPath = Path.Join(root, "synthetic.pfx");
        string encrypted = Path.Join(root, "encrypted.txt"), clear = Path.Join(root, "clear.txt");
        var finished = new TaskCompletionSource<(StorageEfsFileBackupResult Result, string Hash)>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            File.WriteAllText(encrypted, "Encrypted fixture");
            File.WriteAllText(clear, "Clear fixture");
            File.Encrypt(encrypted);
            var ui = new Thread(() =>
            {
                try
                {
                    using var native = StorageReviewResources.EnterNative(ct);
                    var adapter = (IStorageEfsFileBackup)new WindowsStorageTransforms(qualifyNativeArm64: qualifyNativeArm64).CreateEfsJobCandidate();
                    var review = adapter.InspectFiles(StoragePaths.Capture(
                        [ItemRef.ForFileSystemPath(encrypted, EntryKind.File), ItemRef.ForFileSystemPath(clear, EntryKind.File)]), ct);
                    string hash = Assert.Single(review.Keys).CertificateSha1;
                    Assert.Equal(hash, Assert.Single(review.Items[0].Certificates));
                    Assert.Empty(review.Items[1].Certificates);
                    nint owner = CreateWindow(0, "STATIC", "FileCat selected EFS key test owner", 0x00CF0000, 80, 80, 480, 360, 0, 0, 0, 0);
                    Assert.NotEqual(0, owner);
                    try
                    {
                        ShowWindow(owner, 5);
                        var controller = Task.Run(() => WindowsEfsBackupWizardDriver.Drive(owner, complete, backupPath, FixturePassword), ct);
                        var result = adapter.BackUpFileKey(review, hash, owner, ct);
                        controller.GetAwaiter().GetResult();
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
            Assert.Equal(complete, result.WindowsReportedExport);
            Assert.Equal(!complete, result.Canceled);
            if (complete)
            {
                Assert.Equal(0, Assert.Single(result.CoveredItems));
                using var restored = X509CertificateLoader.LoadPkcs12FromFile(backupPath, FixturePassword, X509KeyStorageFlags.EphemeralKeySet);
                Assert.Equal(hash, restored.Thumbprint);
                Assert.True(restored.HasPrivateKey);
            }
            else
            {
                Assert.Empty(result.CoveredItems);
                Assert.False(File.Exists(backupPath));
            }
            Assert.Equal("Encrypted fixture", File.ReadAllText(encrypted));
            Assert.Equal("Clear fixture", File.ReadAllText(clear));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolated_CryptoAPI_wizard_cancels_or_exports_the_compared_private_key(bool complete)
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        Assert.SkipUnless(WindowsStorageTransforms.SupportsHost(qualifyNativeArm64) &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_BACKUP_WIZARD") == "1",
            "Requires the disposable Windows native export fixture.");
        string host = Path.Join(AppContext.BaseDirectory, "FileCat.ShellHost.exe");
        Assert.True(File.Exists(host));
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-capi-export-").FullName;
        string path = Path.Join(root, "synthetic.pfx"), name = "FileCat.EfsIsolatedExport." + Guid.NewGuid().ToString("N");
        using var key = new RSACryptoServiceProvider(2048, new CspParameters(24)
        {
            KeyContainerName = name, KeyNumber = (int)KeyNumber.Exchange, Flags = CspProviderFlags.NoPrompt,
        });
        Assert.Equal(name, key.CspKeyContainerInfo.KeyContainerName);
        key.PersistKeyInCsp = false;
        Assert.True(key.CspKeyContainerInfo.Exportable);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true)); // Keep the temporary certificate out of EFS default selection.
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User!.Value, hash = certificate.Thumbprint;
        var association = new StorageEfsAssociationSnapshot(new(1, "00000000000000000000000000000001"), sid,
            [new(sid, hash, null)], [new(sid, hash, true, true, StorageEfsKeyState.InteractionRequired,
                StorageEfsKeyProvider.Capi, DateTime.UtcNow, null, null)]);
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
        store.Add(certificate);
        var finished = new TaskCompletionSource<WindowsEfsKeys.BackupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var ui = new Thread(() =>
            {
                nint owner = 0;
                try
                {
                    owner = CreateWindow(0, "STATIC", "FileCat isolated export owner", 0x00CF0000, 80, 80, 480, 360, 0, 0, 0, 0);
                    Assert.NotEqual(0, owner);
                    ShowWindow(owner, 5);
                    var export = Task.Run(() =>
                    {
                        using var native = StorageReviewResources.EnterNative(ct);
                        var driver = Task.Run(() => WindowsEfsBackupWizardDriver.DriveIsolated(owner, complete, path, FixturePassword, host), ct);
                        var result = new WindowsEfsKeys(qualifyNativeArm64).BackUpAssociatedCertificate(association, hash, owner, true, ct,
                            invokeProviderOwner: _ => throw new InvalidOperationException("CryptoAPI must use its separate worker."));
                        driver.GetAwaiter().GetResult();
                        return result;
                    }, ct);
                    var elapsed = Stopwatch.StartNew();
                    while (!export.IsCompleted)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (elapsed.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("The isolated export did not retire.");
                        while (PeekMessage(out var message, 0, 0, 0, 1))
                        {
                            TranslateMessage(ref message);
                            DispatchMessage(ref message);
                        }
                        Thread.Sleep(10);
                    }
                    finished.TrySetResult(export.GetAwaiter().GetResult());
                }
                catch (Exception error) { finished.TrySetException(error); }
                finally { if (owner != 0) DestroyWindow(owner); }
            }) { IsBackground = true };
            ui.SetApartmentState(ApartmentState.STA);
            ui.Start();
            var result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(35), ct);
            Assert.Null(result.Problem);
            if (complete)
            {
                Assert.Equal(WindowsEfsKeys.BackupState.WindowsReportedComplete, result.State);
                Assert.Equal(association.Identity, result.Completion?.ObservedIdentity);
                Assert.Equal(hash, result.Completion?.CertificateSha1);
                using var restored = X509CertificateLoader.LoadPkcs12FromFile(path, FixturePassword, X509KeyStorageFlags.EphemeralKeySet);
                Assert.Equal(hash, restored.Thumbprint);
                Assert.True(restored.HasPrivateKey);
            }
            else
            {
                Assert.Equal(WindowsEfsKeys.BackupState.Canceled, result.State);
                Assert.Equal(1223, result.NativeError);
                Assert.Null(result.Completion);
                Assert.False(File.Exists(path));
            }
        }
        finally
        {
            store.Remove(certificate);
            Directory.Delete(root, recursive: true);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam, LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out WindowMessage message, nint window, uint min, uint max, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref WindowMessage message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref WindowMessage message);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindow(uint exStyle, string className, string caption, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int show);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}
