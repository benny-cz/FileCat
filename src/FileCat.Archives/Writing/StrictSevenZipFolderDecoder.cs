using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using SharpCompress.Compressors.Filters;

namespace FileCat.Archives.Writing;

internal sealed record SevenZipPackedWitness(SevenZipRawPack Physical, string Sha256);

/// <summary>One complete non-solid graph. Layers and ranges borrow the retained source; this owns only decoder state.</summary>
internal sealed class StrictSevenZipFolderDecoder : Stream
{
    private readonly List<Node> nodes = [];
    private readonly List<PackRange> packs = [];
    private readonly Node last;
    private bool disposed;
    internal long ReservedDecoderHeapBytes { get; }
    internal bool Complete => last.Complete && nodes.All(node => node.Complete) && packs.All(pack => pack.Complete);
    internal IReadOnlyList<SevenZipPackedWitness> Witnesses
    { get { ObjectDisposedException.ThrowIf(disposed, this); return Complete ? packs.Select(pack => new SevenZipPackedWitness(pack.Physical, pack.Digest)).ToArray() : throw Invalid(); } }

    internal StrictSevenZipFolderDecoder(Stream source, SevenZipRawFolder folder, IReadOnlyList<SevenZipRawPack> physical,
        SevenZipReadPasswordSession? password, ArchiveWriteBudget budget, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source); ct.ThrowIfCancellationRequested();
        int count = folder.Coders.Count;
        if (!source.CanRead || !source.CanSeek || count is < 1 or > 32 || folder.UnpackedSizes.Count != count ||
            folder.FinalOutput < 0 || folder.FinalOutput >= count || folder.Bindings.Count != count - 1 ||
            folder.PackedInputs.Count is < 1 or > 128 || folder.FirstPack < 0 || folder.FirstPack > physical.Count - folder.PackedInputs.Count) throw Invalid();
        int[] starts = new int[count]; int inputs = 0; long heap = checked(65536L * folder.PackedInputs.Count), work = 0;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested(); var coder = folder.Coders[i]; starts[i] = inputs;
            if (coder is null || coder.Outputs != 1 || coder.Inputs != (SevenZipStrictMetadata.Method(coder) == 0x0303011b ? 4 : 1)) throw Invalid();
            inputs = checked(inputs + coder.Inputs); if (inputs > 128) throw Invalid();
            long size = folder.UnpackedSizes[i]; if (size < 0 || size > ArchiveWritePolicy.MaxPhysicalBytes) throw Limit();
            work = checked(work + size); if (work > 4 * ArchiveWritePolicy.MaxLogicalBytes) throw Limit();
            heap = checked(heap + Heap(coder));
        }
        int[] bound = Enumerable.Repeat(-1, inputs).ToArray(), packed = Enumerable.Repeat(-1, inputs).ToArray();
        bool[] usedOutput = new bool[count];
        foreach (var binding in folder.Bindings)
        {
            if (binding.Input < 0 || binding.Input >= inputs || binding.Output < 0 || binding.Output >= count ||
                bound[binding.Input] >= 0 || usedOutput[binding.Output]) throw Invalid();
            bound[binding.Input] = binding.Output; usedOutput[binding.Output] = true;
        }
        if (usedOutput[folder.FinalOutput] || usedOutput.Count(value => !value) != 1) throw Invalid();
        for (int i = 0; i < folder.PackedInputs.Count; i++)
        {
            int input = folder.PackedInputs[i];
            if (input < 0 || input >= inputs || bound[input] >= 0 || packed[input] >= 0) throw Invalid(); packed[input] = i;
            var pack = physical[folder.FirstPack + i];
            if (pack.Offset < 32 || pack.Length < 0 || pack.Offset > source.Length || pack.Length > source.Length - pack.Offset) throw Invalid();
        }
        if (Enumerable.Range(0, inputs).Any(i => bound[i] < 0 && packed[i] < 0)) throw Invalid();
        byte[] admission = new byte[count]; Visit(folder.FinalOutput);
        if (admission.Any(value => value != 2)) throw Invalid();
        if (folder.Encrypted && password is null) throw Unsupported();
        // Metadata and live decoder state have distinct ceilings. Reserve the complete graph against the worker
        // envelope, leaving headroom for CLR/proxy buffers, before a codec can read or allocate its dictionary.
        budget.AddMetadata(0, checked(count * 256L + folder.PackedInputs.Count * 128L));
        ArchiveWriteBudget.Reserve(budget.RetainedHeapBytes, heap, ArchiveWritePolicy.MaxWorkerMemoryBytes - (64 << 20));
        ReservedDecoderHeapBytes = heap;
        byte[] state = new byte[count]; Node?[] built = new Node[count];
        try
        {
            foreach (var pack in physical.Skip(folder.FirstPack).Take(folder.PackedInputs.Count)) packs.Add(new(source, pack, ct));
            last = Build(folder.FinalOutput);
            if (state.Any(value => value != 2)) throw Invalid();
        }
        catch { try { DisposeOwned(); } catch { } throw; }

        void Visit(int index)
        {
            if (admission[index] == 1) throw Invalid(); if (admission[index] == 2) return; admission[index] = 1;
            for (int i = starts[index]; i < starts[index] + folder.Coders[index].Inputs; i++) if (bound[i] >= 0) Visit(bound[i]);
            admission[index] = 2;
        }

        Node Build(int index)
        {
            ct.ThrowIfCancellationRequested(); if (state[index] == 1) throw Invalid(); if (built[index] is { } existing) return existing;
            state[index] = 1; var coder = folder.Coders[index]; Stream[] sources = new Stream[coder.Inputs]; long[] lengths = new long[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                int input = starts[index] + i;
                if (bound[input] is >= 0 and var parent) { var node = Build(parent); sources[i] = node; lengths[i] = node.Length; }
                else { var range = packs[packed[input]]; sources[i] = range; lengths[i] = range.Length; }
            }
            long length = folder.UnpackedSizes[index], totalInput = lengths.Sum();
            if (length > 64L << 20 && length > Math.Max(1, totalInput) * 1000) throw Limit();
            Stream layer = Create(coder, sources, lengths, length, password, ct);
            Node result;
            try { result = new(layer, length, ct); } catch { layer.Dispose(); throw; }
            nodes.Add(result); built[index] = result; state[index] = 2; return result;
        }
    }

    private static long Heap(SevenZipCoder coder)
    {
        var p = coder.Properties.AsSpan(); if (p.Length > 64 || coder.MethodId.Length is < 1 or > 8) throw Invalid();
        ulong method = SevenZipStrictMetadata.Method(coder); long heap = 256 << 10;
        switch (method)
        {
            case 0: case 0x040108: case 0x040202: case 0x0303011b: case 0x020302: case 0x020304:
            case 0x03030103: case 0x03030205: case 0x03030401: case 0x03030501: case 0x03030701: case 0x03030805:
                if (!p.IsEmpty) throw Unsupported();
                if (method == 0x040202) heap += 10 << 20;
                if (method == 0x040108) heap += 1 << 20;
                break;
            case 3: if (p.Length != 1) throw Invalid(); break;
            case 10: case 11: _ = BranchFilterProperties.InitialPosition(coder.Properties, method == 10 ? 3u : 1u); break;
            case 0x21: case 0x030101:
                long dictionary;
                if (method == 0x21)
                { if (p.Length != 1 || p[0] > 40) throw Invalid(); dictionary = p[0] == 40 ? uint.MaxValue : (2L | (p[0] & 1)) << ((p[0] >> 1) + 11); }
                else { if (p.Length != 5 || p[0] >= 225) throw Invalid(); dictionary = Math.Max(4096L, BinaryPrimitives.ReadUInt32LittleEndian(p[1..])); }
                if (dictionary > StrictLzmaReadStream.MaximumDictionaryBytes) throw Limit(); heap += dictionary + (6 << 20); break;
            case 0x030401:
                if (p.Length != 5 || p[0] is < 2 or > 64) throw Invalid();
                uint model = BinaryPrimitives.ReadUInt32LittleEndian(p[1..]); if (model < 2048) throw Invalid();
                if (model > StrictPpmdReadStream.MaximumModelBytes) throw Limit(); heap += model + (1 << 20); break;
            case 0x04f71101:
                // Current interoperable extension producer profile. Other versions/properties need qualification.
                if (p.Length != 5 || p[0] != 1 || p[1] != 5 || p[3] != 0 || p[4] != 0) throw Unsupported();
                heap += ArchiveWritePolicy.MaxZstdWindowBytes + (8 << 20); break;
            case 0x06f10701:
                if (p.Length is < 1 or > 34) throw Invalid();
                int power = p[0] & 63; if (power > 24 && power != 63) throw Limit(); break;
            default: throw Unsupported();
        }
        return heap;
    }

    private static Stream Create(SevenZipCoder coder, Stream[] sources, long[] lengths, long length,
        SevenZipReadPasswordSession? password, CancellationToken ct)
    {
        ulong method = SevenZipStrictMetadata.Method(coder); var p = coder.Properties; Stream input = sources[0];
        if (method == 0x0303011b) return new StrictBcj2ReadStream(sources, lengths, length, ct);
        if (method is 3 or 10 or 11 or 0x020302 or 0x020304 or 0x03030103 or 0x03030205 or 0x03030401 or 0x03030501 or 0x03030701 or 0x03030805)
        {
            if (length != lengths[0]) throw Invalid(); var borrowed = new Borrowed(input);
            return method switch
            {
                3 => new DeltaFilter(false, borrowed, p), 10 => new BCJFilterARM64(false, borrowed, BranchFilterProperties.InitialPosition(p, 3)),
                11 => new BCJFilterRISCV(false, borrowed, BranchFilterProperties.InitialPosition(p, 1)),
                0x020302 => new SwapFilter(false, borrowed, 2), 0x020304 => new SwapFilter(false, borrowed, 4),
                0x03030103 => new BCJFilter(false, borrowed), 0x03030205 => new BCJFilterPPC(false, borrowed),
                0x03030401 => new BCJFilterIA64(false, borrowed), 0x03030501 => new BCJFilterARM(false, borrowed),
                0x03030701 => new BCJFilterARMT(false, borrowed), _ => new BCJFilterSPARC(false, borrowed),
            };
        }
        return method switch
        {
            0 when length == lengths[0] => StrictSevenZipHeaderDecoder.Copy(input, length, ct),
            0 => throw Invalid(),
            0x21 or 0x030101 => new StrictLzmaReadStream(input, p, method == 0x21, lengths[0], length, ct),
            0x030401 => new StrictPpmdReadStream(input, p, lengths[0], length, ct),
            0x040108 => new StrictDeflateReadStream(input, lengths[0], length, ct),
            0x040202 => new StrictBzip2ReadStream(input, lengths[0], ct),
            0x04f71101 => new StrictZstdReadStream(input, lengths[0], ct),
            0x06f10701 => StrictSevenZipHeaderDecoder.Aes(input, lengths[0], length, p, password, ct),
            _ => throw Unsupported(),
        };
    }

    private sealed class Node(Stream decoder, long length, CancellationToken ct) : Stream
    {
        private readonly byte[] bytes = new byte[ArchiveIoAuthority.MaximumReadBytes];
        private long position; private bool disposed, failed;
        internal bool Complete { get; private set; }
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); if (failed) throw Invalid(); if (buffer.IsEmpty || Complete) return 0;
            try
            {
                int wanted = (int)Math.Min(Math.Min(buffer.Length, bytes.Length), length - position);
                if (wanted == 0) { if (decoder.Read(bytes, 0, 1) != 0) throw Invalid(); Complete = true; return 0; }
                int n = decoder.Read(bytes, 0, wanted); if (n <= 0 || n > wanted) throw Invalid();
                bytes.AsSpan(0, n).CopyTo(buffer); position += n; return n;
            }
            catch { failed = true; throw; }
        }
        public override int ReadByte() { Span<byte> value = stackalloc byte[1]; return Read(value) == 0 ? -1 : value[0]; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        protected override void Dispose(bool disposing)
        { if (disposing && !disposed) { disposed = true; try { decoder.Dispose(); } finally { CryptographicOperations.ZeroMemory(bytes); } } base.Dispose(disposing); }
        public override bool CanRead => !disposed; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => length; public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class PackRange(Stream source, SevenZipRawPack physical, CancellationToken ct) : Stream
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private ArchiveCrc32 crc = new(); private long position; private string? digest; private bool disposed;
        internal SevenZipRawPack Physical => physical;
        internal bool Complete => position == physical.Length;
        internal string Digest
        {
            get
            {
                if (!Complete || physical.Crc32 is { } expected && expected != crc.Value) throw Invalid();
                return digest ??= Convert.ToHexString(hash.GetHashAndReset());
            }
        }
        public override int Read(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested(); if (buffer.IsEmpty || Complete) return 0;
            int wanted = (int)Math.Min(Math.Min(buffer.Length, ArchiveIoAuthority.MaximumReadBytes), physical.Length - position);
            source.Position = checked(physical.Offset + position); int n = source.Read(buffer[..wanted]); if (n <= 0 || n > wanted) throw Invalid();
            hash.AppendData(buffer[..n]); crc.Append(buffer[..n]); position += n; return n;
        }
        public override int ReadByte() { Span<byte> one = stackalloc byte[1]; return Read(one) == 0 ? -1 : one[0]; }
        public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
        protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; hash.Dispose(); } base.Dispose(disposing); }
        public override bool CanRead => !disposed; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => physical.Length; public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class Borrowed(Stream input) : Stream
    {
        public override int Read(byte[] b, int o, int c) => input.Read(b, o, c);
        public override int Read(Span<byte> b) => input.Read(b);
        public override bool CanRead => input.CanRead; public override bool CanWrite => false; public override bool CanSeek => false;
        public override long Length => input.Length; public override long Position { get => input.Position; set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
    private void DisposeOwned()
    {
        Exception? first = null;
        for (int i = nodes.Count - 1; i >= 0; i--) try { nodes[i].Dispose(); } catch (Exception ex) { first ??= ex; }
        nodes.Clear();
        foreach (var pack in packs) try { pack.Dispose(); } catch (Exception ex) { first ??= ex; }
        packs.Clear();
        if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
    }
    public override int Read(Span<byte> buffer) { ObjectDisposedException.ThrowIf(disposed, this); return last.Read(buffer); }
    public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
    protected override void Dispose(bool disposing) { if (disposing && !disposed) { disposed = true; DisposeOwned(); } base.Dispose(disposing); }
    public override bool CanRead => !disposed; public override bool CanWrite => false; public override bool CanSeek => false;
    public override long Length => last.Length; public override long Position { get => last.Position; set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidArchive, "The 7z graph has invalid bindings, sizes, incomplete nodes or trailing input.");
    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.UnsupportedFeature, "The 7z graph method or properties need a separately qualified decoder profile.");
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The complete 7z graph exceeds its size, work, expansion or simultaneous decoder-memory budget.");
}
