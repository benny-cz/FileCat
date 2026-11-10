using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Fact]
    public void Held_stage_and_original_exchange_atomically_and_retire_the_exact_original()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
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
    public void Symlink_substitution_refuses_exchange_without_following_its_target()
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path);
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
}
