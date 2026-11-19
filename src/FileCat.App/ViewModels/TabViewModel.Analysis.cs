using System.Globalization;
using FileCat.App.Controls;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class TabViewModel
{
    private readonly object _analysisGate = new();
    private RetainedAnalysis? _retainedAnalysis;
    private AnalysisCompletion? _analysisCompletion;
    private CancellationTokenSource? _nameWait;

    private sealed record RetainedAnalysis(EntryStore Store, int Count, string FieldId,
        MetadataOrderSnapshot Values, MetadataService.SnapshotClaim Claim) : IDisposable
    {
        public void Dispose() { Claim.Dispose(); Values.Dispose(); }
    }
    private sealed record AnalysisCompletion(RetainedAnalysis Retained, AnalysisCounts Counts, int Version,
        TaskCompletionSource<bool> Accepted);

    private sealed class AnalysisCounts
    {
        public int Completed, Considered, Current, LastKnown, Absent, Gone, Denied, Placeholder, Unsupported, Failed, Partial;
        public bool Prepared;
        public void Add(MetadataValue value, bool placeholder)
        {
            Completed++;
            if (value.State == MetadataState.Available)
            {
                if (value.Observation == MetadataObservation.LastKnown) LastKnown++; else Current++;
                if (value.Value is FileDetailsFields.Cell { Partial: true }) Partial++;
            }
            else if (value.State == MetadataState.Absent)
            { if (value.Observation == MetadataObservation.Gone) Gone++; else Absent++; }
            else if (value.State == MetadataState.Denied) Denied++;
            else if (value.State == MetadataState.Unsupported)
            { if (placeholder) Placeholder++; else Unsupported++; }
            else if (value.State == MetadataState.Failed) Failed++;
        }
    }

    private static string PartialMetadataOrder(string title) =>
        $"Sorted by {title} using the results read so far; unavailable values sort last within their folder/file partition. Choose View → Analyze folder to analyze the listed items.";

    private MetadataOrderSnapshot NewMetadataSnapshot(int? characters = null) => new(Services.ListingIndexes,
        Services.MetadataSnapshotMaxRows, characters ?? Services.MetadataSnapshotMaxCharacters);

    private MetadataOrderSnapshot CaptureMetadataOrder(EntryStore store, int count, string fieldId, CancellationToken ct)
    {
        if (Listing.MetadataKeys is { } input)
            return MetadataOrderSnapshot.Capture(store, count, fieldId, input, Services.ListingIndexes, ct);
        RetainedAnalysis? retained;
        IDisposable? lease = null;
        lock (_analysisGate)
        {
            retained = _retainedAnalysis;
            if (retained is not null && ReferenceEquals(retained.Store, store) && retained.FieldId == fieldId && retained.Claim.IsCurrent)
                lease = retained.Values.Lease();
            else retained = null;
        }
        try
        {
            var result = CaptureMetadataOrderCore(store, count, fieldId, retained, ct);
            if (retained is null || retained.Claim.TryPublish(() => { })) return result;
            result.Dispose(); // A fresh publication won the race; old retained owners cannot override it.
            return CaptureMetadataOrderCore(store, count, fieldId, null, ct);
        }
        finally { lease?.Dispose(); }
    }

    private MetadataOrderSnapshot CaptureMetadataOrderCore(EntryStore store, int count, string fieldId,
        RetainedAnalysis? retained, CancellationToken ct)
    {
        var field = Services.Metadata.Field(fieldId);
        var location = Location;
        var comparison = CultureInfo.CurrentCulture.CompareInfo;
        int remainingChars = Math.Max(0, Services.MetadataSnapshotMaxCharacters - (retained?.Values.RetainedCharacters ?? 0));
        bool principalField = fieldId is "owner" or "group", nameCharacterLimit = false;
        using var names = principalField ? Services.PrincipalNames.Capture(Services.ListingIndexes, remainingChars, out nameCharacterLimit) : null;
        var snapshot = NewMetadataSnapshot(remainingChars - (names?.Characters ?? 0));
        try
        {
            if (principalField && names is null)
            {
                snapshot.Limit(nameCharacterLimit ? MetadataCapacity.Characters : MetadataCapacity.SharedMemory);
                snapshot.Seal();
                return snapshot;
            }
            if (location is not null)
            {
                var provider = Services.Providers.For(location);
                bool slow = IsSlowLocation;
                using var scan = new EntryStore.Scan(store, count);
                for (int i = 0; i < count; i++)
                {
                    if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                    MetadataValue value = default;
                    if (retained is not null && !retained.Values.TryValue(i, out value)) continue;
                    var entry = scan.Materialize(i);
                    if (entry.Kind == EntryKind.Parent) continue;
                    IComparable? key = null;
                    if (fieldId == ColumnSpec.FolderSortKey)
                        value = new(MetadataState.Available, key = GetFolderText(entry));
                    else if (retained is not null)
                    {
                        if (!MetadataApplies(entry, fieldId) || entry.Has(EntryFlags.Offline) && field?.MetadataOnlyOnOffline != true)
                            value = Services.Metadata.Peek(fieldId, "", entry, slow);
                    }
                    else
                    {
                        if (field is null || !MetadataApplies(entry, fieldId) ||
                            field.ObservationLane && !HasFilesystemMetadataOrigin(location, entry) ||
                            provider.GetItemRef(location, entry).FileSystemPath is not { } path) continue;
                        value = Services.Metadata.Peek(fieldId, path, entry,
                            slow || field.ObservationLane && SlowMetadataOrigin(path, PathUtil.GetDeviceKey(path)));
                        if (value.State is MetadataState.NotRequested or MetadataState.Pending) continue;
                    }
                    PrincipalName? name = null;
                    if (value.State == MetadataState.Available)
                    {
                        if (value.Value is Principal p)
                            name = names?.Answers.GetValueOrDefault(p) ?? new PrincipalName(NameStatus.NotRequested);
                        else if (field?.ObservationLane != true || value.Observation == MetadataObservation.Current)
                            key ??= retained?.Values.Key(i) ?? field?.CaptureKey(value) ?? value.Value as IComparable;
                    }
                    // Verification reports contain arbitrary detail/check graphs. Ordering retains only the
                    // existing scalar rank and observation state, never that report graph.
                    if (value.Value is Core.Verification.VerificationResult) value = value with { Value = null };
                    if (!snapshot.TryAdd(i, value, key, name, comparison) && snapshot.Capacity != MetadataCapacity.None) break;
                }
            }
            snapshot.NameSummary = DescribeNames(snapshot.PrincipalNames, fieldId == "group" ? "groups" : "owners");
            snapshot.Seal();
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    private static string? DescribeNames(IReadOnlyDictionary<Principal, (NameStatus Status, PrincipalOrderKey Key)>? names, string noun)
    {
        if (names is null) return null;
        var counts = new int[5];
        foreach (var answer in names.Values) counts[(int)answer.Status]++;
        var parts = new List<string>();
        if (counts[(int)NameStatus.NotFound] > 0) parts.Add($"{counts[(int)NameStatus.NotFound]:N0} {noun} have no name here");
        if (counts[(int)NameStatus.Unavailable] > 0) parts.Add($"{counts[(int)NameStatus.Unavailable]:N0} name lookups are unavailable");
        if (counts[(int)NameStatus.Pending] > 0) parts.Add($"{counts[(int)NameStatus.Pending]:N0} name lookups are pending");
        if (counts[(int)NameStatus.NotRequested] > 0) parts.Add($"{counts[(int)NameStatus.NotRequested]:N0} {noun} have no cached name");
        return parts.Count == 0 ? null : string.Join("; ", parts) + $". These {noun} are ordered by ID until you sort again.";
    }

    private void RetireMetadataAnalysis(bool reportPartial = true)
    {
        bool active = _analysis is not null;
        _analysis?.Cancel();
        _analysis = null;
        _nameWait?.Cancel();
        AnalysisStatus = null;
        RetainedAnalysis? retained;
        lock (_analysisGate)
        {
            retained = _retainedAnalysis;
            _retainedAnalysis = null;
        }
        _analysisCompletion?.Accepted.TrySetResult(false);
        _analysisCompletion = null;
        retained?.Dispose();
        if (active || retained is not null)
            Banner = reportPartial && Listing.Sort is { Field: SortField.Metadata, MetadataId: { } id }
                ? PartialMetadataOrder(Services.Metadata.Field(id)?.Title ?? id) : null;
    }

    private void CheckMetadataAnalysis()
    {
        RetainedAnalysis? retained;
        lock (_analysisGate) retained = _retainedAnalysis;
        if (retained is null) return;
        if (!ReferenceEquals(retained.Store, Listing.Store) || !retained.Claim.IsCurrent ||
            Listing.Sort.Field != SortField.Metadata || Listing.Sort.MetadataId != retained.FieldId)
        {
            bool changed = !retained.Claim.IsCurrent;
            RetireMetadataAnalysis();
            if (changed) Banner = PartialMetadataOrder(Services.Metadata.Field(retained.FieldId)?.Title ?? retained.FieldId);
            return;
        }
        var completion = _analysisCompletion;
        if (retained.Store.Count != retained.Count)
        {
            _analysisCompletion = null;
            completion?.Accepted.TrySetResult(false);
            Banner = PartialMetadataOrder(Services.Metadata.Field(retained.FieldId)?.Title ?? retained.FieldId);
            return; // Previously retained observations remain usable; appended rows have fixed unknown keys.
        }
        if (completion is null) return;
        if (Listing.ViewSpecVersion != completion.Version)
        {
            _analysisCompletion = null;
            completion.Accepted.TrySetResult(false);
            Banner = PartialMetadataOrder(Services.Metadata.Field(retained.FieldId)?.Title ?? retained.FieldId);
            return;
        }
        if (Listing.AppliedViewSpecVersion != completion.Version) return;
        bool accepted = false;
        retained.Claim.TryPublish(() => retained.Store.TryAtCount(retained.Count, () =>
        {
            Banner = DescribeAnalysis(retained.FieldId, completion.Counts, Listing.AppliedMetadataCoverage);
            _analysisCompletion = null;
            accepted = true;
        }));
        completion.Accepted.TrySetResult(accepted);
        if (!accepted) CheckMetadataAnalysis();
    }

    private string DescribeAnalysis(string id, AnalysisCounts counts, MetadataOrderCoverage? coverage)
    {
        string title = Services.Metadata.Field(id)?.Title ?? id;
        string text = $"Read {title} for {counts.Completed:N0} items: {counts.Current:N0} current values" +
            (counts.LastKnown > 0 ? $", {counts.LastKnown:N0} last-known values." : ".");
        var outcomes = new List<string>();
        if (counts.Partial > 0) outcomes.Add($"{counts.Partial:N0} inventories with incomplete category coverage");
        if (counts.Absent > 0) outcomes.Add($"{counts.Absent:N0} without a value");
        if (counts.Gone > 0) outcomes.Add($"{counts.Gone:N0} gone");
        if (counts.Denied > 0) outcomes.Add($"{counts.Denied:N0} with no access");
        if (counts.Placeholder > 0) outcomes.Add($"{counts.Placeholder:N0} placeholders not read");
        if (counts.Unsupported > 0) outcomes.Add($"{counts.Unsupported:N0} unsupported");
        if (counts.Failed > 0) outcomes.Add($"{counts.Failed:N0} failed");
        if (outcomes.Count > 0) text += " " + string.Join("; ", outcomes) + ".";
        int r = coverage?.Count ?? 0;
        text += r == counts.Completed ? $" The order retains all {r:N0} items' results." : $" The order retains {r:N0} of {counts.Completed:N0} items' results.";
        MetadataCapacity capacity = coverage?.Capacity ?? MetadataCapacity.SharedMemory;
        lock (_analysisGate)
            if (_retainedAnalysis is { } retained) capacity |= retained.Values.Capacity;
        if (capacity != MetadataCapacity.None)
        {
            var limits = new List<string>();
            if (capacity.HasFlag(MetadataCapacity.Rows)) limits.Add("Row");
            if (capacity.HasFlag(MetadataCapacity.Characters)) limits.Add("Character");
            if (capacity.HasFlag(MetadataCapacity.SharedMemory)) limits.Add("Shared-memory");
            text += " " + string.Join("/", limits) + " limit reached; unretained items have unknown keys and sort last within their folder/file partition.";
        }
        else if (r < counts.Completed) text += " Unretained items have unknown keys and sort last within their folder/file partition.";
        else if (outcomes.Count > 0) text += " Unavailable values sort last within their folder/file partition.";
        if (coverage?.Names is { } names) text += " " + names;
        if (!Columns.Any(c => c.MetadataId == id)) text += " Add this column in Settings → Columns to see its values.";
        return text;
    }

    public async Task AnalyzeAsync(string fieldId)
    {
        if (Location is not { } location || !CanAnalyzeMetadata(fieldId) || Services.Metadata.Field(fieldId) is not { } field) return;
        bool originalDevices = field.ObservationLane && !location.IsFileSystem;
        RetireMetadataAnalysis();
        if (Listing.Sort.Field != SortField.Metadata || Listing.Sort.MetadataId != fieldId)
            ApplyExplicitSort(Listing.Sort with { Field = SortField.Metadata, MetadataId = fieldId, Descending = false });
        using var cts = new CancellationTokenSource();
        _analysis = cts;
        var provider = Services.Providers.For(location);
        var store = Listing.Store;
        int count = store.Count;
        bool slow = IsSlowLocation, left = false, metadataChanged = false, transferred = false;
        var origin = new object();
        var claim = Services.Metadata.TrackSnapshot(fieldId, origin, location.IsFileSystem ? location.Path : null);
        // Observations must leave room for copied key/name references in the final ordering snapshot.
        // Both shares remain below the same total character ceiling; neither is an allocation target.
        var values = NewMetadataSnapshot(Services.MetadataSnapshotMaxCharacters / 4);
        var retained = new RetainedAnalysis(store, count, fieldId, values, claim);
        var counts = new AnalysisCounts();
        AnalysisStatus = $"Analyzing {field.Title}: preparing… (Esc cancels)";
        void Moved(object? sender, ListingChange change)
        {
            if (ReferenceEquals(store, Listing.Store) && Equals(location, Location) &&
                Listing.Sort.Field == SortField.Metadata && Listing.Sort.MetadataId == fieldId) return;
            left = true;
            cts.Cancel();
        }
        void ClosedTab() { left = true; cts.Cancel(); }
        Listing.Changed += Moved;
        Closed += ClosedTab;
        IDisposable? storeLease = null, workerLease = null;
        Task? work = null;
        try
        {
            storeLease = store.Lease();
            workerLease = values.Lease();
            bool Analyze(CancellationToken ct)
            {
                using var scan = new EntryStore.Scan(store, count);
                counts.Considered = count;
                counts.Prepared = true;
                long last = Environment.TickCount64;
                for (int i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!claim.IsCurrent) { metadataChanged = true; throw new OperationCanceledException(); }
                    var e = scan.Materialize(i);
                    if (e.Kind == EntryKind.Parent) continue;
                    bool applicable = MetadataApplies(e, fieldId);
                    var path = applicable && (!originalDevices || HasFilesystemMetadataOrigin(location, e))
                        ? provider.GetItemRef(location, e).FileSystemPath : null;
                    // Inapplicability is a completed missing value, retained in the same order snapshot.
                    // It must not start a metadata read or disappear from the folder's result count.
                    var value = !applicable ? new MetadataValue(MetadataState.Absent)
                        : path is null ? new MetadataValue(MetadataState.Unsupported)
                        : Services.Metadata.Compute(fieldId, path, e, ct,
                            slow || originalDevices && SlowMetadataOrigin(path, PathUtil.GetDeviceKey(path)), origin);
                    ct.ThrowIfCancellationRequested();
                    if (value.State is MetadataState.NotRequested or MetadataState.Pending) throw new OperationCanceledException();
                    if (!claim.IsCurrent) { metadataChanged = true; throw new OperationCanceledException(); }
                    counts.Add(value, e.Has(EntryFlags.Offline));
                    if (value.Value is Core.Verification.VerificationResult)
                        values.TryAdd(i, value with { Value = null }, field.SortKey?.Invoke(value.Value));
                    else values.TryAdd(i, value, field.ObservationLane ? field.CaptureKey(value) : null);
                    if (Environment.TickCount64 - last >= 200)
                    {
                        last = Environment.TickCount64;
                        int completed = counts.Completed, considered = counts.Considered;
                        Services.Ui.Post(() =>
                        {
                            if (!left && ReferenceEquals(_analysis, cts)) AnalysisStatus = $"Analyzing {field.Title}: {completed:N0} of {considered:N0}… (Esc cancels)";
                        });
                    }
                }
                values.Seal();
                return true;
            }
            // A result container has no filesystem device. The coordinator stays outside the I/O workers;
            // Compute admits and owns each qualified row on the device of that row's captured path.
            if (originalDevices)
            {
                var token = cts.Token;
                work = Task.Run(() => Analyze(token), token);
            }
            else if (field.ObservationLane) work = Core.Records.FileObservationRequests.RunOwned(Services.Io,
                provider.GetDeviceKey(location), IoPriority.Background, Analyze, cts.Token);
            else work = Services.Io.RunOwned(provider.GetDeviceKey(location), IoPriority.Background, Analyze, cts.Token);
            var ownedStore = storeLease;
            var ownedValues = workerLease;
            storeLease = workerLease = null;
            _ = work.ContinueWith(_ => { ownedValues.Dispose(); ownedStore.Dispose(); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await work.WaitAsync(cts.Token);
            if (fieldId is "owner" or "group")
            {
                AnalysisStatus = $"Analyzing {field.Title}: looking up names… (Esc sorts now)";
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                _nameWait = wait;
                try
                {
                    long started = Environment.TickCount64;
                    while (Environment.TickCount64 - started < 10_000 &&
                        !await Services.PrincipalNames.WhenIdle(TimeSpan.FromMilliseconds(100), wait.Token))
                    {
                        if (!claim.IsCurrent) { metadataChanged = true; throw new OperationCanceledException(); }
                    }
                }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested) { }
                finally { if (ReferenceEquals(_nameWait, wait)) _nameWait = null; }
            }
            cts.Token.ThrowIfCancellationRequested();
            var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool attached = false;
            claim.TryPublish(() => store.TryAtCount(count, () =>
            {
                if (left || !ReferenceEquals(_analysis, cts) || !ReferenceEquals(store, Listing.Store) ||
                    Listing.Sort.Field != SortField.Metadata || Listing.Sort.MetadataId != fieldId) return;
                lock (_analysisGate) _retainedAnalysis = retained;
                _analysisCompletion = new(retained, counts, Listing.ViewSpecVersion + 1, accepted);
                transferred = attached = true;
                ExplicitSortRequested?.Invoke(Listing.ViewSpecVersion + 1);
                Listing.Resort();
            }));
            if (!attached)
            {
                metadataChanged = !claim.IsCurrent;
                throw new OperationCanceledException();
            }
            AnalysisStatus = $"Analyzing {field.Title}: sorting… (Esc cancels)";
            await accepted.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!left && ReferenceEquals(_analysis, cts))
            {
                if (transferred) RetireMetadataAnalysis(reportPartial: false);
                Banner = metadataChanged || !claim.IsCurrent ? "Analysis stopped because metadata changed; the order remains partial." :
                    store.Count != count ? "Analysis stopped because the listing changed; the order remains partial." :
                    counts.Prepared ? $"Analysis canceled after {counts.Completed:N0} of {counts.Considered:N0} considered items; the order remains partial. No retained success snapshot is published." :
                    $"Analysis canceled after {counts.Completed:N0} of {Math.Max(0, count - (count > 0 && store[0].Kind == EntryKind.Parent ? 1 : 0)):N0} listed rows; the considered-item count was not ready. The order remains partial. No retained success snapshot is published.";
            }
        }
        finally
        {
            Listing.Changed -= Moved;
            Closed -= ClosedTab;
            storeLease?.Dispose(); workerLease?.Dispose();
            if (!transferred) retained.Dispose();
            if (ReferenceEquals(_analysis, cts)) { _analysis = null; AnalysisStatus = null; }
        }
    }

    public bool CancelAnalysis()
    {
        if (_analysis is null || AnalysisStatus is null) return false;
        if (_nameWait is { } wait) wait.Cancel(); else _analysis.Cancel();
        return true;
    }
}
