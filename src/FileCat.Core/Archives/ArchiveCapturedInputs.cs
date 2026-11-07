using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

internal readonly record struct ArchiveCapturedInputCost(long MetadataBytes, long HeapBytes, long NameChars);
internal interface IArchiveCapturedInputFile { ArchiveCapturedInputs CaptureOwner { get; } }

/// <summary>
/// Reviewed file inputs with on-demand native leases. Factories must open fixed directory-relative names through
/// retained native authority; neither a worker nor this collection chooses/reopens arbitrary filesystem paths.
/// Directory owners outlive the operation. This is not recursive folder enumeration or publication authority.
/// </summary>
internal sealed class ArchiveCapturedInputs : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource stopped = new();
    private readonly CancellationToken stopToken;
    private readonly Dictionary<int, ArchiveRetainedBinding> bindings = [];
    private readonly Dictionary<int, ArchiveCapturedDirectory> directories = [];
    private readonly List<ArchiveTransformInput> inputs = [];
    private readonly HashSet<string> identities = new(StringComparer.Ordinal);
    private readonly ArchiveWriteBudget budget = new();
    private IReadOnlyDictionary<int, ArchiveRetainedBinding>? frozenBindings;
    private IReadOnlyDictionary<int, ArchiveCapturedDirectory>? frozenDirectories;
    private IReadOnlyList<ArchiveTransformInput>? frozenInputs;
    private int disposed;
    private bool poisoned;
    private Action<string, string, CancellationToken>? selectionDestinationReview;

    internal ArchiveCapturedInputs() { stopToken = stopped.Token; budget.AddMetadata(0, 2048); }
    internal void SetSelectionDestinationReview(Action<string, string, CancellationToken> review)
    {
        ArgumentNullException.ThrowIfNull(review);
        lock (gate)
        {
            Live(sealedRequired: false);
            if (frozenBindings is not null || selectionDestinationReview is not null) throw Invalid();
            selectionDestinationReview = review;
        }
    }
    internal void ReviewSelectionDestination(string path, string identity, CancellationToken ct)
    {
        Live(sealedRequired: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopToken);
        using var permit = ArchiveCapturedInputLeases.Rent(cancellation.Token);
        selectionDestinationReview?.Invoke(path, identity, cancellation.Token);
        Live(sealedRequired: true); cancellation.Token.ThrowIfCancellationRequested();
    }
    // The flattened selection, path graph, exclusions and source factories live alongside these bindings.
    // Charge them to this same owner so plan admission cannot omit their retained cost.
    internal void RetainSelection(long nameChars, long heapBytes)
    {
        lock (gate)
        {
            Live(sealedRequired: false);
            if (frozenBindings is not null) throw new InvalidOperationException("The captured input set is sealed.");
            try { budget.AddNameChars(nameChars); budget.AddMetadata(0, heapBytes); }
            catch { poisoned = true; throw; }
        }
    }
    internal IReadOnlyDictionary<int, ArchiveRetainedBinding> Bindings { get { lock (gate) { Live(sealedRequired: true); return frozenBindings!; } } }
    internal IReadOnlyDictionary<int, ArchiveCapturedDirectory> Directories { get { lock (gate) { Live(sealedRequired: true); return frozenDirectories!; } } }
    internal IReadOnlyList<ArchiveTransformInput> Inputs { get { lock (gate) { Live(sealedRequired: true); return frozenInputs!; } } }
    internal ArchiveCapturedInputCost Cost { get { lock (gate) { Live(sealedRequired: true); return new(budget.MetadataBytes, budget.RetainedHeapBytes, budget.NameChars); } } }

    internal void Capture<T>(int id, Func<T> open, CancellationToken ct) where T : Stream, IArchiveRetainedFile
    {
        ArgumentNullException.ThrowIfNull(open);
        lock (gate)
        {
            Live(sealedRequired: false);
            if (frozenBindings is not null) throw new InvalidOperationException("The captured input set is sealed.");
            try
            {
                if (id <= 0 || bindings.ContainsKey(id) || directories.ContainsKey(id)) throw Invalid();
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopToken);
                CancellationToken token = cancellation.Token; token.ThrowIfCancellationRequested();
                // Covers records, closures, descriptors, list/map/set entries and later frozen views. Variable
                // strings/provenance are charged separately. Charge before allocating/retaining a new record.
                budget.AddMember(); budget.AddMetadata(0, 768);
                using var permit = ArchiveCapturedInputLeases.Rent(token);
                using T file = open() ?? throw Invalid();
                long length = SourceLength(file);
                budget.AddLogicalBytes(length); // Before hashing a potentially large native input.
                ArchiveFileObservation observed = file.Observe(token);
                if (observed.Version.Length != length || observed.Version.Identity != file.ObjectIdentity) throw Changed();
                ArchiveTransformInput descriptor = observed.Input(id);
                long nameChars = (long)observed.ParentIdentity.Length + observed.LeafName.Length;
                long allChars = nameChars + observed.Version.Identity.Length + observed.Version.Sha256.Length +
                    observed.MetadataDigest.Length + observed.Origin.Digest.Length;
                long metadataBytes = ArchiveWritePolicy.Utf8.GetByteCount(observed.ParentIdentity) +
                    ArchiveWritePolicy.Utf8.GetByteCount(observed.LeafName) + observed.Origin.Bytes.Length;
                budget.AddNameChars(nameChars);
                budget.AddMetadata(metadataBytes, checked(2 * allChars + observed.Origin.Bytes.Length));
                if (!identities.Add(observed.Version.Identity)) throw Changed();
                token.ThrowIfCancellationRequested();
                bindings.Add(id, new(new CapturedFile<T>(this, open, observed), observed));
                inputs.Add(descriptor);
            }
            catch { poisoned = true; throw; } // A refusal cannot reset resource counters or expose a partial set.
        }
    }

    internal void CaptureDirectory(int id, Func<CancellationToken, ArchiveDirectoryObservation> observe, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(observe);
        lock (gate)
        {
            Live(sealedRequired: false);
            if (frozenBindings is not null) throw new InvalidOperationException("The captured input set is sealed.");
            try
            {
                if (id <= 0 || bindings.ContainsKey(id) || directories.ContainsKey(id)) throw Invalid();
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopToken);
                var token = cancellation.Token; token.ThrowIfCancellationRequested();
                budget.AddMember(); budget.AddMetadata(0, 768);
                using var permit = ArchiveCapturedInputLeases.Rent(token);
                var observed = observe(token) ?? throw Invalid();
                long names = (long)observed.ParentIdentity.Length + observed.LeafName.Length;
                long chars = names + observed.Version.Identity.Length + observed.Version.Sha256.Length +
                    observed.MetadataDigest.Length + observed.Origin.Digest.Length;
                budget.AddNameChars(names);
                budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(observed.ParentIdentity) +
                    ArchiveWritePolicy.Utf8.GetByteCount(observed.LeafName) + observed.Origin.Bytes.Length,
                    checked(2 * chars + observed.Origin.Bytes.Length));
                if (!identities.Add(observed.Version.Identity)) throw Changed();
                token.ThrowIfCancellationRequested();
                directories.Add(id, new(this, id, observe, observed));
            }
            catch { poisoned = true; throw; }
        }
    }

    internal void ReviewDirectory(Func<CancellationToken, ArchiveDirectoryObservation> observe, ArchiveDirectoryObservation expected, CancellationToken ct)
    {
        Live(sealedRequired: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopToken);
        using var permit = ArchiveCapturedInputLeases.Rent(cancellation.Token);
        if (observe(cancellation.Token) is not { } actual || !actual.Matches(expected)) throw Changed();
        Live(sealedRequired: true); cancellation.Token.ThrowIfCancellationRequested();
    }

    internal void Seal(CancellationToken ct)
    {
        lock (gate)
        {
            Live(sealedRequired: false); ct.ThrowIfCancellationRequested();
            if (frozenBindings is not null) return;
            frozenInputs = Array.AsReadOnly(inputs.ToArray());
            frozenBindings = new ReadOnlyDictionary<int, ArchiveRetainedBinding>(bindings);
            frozenDirectories = new ReadOnlyDictionary<int, ArchiveCapturedDirectory>(directories);
        }
    }

    internal ArchiveContainerTransformPlan CreatePlan(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions, CancellationToken ct,
        IReadOnlyList<ArchiveCapturedDirectoryMerge>? directoryMerges = null, long retainedHeapBytes = 0)
    {
        lock (gate)
        {
            Live(sealedRequired: true);
            return profile.Format switch
            {
                ArchiveWriteFormat.Rar5 => ArchiveRar5TransformPlan.CreateCaptured(profile, source, frozenInputs!, edits, additions, Cost, ct, this, directoryMerges, retainedHeapBytes),
                ArchiveWriteFormat.SevenZip => ArchiveSevenZipTransformPlan.CreateCaptured(profile, source, frozenInputs!, edits, additions, Cost, ct, this, directoryMerges, retainedHeapBytes),
                _ => ArchiveTarTransformPlan.CreateCaptured(profile, source, frozenInputs!, edits, additions, Cost, ct, this, directoryMerges, retainedHeapBytes),
            };
        }
    }

    internal ArchiveTransformAddition DirectoryAddition(string name, int id)
    {
        lock (gate)
        {
            Live(sealedRequired: true);
            if (!directories.TryGetValue(id, out var directory)) throw Invalid();
            return ArchiveTransformAddition.FromCapturedDirectory(name, directory);
        }
    }

    internal static void RequirePlanCost(ArchiveContainerTransformPlan plan, IReadOnlyDictionary<int, ArchiveRetainedBinding> captured)
    {
        if (plan.CaptureOwner is { } owner)
        {
            var expected = owner.Bindings;
            if (!ReferenceEquals(plan.Directories, owner.Directories) || plan.CaptureCost != owner.Cost || expected.Count != captured.Count || captured.Any(pair =>
                !expected.TryGetValue(pair.Key, out var binding) || !ReferenceEquals(binding, pair.Value))) throw Invalid();
            return;
        }
        foreach (var binding in captured.Values)
            if (binding?.File is IArchiveCapturedInputFile) throw Invalid();
        if (plan.CaptureCost != default) throw Invalid();
    }

    private void Live(bool sealedRequired)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (poisoned) throw Invalid();
        if (sealedRequired && frozenBindings is null) throw new InvalidOperationException("Seal the reviewed inputs before granting reads or constructing a plan.");
    }
    private static long SourceLength(Stream file)
    {
        if (!file.CanRead || !file.CanSeek || file.CanWrite) throw Invalid();
        long length = file.Length;
        if (length < 0 || length > ArchiveWritePolicy.MaxPayloadBytes) throw ArchiveWriteBudget.Limit();
        return length;
    }

    private sealed class CapturedFile<T>(ArchiveCapturedInputs owner, Func<T> open, ArchiveFileObservation captured) : IArchiveRetainedFile, IArchiveCapturedInputFile
        where T : Stream, IArchiveRetainedFile
    {
        public ArchiveCapturedInputs CaptureOwner => owner;
        public string ObjectIdentity => captured.Version.Identity;
        public ArchiveFileObservation Observe(CancellationToken ct)
        {
            owner.Live(sealedRequired: true);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, owner.stopToken);
            using var permit = ArchiveCapturedInputLeases.Rent(cancellation.Token);
            using T file = open() ?? throw Invalid();
            if (SourceLength(file) != captured.Version.Length || file.ObjectIdentity != ObjectIdentity) throw Changed();
            ArchiveFileObservation actual = file.Observe(cancellation.Token);
            if (!actual.Matches(captured)) throw Changed();
            owner.Live(sealedRequired: true); cancellation.Token.ThrowIfCancellationRequested(); return captured;
        }
        public void Review(ArchiveFileObservation expected, CancellationToken ct)
        { RequireExpected(expected); _ = Observe(ct); }
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct)
        {
            RequireExpected(expected); owner.Live(sealedRequired: true);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, owner.stopToken);
            IDisposable? permit = null; T? file = null; Stream? view = null;
            try
            {
                permit = ArchiveCapturedInputLeases.Rent(cancellation.Token);
                file = open() ?? throw Invalid();
                if (SourceLength(file) != captured.Version.Length || file.ObjectIdentity != ObjectIdentity) throw Changed();
                // Native OpenReadView checks the exact identity/metadata. Do not hash the complete source on each
                // worker-selected ID switch: that would amplify tiny/zero-byte reads outside the IO budget.
                view = file.OpenReadView(captured, cancellation.Token);
                if (!view.CanRead || !view.CanSeek || view.CanWrite || view.Length != captured.Version.Length) throw Changed();
                owner.Live(sealedRequired: true); cancellation.Token.ThrowIfCancellationRequested();
                return new OwnedView(owner, view, file, permit, cancellation);
            }
            catch
            {
                try { view?.Dispose(); }
                finally { try { file?.Dispose(); } finally { permit?.Dispose(); cancellation.Dispose(); } }
                throw;
            }
        }
        private void RequireExpected(ArchiveFileObservation expected)
        { if (expected is null || !expected.Matches(captured)) throw Changed(); }
    }

    /// <summary>The read view, native file and global permit have the same borrower-owned lifetime.</summary>
    private sealed class OwnedView(ArchiveCapturedInputs owner, Stream inner, IDisposable file, IDisposable permit,
        CancellationTokenSource cancellation) : Stream
    {
        private readonly object gate = new(); private bool disposed;
        public override bool CanRead { get { lock (gate) return !disposed && Volatile.Read(ref owner.disposed) == 0 && inner.CanRead; } }
        public override bool CanSeek => CanRead;
        public override bool CanWrite => false;
        public override long Length { get { lock (gate) { Live(); return inner.Length; } } }
        public override long Position { get { lock (gate) { Live(); return inner.Position; } } set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) { lock (gate) { Live(); return inner.Read(buffer); } }
        public override long Seek(long offset, SeekOrigin origin) { lock (gate) { Live(); return inner.Seek(offset, origin); } }
        private void Live() { ObjectDisposedException.ThrowIf(disposed, this); owner.Live(sealedRequired: true); cancellation.Token.ThrowIfCancellationRequested(); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            lock (gate)
            {
                if (disposed) return; disposed = true;
                try { inner.Dispose(); }
                finally { try { file.Dispose(); } finally { permit.Dispose(); cancellation.Dispose(); } }
            }
            base.Dispose(disposing);
        }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { stopped.Cancel(); } finally { stopped.Dispose(); }
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The reviewed archive input set is invalid or incomplete.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "A captured archive input changed or aliases another selected input.");
}

/// <summary>Process-wide backpressure across captured inputs and preparations; never one descriptor per member.</summary>
internal static class ArchiveCapturedInputLeases
{
    internal static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(5);
    private static readonly SemaphoreSlim available = new(ArchiveWritePolicy.MaxInputLeases, ArchiveWritePolicy.MaxInputLeases);
    internal static IDisposable Rent(CancellationToken ct)
    {
        if (!available.Wait(MaximumWait, ct)) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded,
            "Other archive tasks are using the available input leases. Retry when one finishes.");
        return new Permit();
    }
    private sealed class Permit : IDisposable
    {
        private int returned;
        public void Dispose() { if (Interlocked.Exchange(ref returned, 1) == 0) available.Release(); }
    }
}
