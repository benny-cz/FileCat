namespace FileCat.Recovery;

/// <summary>How well data fits a file's type, judged by the bytes its format always starts with. Ordered: better is higher.</summary>
internal enum ContentFit
{
    /// <summary>The type is known and the data does not start like it.</summary>
    Mismatch,

    /// <summary>All zeros: space never written or cleared since.</summary>
    Blank,

    /// <summary>Nothing about the type says what its data looks like.</summary>
    Unknown,

    /// <summary>The data starts the way the type always does.</summary>
    Match,
}

/// <summary>
/// Signatures of common file types, for telling a deleted file's content from other data when the file system no longer
/// says exactly where the content is. A match is evidence, not proof: it only ever chooses among places the file system
/// still allows.
/// </summary>
internal static class ContentSignature
{
    private static readonly Dictionary<string, (int Offset, byte[] Bytes)[]> Signatures = Build();

    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "log", "ini", "inf", "xml", "json", "csv", "tsv", "md", "htm", "html", "css", "js", "mjs", "ts", "cs", "py", "sh",
        "bat", "cmd", "ps1", "psm1", "psd1", "reg", "sarif", "yml", "yaml", "toml", "cfg", "conf", "sql", "srt", "vtt", "svg",
        "xsl", "xslt", "xaml", "config", "manifest", "nfo", "url", "properties", "c", "h", "cpp", "hpp", "java", "kt", "go",
        "rs", "rb", "php", "pl", "lua", "adml", "admx", "resx", "vbs", "wsf", "mof", "tex", "rst", "ics", "vcf", "eml",
    };

    private static Dictionary<string, (int, byte[])[]> Build()
    {
        var table = new Dictionary<string, (int, byte[])[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string extensions, params (int Offset, byte[] Bytes)[] signatures)
        {
            foreach (var extension in extensions.Split(' ')) table[extension] = signatures;
        }
        static (int, byte[]) At0(params byte[] bytes) => (0, bytes);
        static (int, byte[]) Ascii(string text, int offset = 0) => (offset, System.Text.Encoding.ASCII.GetBytes(text));

        Add("exe dll sys efi mui cpl ocx scr drv mun ax acm tlb winmd", Ascii("MZ"));
        Add("zip jar apk aab docx xlsx pptx docm xlsm pptm dotx xltx potx odt ods odp odg epub nupkg vsix appx msix appxbundle msixbundle xpi whl kmz 3mf ipa cbz",
            At0(0x50, 0x4B, 0x03, 0x04), At0(0x50, 0x4B, 0x05, 0x06), At0(0x50, 0x4B, 0x07, 0x08));
        Add("pdf", Ascii("%PDF-"));
        Add("png apng", At0(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A));
        Add("jpg jpeg jpe jfif", At0(0xFF, 0xD8, 0xFF));
        Add("gif", Ascii("GIF87a"), Ascii("GIF89a"));
        Add("bmp dib", Ascii("BM"));
        Add("tif tiff dng nef cr2 arw", Ascii("II*\0"), Ascii("MM\0*"));
        Add("orf", Ascii("IIRO"), Ascii("IIRS"), Ascii("MMOR"));
        Add("rw2", Ascii("IIU\0"));
        Add("raf", Ascii("FUJIFILMCCD-RAW"));
        Add("webp wav avi ani", Ascii("RIFF"));
        Add("mp3", Ascii("ID3"), At0(0xFF, 0xFB), At0(0xFF, 0xF3), At0(0xFF, 0xF2), At0(0xFF, 0xFA));
        Add("mp4 m4a m4v m4b mov 3gp 3g2 heic heif avif cr3", Ascii("ftyp", 4));
        Add("mkv mka webm", At0(0x1A, 0x45, 0xDF, 0xA3));
        Add("flac", Ascii("fLaC"));
        Add("ogg oga ogv opus", Ascii("OggS"));
        Add("7z", At0(0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C));
        Add("rar", Ascii("Rar!\u001A\u0007"));
        Add("gz tgz", At0(0x1F, 0x8B));
        Add("bz2 tbz2", Ascii("BZh"));
        Add("xz txz", At0(0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00));
        Add("zst", At0(0x28, 0xB5, 0x2F, 0xFD));
        Add("cab", Ascii("MSCF"));
        Add("msi msp msm mst doc dot xls xlt ppt pot msg pub vsd", At0(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1));
        Add("wim esd swm", Ascii("MSWIM\0\0\0"));
        Add("vhdx", Ascii("vhdxfile"));
        Add("vmdk", Ascii("KDMV"), Ascii("# Disk DescriptorFile"));
        Add("qcow2", At0(0x51, 0x46, 0x49, 0xFB));
        Add("sqlite sqlite3", Ascii("SQLite format 3\0"));
        Add("ttf", At0(0x00, 0x01, 0x00, 0x00), Ascii("true"));
        Add("otf", Ascii("OTTO"));
        Add("ttc", Ascii("ttcf"));
        Add("woff", Ascii("wOFF"));
        Add("woff2", Ascii("wOF2"));
        Add("class", At0(0xCA, 0xFE, 0xBA, 0xBE));
        Add("ico", At0(0x00, 0x00, 0x01, 0x00));
        Add("cur", At0(0x00, 0x00, 0x02, 0x00));
        Add("psd", Ascii("8BPS"));
        Add("rtf", Ascii("{\\rtf"));
        Add("so ko", At0(0x7F, 0x45, 0x4C, 0x46));
        Add("deb", Ascii("!<arch>"));
        Add("rpm", At0(0xED, 0xAB, 0xEE, 0xDB));
        Add("evtx", Ascii("ElfFile\0"));
        Add("lnk", At0(0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00));
        Add("chm", Ascii("ITSF"));
        return table;
    }

    /// <summary>How <paramref name="head"/> (the first bytes of a place, at most the file's size) fits the file named.</summary>
    public static ContentFit Check(string name, ReadOnlySpan<byte> head, long size)
    {
        if (head.IsEmpty) return ContentFit.Unknown;
        if (head.IndexOfAnyExcept((byte)0) < 0) return ContentFit.Blank;
        string extension = Path.GetExtension(name).TrimStart('.');
        if (Signatures.TryGetValue(extension, out var signatures))
        {
            foreach (var (offset, bytes) in signatures)
            {
                if (head.Length >= offset + bytes.Length && head.Slice(offset, bytes.Length).SequenceEqual(bytes)) return ContentFit.Match;
            }
            // A file too short to hold its type's signature says nothing either way.
            return signatures.All(s => size < s.Offset + s.Bytes.Length) ? ContentFit.Unknown : ContentFit.Mismatch;
        }
        if (Text.Contains(extension)) return LooksLikeText(head) ? ContentFit.Match : ContentFit.Mismatch;
        return ContentFit.Unknown;
    }

    /// <summary>A byte-order mark, UTF-16 text, or bytes without NULs and with almost no control characters.</summary>
    internal static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) || head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return true;
        int control = 0, utf16 = 0;
        for (int i = 0; i < head.Length; i++)
        {
            byte b = head[i];
            if (b == 0)
            {
                if ((i & 1) == 1 && head[i - 1] is >= 0x09 and < 0x7F) utf16++;
                else return false;
                continue;
            }
            if (b < 0x20 && b is not ((byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0C or 0x1A or 0x1B)) control++;
        }
        if (utf16 > 0) return utf16 * 2 >= head.Length - 1 && control == 0;
        return control * 100 <= head.Length;
    }
}
