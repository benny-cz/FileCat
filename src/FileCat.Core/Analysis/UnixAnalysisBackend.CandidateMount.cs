using System.Globalization;
using System.Text;

namespace FileCat.Core.Analysis;

public sealed unsafe partial class UnixAnalysisBackend
{
    // A bind mount of a subtree can hide ancestors of a protected directory.
    // For staging, require the mounted filesystem's whole root on Linux.
    internal bool HasWholeCandidateMount()
    {
        Guard();
        if (_mac) return true;
        if (_mount == 0) return false;

        using var table = File.OpenRead("/proc/self/mountinfo");
        byte* facts = stackalloc byte[512];
        if (FstatFs((int)table.SafeFileHandle.DangerousGetHandle(), facts) != 0 || *(ulong*)facts != 0x9fa0)
            return false;
        const int maximumBytes = 1 << 20;
        byte[] bytes = new byte[4096];
        int length = 0;
        while (true)
        {
            int read = table.Read(bytes.AsSpan(length));
            if (read == 0) break;
            length += read;
            if (length > maximumBytes) return false;
            if (length == bytes.Length) Array.Resize(ref bytes, Math.Min(bytes.Length * 2, maximumBytes + 1));
        }
        string prefix = _mount.ToString(CultureInfo.InvariantCulture) + " ";
        using var reader = new StringReader(Encoding.UTF8.GetString(bytes, 0, length));
        for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string[] fields = line.Split(' ', 5, StringSplitOptions.None);
            return fields.Length == 5 && fields[3] == "/";
        }
        return false;
    }
}
