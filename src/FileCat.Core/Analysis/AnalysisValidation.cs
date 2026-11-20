using System.Globalization;
using System.Text;

namespace FileCat.Core.Analysis;

public static partial class AnalysisValidation
{
    public static void Validate(AnalysisSnapshot s, CancellationToken cancellation = default)
    {
        if (s is null || s.Identity is null || s.Policy is null || s.IssueTotals is null || s.BoundaryTotals is null || s.Roots.IsDefault || s.Nodes.IsDefault || s.Objects.IsDefault || s.Facts.IsDefault || s.Issues.IsDefault ||
            s.Roots.Length is < 1 or > AnalysisLimits.Roots || s.Nodes.Length > AnalysisLimits.Observations || s.Facts.Length > AnalysisLimits.InventoryRecords ||
            s.Objects.Length > AnalysisLimits.Observations || s.Issues.Length > AnalysisLimits.Issues ||
            s.Identity.SessionId == Guid.Empty || s.Identity.Epoch < 0 || s.Identity.Revision < 0 || s.Identity.ObservedUtc < s.Identity.StartedUtc ||
            !Enum.IsDefined(s.Phase) || !Enum.IsDefined(s.Coverage) || !Enum.IsDefined(s.Freshness) || !Enum.IsDefined(s.Origin) || !Enum.IsDefined(s.Persistence) ||
            s.Taint == 0 || (s.Taint & ~AnalysisPrivacy.All) != 0 || !Text(s.Identity.HostProfile, 4096) || !Text(s.Identity.TimeZoneId, 256))
            throw Invalid();
        Policy(s.Policy);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var rootMap = new Dictionary<string, RootBinding>(StringComparer.Ordinal);
        foreach (var root in s.Roots)
        {
            if (root is null || !Text(root.Id, 256) || !roots.Add(root.Id) || root.Scheme is not ("file" or "sftp" or "ftp" or "archive") ||
                !Enum.IsDefined(root.Assurance) || !Enum.IsDefined(root.Kind) || !Enum.IsDefined(root.NameRules) || !Enum.IsDefined(root.Disposition) ||
                !Text(root.Label, AnalysisLimits.RawNameBytes) || !Text(root.CanonicalLocator, AnalysisLimits.LocatorBytes) || !Text(root.DisplayLocator, AnalysisLimits.LocatorBytes) ||
                !Optional(root.SourceIdentity, 4096) || !Optional(root.ObservedObjectIdentity, 4096) || !Optional(root.ConnectionIdentityReference, 4096) ||
                root.TraversalBoundary is not ("stay-on-source" or "explicit-mounts") || root.Assurance == IdentityAssurance.Verified && root.SourceIdentity is null ||
                Encoding.UTF8.GetByteCount(root.CanonicalLocator) > AnalysisLimits.LocatorBytes ||
                Encoding.UTF8.GetByteCount(root.DisplayLocator) > AnalysisLimits.LocatorBytes || root.CanonicalLocator.Contains('\0')) throw Invalid();
            if (root.Scheme is "sftp" or "ftp")
                foreach (string locator in new[] { root.CanonicalLocator, root.DisplayLocator })
                    if (Uri.TryCreate(locator, UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0) throw Invalid();
            rootMap.Add(root.Id, root);
            if (root.Disposition is AnalysisRootDisposition.UnavailablePrivateBoundary or AnalysisRootDisposition.ExcludedPrivateState &&
                (root.Assurance != IdentityAssurance.Unknown || root.SourceIdentity is not null || root.ObservedObjectIdentity is not null)) throw Invalid();
        }
        foreach (var root in s.Roots)
            if (root.Disposition == AnalysisRootDisposition.Covered
                ? root.CoveredByRootId is not { } parent || parent == root.Id || !rootMap.TryGetValue(parent, out var covered) || covered.Disposition != AnalysisRootDisposition.Active ||
                    root.ObservedObjectIdentity is null || root.CoveredRelativePath is not { } path || Encoding.UTF8.GetByteCount(path) > AnalysisLimits.LocatorBytes ||
                    path.Length > 0 && path.Split('/').Any(part => !AnalysisNames.IsObservedChild(part, root.NameRules == AnalysisNameRules.Windows,
                        root.NameRules == AnalysisNameRules.Windows ? checked(part.Length * 2) : Encoding.UTF8.GetByteCount(part)))
                : root.CoveredByRootId is not null || root.CoveredRelativePath is not null) throw Invalid();
        var objects = new HashSet<string>(StringComparer.Ordinal);
        var objectMap = new Dictionary<string, AnalysisObject>(StringComparer.Ordinal);
        foreach (var obj in s.Objects)
        {
            cancellation.ThrowIfCancellationRequested();
            if (obj is null || !Text(obj.Id, 512) || !objects.Add(obj.Id) || !Optional(obj.VerifiedIdentity, 4096) || !Optional(obj.VersionEvidence, 4096) ||
                obj.ModifiedPrecisionTicks == 0 || obj.ObservedAliases.IsDefault) throw Invalid();
            Measurement(obj.LogicalLength); Measurement(obj.LinkCount);
            objectMap.Add(obj.Id, obj);
        }
        var nodes = new Dictionary<ulong, AnalysisNode>();
        var depths = new Dictionary<ulong, int>();
        var siblings = new HashSet<(string, ulong, string, string)>();
        var rootNodes = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
        foreach (var node in s.Nodes)
        {
            cancellation.ThrowIfCancellationRequested();
            if (node is null || node.RawName.IsDefault || node.IssueCodes.IsDefault || node.Id == 0 || node.Id == node.ParentId || !nodes.TryAdd(node.Id, node) || !roots.Contains(node.RootId) || !Enum.IsDefined(node.Kind) ||
                !Enum.IsDefined(node.MetadataState) || !node.IncludedByPolicy != (node.MetadataState == MeasurementState.Excluded) ||
                node.Name is null || node.Name.Length == 0 || node.Name.Contains('\0') || node.NameEncoding is not ("utf8" or "utf16le" or "unix-bytes") ||
                node.RawName.Length > AnalysisLimits.RawNameBytes || node.Name.Length > AnalysisLimits.RawNameBytes || !Optional(node.ObservedIdentity, 4096)) throw Invalid();
            byte[] raw = node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name) : node.RawName.ToArray();
            if (AnalysisRawNames.Decode(node.NameEncoding, raw) != node.Name || !siblings.Add((node.RootId, node.ParentId, node.NameEncoding, Convert.ToBase64String(raw)))) throw Invalid();
            if (node.Times is { } times && (times.ObservedUtc < s.Identity.StartedUtc || times.ObservedUtc > s.Identity.ObservedUtc || times.ModifiedPrecisionTicks == 0 ||
                times.CreatedUtc.HasValue != times.CreatedPrecisionTicks.HasValue || times.ChangedUtc.HasValue != times.ChangedPrecisionTicks.HasValue ||
                times.CreatedPrecisionTicks == 0 || times.ChangedPrecisionTicks == 0)) throw Invalid();
            if (node.ParentId != 0)
            {
                if (!nodes.TryGetValue(node.ParentId, out var parent) || parent.Kind != AnalysisKind.Directory || !parent.IncludedByPolicy || parent.MetadataState != MeasurementState.Known || parent.RootId != node.RootId ||
                    !AnalysisNames.IsObservedChild(node.Name, rootMap[node.RootId].NameRules == AnalysisNameRules.Windows, raw.Length)) throw Invalid();
            }
            else if (!rootNodes.Add(node.RootId) || rootMap[node.RootId].Disposition != AnalysisRootDisposition.Active || rootMap[node.RootId].Kind != node.Kind) throw Invalid();
            int depth = node.ParentId == 0 ? 0 : depths[node.ParentId] + 1;
            if (depth > AnalysisLimits.Depth) throw Invalid();
            depths.Add(node.Id, depth);
            if (node.Kind == AnalysisKind.File && (node.ObjectId is null || !objects.Contains(node.ObjectId)) ||
                node.Kind != AnalysisKind.File && node.ObjectId is not null) throw Invalid();
            if (node.ObjectId is { } oid)
            {
                if (node.ObservedIdentity is not null && objectMap[oid].VerifiedIdentity != node.ObservedIdentity) throw Invalid();
                if (node.MetadataState != MeasurementState.Known && (node.ObservedIdentity is not null ||
                    objectMap[oid].VerifiedIdentity is not null || objectMap[oid].LogicalLength.State != node.MetadataState)) throw Invalid();
                if (!aliases.TryGetValue(oid, out var members)) aliases[oid] = members = [];
                members.Add(node.Id);
            }
        }
        foreach (var obj in s.Objects)
            if (obj.ObservedAliases.Length > 0)
            {
                if (!aliases.TryGetValue(obj.Id, out var members) || members.Count != obj.ObservedAliases.Length) throw Invalid();
                var declared = obj.ObservedAliases.ToHashSet();
                if (declared.Count != members.Count || members.Any(id => !declared.Contains(id))) throw Invalid();
            }
        var factIds = new HashSet<string>(StringComparer.Ordinal);
        var factObjects = new Dictionary<string, List<StorageFact>>(StringComparer.Ordinal);
        foreach (var f in s.Facts)
        {
            cancellation.ThrowIfCancellationRequested();
            if (f is null || f.Includes.IsDefault || !factIds.Add(f.Id) || !Text(f.Id, 512) || !Text(f.ComponentId, 256) ||
                !Text(f.BasisId, 256) || !Text(f.Method, 256) || f.Includes.Length > AnalysisLimits.Components ||
                (f.ObjectId is null) == (f.DirectoryId is null)) throw Invalid();
            if (f.ObjectId is { } o && !objects.Contains(o) || f.DirectoryId is { } d && (!nodes.TryGetValue(d, out var n) || n.Kind != AnalysisKind.Directory)) throw Invalid();
            Measurement(f.Measurement);
            string key = f.ObjectId is { } objectId ? "o:" + objectId : "d:" + f.DirectoryId;
            if (!factObjects.TryGetValue(key, out var list)) factObjects[key] = list = [];
            if (list.Count >= AnalysisLimits.Components) throw Invalid();
            if (f.Includes.Any(i => !Text(i, 256)) || f.Includes.Distinct(StringComparer.Ordinal).Count() != f.Includes.Length) throw Invalid();
            list.Add(f);
        }
        foreach (var list in factObjects.Values) AnalysisAggregate.SelectComponents(list.ToArray());
        var issueReferences = new HashSet<(ulong, string)>();
        foreach (var issue in s.Issues)
        {
            if (issue is null || !Text(issue.Code, 256) || issue.NodeId.HasValue && !nodes.ContainsKey(issue.NodeId.Value) || issue.RootId is { } r && !roots.Contains(r) || !Enum.IsDefined(issue.State)) throw Invalid();
            if (issue.NodeId is { } id) issueReferences.Add((id, issue.Code));
        }
        foreach (var pair in s.IssueTotals)
            if (!Enum.IsDefined(pair.Key) || pair.Value < (ulong)s.Issues.Count(i => i.State == pair.Key)) throw Invalid();
        if (s.IssueTotals.Count > 0 && s.Issues.Any(i => !s.IssueTotals.ContainsKey(i.State))) throw Invalid();
        var sampledBoundaries = AnalysisCoverage.SampledBoundaries(s);
        foreach (var pair in s.BoundaryTotals)
            if (!Enum.IsDefined(pair.Key) || pair.Value < sampledBoundaries.GetValueOrDefault(pair.Key)) throw Invalid();
        if (s.BoundaryTotals.Count > 0 && sampledBoundaries.Keys.Any(k => !s.BoundaryTotals.ContainsKey(k))) throw Invalid();
        ulong boundaryCount = (s.BoundaryTotals.Count > 0 ? s.BoundaryTotals : sampledBoundaries).Values.Aggregate(0ul, (sum, value) => checked(sum + value));
        ulong issueCount = s.IssueTotals.Count > 0 ? s.IssueTotals.Values.Aggregate(0ul, (sum, value) => checked(sum + value)) : (ulong)s.Issues.Length;
        if (boundaryCount > issueCount) throw Invalid();
        foreach (var node in s.Nodes)
            if (node.IssueCodes.Any(code => !issueReferences.Contains((node.Id, code)))) throw Invalid();
        if (s.CoverageDetails is { } coverage)
        {
            if (coverage.EnumerationRequested > (ulong)s.Nodes.Count(n => n.Kind == AnalysisKind.Directory) || coverage.EnumerationCompleted > coverage.EnumerationRequested ||
                coverage.EnumerationFailed > coverage.EnumerationRequested - coverage.EnumerationCompleted) throw Invalid();
            var expected = AnalysisCoverage.Create(s, coverage.EnumerationRequested, coverage.EnumerationCompleted, coverage.EnumerationFailed);
            if (coverage.Qualification is { } qualification)
            {
                ValidateQualifiedCoverage(s, coverage, qualification, nodes, objectMap, aliases, factObjects, cancellation);
                expected = expected with { DenominatorFinal = qualification.EnumerationKnown && s.Phase == AnalysisPhase.Finished && s.Coverage == NamespaceCoverage.CompleteUnderPolicy,
                    StaleScopeNodes = coverage.StaleScopeNodes, StaleRootIds = coverage.StaleRootIds };
            }
            if (coverage.BoundaryTotals is null || !coverage.BoundaryTotals.OrderBy(p => p.Key).SequenceEqual(expected.BoundaryTotals.OrderBy(p => p.Key)) ||
                coverage.DenominatorFinal != expected.DenominatorFinal || coverage.Boundaries != expected.Boundaries || coverage.ExcludedIssues != expected.ExcludedIssues ||
                coverage.FailedIssues != expected.FailedIssues || coverage.PendingIssues != expected.PendingIssues || coverage.IdentityVerifiedPaths != expected.IdentityVerifiedPaths ||
                coverage.IdentityUnverifiedPaths != expected.IdentityUnverifiedPaths || !coverage.MeasurementStates.OrderBy(p => p.Key).SequenceEqual(expected.MeasurementStates.OrderBy(p => p.Key)) ||
                !coverage.StaleScopeNodes.SequenceEqual(expected.StaleScopeNodes) || !coverage.StaleRootIds.SequenceEqual(expected.StaleRootIds)) throw Invalid();
        }
    }
    private static bool Text(string? value, int bytes) => value is { Length: > 0 } && !value.Contains('\0') && Encoding.UTF8.GetByteCount(value) <= bytes;
    private static bool Optional(string? value, int bytes) => value is null || Text(value, bytes);
    private static void Policy(ScanPolicy p)
    {
        if (p.SchemaVersion != 1 || p.ClassificationVersion != 1 || p.AllocationVersion != 1 || p.AttributionVersion != 1 ||
            p.AnalysisMode is not ("disk" or "logical") || p.ComponentPreset is not ("standard" or "detailed") || p.MountPolicy is not ("stay-on-source" or "explicit-mounts") ||
            p.PackagePolicy is not ("opaque" or "explicit" or "contents-included") || p.CloudPolicy is not ("metadata-only" or "explicit-content") ||
            p.Limits is null || p.BasisIds.IsDefault || p.Exclusions.IsDefault || p.BasisIds.Length > AnalysisLimits.Components || p.Exclusions.Length > 4096 || p.Limits.Count > 64 ||
            p.BasisIds.Any(id => !Text(id, 256)) || p.BasisIds.Distinct(StringComparer.Ordinal).Count() != p.BasisIds.Length ||
            p.Exclusions.Any(rule => !Text(rule, AnalysisLimits.QueryBytes)) || p.Limits.Keys.Any(key => !Text(key, 256))) throw Invalid();
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
