using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

/// <summary>
/// Retains native private-profile preparation through an indexed acquisition. Internal until the
/// native source, IPC-domain, resource and package gates allow the public acquisition switch.
/// </summary>
internal sealed partial class AnalysisIndexedAcquisition : IAsyncDisposable
{
    private const int DefaultScratchBoundary = 3;
    private readonly AppServices _services;
    private readonly bool _indexedStorage;
    internal bool IndexedStorage => _indexedStorage;
    private readonly string[] _privatePaths;
    private readonly IAnalysisBackend?[] _protection = new IAnalysisBackend?[DefaultScratchBoundary + 1];
    private readonly bool[] _ancestorGuard = new bool[DefaultScratchBoundary + 1];
    internal Task<AnalysisRollingCaptureOutcome?> HistoryCompletion { get; private set; } = Task.FromResult<AnalysisRollingCaptureOutcome?>(null);
    private readonly object _gate = new();
    private AnalysisPrivateDirectory? _scratch, _quotaDirectory;
    private AnalysisSession? _session;
    private Task? _close;
    private readonly AnalysisViewWork _imports = new();
    private bool _importStarted;
    private Guid _profile;
    private readonly string _ephemeralProfile = "ephemeral:" + Guid.NewGuid().ToString("N");
    internal AnalysisHostProfileIdentity? Profile { get; private set; }
    internal string ProfileIdentity => _rescanProfileCorrelation ?? Profile?.ToString() ?? _ephemeralProfile;
    internal string? MemoryPreparationNotice { get; private set; }
    private AnalysisIndexedAcquisition(AppServices services, bool indexedStorage, bool useConfiguredScratch)
    {
        _services = services; _indexedStorage = indexedStorage;
        _privatePaths = [services.Paths.AnalysisSnapshotsDirectory,
            useConfiguredScratch ? services.Paths.AnalysisScratchDirectory : services.Paths.DefaultAnalysisScratchDirectory,
            services.Paths.AnalysisHistoryDirectory, services.Paths.DefaultAnalysisScratchDirectory];
        if (useConfiguredScratch && services.Paths.AnalysisScratchSelectionError is { } reason) UseMemoryPreparation(reason);
    }

    internal static async Task<AnalysisIndexedAcquisition> PrepareAsync(AppServices services, CancellationToken cancellation = default,
        bool indexedStorage = true, Action<string>? preparationDeviceChanged = null, bool useConfiguredScratch = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        cancellation.ThrowIfCancellationRequested(); var owner = new AnalysisIndexedAcquisition(services, indexedStorage, useConfiguredScratch);
        try
        {
            preparationDeviceChanged?.Invoke(PathUtil.GetDeviceKey(owner._privatePaths[0]));
            await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(owner._privatePaths[0]), token =>
            {
                owner.CheckPreparation(0, token);
                try
                {
                    owner._quotaDirectory = OpenDirectory(owner._privatePaths[0]);
                    using var store = new AnalysisPrivateInventoryStore(owner._quotaDirectory, leaveOpen: true);
                    owner._profile = new AnalysisPrivateProfileState(store).LoadOrCreate(token);
                }
                catch (Exception error) when (StorageUnavailable(error)) { owner.UseMemoryPreparation(); }
                owner.CheckPreparation(0, token);
                owner.PrepareProtection(0, token);
                if (owner._ancestorGuard[0]) owner._profile = Guid.Empty;
                if (owner._quotaDirectory is { } directory && !owner._ancestorGuard[0])
                {
                    // Object disagreement and state changes after a successful read are hard refusals,
                    // not ordinary unavailable-storage fallbacks.
                    RequireSamePrivateRoot(directory, owner._protection[0]!);
                    if (owner._profile != Guid.Empty)
                    {
                        using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
                        if (new AnalysisPrivateProfileState(store).LoadOrCreate(token) != owner._profile)
                            throw new IOException("The private profile changed during indexed preparation.");
                    }
                }
                return true;
            }, cancellation).ConfigureAwait(false);
            await owner.PrepareScratchAsync(cancellation, preparationDeviceChanged).ConfigureAwait(false);
            preparationDeviceChanged?.Invoke(PathUtil.GetDeviceKey(owner._privatePaths[2]));
            await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(owner._privatePaths[2]), token =>
            {
                owner.CheckPreparation(2, token);
                AnalysisPrivateDirectory? history = null;
                try { history = OpenDirectory(owner._privatePaths[2]); }
                catch (Exception error) when (StorageUnavailable(error)) { owner.UseMemoryPreparation(); }
                try
                {
                    owner.PrepareProtection(2, token);
                    if (history is not null && !owner._ancestorGuard[2]) RequireSamePrivateRoot(history, owner._protection[2]!);
                }
                finally { if (history is not null) DisposePreparedOnWorker(history); }
                return true;
            }, cancellation).ConfigureAwait(false);
            if (!indexedStorage || owner.MemoryPreparationNotice is not null) await owner.CloseUnusedStorageAsync().ConfigureAwait(false);
            if (owner._profile != Guid.Empty)
            {
                preparationDeviceChanged?.Invoke("analysis-host-context");
                owner.Profile = await AnalysisIo.Run(services.Io, "analysis-host-context", _ => AnalysisNativeHostProfile.Read(owner._profile), cancellation).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested(); return owner;
        }
        catch { await owner.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private static bool StorageUnavailable(Exception error) => !AnalysisCloseUnconfirmedException.IsPresent(error) &&
        error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException;
    private static void DisposePreparedOnWorker(IDisposable source)
    {
        try { source.Dispose(); }
        catch (Exception error) when (error is not AnalysisCloseUnconfirmedException) { throw new AnalysisCloseUnconfirmedException(error); }
    }
    private void UseMemoryPreparation(string reason = "Secure private storage is unavailable.") =>
        MemoryPreparationNotice ??= reason + " Using bounded memory; existing private state was preserved.";

    private async Task PrepareScratchAsync(CancellationToken cancellation, Action<string>? deviceChanged)
    {
        string defaultPath = _privatePaths[DefaultScratchBoundary];
        if (!PathUtil.SafetyComparer.Equals(_privatePaths[1], defaultPath))
        {
            if (MemoryPreparationNotice is null)
            {
                string device = PathUtil.GetDeviceKey(_privatePaths[1]);
                deviceChanged?.Invoke(device);
                bool ready = await AnalysisIo.Run(_services.Io, device, token =>
                {
                    CheckPreparation(1, token);
                    AnalysisPrivateDirectory scratch;
                    try { _scratch = scratch = OpenDirectory(_privatePaths[1]); }
                    catch (Exception error) when (StorageUnavailable(error))
                    {
                        UseMemoryPreparation("The selected private scratch folder is unavailable.");
                        return false;
                    }
                    CheckPreparation(1, token);
                    PrepareProtection(1, token);
                    if (!_ancestorGuard[1])
                    {
                        RequireSamePrivateRoot(scratch, _protection[1]!);
                        return true;
                    }
                    DisposePreparedOnWorker(Interlocked.Exchange(ref _protection[1], null)!);
                    _ancestorGuard[1] = false; _privateWitnesses[1] = null;
                    DisposePreparedOnWorker(Interlocked.Exchange(ref _scratch, null)!);
                    return false;
                }, cancellation).ConfigureAwait(false);
                if (ready)
                {
                    string defaultDevice = PathUtil.GetDeviceKey(defaultPath);
                    deviceChanged?.Invoke(defaultDevice);
                    await AnalysisIo.Run(_services.Io, defaultDevice, token =>
                    {
                        CheckPreparation(DefaultScratchBoundary, token);
                        try { _ = File.GetAttributes(defaultPath); }
                        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return true; }
                        PrepareProtection(DefaultScratchBoundary, token);
                        return true;
                    }, cancellation).ConfigureAwait(false);
                    return;
                }
            }
            _privatePaths[1] = defaultPath;
        }

        string defaultScratchDevice = PathUtil.GetDeviceKey(defaultPath);
        deviceChanged?.Invoke(defaultScratchDevice);
        await AnalysisIo.Run(_services.Io, defaultScratchDevice, token =>
        {
            CheckPreparation(1, token);
            if (MemoryPreparationNotice is null)
            {
                try { _scratch = OpenDirectory(defaultPath); }
                catch (Exception error) when (StorageUnavailable(error)) { UseMemoryPreparation(); }
            }
            CheckPreparation(1, token); PrepareProtection(1, token);
            if (_scratch is { } directory && !_ancestorGuard[1]) RequireSamePrivateRoot(directory, _protection[1]!);
            return true;
        }, cancellation).ConfigureAwait(false);
    }
    private void CheckPreparation(int index, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_services.Io.IsStopped) throw new OperationCanceledException("Analysis preparation I/O has stopped.");
        if (_services.Io.GetHealth(PathUtil.GetDeviceKey(_privatePaths[index])) == DeviceHealth.NotResponding) throw new AnalysisDeviceUnavailableException();
    }
    private void PrepareProtection(int index, CancellationToken cancellation)
    {
        string path = _privatePaths[index];
        for (int depth = 0; depth <= AnalysisLimits.Depth; depth++)
        {
            CheckPreparation(index, cancellation);
            IAnalysisBackend? root = null;
            try { root = PrepareRoot(path); }
            catch (Exception error) when (StorageUnavailable(error)) { }
            if (root is not null)
            {
                try { CheckPreparation(index, cancellation); } catch { DisposePreparedOnWorker(root); throw; }
                if (root.RootEntry.Kind == AnalysisKind.Directory)
                {
                    _protection[index] = root; _ancestorGuard[index] = depth != 0;
                    _privateWitnesses[index] = root.Root with { Kind = root.RootEntry.Kind, ObservedObjectIdentity = root.RootEntry.Identity };
                    return;
                }
                DisposePreparedOnWorker(root); // Still on this preparation's actual admitted worker.
            }
            UseMemoryPreparation();
            path = Path.GetDirectoryName(path) ?? throw new IOException("A native private-state boundary could not be captured.");
        }
        throw new IOException("The native private-state boundary exceeds the preparation depth limit.");
    }

    internal static void RequireSamePrivateRoot(AnalysisPrivateDirectory directory, IAnalysisBackend protection)
    {
        if (!directory.MatchesRoot(protection)) throw new IOException("The private directory and its exclusion root identify different native objects.");
    }

    // Sources are already captured/reviewed native capabilities. Ownership transfers on successful
    // Start exactly as in AnalysisCoordinator; no source or profile is reconstructed from saved data.
    internal AnalysisSession Start(IReadOnlyList<IAnalysisBackend> sources, IProgress<AnalysisProgress>? progress = null,
        AnalysisComponentPreset components = AnalysisComponentPreset.Standard,
        ImmutableArray<AnalysisFolderExclusion> exclusions = default) => Start(sources, progress, components, null, exclusions);

    internal Task<FileCat.Platform.Windows.Analysis.WindowsAnalysisBackend.NtfsScan> PrepareNtfsAsync(string selection, nint owner,
        CancellationToken cancellation)
    {
        if (!_indexedStorage || MemoryPreparationNotice is not null || _services.Paths.IsPortable)
            throw new NotSupportedException("Fast NTFS analysis requires installed FileCat and verified private scratch. Use standard analysis here.");
        return AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(selection), token =>
            FileCat.Platform.Windows.Analysis.WindowsAnalysisBackend.PrepareNtfsDiscovery(selection, _services.Paths.IsPortable,
                _services.Paths.ElevationExchangeDirectory, owner, token), cancellation);
    }

    internal AnalysisSession StartNtfs(FileCat.Platform.Windows.Analysis.WindowsAnalysisBackend.NtfsScan scan,
        IProgress<AnalysisProgress>? progress, AnalysisComponentPreset components) => Start([scan.Source], progress, components, scan, default);

    private AnalysisSession Start(IReadOnlyList<IAnalysisBackend> sources, IProgress<AnalysisProgress>? progress,
        AnalysisComponentPreset components, IAnalysisNtfsScan? ntfs, ImmutableArray<AnalysisFolderExclusion> exclusions)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This indexed preparation already owns an acquisition or inventory load.");
            int sourceCount = sources.Count;
            if (sourceCount is < 1 or > AnalysisLimits.Roots) throw new ArgumentException("Choose between one and 32 prepared roots.", nameof(sources));
            if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
            var locations = ntfs is null ? sources.Select(RuntimeReopenLocator).ToArray() : [];
            var privateRoots = Enumerable.Range(0, _protection.Length).Where(index => _protection[index] is not null)
                .Select(index => new AnalysisPrivateRoot(PathUtil.GetDeviceKey(_privatePaths[index]), () => TakeProtection(index))
                    { RequiresDisjointSource = _ancestorGuard[index] }).ToImmutableArray();
            var storage = new AnalysisStorageOptions(PathUtil.GetDeviceKey(_privatePaths[1]),
                () => Interlocked.Exchange(ref _scratch, null) ?? throw new IOException("Private scratch ownership has already transferred."),
                privateRoots)
            { CreateProfileQuota = _indexedStorage && MemoryPreparationNotice is null ? OpenProfileQuota : null, ProfileDeviceKey = PathUtil.GetDeviceKey(_privatePaths[0]),
                AllowMemoryFallback = true, MemoryOnly = !_indexedStorage || MemoryPreparationNotice is not null };
            var coordinator = new AnalysisCoordinator(_services.Io, ProfileIdentity);
            if (ntfs is not null && _rescanPredecessor is not null)
                throw new InvalidOperationException("Fast NTFS analysis requires a new, explicitly approved session.");
            _session = ntfs is not null ? coordinator.StartNtfs(ntfs, storage, progress, components) :
                _rescanPredecessor is { } previous ? coordinator.Rescan(previous, sources, progress, components, storage) :
                coordinator.Start(sources, progress, components, storage, exclusions);
            _rescanPredecessor = null;
            _runtimeLocations = locations; _runtimeComponents = components;
            HistoryCompletion = CompleteHistoryAsync(_session);
            return _session;
        }
    }
    private async Task<AnalysisRollingCaptureOutcome?> CompleteHistoryAsync(AnalysisSession session)
    {
        await session.Completion.ConfigureAwait(false);
        return await _services.AnalysisHistory.CaptureCompletedAsync(session).ConfigureAwait(false);
    }
    private AnalysisProfileQuota OpenProfileQuota()
    {
        var directory = Interlocked.Exchange(ref _quotaDirectory, null) ?? throw new IOException("Private profile capacity ownership has already transferred.");
        try { return AnalysisProfileQuota.Open(_profile, directory); }
        catch { DisposePreparedOnWorker(directory); throw; } // Factory runs on the admitted storage worker.
    }
    private IAnalysisBackend TakeProtection(int index) => Interlocked.Exchange(ref _protection[index], null)
        ?? throw new IOException("Private exclusion ownership has already transferred.");
    private static IAnalysisBackend PrepareRoot(string path) => OperatingSystem.IsWindows()
        ? new FileCat.Platform.Windows.Analysis.WindowsAnalysisBackend(path) : new UnixAnalysisBackend(path);
    private static AnalysisPrivateDirectory OpenDirectory(string path)
    {
        string parent = Path.GetDirectoryName(path) ?? throw new IOException("The private profile parent is unavailable.");
        return OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(parent, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(parent, Path.GetFileName(path));
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = completion.Task;
        }
        _ = CompleteCloseAsync(completion); return new(completion.Task);
    }
    private async Task CompleteCloseAsync(TaskCompletionSource completion)
    {
        try { await CloseAsync().ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private async Task CloseAsync()
    {
        Exception? failure = null;
        void Remember(Exception error) => failure = AnalysisViewWork.PreserveCloseFailure(failure, error);
        try { await _imports.CloseAsync().ConfigureAwait(false); }
        catch (Exception error) { Remember(error); }
        try { await CloseRuntimeChangeHintsAsync().ConfigureAwait(false); }
        catch (Exception error) { Remember(error); }
        if (_session is { } session)
        {
            try { session.Stop(); }
            catch (Exception error) { Remember(error); }
            try { await session.Completion.ConfigureAwait(false); }
            catch (Exception error) { Remember(error); }
            try { await HistoryCompletion.ConfigureAwait(false); }
            catch (Exception error) { Remember(error); }
            if (_services.AnalysisHistory.UnconfirmedClose is { } historyClose) Remember(historyClose);
            try { session.Dispose(); }
            catch (Exception error) { Remember(error); }
            try { await session.DatasetCloseCompletion.ConfigureAwait(false); }
            catch (Exception error) { Remember(error); }
            if (session.SourceCloseFailures != 0)
                Remember(new AnalysisCloseUnconfirmedException(new IOException("Captured analysis sources reported unconfirmed closure.")));
        }
        for (int index = 0; index < _protection.Length; index++)
        {
            if (Interlocked.Exchange(ref _protection[index], null) is not { } protection) continue;
            try { await AnalysisIo.ReleaseAsync(_services.Io, protection).ConfigureAwait(false); }
            catch (Exception error) { Remember(error); }
        }
        try { await CloseUnusedStorageAsync().ConfigureAwait(false); } catch (Exception error) { Remember(error); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private async Task CloseUnusedStorageAsync()
    {
        Exception? failure = null;
        if (Interlocked.Exchange(ref _scratch, null) is { } scratch)
        {
            try { await AnalysisIo.ReleaseAsync(_services.Io, PathUtil.GetDeviceKey(_privatePaths[1]), scratch).ConfigureAwait(false); }
            catch (Exception error) { failure = AnalysisViewWork.PreserveCloseFailure(failure, error); }
        }
        if (Interlocked.Exchange(ref _quotaDirectory, null) is { } quotaDirectory)
        {
            try { await AnalysisIo.ReleaseAsync(_services.Io, PathUtil.GetDeviceKey(_privatePaths[0]), quotaDirectory).ConfigureAwait(false); }
            catch (Exception error) { failure = AnalysisViewWork.PreserveCloseFailure(failure, error); }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
