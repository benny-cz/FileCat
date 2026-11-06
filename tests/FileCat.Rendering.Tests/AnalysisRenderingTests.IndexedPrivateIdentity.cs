using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task PrivateDirectoryExclusionRequiresTheSameHeldNativeObject()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        await AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(fixture.Root), _ =>
        {
            using AnalysisPrivateDirectory first = OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(fixture.Root, "first") : new UnixAnalysisPrivateDirectory(fixture.Root, "first");
            using AnalysisPrivateDirectory second = OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(fixture.Root, "second") : new UnixAnalysisPrivateDirectory(fixture.Root, "second");
            using var firstRoot = NativeRoot(Path.Combine(fixture.Root, "first"));
            using var secondRoot = NativeRoot(Path.Combine(fixture.Root, "second"));
            Assert.True(first.MatchesRoot(firstRoot)); Assert.True(second.MatchesRoot(secondRoot));
            Assert.False(first.MatchesRoot(secondRoot)); Assert.False(second.MatchesRoot(firstRoot));
            AnalysisIndexedAcquisition.RequireSamePrivateRoot(first, firstRoot);
            Assert.Throws<IOException>(() => AnalysisIndexedAcquisition.RequireSamePrivateRoot(first, secondRoot));
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [AvaloniaFact]
    public async Task RestoredUnixPrivatePathCannotAcceptAReplacementExclusionCapability()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows private directory handles deny delete sharing; Unix supports the rename-and-restore fixture.");
        OrdinaryPreparation(); using var fixture = new PreparationFixture();
        await AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(fixture.Root), token =>
        {
            string selected = Path.Combine(fixture.Root, "anchor"), parked = Path.Combine(fixture.Root, "parked"), replacementPath = Path.Combine(fixture.Root, "replacement");
            var directory = new UnixAnalysisPrivateDirectory(fixture.Root, "anchor");
            using var store = new AnalysisPrivateInventoryStore(directory);
            var state = new AnalysisPrivateProfileState(store); Guid profile = state.LoadOrCreate(token);
            using var originalRoot = NativeRoot(selected);
            Directory.Move(selected, parked);
            using var replacement = new UnixAnalysisPrivateDirectory(fixture.Root, "anchor");
            using var replacementRoot = NativeRoot(selected);
            Directory.Move(selected, replacementPath); Directory.Move(parked, selected);
            // The old UUID/ancestry recheck passes after A is restored. The retained protection
            // still names B, so only an object comparison catches this complete ABA transition.
            Assert.Equal(profile, state.LoadOrCreate(token));
            Assert.True(directory.MatchesRoot(originalRoot)); Assert.False(directory.MatchesRoot(replacementRoot));
            Assert.Throws<IOException>(() => AnalysisIndexedAcquisition.RequireSamePrivateRoot(directory, replacementRoot));
            return true;
        }, TestContext.Current.CancellationToken);
    }
}
