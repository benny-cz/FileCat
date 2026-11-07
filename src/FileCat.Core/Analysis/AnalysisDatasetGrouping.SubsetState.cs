using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    private sealed class SubsetAmount
    {
        internal UInt128 Known, Unverified;
        internal ulong Files;
        internal readonly Dictionary<MeasurementState, ulong> Missing = [];
        internal void Add(AnalysisTotals value)
        {
            Known = checked(Known + value.Known); Unverified = checked(Unverified + value.IdentityUnverified);
            foreach (var pair in value.Missing) Missing[pair.Key] = checked(Missing.GetValueOrDefault(pair.Key) + pair.Value);
        }
        internal void Logical(AnalysisMeasurement value)
        {
            Files = checked(Files + 1);
            if (value.IsKnown) Known = checked(Known + value.Value!.Value);
            else Missing[value.State] = checked(Missing.GetValueOrDefault(value.State) + 1);
        }
        internal AnalysisTotals Export(bool compatible, ulong files) => new(Known, Unverified, files, Missing.ToImmutableDictionary(), compatible);
        internal AnalysisLogicalTotals ExportLogical() => new(Known, Files, Missing.ToImmutableDictionary());
    }

    private sealed class SubsetState : IAnalysisScratchOwned
    {
        internal readonly State Snapshot;
        internal readonly string? ParentKey;
        internal readonly AnalysisDatasetGroup? Parent;
        internal readonly int Skipped;
        internal readonly AnalysisIndexRange Geometry;
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisSortedIndexReader? Selected, Paths, Order, Charges, SharedObjects, Candidates, Members;
        internal readonly SubsetAmount Amount = new(), Logical = new();
        internal ulong Rows, ChargeCount, MemberCount;
        internal bool Shared, Unknown;
        internal bool Overview => ParentKey is null;
        internal string ScopeKey => AnalysisTree.OtherKey(ParentKey ?? AnalysisTree.RootKey, Skipped);
        internal AnalysisSortedIndexReader GeometryIndex => Overview ? Snapshot.GroupGeometry! : Snapshot.MemberGeometry!;
        private SubsetState(State snapshot, string? parent, int skipped, AnalysisDatasetGroup? group, AnalysisIndexRange geometry)
        { Snapshot = snapshot; ParentKey = parent; Skipped = skipped; Parent = group; Geometry = geometry; }
        internal static SubsetState Create(State source, string? parent, int skipped, CancellationToken token)
        {
            if (skipped < 1) throw new ArgumentOutOfRangeException(nameof(skipped));
            if (parent == AnalysisTree.RootKey) parent = null;
            var group = parent is null ? null : source.Group(parent, token);
            var geometry = parent is null ? new AnalysisIndexRange(1, source.GroupGeometry!.Count) : source.MemberGeometry!.PrefixRange(Hash(parent), token);
            if ((ulong)skipped > geometry.Count) throw new ArgumentOutOfRangeException(nameof(skipped));
            var retained = source.Retain(); SubsetState? result = null;
            try
            {
                result = new(retained, parent, skipped, group, geometry); result.Workspace = retained.Graph.BorrowWorkspace(); return result;
            }
            catch { if (result is not null) result.Dispose(); else retained.Dispose(); throw; }
        }
        internal IEnumerable<AnalysisSortInput> SelectedEntries(CancellationToken token)
        {
            for (ulong at = (ulong)Skipped; at < Geometry.Count; at++)
            {
                token.ThrowIfCancellationRequested(); var entry = GeometryIndex.Read(Geometry.First + at, token);
                var item = Snapshot.GeometryItem(Overview, entry, token); Unknown |= item.Unknown;
                if (Overview) Amount.Add(DecodeSummary(Snapshot.Summaries!.Read(entry.Value, token)).Totals);
                else if ((entry.Value & GeometryBucket) != 0)
                { Shared = true; Amount.Add(DecodeSummary(Snapshot.Summaries!.Read(entry.Value & ~GeometryBucket, token)).Shared); }
                else { Amount.Add(Snapshot.ReadMemberRow(entry.Value, token, out _, out _, out _).Assigned); Rows = checked(Rows + 1); }
                if (Overview) Rows = checked(Rows + 1);
                yield return new(new(Number(entry.Value), entry.Value), 2048 + item.Name.Length * 2);
            }
        }
        internal IEnumerable<AnalysisSortInput> PathEntries(CancellationToken token)
        {
            for (ulong at = (ulong)Skipped; at < Geometry.Count; at++)
            {
                token.ThrowIfCancellationRequested(); var entry = GeometryIndex.Read(Geometry.First + at, token);
                if (Overview || (entry.Value & GeometryBucket) != 0) { yield return new(null, 256); continue; }
                var row = Snapshot.ReadMemberRow(entry.Value, token, out _, out _, out long bytes);
                yield return new(new(Number(row.Member.Path.Id), entry.Value), checked((int)bytes));
            }
        }
        internal bool HasToken(ulong source, CancellationToken token) => Selected!.FindRange(Number(source), token).Count != 0;
        internal IEnumerable<AnalysisSortInput> OrderEntries(CancellationToken token)
        {
            bool reference = Parent?.Kind != AnalysisGroupKind.Entries;
            var index = Overview ? Snapshot.Order! : reference ? Snapshot.ReferenceOrder! : Snapshot.MemberOrder!;
            var range = Overview ? new AnalysisIndexRange(1, index.Count) : index.PrefixRange(Hash(ParentKey!), token);
            for (ulong at = 0; at < range.Count; at++)
            {
                token.ThrowIfCancellationRequested(); var entry = index.Read(range.First + at, token);
                ulong source = !Overview && reference ? entry.Value | GeometryReference : entry.Value;
                yield return new(HasToken(source, token) ? new(Overview ? entry.Key : entry.Key.AsSpan(32).ToArray(), source) : null, entry.Key.Length + 256);
            }
        }
        private bool SelectedGroup(byte[] hash, CancellationToken token)
        {
            var found = Snapshot.Lookup!.FindRange(hash, token); if (found.Count != 1) throw Invalid();
            return HasToken(Snapshot.Lookup.Read(found.First, token).Value, token);
        }
        internal AnalysisDatasetGroup RequireGroup(string key, CancellationToken token)
        {
            var group = Snapshot.Group(key, token);
            if (Overview ? !SelectedGroup(Hash(key), token) : key != ParentKey) throw new InvalidOperationException("This captured group is outside the Other scope.");
            return group;
        }
        internal IEnumerable<AnalysisSortInput> ChargeEntries(CancellationToken token)
        {
            var index = Snapshot.ChargeIndex!; var range = Overview ? new AnalysisIndexRange(1, index.Count) : index.PrefixRange(Hash(ParentKey!), token);
            for (ulong at = 0; at < range.Count; at++)
            {
                token.ThrowIfCancellationRequested(); var entry = index.Read(range.First + at, token);
                var item = DecodeEvent(Snapshot.Charges!.Read(entry.Value, token));
                bool selected = Overview ? SelectedGroup(Hash(item.Definition.Key), token) : item.Path == 0 ? Shared : Paths!.FindRange(Number(item.Path), token).Count != 0;
                if (selected) ChargeCount = checked(ChargeCount + 1);
                yield return new(selected ? entry : null, 2048 + entry.Key.Length);
            }
        }
        internal IEnumerable<AnalysisSortInput> SharedEntries(CancellationToken token)
        {
            if (!Shared || Overview) yield break;
            var range = Charges!.PrefixRange(Join(Hash(ParentKey!), Number(0)), token);
            for (ulong at = 0; at < range.Count; at++)
            {
                token.ThrowIfCancellationRequested(); var item = DecodeEvent(Snapshot.Charges!.Read(Charges.Read(range.First + at, token).Value, token));
                var charge = item.Charge ?? throw Invalid();
                if (item.Path != 0 || !charge.Shared || charge.DirectorySelf || charge.ObjectId is null) throw Invalid();
                var obj = Snapshot.Object(charge.ObjectId, token) ?? throw Invalid();
                if (obj.VerifiedIdentity is null) throw Invalid();
                ulong canonical = Canonical(obj, token);
                yield return new(new(Number(canonical), canonical), 2048 + obj.Id.Length * 2);
            }
        }
        private ulong Canonical(AnalysisObject obj, CancellationToken token)
        {
            var range = Snapshot.Graph.Find(AnalysisIndexedGraphRevision.ObjectIdentities, AnalysisGraphKeys.ObjectIdentity(obj), cancellation: token);
            if (range.Count == 0) throw Invalid(); return Snapshot.Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIdentities, range.First, token).Value;
        }
        internal IEnumerable<AnalysisSortInput> CandidateEntries(CancellationToken token)
        {
            foreach (bool reference in new[] { false, true })
            {
                var records = reference ? Snapshot.ReferencePlacements! : Snapshot.MemberPlacements!;
                var order = reference ? Snapshot.ReferenceOrder! : Snapshot.MemberOrder!;
                var range = Overview ? new AnalysisIndexRange(1, records.Count) :
                    (Parent!.Kind != AnalysisGroupKind.Entries) == reference ? order.PrefixRange(Join(Hash(ParentKey!), [(byte)AnalysisTableSort.ActiveMetric]), token) : default;
                for (ulong at = 0; at < range.Count; at++)
                {
                    token.ThrowIfCancellationRequested(); ulong row = Overview ? range.First + at : order.Read(range.First + at, token).Value;
                    var placement = DecodePlacement(records.Read(row, token)); if (placement.Reference != reference) throw Invalid();
                    bool selected = Overview ? SelectedGroup(placement.GroupHash, token) : HasToken(reference ? row | GeometryReference : row, token);
                    if (!Overview && !selected && Shared)
                    {
                        var path = Snapshot.Graph.ReadPath(placement.Path, token); var obj = Snapshot.Object(path.ObjectId, token);
                        selected = obj is not null && obj.VerifiedIdentity is not null && SharedObjects!.FindRange(Number(Canonical(obj, token)), token).Count != 0;
                    }
                    yield return new(selected ? new(Number(placement.Path), Snapshot.Graph.PathRow(placement.Path, token)) : null, 4096);
                }
            }
        }
        internal IEnumerable<AnalysisSortInput> MemberEntries(CancellationToken token)
        {
            ulong previous = 0;
            for (ulong at = 1; at <= Candidates!.Count; at++)
            {
                token.ThrowIfCancellationRequested(); var entry = Candidates.Read(at, token);
                if (entry.Key.Length != 8) throw Invalid(); ulong id = BinaryPrimitives.ReadUInt64BigEndian(entry.Key);
                if (id == previous) { yield return new(null, 256); continue; }
                if (id == 0) throw Invalid(); previous = id;
                var path = Snapshot.Graph.ReadPathRow(entry.Value, token); if (path.Id != id) throw Invalid(); MemberCount = checked(MemberCount + 1);
                var obj = path.Kind == AnalysisKind.File && path.IncludedByPolicy ? Snapshot.Object(path.ObjectId, token) : null;
                if (path.Kind == AnalysisKind.File && path.IncludedByPolicy) Logical.Logical(obj?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported));
                yield return new(entry, checked((int)(AnalysisIndexedFilter.Cost(path) + 2048 + (obj?.Id.Length ?? 0) * 2L)));
            }
        }
        internal AnalysisDatasetPage<AnalysisTreeItem> ReadGeometry(ulong offset, int maximum, CancellationToken token)
        {
            ulong count = Geometry.Count - (ulong)Skipped; ValidatePage(offset, maximum, count);
            var rows = ImmutableArray.CreateBuilder<AnalysisTreeItem>(); long bytes = 0;
            while (offset + (ulong)rows.Count < count && rows.Count < maximum)
            {
                var entry = GeometryIndex.Read(Geometry.First + (ulong)Skipped + offset + (ulong)rows.Count, token);
                var item = Snapshot.GeometryItem(Overview, entry, token);
                if (!Fits(1024 + item.Name.Length * 2L, ref bytes)) break; rows.Add(item with { ParentKey = ScopeKey });
            }
            return new(offset, count, rows.ToImmutable());
        }
        internal AnalysisDatasetPage<T> ReadTable<T>(AnalysisTableSort sort, ulong offset, int maximum, bool descending,
            Func<ulong, CancellationToken, (T Row, long Bytes)> read, CancellationToken token)
        {
            if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort)); ValidatePage(offset, maximum, Rows);
            byte[] prefix = [(byte)sort]; var range = Order!.PrefixRange(prefix, token); if (range.Count != Rows) throw Invalid();
            var rows = ImmutableArray.CreateBuilder<T>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var item = read(Order.Read(OrderedPosition(Order, prefix, range, offset + (ulong)rows.Count, sort, descending, token), token).Value, token);
                if (!Fits(item.Bytes, ref bytes)) break; rows.Add(item.Row);
            }
            return new(offset, range.Count, rows.ToImmutable());
        }
        public void Dispose()
        {
            Exception? error = null;
            foreach (var item in new IDisposable?[] { Members, Candidates, SharedObjects, Charges, Order, Paths, Selected, Workspace, Snapshot })
                try { item?.Dispose(); } catch (Exception failure) { error = error is null ? failure : new AggregateException(error, failure); }
            if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
