namespace FileCat.Archives.Rar;

/// <summary>One RAR compression method's decoder. It writes into a <see cref="RarWindow"/> and keeps its state across the members of a solid run.</summary>
internal abstract class RarDecoder
{
    /// <summary>
    /// Starts a member whose packed data <paramref name="input"/> now reads. A solid member continues the previous
    /// member's tables and history (<paramref name="solid"/>); otherwise the decoder starts afresh.
    /// </summary>
    public abstract void StartMember(RarBitReader input, bool solid);

    /// <summary>
    /// Decodes until the window has no room for another symbol (returns false) or the member's data ends (returns true).
    /// </summary>
    public abstract bool Decode(RarWindow window);

    /// <summary>
    /// True when a member's data ends with a marker that the next member of a solid run depends on (RAR 2.9). Once
    /// the member's bytes are all out, the member stream then has the decoder read up to that marker, even for an
    /// empty member; any further output is damage.
    /// </summary>
    public virtual bool EndsWithMarker => false;

    /// <summary>Reads a value of up to 64 bits, most significant part first.</summary>
    protected static long ReadLong(RarBitReader input, int count)
    {
        if (count <= 32) return input.Read(count);
        long high = input.Read(count - 32);
        return high << 32 | input.Read(32);
    }
}
