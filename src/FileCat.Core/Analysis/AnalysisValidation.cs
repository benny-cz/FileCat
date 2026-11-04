using System.Globalization;
using System.Text;

namespace FileCat.Core.Analysis;

public static class AnalysisValidation
{
    public static void Validate(AnalysisSnapshot s, CancellationToken cancellation = default)
    {
        if (s.Roots.Length is < 1 or > AnalysisLimits.Roots || s.Nodes.Length > AnalysisLimits.Observations ||
            s.Objects.Length > AnalysisLimits.Observations || s.Issues.Length > AnalysisLimits.Issues ||
            s.Identity.Epoch < 0 || s.Identity.Revision < 0 || s.Identity.ObservedUtc < s.Identity.StartedUtc)
            throw Invalid();
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in s.Roots)
        {
            if (root.Id.Length is < 1 or > 256 || !roots.Add(root.Id) || root.Scheme is not ("file" or "sftp" or "ftp" or "archive") ||
                Encoding.UTF8.GetByteCount(root.CanonicalLocator) > AnalysisLimits.LocatorBytes ||
                Encoding.UTF8.GetByteCount(root.DisplayLocator) > AnalysisLimits.LocatorBytes || root.CanonicalLocator.Contains('\0')) throw Invalid();
        }
        var objects = new HashSet<string>(StringComparer.Ordinal);
        var objectMap = new Dictionary<string, AnalysisObject>(StringComparer.Ordinal);
        foreach (var obj in s.Objects)
        {
            cancellation.ThrowIfCancellationRequested();
            if (obj.Id.Length is < 1 or > 512 || !objects.Add(obj.Id) || obj.VerifiedIdentity?.Length > 4096 || obj.VersionEvidence?.Length > 4096) throw Invalid();
            Measurement(obj.LogicalLength); Measurement(obj.LinkCount);
            objectMap.Add(obj.Id, obj);
        }
        var nodes = new Dictionary<ulong, AnalysisNode>();
        var depths = new Dictionary<ulong, int>();
        foreach (var node in s.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            if (node.Id == 0 || node.Id == node.ParentId || !nodes.TryAdd(node.Id, node) || !roots.Contains(node.RootId) || !Enum.IsDefined(node.Kind) ||
                node.Name.Length == 0 || node.Name.Contains('\0') || node.NameEncoding is not ("utf8" or "utf16le" or "unix-bytes") ||
                node.RawName.Length > AnalysisLimits.RawNameBytes || Encoding.UTF8.GetByteCount(node.Name) > AnalysisLimits.RawNameBytes) throw Invalid();
            if (node.ParentId != 0)
            {
                if (!nodes.TryGetValue(node.ParentId, out var parent) || parent.Kind != AnalysisKind.Directory || parent.RootId != node.RootId ||
                    !AnalysisNames.IsOrdinaryChild(node.Name, windows: false)) throw Invalid();
            }
            int depth = node.ParentId == 0 ? 0 : depths[node.ParentId] + 1;
            if (depth > AnalysisLimits.Depth) throw Invalid();
            depths.Add(node.Id, depth);
            if (node.Kind == AnalysisKind.File && (node.ObjectId is null || !objects.Contains(node.ObjectId)) ||
                node.Kind != AnalysisKind.File && node.ObjectId is not null) throw Invalid();
        }
        var factIds = new HashSet<string>(StringComparer.Ordinal);
        var factObjects = new Dictionary<string, List<StorageFact>>(StringComparer.Ordinal);
        foreach (var f in s.Facts)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!factIds.Add(f.Id) || f.Id.Length is < 1 or > 512 || f.ComponentId.Length is < 1 or > 256 ||
                f.BasisId.Length is < 1 or > 256 || f.Method.Length is < 1 or > 256 || f.Includes.Length > AnalysisLimits.Components ||
                (f.ObjectId is null) == (f.DirectoryId is null)) throw Invalid();
            if (f.ObjectId is { } o && !objects.Contains(o) || f.DirectoryId is { } d && (!nodes.TryGetValue(d, out var n) || n.Kind != AnalysisKind.Directory)) throw Invalid();
            Measurement(f.Measurement);
            string key = f.ObjectId is { } objectId ? "o:" + objectId : "d:" + f.DirectoryId;
            if (!factObjects.TryGetValue(key, out var list)) factObjects[key] = list = [];
            if (list.Count >= AnalysisLimits.Components) throw Invalid();
            if (f.Includes.Any(i => i.Length is < 1 or > 256) || f.Includes.Distinct(StringComparer.Ordinal).Count() != f.Includes.Length) throw Invalid();
            list.Add(f);
        }
        foreach (var list in factObjects.Values) AnalysisAggregate.SelectComponents(list.ToArray());
        foreach (var issue in s.Issues)
            if (issue.Code.Length is < 1 or > 256 || issue.NodeId.HasValue && !nodes.ContainsKey(issue.NodeId.Value) || !Enum.IsDefined(issue.State)) throw Invalid();
    }

    public static UInt128 Unsigned(string text)
    {
        if (text.Length is < 1 or > 39 || text.Length > 1 && text[0] == '0' || text.Any(c => !char.IsAsciiDigit(c)) ||
            !UInt128.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) throw Invalid();
        return value;
    }
    public static ulong Id(string text)
    {
        var value = Unsigned(text);
        if (value > ulong.MaxValue) throw Invalid();
        return (ulong)value;
    }
    private static void Measurement(AnalysisMeasurement m)
    {
        if (!Enum.IsDefined(m.State) || m.State == MeasurementState.Known != m.Value.HasValue) throw Invalid();
    }
    private static InvalidDataException Invalid() => new("Invalid analysis record or relationship.");
}
