using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.App.ViewModels;

internal sealed record FastNtfsScopeReview(string Selection, string Volume, AnalysisComponentPreset Components);

public sealed partial class DiskSpaceViewModel
{
    private const string StandardOnly = "Use standard analysis here.";
    internal const string FastNtfsFolderExclusionsReason = "Fast NTFS reads the whole volume. Use standard analysis to apply folder exclusions.";
#if FILECAT_FAST_NTFS_QUALIFICATION
    private const bool InstalledFastNtfsQualification = true;
#else
    private const bool InstalledFastNtfsQualification = false;
#endif
    private string? _fastHelperProblem = "Checking the installed read-only NTFS helper…";
    private bool _fastHelperCheckStarted;
    private bool _fastNtfs;

    private string? FastNtfsConfigurationProblem => _services is null ? "Open a new live analysis to choose a scan."
        : !OperatingSystem.IsWindows() ? "Fast NTFS scan requires Windows and a local NTFS volume. " + StandardOnly
        : _services.Paths.IsPortable ? "Fast NTFS scan requires the installed Windows edition. " + StandardOnly
        : !_indexedStorage && !InstalledFastNtfsQualification ? new AnalysisCapabilities().For(AnalysisVerb.FastNtfs).Reason
        : AnalysisAdmission.Refusal();

    internal string? FastNtfsUnavailableReason => FastNtfsConfigurationProblem ?? _fastHelperProblem;
    internal bool FastNtfsInUse => _fastNtfs && _session is not null;
    internal bool CanPause => Running && (_rescanSession ?? _session)?.CanPause == true;

    internal async Task CheckFastNtfsHelperAsync()
    {
        if (_disposed || _services is null || FastNtfsConfigurationProblem is not null || _fastHelperCheckStarted) return;
        _fastHelperCheckStarted = true;
        using var actual = _actualWork.Enter();
        var token = _selectionLifetime.Token;
        string? problem;
        try
        {
            problem = await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(AppContext.BaseDirectory), _token =>
                ElevationBroker.LocateNtfsMetadata(_services.Paths.IsPortable, out var reason) is null ? reason : null, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { problem = "The installed read-only NTFS helper could not be verified. " + StandardOnly; }
        if (_disposed) return;
        _fastHelperProblem = problem;
        Changed?.Invoke();
    }

    internal Task StartFastNtfsAsync(IReadOnlyList<string> roots, nint owner,
        Func<FastNtfsScopeReview, CancellationToken, Task<bool>> confirm, ImmutableArray<AnalysisFolderExclusion> exclusions = default) =>
        StartAnalysisAsync(roots, new(owner, confirm), exclusions);

    private sealed record FastNtfsRequest(nint Owner, Func<FastNtfsScopeReview, CancellationToken, Task<bool>> Confirm);
}
