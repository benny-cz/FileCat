using System.Buffers.Binary;

namespace FileCat.Core.Analysis;

/// <summary>Private descending ordinal-ignore-case key, never a normalized filesystem locator.</summary>
internal static class AnalysisHierarchyNameKey
{
    internal static byte[] Create(ulong parent, ulong id, string name)
    {
        if (name.Length is < 1 or > AnalysisLimits.RawNameBytes || name.Contains('\0') || id == 0) throw new InvalidDataException("Invalid captured ordering name.");
        string upper = name.ToUpperInvariant();
        if (upper.Length != name.Length) throw new IOException("The runtime cannot represent this captured name's bounded ordinal order.");
        byte[] key = new byte[checked(18 + name.Length * 2)]; BinaryPrimitives.WriteUInt64BigEndian(key, parent);
        for (int at = 0; at < name.Length;)
        {
            int width = char.IsHighSurrogate(name[at]) && at + 1 < name.Length && char.IsLowSurrogate(name[at + 1]) ? 2 : 1;
            // Ordinal casing has exceptions to invariant text casing (notably U+017F). Ask the
            // actual public comparer before using a candidate, including valid surrogate pairs.
            // This also respects the runtime's configured ICU/NLS/invariant comparison behavior.
            bool equivalent = name.AsSpan(at, width).Equals(upper.AsSpan(at, width), StringComparison.OrdinalIgnoreCase);
            for (int unit = 0; unit < width; unit++)
                BinaryPrimitives.WriteUInt16BigEndian(key.AsSpan(8 + (at + unit) * 2), unchecked((ushort)~(equivalent ? upper[at + unit] : name[at + unit])));
            at += width;
        }
        // A captured name contains no NUL; the complemented terminator distinguishes prefixes.
        key[8 + name.Length * 2] = key[9 + name.Length * 2] = byte.MaxValue;
        BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(10 + name.Length * 2), id); return key;
    }
}
