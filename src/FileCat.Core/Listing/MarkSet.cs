using System.Numerics;

namespace FileCat.Core.Listing;

/// <summary>Growable bit set of marked store indices; a selection never becomes a million objects (§4.3).</summary>
public sealed class MarkSet
{
    private ulong[] _bits = [];

    public int Count { get; private set; }

    public bool Get(int index) =>
        index >= 0 && (index >> 6) < _bits.Length && (_bits[index >> 6] & (1UL << (index & 63))) != 0;

    /// <summary>Sets a mark; returns true when the value changed.</summary>
    public bool Set(int index, bool value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int word = index >> 6;
        ulong bit = 1UL << (index & 63);
        if (value)
        {
            if (word >= _bits.Length) Array.Resize(ref _bits, Math.Max(word + 1, _bits.Length * 2));
            if ((_bits[word] & bit) != 0) return false;
            _bits[word] |= bit;
            Count++;
            return true;
        }
        if (word >= _bits.Length || (_bits[word] & bit) == 0) return false;
        _bits[word] &= ~bit;
        Count--;
        return true;
    }

    public void Clear()
    {
        Array.Clear(_bits);
        Count = 0;
    }

    public IEnumerable<int> Enumerate()
    {
        for (int w = 0; w < _bits.Length; w++)
        {
            ulong v = _bits[w];
            while (v != 0)
            {
                int bit = BitOperations.TrailingZeroCount(v);
                yield return (w << 6) + bit;
                v &= v - 1;
            }
        }
    }
}
