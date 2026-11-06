using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

/// <summary>
/// Retains native private-profile preparation through an indexed acquisition. Internal until the
/// native source, IPC-domain, resource and package gates allow the public acquisition switch.
/// </summary>
internal sealed class AnalysisIndexedAcquisition : IAsyncDisposable
{
    private readonly AppServices _services;
    private readonly string[] _privatePaths;
    private readonly IAnalysisBackend?[] _protection = new IAnalysisBackend?[2];
    private readonly object _gate = new();
    private AnalysisPrivateDirectory? _scratch;
    private AnalysisSession? _session;
    private Task? _close;
    private Guid _profile;
    internal AnalysisHostProfileIdentity Profile { get; private set; } = null!;
    private AnalysisIndexedAcquisition(AppServices services)
    { _services = services; _privatePaths = [services.Paths.AnalysisSnapshotsDirectory, services.Paths.AnalysisScratchDirectory]; }

    internal static async Task<AnalysisIndexedAcquisition> PrepareAsync(AppServices services, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        cancellation.ThrowIfCancellationRequested(); var owner = new AnalysisIndexedAcquisition(services);
        try
        {
            await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(owner._privatePaths[0]), token =>
            {
                var directory = OpenDirectory(owner._privatePaths[0]);
                using var store = new AnalysisPrivateInventoryStore(directory);
                owner._profile = new AnalysisPrivateProfileState(store).LoadOrCreate(token);
                owner._protection[0] = PrepareRoot(owner._privatePaths[0]);
                RequireSamePrivateRoot(directory, owner._protection[0]!);
                // Revalidate the original anchored store after preparing the exclusion capability.
                // A substituted private directory cannot supply the UUID for another directory.
                if (new AnalysisPrivateProfileState(store).LoadOrCreate(token) != owner._profile)
                    throw new IOException("The private profile changed during indexed preparation.");
                return true;
            }, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(services.Io, PathUtil.GetDeviceKey(owner._privatePaths[1]), token =>
            {
                token.ThrowIfCancellationRequested(); owner._scratch = OpenDirectory(owner._privatePaths[1]);
                owner._protection[1] = PrepareRoot(owner._privatePaths[1]);
                RequireSamePrivateRoot(owner._scratch, owner._protection[1]!); return true;
            }, cancellation).ConfigureAwait(false);
            owner.Profile = await AnalysisIo.Run(services.Io, "analysis-host-context", _ => AnalysisNativeHostProfile.Read(owner._profile), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); return owner;
        }
        catch { await owner.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal static void RequireSamePrivateRoot(AnalysisPrivateDirectory directory, IAnalysisBackend protection)
    {
        if (!directory.MatchesRoot(protection)) throw new IOException("The private directory and its exclusion root identify different native objects.");
    }

    // Sources are already captured/reviewed native capabilities. Ownership transfers on successful
    // Start exactly as in AnalysisCoordinator; no source or profile is reconstructed from saved data.
    internal AnalysisSession Start(IReadOnlyList<IAnalysisBackend> sources, IProgress<AnalysisProgress>? progress = null,
        AnalysisComponentPreset components = AnalysisComponentPreset.Standard)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null) throw new InvalidOperationException("This indexed preparation already owns an acquisition.");
            var storage = new AnalysisStorageOptions(PathUtil.GetDeviceKey(_privatePaths[1]),
                () => Interlocked.Exchange(ref _scratch, null) ?? throw new IOException("Private scratch ownership has already transferred."),
                [new(PathUtil.GetDeviceKey(_privatePaths[0]), () => TakeProtection(0)), new(PathUtil.GetDeviceKey(_privatePaths[1]), () => TakeProtection(1))])
            { CreateProfileQuota = () => AnalysisProfileQuota.Open(_profile) };
            return _session = new AnalysisCoordinator(_services.Io, Profile.ToString()).Start(sources, progress, components, storage);
        }
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
        if (_session is { } session)
        {
            try { session.Stop(); }
            catch (Exception error) { failure = error; }
            try { await session.Completion.ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
            try { session.Dispose(); }
            catch (Exception error) { failure ??= error; }
            try { await session.DatasetCloseCompletion.ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
        }
        for (int index = 0; index < _protection.Length; index++)
        {
            if (Interlocked.Exchange(ref _protection[index], null) is not { } protection) continue;
            try { await AnalysisIo.ReleaseAsync(_services.Io, protection).ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
        }
        if (Interlocked.Exchange(ref _scratch, null) is { } scratch)
        {
            try { await AnalysisIo.ReleaseAsync(_services.Io, PathUtil.GetDeviceKey(_privatePaths[1]), scratch).ConfigureAwait(false); }
            catch (Exception error) { failure ??= error; }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
