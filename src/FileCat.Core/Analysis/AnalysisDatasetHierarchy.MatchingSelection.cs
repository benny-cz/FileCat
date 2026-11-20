using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetHierarchy
{
    public async Task<AnalysisSelection> CaptureMatchingFilesAsync(ulong scope = 0, int maximumPaths = AnalysisLimits.ReviewPaths,
        CancellationToken cancellation = default)
    {
        if (maximumPaths is < 1 or > AnalysisLimits.ReviewPaths) throw new ArgumentOutOfRangeException(nameof(maximumPaths));
        using var admission = AnalysisExplorerAdmission.Acquire(); cancellation.ThrowIfCancellationRequested();
        return await _owned.UseAsync(async state =>
        {
            var cursor = await AnalysisIo.Run(_io, _device, token => new MatchingCapture(state, scope, maximumPaths, token), cancellation).ConfigureAwait(false);
            while (!cursor.Complete)
                await AnalysisIo.Run(_io, _device, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!cursor.Complete && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += cursor.Step(token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
            return new AnalysisSelection(identity.SessionId, identity.Epoch, identity.Revision, cursor.Ids.ToImmutable())
            {
                Kind = AnalysisSelectionKind.VisibleMatches,
                ScopeSummary = scope == 0 ? "Matching files in the captured analysis" : "Matching files in captured folder " + scope,
                FilterSummary = Filter is not { } filter ? "" : filter.Request.Summary +
                    (filter.Request.RootId is { } root ? " · captured root " + root : "") +
                    (filter.Request.CaseSensitive ? " · case sensitive" : "") + (filter.Request.UnknownOnly ? " · uncertain results" : "")
            };
        }).ConfigureAwait(false);
    }
    private sealed class MatchingCapture
    {
        private readonly State _state;
        private readonly ulong _scope;
        private readonly AnalysisIndexedPathRecord _folder;
        private readonly int _maximum;
        private ulong _position, _candidate, _walk;
        private int _depth;
        internal ImmutableArray<ulong>.Builder Ids { get; } = ImmutableArray.CreateBuilder<ulong>();
        internal bool Complete => _position == _state.Indexes[4].Count && _candidate == 0;
        internal MatchingCapture(State state, ulong scope, int maximum, CancellationToken token)
        {
            _state = state; _scope = scope; _maximum = maximum;
            if (scope != 0)
            {
                _folder = state.Graph.ReadPathRecord(scope, token);
                if (_folder.Node.Kind != AnalysisKind.Directory || state.SelectionFrame(scope, token)?.Visible != true)
                    throw new InvalidOperationException("Only a captured visible folder can define this selection scope.");
            }
            else if (state.Indexes[4].Count > (ulong)maximum) throw Limit();
        }
        internal long Step(CancellationToken token)
        {
            if (_candidate != 0)
            {
                var ancestor = _state.Graph.ReadPathRecord(_walk, token);
                if (++_depth > AnalysisLimits.Depth + 1) throw Invalid();
                if (ancestor.Node.Id == _scope) { Add(_candidate); _candidate = 0; }
                else if (ancestor.Depth <= _folder!.Depth || ancestor.Node.ParentId == 0) _candidate = 0;
                else _walk = ancestor.Node.ParentId;
                return AnalysisIndexedFilter.Cost(ancestor.Node);
            }
            var frame = _state.SelectionId(_state.Indexes[4].Read(++_position, token).Value, token);
            if (_scope == 0) { Add(frame); return 512; }
            var path = _state.Graph.ReadPathRecord(frame, token);
            if (path.RootIndex == _folder!.RootIndex && path.Depth > _folder.Depth)
            { _candidate = frame; _walk = path.Node.ParentId; _depth = 0; }
            return AnalysisIndexedFilter.Cost(path.Node);
        }
        private void Add(ulong id) { if (Ids.Count == _maximum) throw Limit(); Ids.Add(id); }
        private InvalidOperationException Limit() => new($"Refine this captured scope to at most {_maximum:N0} matching file paths. No matches were added.");
    }
}
