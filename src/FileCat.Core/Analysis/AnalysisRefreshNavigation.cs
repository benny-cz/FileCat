namespace FileCat.Core.Analysis;

/// <summary>Conservative navigation continuity. This never maps marks or operation authority.</summary>
public static partial class AnalysisRefreshNavigation
{
    public static string? Map(AnalysisTree previous, AnalysisSnapshot current, string key)
    {
        if (current.Identity.SessionId != previous.Snapshot.Identity.SessionId || current.Identity.Epoch != previous.Snapshot.Identity.Epoch + 1)
            return null;
        if (key == AnalysisTree.RootKey) return key;
        if (previous.Items.GetValueOrDefault(key)?.NodeId is not { } id) return null;
        var before = previous.Snapshot;
        var oldNodes = before.Nodes.ToDictionary(n => n.Id);
        var newNodes = current.Nodes.ToDictionary(n => n.Id);
        if (!oldNodes.TryGetValue(id, out var old) || old.ObservedIdentity is null || old.Times?.CreatedUtc is null || old.Times.CreatedPrecisionTicks is null)
            return null;
        var oldRoot = before.Roots.FirstOrDefault(r => r.Id == old.RootId);
        if (oldRoot is null || oldRoot.SourceIdentity is null || oldRoot.ObservedObjectIdentity is null) return null;
        var roots = current.Roots.Where(r => r.Scheme == oldRoot.Scheme && r.SourceIdentity == oldRoot.SourceIdentity &&
            r.CanonicalLocator == oldRoot.CanonicalLocator && r.ObservedObjectIdentity == oldRoot.ObservedObjectIdentity && r.NameRules == oldRoot.NameRules).ToArray();
        if (roots.Length != 1) return null;
        AnalysisNode? match = null;
        foreach (var node in current.Nodes)
        {
            if (node.RootId != roots[0].Id || node.ObservedIdentity != old.ObservedIdentity || node.Kind != old.Kind ||
                node.Times?.CreatedUtc != old.Times.CreatedUtc || node.Times?.CreatedPrecisionTicks != old.Times.CreatedPrecisionTicks ||
                !SamePath(old, node)) continue;
            if (match is not null) return null;
            match = node;
        }
        return match is null ? null : AnalysisTree.Key(match.Id);

        bool SamePath(AnalysisNode a, AnalysisNode b)
        {
            for (int depth = 0; depth <= AnalysisLimits.Depth; depth++)
            {
                if (a.Name != b.Name || a.NameEncoding != b.NameEncoding || !a.RawName.AsSpan().SequenceEqual(b.RawName.AsSpan())) return false;
                if (a.ParentId == 0 || b.ParentId == 0) return a.ParentId == b.ParentId;
                if (!oldNodes.TryGetValue(a.ParentId, out a!) || !newNodes.TryGetValue(b.ParentId, out b!)) return false;
            }
            return false;
        }
    }
}
