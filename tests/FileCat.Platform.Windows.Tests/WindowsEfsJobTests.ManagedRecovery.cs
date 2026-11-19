using System.Security.Cryptography.X509Certificates;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsEfsJobTests
{
    private const string ManagedPayload = "FileCat managed EFS recovery canary";
    private const string ManagedNamedPayload = "FileCat managed EFS recovery named stream";

    [Fact]
    public void Managed_recovery_fixture_encrypts_with_a_reviewed_user_key()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(arm64);
        string root = ManagedRecoveryRoot();
        string path = Path.Join(root, "managed-source.bin");
        var fs = new WindowsFileOperations();
        File.WriteAllText(path, ManagedPayload);
        File.WriteAllText(path + ":fixture", ManagedNamedPayload);
        File.WriteAllText(Path.Join(root, "managed-plain-control.bin"), ManagedPayload);
        string identity = Assert.IsType<string>(fs.GetFileIdentity(path));
        string evidence = Directory.CreateTempSubdirectory("filecat-managed-efs-owner-").FullName;
        try
        {
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => evidence, qualifyNativeArm64: arm64);
            var manager = Manager(evidence, adapter.CreateEfsJobCandidate((_, sid, consent, ct) =>
            {
                var setup = adapter.InspectEfsSetup(sid, consent, explicitSetupAccepted: true, ct);
                Assert.Null(setup.Problem);
                var key = Assert.Single(setup.Association!.CurrentUserKeys);
                return StorageEfsRecoveryApproval.DeclareArrangement(consent, setup, key.CertificateSha1, acknowledged: true);
            }), arm64);
            var job = Run(manager, StorageAction.EncryptEfs, [ItemRef.ForFileSystemPath(path, EntryKind.File)]);
            AssertCompleted(job, changed: 1, already: 0);
            var call = Assert.Single(Evidence(manager, job).Calls);
            var reviewed = Assert.IsType<StorageEfsReviewedKey>(call.EfsReviewedKey);
            var actual = Assert.IsType<StorageEfsResultKeys>(call.Result?.EfsKeys);
            Assert.True(actual.UserAssociationsEstablished);
            Assert.Equal(reviewed.CertificateSha1, Assert.Single(actual.Keys).CertificateSha1);
            Assert.NotEqual(File.ReadAllText(Path.Join(root, "managed-agent.txt")).Trim(), reviewed.CertificateSha1);
            Assert.True(IsEncrypted(path));
            Assert.Equal(identity, fs.GetFileIdentity(path));
            Assert.Equal(ManagedPayload, File.ReadAllText(path));
            Assert.Equal(ManagedNamedPayload, File.ReadAllText(path + ":fixture"));
            File.WriteAllText(Path.Join(root, "managed-identity.txt"), identity);
        }
        finally { Directory.Delete(evidence, recursive: true); }
    }

    [Fact]
    public void Managed_recovery_agent_decrypts_the_original_under_an_independent_account()
    {
        bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireEfs(arm64);
        string root = ManagedRecoveryRoot();
        string thumbprint = File.ReadAllText(Path.Join(root, "managed-agent.txt")).Trim();
        using var certificates = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        certificates.Open(OpenFlags.ReadOnly);
        var certificate = Assert.Single(certificates.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false));
        Assert.True(certificate.HasPrivateKey);
        string path = Path.Join(root, "managed-source.bin");
        var fs = new WindowsFileOperations();
        string identity = File.ReadAllText(Path.Join(root, "managed-identity.txt"));
        Assert.True(IsEncrypted(path));
        Assert.Equal(identity, fs.GetFileIdentity(path));
        Assert.Equal(ManagedPayload, File.ReadAllText(path));
        Assert.Equal(ManagedNamedPayload, File.ReadAllText(path + ":fixture"));
        string evidence = Directory.CreateTempSubdirectory("filecat-managed-efs-agent-").FullName;
        try
        {
            var manager = Manager(evidence, new WindowsStorageTransforms(qualifyNativeArm64: arm64).CreateEfsJobCandidate(), arm64);
            var job = Run(manager, StorageAction.DecryptEfs, [ItemRef.ForFileSystemPath(path, EntryKind.File)]);
            AssertCompleted(job, changed: 1, already: 0);
            var call = Assert.Single(Evidence(manager, job).Calls);
            Assert.Null(call.EfsReviewedKey);
            Assert.True(call.Result?.NativeCallAttempted is true);
            Assert.False(IsEncrypted(path));
            Assert.Equal(identity, fs.GetFileIdentity(path));
            Assert.Equal(ManagedPayload, File.ReadAllText(path));
            Assert.Equal(ManagedNamedPayload, File.ReadAllText(path + ":fixture"));
        }
        finally { Directory.Delete(evidence, recursive: true); }
    }

    private static string ManagedRecoveryRoot()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_EFS_MANAGED_RECOVERY_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires the isolated two-account managed EFS recovery fixture.");
        return root;
    }
}
