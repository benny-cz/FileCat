namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisComparisonEntities
{
    private const byte Fresh = 1, UniformObject = 2, UniformPath = 4, UniformVersion = 8, HasArea = 16, Directory = 32;
    internal sealed partial class State
    {
        private enum Stage { Start, Charges, Members, Emit, Done }
        private Stage _stage;
        private ulong _next = 1, _member, _charge;
        private AnalysisIndexRange _members, _charges;
        private AnalysisNode? _firstNode;
        private AnalysisObject? _firstObject;
        private ulong _firstObjectRow, _parent;
        private UInt128 _amount;
        private uint _rootMask;
        private byte _flags;
        private MeasurementState _chargeState, _unavailable;
        internal bool Finished => _stage == Stage.Done;
        internal long Step(AnalysisObjectChargeIndex.State? charges, CancellationToken token)
        {
            switch (_stage)
            {
                case Stage.Start:
                    if (_next > Members!.Count) { _stage = Stage.Done; return 1; }
                    var entry = Members.Read(_next, token);
                    _members = Members.FindRange(entry.Key, token);
                    if (_members.First != _next || _members.Count == 0) throw AnalysisIndexedGraph.Invalid();
                    _charges = Charges?.FindRange(entry.Key, token) ?? default;
                    _member = _charge = _firstObjectRow = _parent = 0; _amount = 0; _rootMask = 0;
                    _firstNode = null; _firstObject = null;
                    _flags = UniformObject | UniformPath | UniformVersion;
                    if (descriptor.Freshness == AnalysisFreshness.Observed) _flags |= Fresh;
                    _chargeState = _unavailable = MeasurementState.Known;
                    _stage = Stage.Charges; return 128L + entry.Key.Length;
                case Stage.Charges:
                    if (_charge == _charges.Count) { _stage = Stage.Members; return 1; }
                    var charge = charges!.Read(Charges!.Read(_charges.First + _charge++, token).Value, token, out long cost);
                    if (!charge.Measurement.IsKnown)
                    {
                        if (MissingRank(charge.Measurement.State) > MissingRank(_chargeState)) _chargeState = charge.Measurement.State;
                    }
                    else if (metric == AnalysisMetric.OnDisk || !charge.IdentityUnverified)
                    {
                        if ((_flags & HasArea) != 0 && _parent != charge.ParentId) throw AnalysisIndexedGraph.Invalid();
                        _flags |= HasArea; _amount = checked(_amount + charge.Measurement.Value!.Value); _parent = charge.ParentId;
                    }
                    return cost;
                case Stage.Members:
                    if (_member == _members.Count) { _stage = Stage.Emit; return 1; }
                    ulong pathRow = Members!.Read(_members.First + _member++, token).Value;
                    var path = _graph!.ReadPathRowRecord(pathRow, token); var node = path.Node;
                    var value = Object(node, token); var obj = value.Object;
                    _rootMask |= 1U << path.RootIndex;
                    if (node.MetadataState != MeasurementState.Known) _flags &= unchecked((byte)~Fresh);
                    if (_firstNode is null)
                    { _firstNode = node; if (node.Kind == AnalysisKind.Directory) _flags |= Directory; }
                    else if (!SamePathMetadata(_firstNode, node)) _flags &= unchecked((byte)~UniformPath);
                    if (obj is not null)
                    {
                        if (_firstObject is null) { _firstObject = obj; _firstObjectRow = value.Row; }
                        else
                        {
                            if (!SameObjectMetadata(_firstObject, obj)) _flags &= unchecked((byte)~UniformObject);
                            if (_firstObject.VersionEvidence != obj.VersionEvidence) _flags &= unchecked((byte)~UniformVersion);
                        }
                    }
                    var state = node.Kind == AnalysisKind.Unknown ? MeasurementState.Failed : _chargeState;
                    if (metric is AnalysisMetric.FileSize or AnalysisMetric.Files)
                    {
                        if (_members.Count != 1) throw AnalysisIndexedGraph.Invalid();
                        AnalysisMeasurement measurement = node.Kind == AnalysisKind.Unknown ? AnalysisMeasurement.Missing(MeasurementState.Failed) :
                            metric == AnalysisMetric.Files ? AnalysisMeasurement.Known(1) : obj?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                        state = measurement.State;
                        if (measurement.IsKnown) { _flags |= HasArea; _amount = measurement.Value!.Value; _parent = node.ParentId; }
                    }
                    else if (node.Kind == AnalysisKind.File && obj is null) state = MeasurementState.Unsupported;
                    if (_unavailable == MeasurementState.Known && state != MeasurementState.Known) _unavailable = state;
                    return PathBytes(node) + 1024L + (obj?.VersionEvidence?.Length ?? 0) * 2L;
                case Stage.Emit:
                    bool known = _unavailable == MeasurementState.Known && (_flags & HasArea) != 0;
                    var measurementValue = known ? AnalysisMeasurement.Known(_amount) : AnalysisMeasurement.Missing(
                        _unavailable == MeasurementState.Known ? MeasurementState.Unsupported : _unavailable);
                    _writer!.Append(Encode(new(_members.First, _members.Count, _firstObjectRow, _parent, measurementValue, _rootMask, _flags)), token);
                    _next = checked(_members.First + _members.Count); _stage = Stage.Start; return 128;
                default: throw AnalysisIndexedGraph.Invalid();
            }
        }
        // Mirrors the projection's per-path unmeasured-state precedence; it never sums missing values.
        private static int MissingRank(MeasurementState state) => state switch
        { MeasurementState.Known => 0, MeasurementState.Changed => 5, MeasurementState.Failed => 4, MeasurementState.Pending => 3, MeasurementState.Excluded => 2, _ => 1 };
    }

    internal static bool SameObjectMetadata(AnalysisObject a, AnalysisObject b) => a.LogicalLength == b.LogicalLength &&
        a.LinkCount == b.LinkCount && a.ModifiedUtc == b.ModifiedUtc && a.ModifiedPrecisionTicks == b.ModifiedPrecisionTicks;
    internal static bool SamePathMetadata(AnalysisNode a, AnalysisNode b) => a.Kind == b.Kind && a.Sparse == b.Sparse &&
        a.Compressed == b.Compressed && a.Cloud == b.Cloud && a.Times?.ModifiedUtc == b.Times?.ModifiedUtc &&
        a.Times?.ModifiedPrecisionTicks == b.Times?.ModifiedPrecisionTicks && a.Times?.CreatedUtc == b.Times?.CreatedUtc &&
        a.Times?.CreatedPrecisionTicks == b.Times?.CreatedPrecisionTicks && a.Times?.ChangedUtc == b.Times?.ChangedUtc &&
        a.Times?.ChangedPrecisionTicks == b.Times?.ChangedPrecisionTicks;
}
