using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsEfsBackupWizardTests
{
    private const uint PressWizardButton = 0x471;
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
                        var nativeReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        var controller = Task.Run(() => DriveWizard(owner, complete, backupPath, nativeReturned.Task), ct);
                        var result = keys.BackUpAssociatedCertificate(setup.Association, hash, owner, true, ct);
                        nativeReturned.SetResult();
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

    private static void DriveWizard(nint parent, bool complete, string path, Task nativeReturned)
    {
        var wizard = WaitFor(() => TopLevelWindow(parent, "Back up EFS key"), "parented export wizard");
        try
        {
            if (!complete)
            {
                PostMessage(wizard, PressWizardButton, 5, 0);
                return;
            }
            WaitFor(() => Control(wizard, 100, "Welcome to the Certificate Export Wizard"), "welcome page");
            PostMessage(wizard, PressWizardButton, 1, 0);
            WaitFor(() => Control(wizard, 100, "Export File Format"), "private-key format page");
            Assert.False(IsWindowEnabled(WaitFor(() => Control(wizard, 108), "private-key deletion checkbox")));
            Assert.False(IsWindowEnabled(WaitFor(() => Control(wizard, 103), "public certificate-only format")));
            PostMessage(wizard, PressWizardButton, 1, 0);
            WaitFor(() => Control(wizard, 100, "Security"), "security page");
            PostMessage(WaitFor(() => Control(wizard, 1029), "password option"), 0xF5, 0, 0);
            var password = WaitFor(() => Control(wizard, 101, requireEnabled: true), "password field");
            var confirm = WaitFor(() => Control(wizard, 102, requireEnabled: true), "password confirmation");
            Assert.True(SetWindowText(password, FixturePassword));
            Assert.True(SetWindowText(confirm, FixturePassword));
            PostMessage(wizard, PressWizardButton, 1, 0);
            WaitFor(() => Control(wizard, 100, "File to Export"), "backup destination page");
            Assert.True(SetWindowText(WaitFor(() => Control(wizard, 101), "destination input"), path));
            PostMessage(wizard, PressWizardButton, 1, 0);
            WaitFor(() => Control(wizard, 100, "Completing the Certificate Export Wizard"), "completion page");
            PostMessage(wizard, PressWizardButton, 2, 0);
            var completion = WaitFor(() => nativeReturned.IsCompleted ? wizard : TopLevelWindow(wizard, title: null), "export completion");
            if (completion != wizard)
                PostMessage(WaitFor(() => Control(completion, 2, "OK"), "result acknowledgment"), 0xF5, 0, 0);
        }
        catch
        {
            PostMessage(wizard, PressWizardButton, 5, 0);
            throw;
        }
    }

    private static nint TopLevelWindow(nint owner, string? title)
    {
        nint result = 0;
        EnumWindows((window, _) =>
        {
            if (GetWindow(window, 4) != owner || !IsWindowVisible(window) || title is not null && WindowText(window) != title) return true;
            result = window;
            return false;
        }, 0);
        return result;
    }

    private static nint Control(nint window, int id, string? text = null, bool requireEnabled = false)
    {
        nint result = 0;
        EnumChildWindows(window, (child, _) =>
        {
            if (!IsWindowVisible(child) || GetDlgCtrlID(child) != id || requireEnabled && !IsWindowEnabled(child) ||
                text is not null && WindowText(child) != text) return true;
            result = child;
            return false;
        }, 0);
        return result;
    }

    private static nint WaitFor(Func<nint> locate, string description)
    {
        var time = Stopwatch.StartNew();
        do
        {
            nint handle = locate();
            if (handle != 0) return handle;
            Thread.Sleep(25);
        } while (time.Elapsed < TimeSpan.FromSeconds(3));
        throw new TimeoutException("Could not reach the wizard's " + description + ".");
    }

    private static string WindowText(nint window)
    {
        var caption = new StringBuilder(256);
        GetWindowText(window, caption, caption.Capacity);
        return caption.ToString();
    }

    private delegate bool EnumerateWindow(nint window, nint state);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindow(uint exStyle, string className, string caption, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int show);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(nint window);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, nint state);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumerateWindow callback, nint state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint window, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint window, uint message, nint value, nint extra);
}
