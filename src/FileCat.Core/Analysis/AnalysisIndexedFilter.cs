using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisFilterRequest(string Text, bool CaseSensitive = false, bool UnknownOnly = false, string? RootId = null)
{
    public AnalysisRootDisplayIds? RootDisplayIds { get; init; }
}
public sealed record AnalysisFilterSummary(AnalysisFilterRequest Request, ulong DefinitePaths, ulong UnknownPaths);

/// <summary>Construction-only membership, bounded globally to eight MiB. No source or operation capability.</summary>
internal sealed class AnalysisIndexedFilter : IDisposable, IAnalysisRootMembership
{
    private static readonly object BudgetGate = new();
    private static long Reserved;
    private readonly long _reservation;
    private readonly AnalysisIndexedGraphReader _source;
    private readonly bool _comparison;
    private byte[]? _truth, _context;
    private readonly Dictionary<string, Scope> _scopes = new(StringComparer.Ordinal);
    private readonly HashSet<ulong> _ancestors = [];
    private readonly List<string> _parts = [];
    private AnalysisNode? _node;
    private ulong _walk;
    private int _depth, _characters;
    private bool _textAvailable, _walking;
    internal readonly AnalysisFilter Filter;
    internal readonly AnalysisFilterRequest Request;
    internal ulong DefinitePaths, UnknownPaths;
    internal bool NeedsPath => (Filter.Needs & AnalysisFilterNeeds.Path) != 0;
    internal bool NeedsAllocation => (Filter.Needs & AnalysisFilterNeeds.Allocation) != 0;
    internal bool NeedsObject => (Filter.Needs & (AnalysisFilterNeeds.Object | AnalysisFilterNeeds.State)) != 0;
    internal bool NeedsRootWalk => _scopes.Values.Any(s => s.Known && s.Ancestor.HasValue);
    internal bool NamespaceOnly => Filter.NamespaceOnly && !Request.UnknownOnly;
    internal bool IsUnfiltered => string.IsNullOrWhiteSpace(Request.Text) && Request.RootId is null;
    private sealed record Scope(string? Root, ulong? Ancestor, bool Known);
    private AnalysisIndexedFilter(AnalysisFilter filter, AnalysisFilterRequest request, AnalysisIndexedGraphReader source, bool comparison)
    {
        Filter = filter; Request = request; _source = source; _comparison = comparison; ulong paths = comparison ? 0 : source.Count(AnalysisIndexedGraph.Paths);
        int truths = checked((int)((paths + 3) / 4)), contexts = checked((int)((paths + 7) / 8)); _reservation = truths + contexts;
        lock (BudgetGate)
        {
            if (_reservation > AnalysisLimits.PendingBytes - Reserved) throw new IOException("The shared filter-membership memory budget is in use.");
            Reserved += _reservation;
        }
        try { _truth = new byte[truths]; _context = new byte[contexts]; }
        catch { lock (BudgetGate) Reserved -= _reservation; throw; }
    }
    internal static async Task<AnalysisIndexedFilter> CreateAsync(AnalysisIndexedGraphReader source, AnalysisFilterRequest request, DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation, bool comparison = false)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Text);
        if (request.RootId is not null && !source.Header.Roots.Any(r => r.Id == request.RootId)) throw new FormatException("Choose a captured root ID.");
        if (string.IsNullOrWhiteSpace(request.Text) && request.RootId is null) request = request with { UnknownOnly = false };
        var filter = await AnalysisIo.Run(io, "analysis-filter-calendar", _ =>
        {
            TimeZoneInfo? zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(source.Header.Identity.TimeZoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = null; }
            return AnalysisFilter.Parse(request.Text, zone ?? TimeZoneInfo.Utc, request.CaseSensitive, roots: source.Header.Roots, allowDateOnly: zone is not null,
                rootDisplayIds: request.RootDisplayIds, comparison: comparison);
        }, cancellation).ConfigureAwait(false);
        var result = new AnalysisIndexedFilter(filter, request, source, comparison);
        try
        {
            foreach (var root in source.Header.Roots) result._scopes[root.Id] = new(root.Disposition == AnalysisRootDisposition.Active ? root.Id : null, null,
                root.Disposition is AnalysisRootDisposition.Active or AnalysisRootDisposition.ExcludedPrivateState);
            if (request.RootId is not null || (filter.Needs & AnalysisFilterNeeds.Roots) != 0)
                foreach (var root in source.Header.Roots.Where(r => r.Disposition == AnalysisRootDisposition.Covered))
                    await result.ResolveAsync(source, root, io, device, cancellation).ConfigureAwait(false);
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    // Covered roots have at most32 bindings. Resolve by observed identity AND the recorded namespace alias.
    private async Task ResolveAsync(AnalysisIndexedGraphReader source, RootBinding root, DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation)
    {
        if (root.CoveredByRootId is not { } covering || root.CoveredRelativePath is not { } relative || root.ObservedObjectIdentity is not { } identity) return;
        string[] parts = relative.Length == 0 ? [] : relative.Split('/'); if (parts.Length > AnalysisLimits.Depth) return;
        ulong at = 1, exact = 0, folded = 0; int exactCount = 0, foldedCount = 0;
        AnalysisNode? candidate = null; ulong current = 0; int part = -1, depth = 0; bool equal = true, insensitive = true;
        while (at <= source.Count(AnalysisIndexedGraph.Paths) || candidate is not null)
            await AnalysisIo.Run(io, device, token =>
            {
                int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                while ((at <= source.Count(AnalysisIndexedGraph.Paths) || candidate is not null) && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested();
                    if (candidate is null)
                    {
                        var node = source.ReadPathRow(at++, token); bytes += Cost(node);
                        if (node.RootId != covering || node.Kind != root.Kind) continue;
                        string? observed = node.ObservedIdentity;
                        if (observed is null && node.ObjectId is { } id)
                        {
                            var range = source.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
                            if (range.Count != 1) throw new InvalidDataException("Ambiguous covered-root object.");
                            observed = source.ReadObject(source.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token).VerifiedIdentity; bytes += 10_240;
                        }
                        if (observed != identity) continue;
                        candidate = node; current = node.Id; part = parts.Length - 1; depth = 0; equal = insensitive = true;
                    }
                    else
                    {
                        var node = source.ReadPath(current, token); bytes += Cost(node);
                        if (++depth > AnalysisLimits.Depth + 1) throw new InvalidDataException("Invalid covered-root ancestry.");
                        if (node.ParentId == 0)
                        {
                            if (part == -1 && equal) { exact = candidate.Id; exactCount = Math.Min(2, exactCount + 1); }
                            if (part == -1 && insensitive) { folded = candidate.Id; foldedCount = Math.Min(2, foldedCount + 1); }
                            candidate = null;
                        }
                        else if (part < 0 || node.NameEncoding != "utf16le" && !AnalysisRawNames.HasLosslessText(node)) candidate = null;
                        else
                        {
                            equal &= node.Name.Equals(parts[part], StringComparison.Ordinal); insensitive &= node.Name.Equals(parts[part], StringComparison.OrdinalIgnoreCase);
                            part--; current = node.ParentId; if (!insensitive) candidate = null;
                        }
                    }
                    if (clock.ElapsedMilliseconds >= 20) break;
                }
                return true;
            }, cancellation).ConfigureAwait(false);
        ulong match = exactCount == 1 ? exact : exactCount == 0 && foldedCount == 1 ? folded : 0;
        if (match != 0) _scopes[root.Id] = new(covering, match, true);
    }
    internal void Begin(AnalysisNode node)
    {
        _node = node; _ancestors.Clear(); _ancestors.Add(node.Id); _parts.Clear(); _walk = node.Id; _depth = _characters = 0; _textAvailable = true;
        _walking = NeedsPath || NeedsRootWalk;
    }
    internal bool Walking => _walking;
    internal long EvaluationBytes => 1024L + (NeedsPath && _textAvailable ? _characters * 2L : 0);
    internal long Walk(AnalysisIndexedGraphReader source, CancellationToken token)
    {
        var node = source.ReadPath(_walk, token); if (++_depth > AnalysisLimits.Depth + 1) throw new InvalidDataException("Invalid filter ancestry.");
        if (node.ParentId == 0) _walking = false;
        else { _ancestors.Add(node.ParentId); _walk = node.ParentId; }
        if (node.ParentId != 0 || _comparison && node.Kind != AnalysisKind.Directory)
        {
            if (NeedsPath)
            {
                _textAvailable &= AnalysisRawNames.HasLosslessText(node); _characters = checked(_characters + node.Name.Length + 1);
                if (_characters > AnalysisLimits.LocatorBytes / 2) { _textAvailable = false; _parts.Clear(); }
                else if (_textAvailable) _parts.Add(node.Name);
            }
        }
        return Cost(node);
    }
    public AnalysisTruth IsMember(string root)
    {
        if (_node is null || !_scopes.TryGetValue(root, out var scope) || !scope.Known) return AnalysisTruth.Unknown;
        if (_node.RootId != scope.Root) return AnalysisTruth.False;
        return !scope.Ancestor.HasValue || _ancestors.Contains(scope.Ancestor.Value) ? AnalysisTruth.True : AnalysisTruth.False;
    }
    internal AnalysisTruth Evaluate(AnalysisObject? obj, AnalysisMeasurement allocation, MeasurementState active, AnalysisFreshness freshness)
        => EvaluateCore(obj, allocation, active, freshness != AnalysisFreshness.Observed,
            _node?.MetadataState == MeasurementState.Failed || active == MeasurementState.Failed, null, null);
    internal AnalysisTruth EvaluateComparison(AnalysisObject? obj, AnalysisMeasurement allocation, MeasurementState active, bool fresh, string change, AnalysisDelta? delta)
        => EvaluateCore(obj, allocation, active, !fresh, _node?.MetadataState == MeasurementState.Failed, change, delta);
    private AnalysisTruth EvaluateCore(AnalysisObject? obj, AnalysisMeasurement allocation, MeasurementState active, bool stale, bool unreadable, string? change, AnalysisDelta? delta)
    {
        if (_walking || _node is null) throw new InvalidOperationException("The filter path is not ready.");
        string path = NeedsPath && _textAvailable ? string.Join('/', _parts.AsEnumerable().Reverse()) : "";
        var result = Filter.Evaluate(new(_node, obj, path, allocation, Change: change, Stale: stale,
            Unreadable: unreadable, ActiveState: active, RelativePathTextAvailable: _textAvailable, ExactDelta: delta) { RootMembership = this });
        return Request.RootId is { } root ? AnalysisFilter.And(result, IsMember(root)) : result;
    }
    internal bool NamespaceMatches(AnalysisNode node, IEnumerable<ulong> ancestors, AnalysisFreshness freshness)
    {
        if (!NamespaceOnly) return false;
        _node = node; _ancestors.Clear(); _ancestors.Add(node.Id); foreach (ulong id in ancestors) _ancestors.Add(id);
        _walking = false; _textAvailable = true;
        return Evaluate(null, AnalysisMeasurement.Missing(MeasurementState.Unsupported), MeasurementState.Known, freshness) == AnalysisTruth.True;
    }
    internal void Record(ulong path, AnalysisTruth truth, CancellationToken cancellation = default)
    {
        path = _source.PathRow(path, cancellation);
        int at = checked((int)((path - 1) / 4)), shift = (int)((path - 1) % 4) * 2;
        _truth![at] = (byte)((_truth[at] & ~(3 << shift)) | ((int)truth << shift));
        if (truth == AnalysisTruth.True) DefinitePaths++; else if (truth == AnalysisTruth.Unknown) UnknownPaths++;
    }
    internal AnalysisTruth Truth(ulong path, CancellationToken cancellation = default)
    { path = _source.PathRow(path, cancellation); return (AnalysisTruth)((_truth![checked((int)((path - 1) / 4))] >> ((int)((path - 1) % 4) * 2)) & 3); }
    internal bool Matches(ulong path, CancellationToken cancellation = default) => Truth(path, cancellation) == (Request.UnknownOnly ? AnalysisTruth.Unknown : AnalysisTruth.True);
    internal void Context(ulong path, CancellationToken cancellation = default)
    { path = _source.PathRow(path, cancellation); _context![checked((int)((path - 1) / 8))] |= (byte)(1 << (int)((path - 1) % 8)); }
    internal bool IsContext(ulong path, CancellationToken cancellation = default)
    { path = _source.PathRow(path, cancellation); return (_context![checked((int)((path - 1) / 8))] & (1 << (int)((path - 1) % 8))) != 0; }
    internal AnalysisFilterSummary Summary => new(Request, DefinitePaths, UnknownPaths);
    public void Dispose()
    {
        if (_truth is null) return; _truth = _context = null; _node = null; _parts.Clear(); _ancestors.Clear();
        lock (BudgetGate) Reserved -= _reservation;
    }
    internal static long Cost(AnalysisNode node) => 1024L + node.Name.Length * 2L + node.RawName.Length;
}
