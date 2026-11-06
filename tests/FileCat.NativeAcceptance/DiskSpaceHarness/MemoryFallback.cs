using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private string? _memoryProbeStage;
    private object? _memoryProbeView;
    private async Task MemoryFallbackProbeAsync()
    {
        var sources = new List<IAnalysisBackend>();
        try
        {
            var services = _services!; var window = _window!;
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();
            await using (var initial = await AnalysisIndexedAcquisition.PrepareAsync(services, _indexedLifetime.Token)) { }
            const string unsupported = "{\"schema\":\"future\",\"canary\":\"generated-private-state\"}";
            File.WriteAllText(services.Paths.AnalysisPrivateStateFile, unsupported);
            string stateHash = Hash(services.Paths.AnalysisPrivateStateFile);
            Directory.Delete(services.Paths.AnalysisScratchDirectory, recursive: false);
            string parent = Path.GetDirectoryName(services.Paths.AnalysisScratchDirectory)!;
            string neighbor = Path.Combine(parent, "PRIVATE_BOUNDARY_CANARY.bin"); File.WriteAllText(neighbor, "untouched generated private neighbor");
            string neighborHash = Hash(neighbor);
            DiskSpaceViewModel model; AnalysisSnapshot snapshot; AnalysisSession? session = null;
            if (Program.MemoryLifecycleProbe)
            {
                model = new DiskSpaceViewModel(services); _models.Add(model); window.AddSession(model, [_fixture, parent]);
                await model.StartAsync([_fixture, parent]);
                await ReadyAsync(() => model.Phase == AnalysisPhase.Finished);
                await model.RebuildAsync(true); await ReadyAsync(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished);
                snapshot = model.Tree!.Snapshot;
            }
            else
            {
            _indexedAcquisition = await AnalysisIndexedAcquisition.PrepareAsync(services, _indexedLifetime.Token);
            if (_indexedAcquisition.Profile is not null || _indexedAcquisition.MemoryPreparationNotice is null || Directory.Exists(services.Paths.AnalysisScratchDirectory))
                throw new IOException("The generated unsupported private state did not produce preserving memory preparation.");
            foreach (string path in new[] { _fixture, parent })
                sources.Add(await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(path), _ => OperatingSystem.IsWindows()
                    ? (IAnalysisBackend)new WindowsAnalysisBackend(path) : new UnixAnalysisBackend(path), _indexedLifetime.Token));
            session = _indexedAcquisition.Start(sources); sources.Clear();
            using var stop = _indexedLifetime.Token.Register(session.Stop);
            await session.Completion.WaitAsync(TimeSpan.FromMinutes(3));
            snapshot = session.Snapshot();
            var acquired = await window.AddAcquiredSessionAsync(session, cancellation: _indexedLifetime.Token);
            model = acquired.Memory ?? throw new InvalidDataException("The unavailable scratch acquisition did not select its memory view.");
            if (acquired.Indexed is not null) throw new InvalidDataException("The unavailable scratch acquisition unexpectedly selected an indexed view.");
            _models.Add(model);
            await _indexedAcquisition.DisposeAsync(); _indexedAcquisition = null;
            }
            AnalysisValidation.Validate(snapshot, _indexedLifetime.Token);
            _memoryProbeStage = "source-complete";
            _memoryProbeView = new { paths = snapshot.Nodes.Length, files = snapshot.Nodes.Count(n => n.Kind == AnalysisKind.File),
                roots = snapshot.Roots.Select(r => new { r.Id, r.Disposition }).ToArray(), issues = snapshot.Issues.Select(i => i.Code).ToArray(), usesMemoryFallback = session?.UsesMemoryFallback ?? true };
            WriteReport("memory-probe-progress");
            if (session is { UsesMemoryFallback: false } || session?.DatasetDescriptor is not null || model.SourceCloseFailures != 0 || snapshot.Coverage != NamespaceCoverage.Partial ||
                snapshot.Roots[1].Disposition != AnalysisRootDisposition.UnavailablePrivateBoundary || snapshot.Roots[1].SourceIdentity is not null || snapshot.Roots[1].ObservedObjectIdentity is not null ||
                snapshot.Nodes.Any(n => n.Name.Contains("PRIVATE_", StringComparison.Ordinal))) throw new InvalidDataException("The native memory scope/exclusion observation differs from the generated fixture.");
            void ObserveView() { _memoryProbeView = new { model.Query, applied = model.Tree?.Projection.FilterText,
                matchingPaths = model.Tree?.Projection.Totals.MatchingPaths, paths = model.Tree?.Snapshot.Nodes.Length,
                rows = model.Rows.Length, scope = model.Selection.Scope, model.Status, model.FilterError,
                issues = model.Tree?.Snapshot.Issues.Select(i => i.Code).ToArray() }; WriteReport("memory-probe-progress"); }
            model.Changed += ObserveView;
            _memoryProbeStage = "initial-view"; ObserveView();
            await model.RebuildAsync(true);
            await ReadyAsync(() => model.Tree?.Projection.Totals.MatchingPaths == (ulong)_originals.Count);
            _memoryProbeStage = "query-one"; ObserveView();
            model.Query = "name:item-0001.txt"; await model.RebuildAsync(true);
            await ReadyAsync(() => model.Tree is { } tree && tree.Projection.FilterText == model.Query && tree.Projection.Totals.MatchingPaths == 1);
            ulong queryMatches = model.Tree!.Projection.Totals.MatchingPaths;
            _memoryProbeStage = "query-clear"; ObserveView();
            model.Query = ""; await model.RebuildAsync(true);
            await ReadyAsync(() => model.Tree is { } tree && tree.Projection.FilterText.Length == 0 && tree.Projection.Totals.MatchingPaths == (ulong)_originals.Count);
            using (var displayed = model.CaptureDisplayedRevision())
            using (var retained = displayed.Retain())
                if (retained.Snapshot.Identity != snapshot.Identity) throw new InvalidDataException("The memory view lost the captured acquisition revision.");
            _memoryProbeStage = "scope-open"; ObserveView();
            model.Focus(AnalysisTree.Key(1), open: true);
            await ReadyAsync(() => model.Selection.Scope == AnalysisTree.Key(1) && model.Rows.Length > 0);
            if (model.AcquisitionNotice?.Contains("Not read because a private-state boundary", StringComparison.Ordinal) != true ||
                model.ContinuityNotice is null || model.HasPrivateStorage)
                throw new InvalidDataException("The native memory view did not preserve its scope, continuity and storage explanation.");
            if (Hash(services.Paths.AnalysisPrivateStateFile) != stateHash || Hash(neighbor) != neighborHash || Directory.Exists(services.Paths.AnalysisScratchDirectory))
                throw new IOException("The generated private state or unavailable scratch location changed.");
            _memoryProbeStage = "capture"; ObserveView();
            await Task.Delay(300); window.UpdateLayout();
            // Render only this actual native process's own FileCat visual tree. No screen API,
            // other window, desktop pixels, privacy-setting change or capture permission is used.
            double scale = window.RenderScaling;
            var size = new PixelSize(checked((int)Math.Ceiling(window.ClientSize.Width * scale)), checked((int)Math.Ceiling(window.ClientSize.Height * scale)));
            using (var image = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
            { image.Render(window); image.Save(Path.Combine(_run, "native-memory-window-render.png"), new PngBitmapEncoderOptions()); }
            _indexedProbe = new
            {
                kind = Program.MemoryLifecycleProbe ? "memory-lifecycle" : "memory-fallback", paths = snapshot.Nodes.Length, objects = snapshot.Objects.Length,
                matchingPaths = model.Tree!.Projection.Totals.MatchingPaths, queryMatches,
                unavailableRoots = snapshot.Roots.Count(r => r.Disposition == AnalysisRootDisposition.UnavailablePrivateBoundary),
                privateNamesObserved = false, sourceCloseFailures = model.SourceCloseFailures, acquisitionOwnerClosed = !Program.MemoryLifecycleProbe,
                publicStartLifecycle = Program.MemoryLifecycleProbe,
                stateUnchanged = Hash(services.Paths.AnalysisPrivateStateFile) == stateHash, neighborUnchanged = Hash(neighbor) == neighborHash,
                privateStateSha256 = stateHash, neighborSha256 = neighborHash, privateStateFile = services.Paths.AnalysisPrivateStateFile,
                neighborFile = neighbor, scratch = services.Paths.AnalysisScratchDirectory, scratchCreated = false,
                model.AcquisitionNotice, model.ContinuityNotice, view = "Memory", capturedRevisionRetained = true,
                captureScope = "FileCat visual tree from actual native window; no system frame, compositor or physical-input qualification",
                captureFile = "native-memory-window-render.png", pixelWidth = size.Width, pixelHeight = size.Height, nativeDesktopInputQualified = false
            };
            _indexedProbeFinished = true; WriteReport("memory-fallback-probe-finished");
        }
        catch (Exception error) { _indexedProbeError = error.GetType().Name + ": " + error.Message; WriteReport("memory-fallback-probe-failed"); }
        finally { foreach (var source in sources) await AnalysisIo.ReleaseAsync(_services!.Io, source); }
    }
}
