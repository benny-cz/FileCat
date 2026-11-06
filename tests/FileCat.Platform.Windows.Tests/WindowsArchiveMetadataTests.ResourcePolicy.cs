using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveMetadataTests
{
    [Theory]
    [InlineData("a.bin", "S-1-17-10001-10002-10003-10004")]
    [InlineData("b.bin", "S-1-17-20001-20002-20003-20004")]
    public void A_read_control_only_source_observes_the_native_resource_claim_and_actual_CAP_identifier(string leaf, string expectedSid)
    {
        RequireWindows(); string? fixtures = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_POLICY_FIXTURES");
        if (fixtures is null) Assert.Skip("Run the explicit resource-policy fixtures prepared by make-windows-resource-policy-fixtures.ps1.");
        Assert.True(Path.IsPathFullyQualified(fixtures!));
        using var root = WindowsArchiveDirectory.Open(fixtures!); using var source = root.OpenSource(leaf);
        var observed = ((IArchiveRetainedFile)source).Observe(TestContext.Current.CancellationToken);
        var metadata = source.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        var security = new RawSecurityDescriptor(metadata.SecurityDescriptor.ToArray(), 0); Assert.NotNull(security.SystemAcl);
        Assert.Single(security.SystemAcl.Cast<GenericAce>(), ace => (byte)ace.AceType == 0x12);
        var scope = Assert.Single(security.SystemAcl.Cast<GenericAce>(), ace => (byte)ace.AceType == 0x13 /* SYSTEM_SCOPED_POLICY_ID_ACE */);
        byte[] bytes = new byte[scope.BinaryLength]; scope.GetBinaryForm(bytes, 0);
        Assert.Equal(expectedSid, new SecurityIdentifier(bytes, 8).Value);
        Assert.Equal(3, observed.Version.Length); Assert.Equal(observed.Version, source.CaptureVersion(TestContext.Current.CancellationToken));
        using var view = ((IArchiveRetainedFile)source).OpenReadView(observed, TestContext.Current.CancellationToken);
        byte[] payload = new byte[3]; view.ReadExactly(payload); Assert.Equal(new byte[] { 1, 2, 3 }, payload); Assert.Equal(-1, view.ReadByte());
    }

    [Fact]
    public void A_resource_claim_change_is_observed_through_the_retained_file_without_a_payload_timestamp_or_DACL_change()
    {
        RequireWindows(); using var fixture = new Fixture(); string path = fixture.PathOf("original.bin"); File.WriteAllBytes(path, [1, 2, 3]);
        // Establish an already-present claim first: its initial assignment can normalize inherited ACL flags.
        SetResourceClaim(path, "before");
        using var root = WindowsArchiveDirectory.Open(fixture.Root); using var source = root.OpenSource("original.bin");
        var retained = (IArchiveRetainedFile)source; var before = retained.Observe(TestContext.Current.CancellationToken);
        var metadata = source.CaptureOuterMetadata(TestContext.Current.CancellationToken);
        string prior = metadata.Digest;
        foreach (string value in new[] { "first", "second" })
        {
            SetResourceClaim(path, value);
            var after = source.CaptureOuterMetadata(TestContext.Current.CancellationToken);
            Assert.NotEqual(prior, after.Digest); prior = after.Digest;
            Assert.Equal(metadata.Identity, after.Identity); Assert.Equal(metadata.CreationTime, after.CreationTime);
            Assert.Equal(metadata.LastWriteTime, after.LastWriteTime); Assert.Equal(metadata.Attributes, after.Attributes);
            Assert.Equal(metadata.OwnerSid, after.OwnerSid); Assert.Equal(metadata.GroupSid, after.GroupSid);
            Assert.Equal(metadata.Origin.Digest, after.Origin.Digest); Assert.Equal(before.Version, source.CaptureVersion(TestContext.Current.CancellationToken));
            var oldSecurity = new RawSecurityDescriptor(metadata.SecurityDescriptor.ToArray(), 0);
            var newSecurity = new RawSecurityDescriptor(after.SecurityDescriptor.ToArray(), 0);
            Assert.Equal(DaclBytes(oldSecurity), DaclBytes(newSecurity));
            Assert.NotNull(newSecurity.SystemAcl);
            Assert.Single(newSecurity.SystemAcl.Cast<GenericAce>(), ace => (byte)ace.AceType == 0x12 /* SYSTEM_RESOURCE_ATTRIBUTE_ACE */);
            Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => retained.Review(before, TestContext.Current.CancellationToken)).Reason);
        }
    }

    [Fact]
    public void A_late_resource_claim_on_the_private_stage_refuses_publication_and_retains_its_exact_bytes()
    {
        RequireWindows(); using var fixture = new Fixture(); using var root = WindowsArchiveDirectory.Open(fixture.Root);
        using var stage = root.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, 1024);
        var publication = (IArchiveCreatePublicationStage)stage; var ct = TestContext.Current.CancellationToken;
        publication.BeginRebuild(1024, ct).Write([1, 2, 3]); var observed = publication.SealForVerification(ct);
        SetResourceClaim(stage.DisplayPath, "before"); observed = ((IArchiveRetainedFile)stage).Observe(ct);
        var review = publication.ReviewCreate(observed, "result.tar", ct);
        SetResourceClaim(stage.DisplayPath, "late");
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() => publication.ClaimNewName("result.tar", review, ct)).Reason);
        Assert.False(File.Exists(fixture.PathOf("result.tar"))); Assert.True(File.Exists(stage.DisplayPath));
        Assert.Equal(observed.Version, stage.CaptureVersion(ct));
        Assert.NotEqual(observed.MetadataDigest, stage.CaptureOuterMetadata(ct).Digest);
    }

    private static byte[]? DaclBytes(RawSecurityDescriptor security)
    {
        if (security.DiscretionaryAcl is not { } acl) return null;
        byte[] bytes = new byte[acl.BinaryLength]; acl.GetBinaryForm(bytes, 0); return bytes;
    }

    private static void SetResourceClaim(string path, string value)
    {
        // Test-owned file and a fixed harmless claim; no machine policy or privileged audit-setting API is changed.
        string sddl = $"S:(RA;;;;;WD;(\"FileCatArchiveTest\",TS,0x0,\"{value}\"))";
        if (!ConvertSddl(sddl, 1, out nint descriptor, out _)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            if (!GetSacl(descriptor, out bool present, out nint sacl, out _) || !present || sacl == 0) throw new IOException("The owned resource-claim fixture was not encoded.");
            uint error = SetNamedSecurity(path, 1 /* SE_FILE_OBJECT */, 0x20 /* ATTRIBUTE_SECURITY_INFORMATION */, 0, 0, 0, sacl);
            if (error != 0) throw new Win32Exception((int)error);
        }
        finally { LocalFree(descriptor); }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSddl(string text, uint revision, out nint descriptor, out uint size);
    [LibraryImport("advapi32.dll", EntryPoint = "GetSecurityDescriptorSacl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSacl(nint descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out nint sacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);
    [LibraryImport("advapi32.dll", EntryPoint = "SetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint SetNamedSecurity(string path, int objectType, uint information, nint owner, nint group, nint dacl, nint sacl);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint pointer);
}
