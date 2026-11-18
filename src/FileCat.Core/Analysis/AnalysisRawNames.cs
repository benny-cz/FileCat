using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Analysis;

public static class AnalysisRawNames
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static string Decode(string encoding, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 1 or > AnalysisLimits.RawNameBytes) throw Invalid();
        try
        {
            if (encoding == "utf8") return Utf8.GetString(bytes);
            if (encoding == "unix-bytes") return Encoding.UTF8.GetString(bytes);
            if (encoding == "utf16le" && bytes.Length % 2 == 0)
            {
                var chars = new char[bytes.Length / 2];
                for (int i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
                return new string(chars);
            }
        }
        catch (DecoderFallbackException) { throw Invalid(); }
        throw Invalid();
    }
    public static byte[] Encode(string encoding, string name)
    {
        if (name.Length is < 1 or > AnalysisLimits.RawNameBytes) throw Invalid();
        try
        {
            if (encoding == "utf16le")
            {
                if (name.Length > AnalysisLimits.RawNameBytes / 2) throw Invalid();
                byte[] bytes = new byte[name.Length * 2];
                for (int i = 0; i < name.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), name[i]);
                return bytes;
            }
            if (encoding is "utf8" or "unix-bytes")
            {
                if (Utf8.GetByteCount(name) > AnalysisLimits.RawNameBytes) throw Invalid();
                return Utf8.GetBytes(name);
            }
        }
        catch (EncoderFallbackException) { throw Invalid(); }
        throw Invalid();
    }
    public static bool HasLosslessText(AnalysisNode node) => HasLosslessText(node.NameEncoding, node.RawName.AsSpan(), node.Name);

    internal static bool HasLosslessText(string encoding, ReadOnlySpan<byte> raw, string name)
    {
        try
        {
            if (encoding == "unix-bytes" && !raw.IsEmpty) _ = Utf8.GetCharCount(raw);
            _ = Utf8.GetByteCount(name);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
        catch (EncoderFallbackException) { return false; }
    }
    private static InvalidDataException Invalid() => new("Invalid encoded analysis name.");
}
