using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    /// <summary>Captures bounded real IDs from this immutable query, without expanding hidden aliases or granting source authority.</summary>
    public Task<AnalysisSelection> CaptureMembersAsync(string? groupKey = null, int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) =>
        CaptureMembersCoreAsync(groupKey, maximumPaths, false, cancellation);
    public Task<AnalysisSelection> CaptureMatchingFilesAsync(string? groupKey = null, int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) =>
        CaptureMembersCoreAsync(groupKey, maximumPaths, true, cancellation);
    private async Task<AnalysisSelection> CaptureMembersCoreAsync(string? groupKey, int maximumPaths, bool filesOnly, CancellationToken cancellation)
    {
        if (maximumPaths is < 1 or > AnalysisLimits.ReviewPaths) throw new ArgumentOutOfRangeException(nameof(maximumPaths));
        cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await _owned.UseAsync(async state =>
        {
            var capture = await AnalysisIo.Run(_io, _device, token => new MemberCapture(state, groupKey, maximumPaths, token, filesOnly), cancellation).ConfigureAwait(false);
            while (!capture.Complete)
                await AnalysisIo.Run(_io, _device, token => { capture.Batch(token); return true; }, cancellation).ConfigureAwait(false);
            return await AnalysisIo.Run(_io, _device, token =>
            {
                token.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
                return new AnalysisSelection(identity.SessionId, identity.Epoch, identity.Revision, capture.Ids.Order().ToImmutableArray())
                {
                    Kind = filesOnly ? AnalysisSelectionKind.VisibleMatches : AnalysisSelectionKind.RealGroupMembers, ScopeSummary = (filesOnly ? "Matching files in captured scope: " : "") + capture.Label,
                    FilterSummary = Filter is not { } filter ? "" : filter.Request.Text +
                        (filter.Request.RootId is { } root ? " · captured root " + root : "") +
                        (filter.Request.CaseSensitive ? " · case sensitive" : "") + (filter.Request.UnknownOnly ? " · uncertain results" : "")
                };
            }, cancellation).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private sealed class MemberCapture
    {
        private readonly AnalysisScratchVectorCheckpoint[] _placements;
        private readonly AnalysisSortedIndexReader? _order;
        private readonly AnalysisIndexRange _range;
        private readonly byte[]? _hash;
        private readonly int _maximum;
        private readonly State _state;
        private readonly bool _filesOnly;
        private int _collection;
        private ulong _position;
        internal readonly HashSet<ulong> Ids = [];
        internal string Label { get; }
        internal bool Complete => _collection == _placements.Length;
        internal MemberCapture(State state, string? key, int maximum, CancellationToken token, bool filesOnly = false)
        {
            _maximum = maximum; _state = state; _filesOnly = filesOnly;
            if (key is null || key == AnalysisTree.RootKey)
            {
                _placements = [state.MemberPlacements!, state.ReferencePlacements!]; Label = "All captured groups";
            }
            else
            {
                var group = state.Group(key, token); bool reference = group.Kind != AnalysisGroupKind.Entries;
                if (!filesOnly && group.Members > (ulong)maximum) throw Limit();
                _hash = Hash(key); _order = reference ? state.ReferenceOrder! : state.MemberOrder!;
                _placements = [reference ? state.ReferencePlacements! : state.MemberPlacements!];
                _range = _order.PrefixRange(Join(_hash, [(byte)AnalysisTableSort.ActiveMetric]), token);
                if (_range.Count != group.Members) throw Invalid(); Label = group.Definition.Label;
            }
        }
        internal void Batch(CancellationToken token)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew(); long bytes = 0;
            for (int records = 0; !Complete && records < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes; records++)
            {
                token.ThrowIfCancellationRequested(); var vector = _placements[_collection];
                ulong count = _order is null ? vector.Count : _range.Count;
                if (_position == count) { _collection++; _position = 0; continue; }
                ulong at = _order is null ? ++_position : _order.Read(_range.First + _position++, token).Value;
                byte[] data = vector.Read(at, token); bytes += data.Length * 3L + 256;
                var placement = DecodePlacement(data);
                if (placement.Path == 0 || _hash is not null && !placement.GroupHash.AsSpan().SequenceEqual(_hash)) throw Invalid();
                if (_filesOnly)
                {
                    var path = _state.Graph.ReadPath(placement.Path, token); bytes += AnalysisIndexedFilter.Cost(path);
                    if (path.Kind != AnalysisKind.File || !path.IncludedByPolicy) continue;
                }
                if (Ids.Add(placement.Path) && Ids.Count > _maximum) throw Limit();
                if (timer.ElapsedMilliseconds >= 20) break;
            }
        }
        private InvalidOperationException Limit() => new($"Refine this captured selection to at most {_maximum:N0} real paths, or choose a smaller group.");
    }
}
