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
        int encoded = 0;
        for (int at = 0; at < name.Length; at++)
        {
            bool pair = char.IsHighSurrogate(name[at]) && at + 1 < name.Length && char.IsLowSurrogate(name[at + 1]);
            encoded = checked(encoded + (pair ? 5 : name[at] == char.MaxValue ? 3 : 2)); if (pair) at++;
        }
        if (encoded > AnalysisExternalSort.MaximumKeyBytes - 18) throw new IOException("A captured name exceeds the bounded ordering-key payload.");
        byte[] key = new byte[18 + encoded]; BinaryPrimitives.WriteUInt64BigEndian(key, parent); int output = 8;
        for (int at = 0; at < name.Length;)
        {
            int width = char.IsHighSurrogate(name[at]) && at + 1 < name.Length && char.IsLowSurrogate(name[at + 1]) ? 2 : 1;
            // Ordinal casing has exceptions to invariant text casing (notably U+017F). Ask the
            // actual public comparer before using a candidate, including valid surrogate pairs.
            // This also respects the runtime's configured ICU/NLS/invariant comparison behavior.
            bool equivalent = name.AsSpan(at, width).Equals(upper.AsSpan(at, width), StringComparison.OrdinalIgnoreCase);
            var value = equivalent ? upper : name;
            if (width == 2)
            {
                // Ordinal-ignore-case compares valid supplementary scalars after
                // BMP values, rather than sorting their first UTF-16 surrogate.
                int scalar = char.ConvertToUtf32(value[at], value[at + 1]);
                key[output++] = 0; key[output++] = 0; key[output++] = unchecked((byte)~(scalar >> 16));
                BinaryPrimitives.WriteUInt16BigEndian(key.AsSpan(output), unchecked((ushort)~scalar)); output += 2;
            }
            else
            {
                char unit = value[at]; BinaryPrimitives.WriteUInt16BigEndian(key.AsSpan(output), unchecked((ushort)~unit)); output += 2;
                // U+FFFF is the escape prefix; its zero suffix precedes all
                // supplementary scalars. Other BMP/unpaired units stay compact.
                if (unit == char.MaxValue) key[output++] = byte.MaxValue;
            }
            at += width;
        }
        // A captured name contains no NUL; the complemented terminator distinguishes prefixes.
        key[output] = key[output + 1] = byte.MaxValue;
        BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(output + 2), id); return key;
    }
}
