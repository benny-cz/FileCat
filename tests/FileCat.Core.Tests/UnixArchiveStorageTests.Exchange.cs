using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Fact]
    public void Cross_directory_exchange_retains_the_original_privately_and_terminates_both_old_bindings()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string targetPath = Path.Combine(root, "original.tar"); File.WriteAllText(targetPath, "old archive");
        using var target = UnixArchiveDirectory.Open(root);
        using var original = target.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
        using var privateParent = target.CreatePrivateStageDirectory(Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var stage = Stage(privateParent); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, TestContext.Current.CancellationToken).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(TestContext.Current.CancellationToken);
        sealedStage = stage.ApplyUpdateMetadata(sealedStage, original, old, old.Origin, TestContext.Current.CancellationToken);

        var receipt = stage.ExchangeRetainedAcrossParents(original, sealedStage, old, TestContext.Current.CancellationToken);

        Assert.Equal(target.Identity.ToString(), receipt.PublicParentIdentity);
        Assert.Equal(privateParent.Identity.ToString(), receipt.PrivateParentIdentity);
        Assert.Equal("original.tar", receipt.PublicLeaf);
        Assert.Equal(stageName, receipt.PrivateLeaf);
        Assert.Equal(sealedStage.Version, receipt.Published);
        Assert.Equal(old.Version, receipt.Retired);
        Assert.Equal("new archive", File.ReadAllText(targetPath));
        Assert.Equal("old archive", File.ReadAllText(Path.Combine(privateParent.DisplayPath, stageName)));
        Assert.False(stage.CanRead);
        Assert.False(original.CanRead);
        Assert.Equal(ArchiveWriteReason.NeedsReview,
            Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)stage).Observe(TestContext.Current.CancellationToken)).Reason);
        Assert.Equal(ArchiveWriteReason.NeedsReview,
            Assert.Throws<ArchiveWriteException>(() => ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken)).Reason);
        using var reopenedPublic = target.OpenSource("original.tar", 4096);
        using var reopenedPrivate = privateParent.OpenSource(stageName, 4096);
        Assert.Equal(sealedStage.Version, reopenedPublic.CaptureVersion(TestContext.Current.CancellationToken));
        Assert.Equal(old.Version, reopenedPrivate.CaptureVersion(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Cross_directory_exchange_refuses_changed_original_before_moving_either_name()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string targetPath = Path.Combine(root, "original.tar"); File.WriteAllText(targetPath, "old archive");
        using var target = UnixArchiveDirectory.Open(root);
        using var original = target.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
        using var privateParent = target.CreatePrivateStageDirectory(Guid.NewGuid(), TestContext.Current.CancellationToken);
        using var stage = Stage(privateParent); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, TestContext.Current.CancellationToken).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(TestContext.Current.CancellationToken);
        sealedStage = stage.ApplyUpdateMetadata(sealedStage, original, old, old.Origin, TestContext.Current.CancellationToken);
        File.AppendAllText(targetPath, " changed");

        Assert.Equal(ArchiveWriteReason.SourceChanged,
            Assert.Throws<ArchiveWriteException>(() => stage.ExchangeRetainedAcrossParents(original, sealedStage, old,
                TestContext.Current.CancellationToken)).Reason);
        Assert.True(stage.CanRead);
        Assert.True(original.CanRead);
        Assert.Equal("old archive changed", File.ReadAllText(targetPath));
        Assert.Equal("new archive", File.ReadAllText(Path.Combine(privateParent.DisplayPath, stageName)));
    }

    [Fact]
    public void Private_stage_directory_is_pinned_on_the_target_volume_and_retains_its_artifact()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        using var target = UnixArchiveDirectory.Open(root);
        using var stageDirectory = target.CreatePrivateStageDirectory(Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.NotEqual(target.Identity, stageDirectory.Identity);
        Assert.Equal(target.Volume, stageDirectory.Volume);
        Assert.Equal((UnixFileMode)0x1c0, File.GetUnixFileMode(stageDirectory.DisplayPath));
        using var stage = Stage(stageDirectory);
        stage.Write("new archive"u8);
        var version = stage.Seal(TestContext.Current.CancellationToken);
        Assert.Equal("new archive", File.ReadAllText(stage.DisplayPath));
        using var reopened = UnixArchiveDirectory.Open(stageDirectory.DisplayPath);
        Assert.Equal(stageDirectory.Identity, reopened.Identity);
        Assert.Equal(version, stage.CaptureVersion(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Empty_nonce_refuses_private_stage_directory_before_creating_a_name()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        using var target = UnixArchiveDirectory.Open(root);
        Assert.Throws<ArgumentException>(() => target.CreatePrivateStageDirectory(Guid.Empty, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void Held_stage_and_original_exchange_atomically_and_retire_the_exact_original()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string target = Path.Combine(root, "original.tar"); File.WriteAllText(target, "old archive");
        using var directory = UnixArchiveDirectory.Open(root);
        using var original = directory.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
        using var stage = Stage(directory); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, TestContext.Current.CancellationToken).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(TestContext.Current.CancellationToken);

        var (published, retired) = stage.ExchangeRetained(original, sealedStage, old, TestContext.Current.CancellationToken);

        Assert.Equal(sealedStage.Version, published.Version);
        Assert.Equal(old.Version, retired.Version);
        Assert.Equal("original.tar", stage.KnownName);
        Assert.Equal(stageName, original.KnownName);
        Assert.Equal("new archive", File.ReadAllText(target));
        Assert.Equal("old archive", File.ReadAllText(Path.Combine(root, stageName)));
        ((IArchiveRetainedFile)stage).Review(published, TestContext.Current.CancellationToken);
        ((IArchiveRetainedFile)original).Review(retired, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Changed_original_refuses_exchange_before_either_name_moves()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string target = Path.Combine(root, "original.tar"); File.WriteAllText(target, "old archive");
        using var directory = UnixArchiveDirectory.Open(root);
        using var original = directory.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
        using var stage = Stage(directory); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, TestContext.Current.CancellationToken).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(TestContext.Current.CancellationToken);
        File.AppendAllText(target, " changed");

        Assert.Equal(ArchiveWriteReason.SourceChanged,
            Assert.Throws<ArchiveWriteException>(() => stage.ExchangeRetained(original, sealedStage, old, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("old archive changed", File.ReadAllText(target));
        Assert.Equal("new archive", File.ReadAllText(Path.Combine(root, stageName)));
    }

    [Fact]
    public void Stopped_but_unsealed_rebuild_cannot_exchange_an_original()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string target = Path.Combine(root, "original.tar"); File.WriteAllText(target, "old archive");
        using var directory = UnixArchiveDirectory.Open(root);
        using var original = directory.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
        using var stage = Stage(directory); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, TestContext.Current.CancellationToken).Write("unverified"u8);
        ((IArchiveRetainedArtifact)stage).StopWrites();
        var unsealed = ((IArchiveRetainedFile)stage).Observe(TestContext.Current.CancellationToken);

        Assert.Equal(ArchiveWriteReason.InvalidRequest,
            Assert.Throws<ArchiveWriteException>(() => stage.ExchangeRetained(original, unsealed, old, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("old archive", File.ReadAllText(target));
        Assert.Equal("unverified", File.ReadAllText(Path.Combine(root, stageName)));
    }

    [Fact]
    public void Symlink_substitution_refuses_exchange_without_following_its_target()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string target = Path.Combine(root, "original.tar"); File.WriteAllText(target, "old archive");
        using var directory = UnixArchiveDirectory.Open(root);
        using var original = directory.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
        using var stage = Stage(directory); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, TestContext.Current.CancellationToken).Write("new archive"u8);
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(TestContext.Current.CancellationToken);
        string displaced = Path.Combine(root, "displaced.tar"); File.Move(target, displaced);
        string poison = Path.Combine(root, "poison.tar"); File.WriteAllText(poison, "poison");
        File.CreateSymbolicLink(target, poison);

        Assert.ThrowsAny<IOException>(() => stage.ExchangeRetained(original, sealedStage, old, TestContext.Current.CancellationToken));
        Assert.Equal("poison", File.ReadAllText(poison));
        Assert.Equal("new archive", File.ReadAllText(Path.Combine(root, stageName)));
        Assert.Equal("old archive", File.ReadAllText(displaced));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_outer_attribute_on_either_object_refuses_exchange(bool onStage)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string target = Path.Combine(root, "original.tar"); File.WriteAllText(target, "old archive");
        string name = OperatingSystem.IsMacOS() ? "org.filecat.unreviewed" : "user.filecat.unreviewed";
        if (!onStage) Assert.True(Xattr.Set(target, name, "unreviewed"));
        using var directory = UnixArchiveDirectory.Open(root);
        using var original = directory.OpenSource("original.tar", 4096);
        var old = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
        using var stage = Stage(directory); string stageName = stage.KnownName;
        ((IArchiveRetainedStage)stage).BeginRebuild(4096, TestContext.Current.CancellationToken).Write("new archive"u8);
        if (onStage) Assert.True(Xattr.Set(stage.DisplayPath, name, "unreviewed"));
        var sealedStage = ((IArchiveRetainedStage)stage).SealForVerification(TestContext.Current.CancellationToken);

        Assert.Equal(ArchiveWriteReason.UnsupportedFeature,
            Assert.Throws<ArchiveWriteException>(() => stage.ExchangeRetained(original, sealedStage, old, TestContext.Current.CancellationToken)).Reason);
        Assert.Equal("old archive", File.ReadAllText(target));
        Assert.Equal("new archive", File.ReadAllText(Path.Combine(root, stageName)));
    }
}
