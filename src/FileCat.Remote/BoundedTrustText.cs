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

    /// <summary>
    /// Passes each line to <paramref name="line"/>, holding one at a time, so that a long file is read (OpenSSH reads a
    /// known_hosts of any length) and a runaway one is not. Returns why the file was not read to its end, or null.
    /// </summary>
    public static string? ForEachLine(string path, long maxBytes, int maxLineChars, Action<string> line)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 << 10,
            FileOptions.SequentialScan);
        string tooLarge = $"is larger than {maxBytes >> 20} MiB", tooLong = $"has a line longer than {maxLineChars >> 10} KiB";
        if (stream.Length > maxBytes) return tooLarge;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var current = new StringBuilder();
        char[] buffer = new char[16 << 10];
        long total = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            if ((total += read) > maxBytes) return tooLarge; // it grew while being read
            int start = 0;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] != '\n') continue;
                if (current.Length + i - start > maxLineChars) return tooLong;
                current.Append(buffer, start, i - start);
                line(Take(current));
                start = i + 1;
            }
            if (current.Length + read - start > maxLineChars) return tooLong;
            current.Append(buffer, start, read - start);
        }
        if (current.Length > 0) line(Take(current));
        return null;

        static string Take(StringBuilder text)
        {
            string value = text.ToString(0, text.Length > 0 && text[^1] == '\r' ? text.Length - 1 : text.Length);
            text.Clear();
            return value;
        }
    }
}
