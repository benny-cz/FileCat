namespace FileCat.Core.Analysis;

internal enum AnalysisNtfsOutcome { Finished, Partial, Canceled }

/// <summary>One trusted, explicitly selected volume. Discovery data cannot implement this capability.</summary>
internal interface IAnalysisNtfsScan
{
    IAnalysisBackend Source { get; }
    UInt128 RootId { get; }
    void ValidateVolume(CancellationToken cancellation);
    // Null means the protection root was proved to be on a different volume. Uncertain identity must throw.
    UInt128? GetPrivateBoundaryId(IAnalysisBackend protection);
    Task<AnalysisNtfsOutcome> CaptureAsync(AnalysisNtfsDiscovery discovery, CancellationToken cancellation);
}

public sealed partial class AnalysisCoordinator
{
    internal AnalysisSession StartNtfs(IAnalysisNtfsScan scan, AnalysisStorageOptions storage, IProgress<AnalysisProgress>? progress = null)
    {
        RequireAdmission();
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(storage);
        var source = scan.Source;
        ValidateStart([source], AnalysisComponentPreset.Standard, storage);
        if (storage.MemoryOnly || scan.RootId == 0 || source.Root.Assurance != IdentityAssurance.Verified ||
            source.Root.NameRules != AnalysisNameRules.Windows || source.RootEntry.Kind != AnalysisKind.Directory ||
            source.RootEntry.Cloud == true || source.RootEntry.Identity is null)
            throw new InvalidOperationException("Fast NTFS analysis requires a held volume and verified private scratch. Standard analysis is available separately.");
        return new(io, [source], progress, _profileIdentity, AnalysisComponentPreset.Standard, storage, ntfs: scan);
    }
}

public sealed partial class AnalysisSession
{
    private readonly IAnalysisNtfsScan? _ntfs;
    private AnalysisTaint SourceTaint => _ntfs is null ? AnalysisTaint.OrdinarySensitive : AnalysisPrivacy.Join(AnalysisTaint.PrivilegedDerived);

    private void FailNtfsDiscovery(MeasurementState state)
    {
        Issue("ntfs-discovery-unavailable", state: state, rootId: _roots[0].Id);
        lock (_sync) if (_phase != AnalysisPhase.Stopped) _phase = AnalysisPhase.Failed;
    }

    private async Task WalkNtfsAsync(IAnalysisNtfsScan scan, RootBinding root)
    {
        // No helper starts before verified private capture and all exclusion witnesses are ready.
        if (_writer is null || _protectedSources.Contains(scan.Source) || IsProtected(scan.Source.RootEntry))
        { FailNtfsDiscovery(MeasurementState.Unsupported); return; }
        var token = _cancel.Token;
        var protectedIds = new HashSet<UInt128>();
        foreach (var protection in _protected)
        {
            // A qualified ancestor guard has already required disjointness in both directions.
            var id = await AnalysisIo.Run(_io, new(scan.Source.IoDeviceKey, protection.IoDeviceKey), _ => scan.GetPrivateBoundaryId(protection), token).ConfigureAwait(false);
            if (id is { } value)
            {
                if (value == 0 || value == scan.RootId) throw new AnalysisSourceChangedException();
                protectedIds.Add(value);
            }
        }

        await PauseBoundaryAsync().ConfigureAwait(false);
        await AnalysisIo.Run(_io, scan.Source.IoDeviceKey, token => { scan.ValidateVolume(token); return true; }, token).ConfigureAwait(false);
        ulong rootPath = await AppendObservationAsync(scan.Source.RootEntry, 0, root.Id, scan.Source.Basis).ConfigureAwait(false)
            ?? throw new IOException("The private analysis store could not record the approved NTFS root.");
        await using var catalog = await OnStorageDeviceAsync(() => _writer!.CreateNtfsDiscoveryAsync(scan.RootId, token)).ConfigureAwait(false);
        var capture = await OnStorageDeviceAsync(() => scan.CaptureAsync(catalog, token)).ConfigureAwait(false);
        if (capture == AnalysisNtfsOutcome.Canceled) { Stop(); return; }
        if (capture == AnalysisNtfsOutcome.Partial) Issue("ntfs-discovery-record-limit", rootPath, MeasurementState.Unsupported, root.Id);
        Issue("ntfs-discovery-alias-coverage-incomplete", rootPath, MeasurementState.Unsupported, root.Id);
        await FlushCaptureIssuesAsync().ConfigureAwait(false);

        ulong total = await OnStorageDeviceAsync(() => catalog.CountAsync(token)).ConfigureAwait(false), inspected = 0;
        // Only one metadata page and at most one page of compact directory tickets per ancestor stay
        // in memory. Parent IDs are unique, and the verified root is absent from the input catalog.
        const int pageRecords = 32;
        var stack = new Stack<NtfsDirectoryCursor>();
        stack.Push(new(new(scan.RootId, rootPath, root.CanonicalLocator.Length * 2), 0));
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (stack.TryPeek(out var parent))
        {
            token.ThrowIfCancellationRequested();
            await PauseBoundaryAsync().ConfigureAwait(false);
            if (parent.Branches.TryDequeue(out var child)) { stack.Push(new(child, parent.Depth + 1)); continue; }
            if (parent.Offset is not { } offset) { stack.Pop(); continue; }
            var page = await OnStorageDeviceAsync(() => catalog.ReadChildrenAsync(parent.Directory.Id, offset, token, pageRecords)).ConfigureAwait(false);
            parent.Offset = page.NextOffset;
            inspected = checked(inspected + (ulong)page.Observations.Length + (uint)page.AmbiguousNames);
            if (page.AmbiguousNames != 0) Issue("ntfs-discovery-ambiguous-name", parent.Directory.Path, MeasurementState.Changed, root.Id);
            var entries = new List<AnalysisEntry>(page.Observations.Length);
            var observations = new List<(UInt128 Id, int LocatorBytes)>(page.Observations.Length);
            var now = DateTimeOffset.UtcNow;
            foreach (var row in page.Observations)
            {
                if (protectedIds.Contains(row.ObjectId))
                { Issue("private-analysis-state-excluded", parent.Directory.Path, MeasurementState.Excluded, root.Id); continue; }
                int locatorBytes = checked(parent.Directory.LocatorBytes + sizeof(char) + row.RawName.Length);
                if (locatorBytes > AnalysisLimits.LocatorBytes)
                { Issue("frontier-read-witness-limit", parent.Directory.Path, MeasurementState.Unsupported, root.Id); continue; }
                entries.Add(row.ToEntry(now));
                observations.Add((row.ObjectId, locatorBytes));
            }
            int remaining = AnalysisLimits.Observations - _capturedPaths;
            bool limit = entries.Count > remaining;
            if (limit) entries.RemoveRange(remaining, entries.Count - remaining);
            var ids = await AppendCapturedBatchAsync(entries, parent.Directory.Path, root.Id, scan.Source.Basis).ConfigureAwait(false);
            for (int i = 0; i < ids.Length; i++)
            {
                var entry = entries[i];
                if (entry.IssueCode is { } reason) Issue(reason, ids[i], entry.MetadataState, root.Id);
                if (entry.Kind != AnalysisKind.Directory || !entry.IncludedByPolicy || entry.MetadataState != MeasurementState.Known || entry.Cloud == true) continue;
                if (parent.Depth + 1 >= AnalysisLimits.Depth) Issue("depth-limit", ids[i], MeasurementState.Unsupported, root.Id);
                else parent.Branches.Enqueue(new(observations[i].Id, ids[i], observations[i].LocatorBytes));
            }
            await FlushCaptureIssuesAsync().ConfigureAwait(false);
            if (limit) { Issue("memory-or-observation-limit", rootPath, MeasurementState.Unsupported, root.Id); return; }
            if (elapsed.ElapsedMilliseconds >= 250) { Report(); elapsed.Restart(); }
        }
        if (inspected > total) throw new InvalidDataException("The NTFS discovery tree accounted for a record more than once.");
        if (inspected != total) Issue("ntfs-discovery-unreachable-or-excluded", rootPath, MeasurementState.Unsupported, root.Id);
    }

    private readonly record struct NtfsDirectory(UInt128 Id, ulong Path, int LocatorBytes);
    private sealed class NtfsDirectoryCursor(NtfsDirectory directory, int depth)
    {
        internal NtfsDirectory Directory { get; } = directory;
        internal int Depth { get; } = depth;
        internal ulong? Offset { get; set; } = 0;
        internal Queue<NtfsDirectory> Branches { get; } = new();
    }
}
