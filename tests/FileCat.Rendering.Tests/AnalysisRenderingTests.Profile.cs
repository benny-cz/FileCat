using System.IO.Compression;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.State;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task PrivateProfileReopensAcrossServiceOwnersAndIsAbsentFromActualDiagnosticsZip()
    {
        OrdinaryPreparation(); string root = Path.Combine(Environment.CurrentDirectory, ".tmp", "analysis-private-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var paths = AppPaths.Resolve(dataRoot: root); AnalysisHostProfileIdentity first; byte[] state;
        try
        {
            using (var services = AppServices.CreateForPaths(paths))
            using (var model = new DiskSpaceViewModel(services, path => new PreparedSource(path)))
            {
                await model.StartAsync([root]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
                first = Assert.IsType<AnalysisHostProfileIdentity>(model.Tree!.Snapshot.Identity.HostProfileIdentity);
                Assert.True(first.HasHostContext); Assert.Null(model.ContinuityNotice); state = File.ReadAllBytes(paths.AnalysisPrivateStateFile);
            }
            using (var services = AppServices.CreateForPaths(paths))
            using (var model = new DiskSpaceViewModel(services, path => new PreparedSource(path)))
            {
                await model.StartAsync([root]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
                Assert.Equal(first, model.Tree!.Snapshot.Identity.HostProfileIdentity); Assert.Equal(state, File.ReadAllBytes(paths.AnalysisPrivateStateFile));
                string canary = "ANALYSIS_PRIVATE_CANARY_" + Guid.NewGuid().ToString("N"); model.Query = canary;
                Directory.CreateDirectory(paths.AnalysisScratchDirectory); File.WriteAllText(Path.Combine(paths.AnalysisScratchDirectory, "private-probe.txt"), canary);
                string zipPath = DiagnosticsBundle.Write(services, root);
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    Assert.DoesNotContain("analysis", entry.FullName, StringComparison.OrdinalIgnoreCase);
                    using var input = entry.Open(); using var bytes = new MemoryStream(); input.CopyTo(bytes);
                    string text = Encoding.UTF8.GetString(bytes.ToArray());
                    foreach (string secret in new[] { canary, first.ProfileId.ToString("D"), first.ProfileId.ToString("N"), first.HostToken! })
                        Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task CorruptPrivateStatePreservesBytesAndStillAllowsMetadataWithAVisibleContinuityReason()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); using var styles = new ProductStyles();
        using (var initial = new DiskSpaceViewModel(fixture.Services, path => new PreparedSource(path)))
        { await initial.StartAsync([fixture.Root]); await Until(() => initial.Phase == AnalysisPhase.Finished); }
        const string corrupt = "{\"schema\":\"future-private-profile\",\"PRIVATE_ROOT_CANARY\":\"must remain untouched\"}";
        File.WriteAllText(fixture.Services.Paths.AnalysisPrivateStateFile, corrupt);
        using var model = new DiskSpaceViewModel(fixture.Services, path => new PreparedSource(path));
        await model.StartAsync([fixture.Root]); await Until(() => model.Phase == AnalysisPhase.Finished); await model.RebuildAsync(true);
        Assert.StartsWith("ephemeral:", model.Tree!.Snapshot.Identity.HostProfile); Assert.Null(model.Tree.Snapshot.Identity.HostProfileIdentity);
        Assert.NotNull(model.ContinuityNotice); Assert.Equal(corrupt, File.ReadAllText(fixture.Services.Paths.AnalysisPrivateStateFile));
        var window = new DiskSpaceWindow(); window.AddSession(model); window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), c => c.IsEffectivelyVisible && c.Text?.Contains("Private profile continuity could not be loaded or created", StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); }
    }
}
