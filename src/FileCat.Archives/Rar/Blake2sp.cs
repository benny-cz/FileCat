using System.Buffers.Binary;
using System.Numerics;

namespace FileCat.Archives.Rar;

/// <summary>
/// BLAKE2sp-256, the hash RAR 5 stores for a member when it is not CRC32: eight BLAKE2s leaves (RFC 7693) take the
/// input's 64-byte blocks in turn, and a root BLAKE2s hashes their digests (the BLAKE2 specification's tree mode).
/// </summary>
internal sealed class Blake2sp
{
    private const int Leaves = 8;
    private const int BlockBytes = 64;
    private const int Stripe = Leaves * BlockBytes;
    private readonly Blake2s[] _leaves = new Blake2s[Leaves];
    private readonly Blake2s _root;
    private readonly byte[] _buffer = new byte[Stripe];
    private int _buffered;

    public Blake2sp()
    {
        for (int i = 0; i < Leaves; i++) _leaves[i] = new Blake2s(nodeOffset: (uint)i, nodeDepth: 0, lastNode: i == Leaves - 1);
        _root = new Blake2s(nodeOffset: 0, nodeDepth: 1, lastNode: true);
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_buffered > 0)
        {
            int fill = Math.Min(Stripe - _buffered, data.Length);
            data[..fill].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += fill;
            data = data[fill..];
            if (_buffered < Stripe) return;
            if (data.IsEmpty) return; // keep a full stripe: the final blocks need the finalization flags
            for (int i = 0; i < Leaves; i++) _leaves[i].Append(_buffer.AsSpan(i * BlockBytes, BlockBytes));
            _buffered = 0;
        }
        // Whole stripes while more input follows them; the last (possibly full) stripe stays buffered.
        while (data.Length > Stripe)
        {
            for (int i = 0; i < Leaves; i++) _leaves[i].Append(data.Slice(i * BlockBytes, BlockBytes));
            data = data[Stripe..];
        }
        data.CopyTo(_buffer);
        _buffered = data.Length;
    }

    public byte[] Finish()
    {
        Span<byte> digest = stackalloc byte[32];
        for (int i = 0; i < Leaves; i++)
        {
            int start = i * BlockBytes;
            if (_buffered > start) _leaves[i].Append(_buffer.AsSpan(start, Math.Min(BlockBytes, _buffered - start)));
            _leaves[i].Finish(digest);
            _root.Append(digest);
        }
        var result = new byte[32];
        _root.Finish(result);
        return result;
    }

    /// <summary>BLAKE2s-256 with the tree parameters BLAKE2sp gives its nodes (fanout 8, depth 2, inner length 32).</summary>
    private sealed class Blake2s
    {
        private static readonly uint[] s_iv =
        [
            0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
        ];

        private static readonly byte[][] s_sigma =
        [
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
            [14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3],
            [11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4],
            [7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8],
            [9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13],
            [2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9],
            [12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11],
            [13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10],
            [6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5],
            [10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0],
        ];

        private readonly uint[] _h = new uint[8];
        private readonly byte[] _block = new byte[BlockBytes];
        private readonly bool _lastNode;
        private int _blockLength;
        private ulong _counter;

        public Blake2s(uint nodeOffset, byte nodeDepth, bool lastNode)
        {
            _lastNode = lastNode;
            Span<byte> parameters = stackalloc byte[32];
            parameters.Clear();
            parameters[0] = 32;      // digest length
            parameters[1] = 0;       // key length
            parameters[2] = Leaves;  // fanout
            parameters[3] = 2;       // depth
            BinaryPrimitives.WriteUInt32LittleEndian(parameters[8..], nodeOffset);
            parameters[14] = nodeDepth;
            parameters[15] = 32;     // inner length
            for (int i = 0; i < 8; i++) _h[i] = s_iv[i] ^ BinaryPrimitives.ReadUInt32LittleEndian(parameters[(4 * i)..]);
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                if (_blockLength == BlockBytes)
                {
                    _counter += BlockBytes;
                    Compress(final: false);
                    _blockLength = 0;
                }
                int n = Math.Min(BlockBytes - _blockLength, data.Length);
                data[..n].CopyTo(_block.AsSpan(_blockLength));
                _blockLength += n;
                data = data[n..];
            }
        }

        public void Finish(Span<byte> digest)
        {
            _counter += (ulong)_blockLength;
            _block.AsSpan(_blockLength).Clear();
            Compress(final: true);
            for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32LittleEndian(digest[(4 * i)..], _h[i]);
        }

        private void Compress(bool final)
        {
            Span<uint> m = stackalloc uint[16];
            Span<uint> v = stackalloc uint[16];
            for (int i = 0; i < 16; i++) m[i] = BinaryPrimitives.ReadUInt32LittleEndian(_block.AsSpan(4 * i));
            for (int i = 0; i < 8; i++)
            {
                v[i] = _h[i];
                v[i + 8] = s_iv[i];
            }
            v[12] ^= (uint)_counter;
            v[13] ^= (uint)(_counter >> 32);
            if (final)
            {
                v[14] = ~v[14];
                if (_lastNode) v[15] = ~v[15];
            }
            foreach (byte[] s in s_sigma)
            {
                G(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
                G(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
                G(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
                G(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
                G(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
                G(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
                G(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
                G(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
            }
            for (int i = 0; i < 8; i++) _h[i] ^= v[i] ^ v[i + 8];
        }

        private static void G(Span<uint> v, int a, int b, int c, int d, uint x, uint y)
        {
            v[a] = v[a] + v[b] + x;
            v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
            v[c] = v[c] + v[d];
            v[b] = BitOperations.RotateRight(v[b] ^ v[c], 12);
            v[a] = v[a] + v[b] + y;
            v[d] = BitOperations.RotateRight(v[d] ^ v[a], 8);
            v[c] = v[c] + v[d];
            v[b] = BitOperations.RotateRight(v[b] ^ v[c], 7);
        }
    }
}
