using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Exact linear Copy/LZMA/LZMA2/AES header graphs. Every layer borrows its predecessor.</summary>
internal sealed class StrictSevenZipHeaderDecoder : Stream
{
    private readonly List<Stream> layers = [];
    private readonly Stream last;
    private bool disposed;
    internal static Stream Copy(Stream input, long length, CancellationToken ct) => new ExactCopy(input, length, ct);
    internal static Stream Aes(Stream input, long packedLength, long length, ReadOnlySpan<byte> properties,
        SevenZipReadPasswordSession? password, CancellationToken ct) => new ExactAes(input, packedLength, length, properties, password, ct);
    internal StrictSevenZipHeaderDecoder(Stream packed, long packedLength, SevenZipRawFolder folder,
        SevenZipReadPasswordSession? password, ArchiveWriteBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (folder.Coders.Count is < 1 or > 32 || folder.PackedInputs.Count != 1 ||
            folder.Coders.Any(coder => coder.Inputs != 1 || coder.Outputs != 1) || folder.UnpackedSizes.Count != folder.Coders.Count ||
            folder.Bindings.Count != folder.Coders.Count - 1 || packedLength < 0) throw Unsupported();
        var visited = new HashSet<int>(); int index = folder.PackedInputs[0]; Stream input = packed; long length = packedLength;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (index < 0 || index >= folder.Coders.Count || !visited.Add(index)) throw Invalid();
                var coder = folder.Coders[index]; long outputLength = folder.UnpackedSizes[index];
                if (outputLength < 0 || outputLength > ArchiveWritePolicy.MaxMetadataBytes) throw Limit();
                var properties = coder.Properties.AsSpan(); ulong method = SevenZipStrictMetadata.Method(coder);
                // Reserve decoder buffers and the worst supported LZMA literal-state allocation before construction.
                long heap = 256 << 10;
                if (method is 0x21 or 0x030101)
                {
                    long dictionary;
                    if (method == 0x21)
                    {
                        if (properties.Length != 1 || properties[0] > 40) throw Invalid();
                        dictionary = properties[0] == 40 ? uint.MaxValue : (2L | (properties[0] & 1)) << ((properties[0] >> 1) + 11);
                    }
                    else
                    {
                        if (properties.Length != 5 || properties[0] >= 225) throw Invalid();
                        dictionary = Math.Max(4096L, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(properties[1..]));
                    }
                    if (dictionary > StrictSevenZipFraming.MaximumHeaderDictionaryBytes) throw Limit();
                    heap += dictionary + (6 << 20);
                }
                budget.AddMetadata(0, heap);
                Stream layer = method switch
                {
                    0 when properties.IsEmpty && length == outputLength => new ExactCopy(input, length, ct),
                    0 => throw Invalid(),
                    0x21 or 0x030101 => new StrictLzmaReadStream(input, properties, method == 0x21, length, outputLength, ct,
                        StrictSevenZipFraming.MaximumHeaderDictionaryBytes),
                    0x06f10701 => new ExactAes(input, length, outputLength, properties, password, ct),
                    _ => throw Unsupported(),
                };
                layers.Add(layer); input = layer; length = outputLength;
                var next = folder.Bindings.Where(binding => binding.Output == index).ToArray();
                if (next.Length == 0)
                {
                    if (index != folder.FinalOutput || visited.Count != folder.Coders.Count) throw Invalid();
                    break;
                }
                if (next.Length != 1) throw Invalid(); index = next[0].Input;
            }
            last = input;
        }
        catch { DisposeLayers(); throw; }
    }
    private void DisposeLayers() { for (int i = layers.Count - 1; i >= 0; i--) layers[i].Dispose(); layers.Clear(); }
    public override int Read(Span<byte> buffer) { ObjectDisposedException.ThrowIf(disposed, this); return last.Read(buffer); }
    public override int Read(byte[] buffer, int offset, int count) { ObjectDisposedException.ThrowIf(disposed, this); return last.Read(buffer, offset, count); }
    protected override void Dispose(bool disposing)
    { if (disposing && !disposed) { disposed = true; DisposeLayers(); } base.Dispose(disposing); }
    public override bool CanRead => !disposed; public override long Length => last.Length; public override long Position { get => last.Position; set => throw new NotSupportedException(); }
    public override bool CanSeek => false; public override bool CanWrite => false;
    public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private sealed class ExactCopy(Stream input, long length, CancellationToken ct) : ReadLayer(length)
    {
        private bool ended;
        public override int Read(Span<byte> buffer)
        {
            Live(); ct.ThrowIfCancellationRequested(); if (buffer.IsEmpty || ended) return 0;
            if (PositionValue == DeclaredLength)
            { if (input.ReadByte() != -1) throw Invalid(); ended = true; return 0; }
            int wanted = (int)Math.Min(Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes), DeclaredLength - PositionValue);
            int n = input.Read(buffer[..wanted]); if (n <= 0 || n > wanted) throw Invalid(); PositionValue += n; return n;
        }
    }
    private sealed class ExactAes : ReadLayer
    {
        private readonly Stream input;
        private readonly long packedLength;
        private readonly CancellationToken ct;
        private readonly ICryptoTransform transform;
        private readonly byte[] cipher = new byte[4096], plain = new byte[4096];
        private long fetched;
        private int offset, available;
        private bool ended;
        internal ExactAes(Stream input, long packedLength, long length, ReadOnlySpan<byte> properties,
            SevenZipReadPasswordSession? password, CancellationToken ct) : base(length)
        {
            if (password is null) throw ArchiveWriteException.PasswordRequired();
            if (properties.Length is < 1 or > 34 || packedLength < length || packedLength - length > 15 || (packedLength & 15) != 0) throw Invalid();
            int power = properties[0] & 63, saltLength = properties[0] >> 7, ivLength = (properties[0] >> 6) & 1, prefix = 1;
            if ((properties[0] & 0xc0) != 0)
            {
                if (properties.Length < 2) throw Invalid(); prefix = 2; saltLength += properties[1] >> 4; ivLength += properties[1] & 15;
            }
            if (properties.Length != prefix + saltLength + ivLength) throw Invalid();
            if (power > 24 && power != 63) throw Limit();
            Span<byte> iv = stackalloc byte[16]; iv.Clear(); properties.Slice(prefix + saltLength, ivLength).CopyTo(iv);
            this.input = input; this.packedLength = packedLength; this.ct = ct;
            try { transform = password.CreateDecryptor(power, properties.Slice(prefix, saltLength), iv); }
            catch (InvalidDataException ex) when (ex.InnerException is ArchiveWriteException) { throw (ArchiveWriteException)ex.InnerException; }
            finally { CryptographicOperations.ZeroMemory(iv); }
        }
        private void Fill()
        {
            if (offset < available || fetched == packedLength) return;
            ct.ThrowIfCancellationRequested(); int wanted = (int)Math.Min(cipher.Length, packedLength - fetched), count = 0;
            while (count < wanted)
            {
                ct.ThrowIfCancellationRequested(); int n = input.Read(cipher.AsSpan(count, wanted - count));
                if (n <= 0 || n > wanted - count) throw Invalid(); count += n;
            }
            if (transform.TransformBlock(cipher, 0, count, plain, 0) != count) throw Invalid();
            fetched += count; offset = 0; available = count;
        }
        public override int Read(Span<byte> buffer)
        {
            Live(); ct.ThrowIfCancellationRequested(); if (buffer.IsEmpty || ended) return 0;
            if (PositionValue == DeclaredLength)
            {
                Fill(); if (fetched != packedLength || available - offset != packedLength - DeclaredLength || input.ReadByte() != -1) throw Invalid();
                byte[] tail = transform.TransformFinalBlock([], 0, 0);
                try { if (tail.Length != 0) throw Invalid(); } finally { CryptographicOperations.ZeroMemory(tail); }
                ended = true; CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(cipher); return 0;
            }
            Fill(); int n = (int)Math.Min(Math.Min(buffer.Length, available - offset), DeclaredLength - PositionValue);
            if (n <= 0) throw Invalid(); plain.AsSpan(offset, n).CopyTo(buffer); offset += n; PositionValue += n; return n;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !Disposed)
            { try { transform.Dispose(); } finally { CryptographicOperations.ZeroMemory(cipher); CryptographicOperations.ZeroMemory(plain); } }
            base.Dispose(disposing);
        }
    }
    private abstract class ReadLayer(long length) : Stream
    {
        protected bool Disposed;
        protected long PositionValue;
        protected readonly long DeclaredLength = length;
        protected void Live() => ObjectDisposedException.ThrowIf(Disposed, this);
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public abstract override int Read(Span<byte> buffer);
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => !Disposed; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => DeclaredLength; public override long Position { get => PositionValue; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidArchive, "The 7z encoded-header graph has invalid extents, properties or trailing data.");
    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.UnsupportedFeature, "This encoded-header graph or password requires further strict decoder qualification.");
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The 7z encoded-header decoder exceeds its resource budget.");
}
