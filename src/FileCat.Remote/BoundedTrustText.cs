using System.Text;

namespace FileCat.Remote;

/// <summary>Read a small local trust file once, bounded by the opened bytes rather than path metadata.</summary>
internal static class BoundedTrustText
{
    public static string[]? ReadLines(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maxBytes) return null;
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) return null;
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (lines.Count == 32768) return null;
            lines.Add(line);
        }
        return lines.ToArray();
    }
}
