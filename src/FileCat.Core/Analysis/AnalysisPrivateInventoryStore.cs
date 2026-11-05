namespace FileCat.Core.Analysis;

/// <summary>A process-owned native directory capability. It is never reconstructed from inventory data.</summary>
public abstract class AnalysisPrivateDirectory : IDisposable
{
    internal AnalysisPrivateDirectory() { }
    internal abstract void Verify();
    internal abstract (UInt128 Capacity, UInt128 Available) Space();
    internal abstract AnalysisPrivateFile CreateStaging();
    internal abstract AnalysisPrivateFile OpenRead(string name);
    internal virtual string[] Entries(CancellationToken cancellation) => throw new NotSupportedException("Private catalog enumeration is unavailable.");
    public abstract void Dispose();
}

/// <summary>Native file ownership stays with this lease, including interrupted publication.</summary>
internal abstract class AnalysisPrivateFile : IDisposable
{
    public abstract Stream Stream { get; }
    public abstract void Verify();
    public abstract void Flush();
    // Create-only and source-handle-bound. Once the name is committed, subsequent errors must be reported as uncertain.
    public abstract void PublishNew(string name);
    public abstract void Dispose();
}

/// <summary>The file was published, but its post-publication durability could not be confirmed. Never retry as replace.</summary>
public sealed class AnalysisPublicationUncertainException(string name, Exception inner)
    : IOException("The new inventory name was published, but durability could not be confirmed. Keep the existing file and inspect it before retrying.", inner)
{
    public string InventoryName { get; } = name;
}

public sealed record AnalysisInventoryReceipt(Guid InventoryId, string Name, long Bytes);

/// <summary>
/// Synchronous private storage for admitted I/O workers. There is deliberately no replace or ordinary path fallback.
/// Loading always uses the inert, bounded importer; the receipt and filename grant no live filesystem authority.
/// </summary>
public sealed class AnalysisPrivateInventoryStore(AnalysisPrivateDirectory directory) : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;

    public AnalysisInventoryReceipt SaveNew(string name, AnalysisSnapshot snapshot, Guid inventoryId, CancellationToken cancellation = default)
    {
        ValidateName(name);
        long bytes = WriteNew(name, stream => AnalysisInventoryFormat.Write(stream, snapshot, inventoryId, cancellation), cancellation);
        return new(inventoryId, name, bytes);
    }

    private long WriteNew(string name, Action<Stream> write, CancellationToken cancellation)
    {
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested();
            using var file = directory.CreateStaging();
            using var stream = new GuardedWriteStream(file.Stream, () => { Guard(); file.Verify(); }, directory.Space, cancellation);
            using (var buffered = new BufferedStream(stream, 64 * 1024))
            {
                write(buffered);
                buffered.Flush();
            }
            cancellation.ThrowIfCancellationRequested(); Guard(); file.Verify();
            file.Flush();
            cancellation.ThrowIfCancellationRequested(); Guard(); file.Verify();
            file.PublishNew(name);
            return stream.Written;
        }
    }

    // Reserved metadata is available only to the trusted catalog/private-state codec. Public inventory
    // SaveNew/Load still require .fcdsa, so decoded names cannot select arbitrary JSON/profile artifacts.
    internal long WriteMetadataNew(string name, byte[] bytes, CancellationToken cancellation)
    {
        AnalysisPrivateNames.Metadata(name); ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 1 or > AnalysisPrivateNames.MetadataBytes) throw new InvalidDataException("Private analysis metadata exceeds its size limit.");
        return WriteNew(name, stream => stream.Write(bytes), cancellation);
    }

    internal byte[] ReadMetadata(string name, CancellationToken cancellation, int maximum = AnalysisPrivateNames.MetadataBytes)
    {
        AnalysisPrivateNames.Metadata(name);
        if (maximum is < 1 or > AnalysisPrivateNames.MetadataBytes) throw new ArgumentOutOfRangeException(nameof(maximum));
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested();
            using var file = directory.OpenRead(name); file.Verify();
            if (file.Stream.Length < 1 || file.Stream.Length > maximum) throw new InvalidDataException("Private analysis metadata exceeds its size limit.");
            using var source = new GuardedReadStream(file.Stream, () => { Guard(); file.Verify(); }, cancellation);
            using var result = new MemoryStream(); byte[] buffer = new byte[64 * 1024];
            int count;
            while ((count = source.Read(buffer)) != 0)
            {
                if (count > maximum - result.Length) throw new InvalidDataException("Private analysis metadata exceeds its size limit.");
                result.Write(buffer, 0, count);
            }
            Guard(); file.Verify(); return result.ToArray();
        }
    }

    internal string[] Entries(CancellationToken cancellation)
    {
        lock (_gate) { Guard(); cancellation.ThrowIfCancellationRequested(); return directory.Entries(cancellation); }
    }

    public AnalysisInventory Load(string name, CancellationToken cancellation = default)
    {
        ValidateName(name);
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested();
            using var file = directory.OpenRead(name);
            file.Verify();
            using var stream = new GuardedReadStream(file.Stream, () => { Guard(); file.Verify(); }, cancellation);
            return AnalysisInventoryFormat.Read(stream, cancellation);
        }
    }

    private void Guard()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        directory.Verify();
    }

    public static void ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is < 7 or > 120 || !name.EndsWith(".fcdsa", StringComparison.Ordinal) ||
            !AnalysisNames.IsOrdinaryChild(name, windows: true) ||
            System.Text.Encoding.UTF8.GetByteCount(name) > 240 || !WellFormedText(name))
            throw new ArgumentException("Choose a portable filename of at most 120 characters ending in .fcdsa.", nameof(name));
    }

    internal static bool WellFormedText(string value)
    {
        for (int i = 0; i < value.Length;)
        {
            if (!System.Text.Rune.TryGetRuneAt(value, i, out var rune)) return false;
            i += rune.Utf16SequenceLength;
        }
        return true;
    }

    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; directory.Dispose(); }
    }

    internal static UInt128 RequiredReserve(UInt128 capacity) =>
        UInt128.Min(2UL * 1024 * 1024 * 1024, UInt128.Max(256UL * 1024 * 1024, capacity / 100 + (capacity % 100 == 0 ? 0u : 1u)));

    private abstract class GuardedStream(Stream inner, Action verify, CancellationToken cancellation) : Stream
    {
        protected readonly Stream Inner = inner;
        protected void Check() { cancellation.ThrowIfCancellationRequested(); verify(); }
        public override bool CanSeek => false;
        public override long Length => Inner.Length;
        public override long Position { get => Inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { Check(); Inner.Flush(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        // The enclosing native lease owns the stream and descriptor.
    }

    private sealed class GuardedWriteStream(Stream inner, Action verify,
        Func<(UInt128 Capacity, UInt128 Available)> space, CancellationToken cancellation) : GuardedStream(inner, verify, cancellation)
    {
        public long Written { get; private set; }
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > AnalysisLimits.InventoryBytes - Written) throw new IOException("The inventory file limit was reached.");
            while (!buffer.IsEmpty)
            {
                Check(); int take = Math.Min(buffer.Length, 64 * 1024);
                var current = space();
                if (current.Capacity == 0 || current.Available > current.Capacity ||
                    current.Available < RequiredReserve(current.Capacity) + (uint)take)
                    throw new IOException("Not enough private destination space remains above the analysis reserve.");
                Inner.Write(buffer[..take]); Written += take; buffer = buffer[take..];
            }
        }
    }

    private sealed class GuardedReadStream(Stream inner, Action verify, CancellationToken cancellation) : GuardedStream(inner, verify, cancellation)
    {
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) { Check(); return Inner.Read(buffer[..Math.Min(buffer.Length, 64 * 1024)]); }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
