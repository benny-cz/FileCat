using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>An exact observation revision and frozen membership. This is not live binding or operation approval.</summary>
public sealed class AnalysisSelectionLease : IDisposable
{
    private AnalysisSnapshotLease? _lease;
    public AnalysisSelection Selection { get; }
    public AnalysisSnapshot Snapshot => _lease?.Snapshot ?? throw new ObjectDisposedException(nameof(AnalysisSelectionLease));
    public AnalysisTaint Taint => AnalysisPrivacy.Join(Snapshot.Taint);
    public long RawLocatorBytes { get; }
    private AnalysisSelectionLease(AnalysisSnapshotLease lease, AnalysisSelection selection, long bytes)
    { _lease = lease; Selection = selection; RawLocatorBytes = bytes; }

    public static AnalysisSelectionLease Capture(AnalysisSnapshotLease published, IEnumerable<ulong> members,
        AnalysisSelectionKind kind, string scopeSummary, string filterSummary)
    {
        if (!Enum.IsDefined(kind) || Encoding.UTF8.GetByteCount(scopeSummary) > AnalysisLimits.LocatorBytes ||
            Encoding.UTF8.GetByteCount(filterSummary) > AnalysisLimits.QueryBytes) throw new ArgumentException("Invalid captured selection description.");
        var lease = published.Retain();
        try
        {
            var snapshot = lease.Snapshot; var ids = new HashSet<ulong>();
            int enumerated = 0;
            foreach (ulong id in members)
            {
                if (++enumerated > AnalysisLimits.ReviewPaths) throw new InvalidOperationException("Refine selection to at most 100,000 captured entries.");
                if (id == 0) throw new ArgumentException("A real observation id is required.", nameof(members));
                ids.Add(id);
                if (ids.Count > AnalysisLimits.ReviewPaths) throw new InvalidOperationException("Refine selection to at most 100,000 real paths.");
            }
            var nodes = snapshot.Nodes.ToDictionary(n => n.Id); var roots = snapshot.Roots.ToDictionary(r => r.Id, StringComparer.Ordinal);
            if (ids.Any(id => !nodes.ContainsKey(id)) || kind == AnalysisSelectionKind.WholeFolder && (ids.Count != 1 || nodes[ids.Single()].Kind != AnalysisKind.Directory) ||
                kind == AnalysisSelectionKind.FilteredMembersOnly && ids.Any(id => nodes[id].Kind == AnalysisKind.Directory))
                throw new ArgumentException("Captured membership does not match its explicit selection kind.", nameof(members));
            long total = 0; var pathBytes = new Dictionary<ulong, long>();
            foreach (ulong id in ids)
            {
                var pending = new Stack<AnalysisNode>(); var current = nodes[id];
                while (!pathBytes.ContainsKey(current.Id))
                {
                    pending.Push(current);
                    if (pending.Count > AnalysisLimits.Depth + 1) throw new InvalidDataException("Invalid selection ancestry.");
                    if (current.ParentId == 0) break;
                    if (!nodes.TryGetValue(current.ParentId, out current!)) throw new InvalidDataException("Invalid selection ancestry.");
                }
                while (pending.TryPop(out var node))
                {
                    var root = roots[node.RootId];
                    long length = node.ParentId == 0 ? root.NameRules == AnalysisNameRules.Windows ? checked(root.CanonicalLocator.Length * 2L) : Encoding.UTF8.GetByteCount(root.CanonicalLocator)
                        : checked(pathBytes[node.ParentId] + (root.NameRules == AnalysisNameRules.Windows ? 2 : 1) + (node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name).Length : node.RawName.Length));
                    if (length > AnalysisLimits.LocatorBytes) throw new InvalidOperationException("A captured locator exceeds its limit.");
                    pathBytes[node.Id] = length;
                }
                total = checked(total + pathBytes[id]);
                if (total > AnalysisLimits.ReviewLocatorBytes) throw new InvalidOperationException("Refine selection to at most 32 MiB of raw locators.");
            }
            var ordered = ids.Order().ToImmutableArray();
            var captured = new AnalysisSelection(snapshot.Identity.SessionId, snapshot.Identity.Epoch, snapshot.Identity.Revision, ordered)
            {
                Kind = kind, ScopeSummary = scopeSummary, FilterSummary = filterSummary,
                ObjectIds = ordered.Select(id => nodes[id].ObjectId).Where(id => id is not null).Select(id => id!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray()
            };
            return new(lease, captured, total);
        }
        catch { lease.Dispose(); throw; }
    }
    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
}
