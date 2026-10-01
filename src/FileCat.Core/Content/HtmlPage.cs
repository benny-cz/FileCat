using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Content;

/// <summary>
/// What the viewer's page view serves (plan §16.1, D-51): the page's own bytes, and for a page on disk the files it
/// refers to in its own folder (images, style sheets, fonts), never anything outside it. Everything else, the web
/// included, is refused by the view, and scripts do not run. The page is addressed on a reserved domain
/// (<see cref="Host"/>), which never resolves, so a request that got past the view would go nowhere.
/// </summary>
public sealed class HtmlPage
{
    /// <summary>A reserved name (RFC 2606): no such site exists.</summary>
    public const string Host = "filecat-page.example";

    /// <summary>The largest page, or file beside it, that is served.</summary>
    public const int MaxBytes = 64 << 20;

    private readonly IContentSource _source;
    private readonly string? _folder;
    private readonly bool _markdown;

    /// <summary>A Markdown file drawn as a page (release issue I25): its pictures load from its folder, as a page's do.</summary>
    public static HtmlPage ForMarkdown(IContentSource source, string displayName) => new(source, displayName, markdown: true);

    public HtmlPage(IContentSource source, string displayName) : this(source, displayName, markdown: false)
    {
    }

    private HtmlPage(IContentSource source, string displayName, bool markdown)
    {
        _markdown = markdown;
        _source = source;
        string name = Path.GetFileName(displayName.TrimEnd('/', '\\'));
        Name = name.Length == 0 ? "page.html" : name;
        if (source.LocalPath is { } local)
        {
            try { _folder = Path.GetDirectoryName(Path.GetFullPath(local)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { _folder = null; }
        }
    }

    /// <summary>The page's file name; its address is the site root plus this name.</summary>
    public string Name { get; }

    /// <summary>Whether the files beside the page are served (it is a file on disk).</summary>
    public bool ServesFolder => _folder is not null;

    public string Address => $"https://{Host}/{Uri.EscapeDataString(Name)}";

    /// <summary>
    /// The bytes and type of what the page asked for at <paramref name="path"/> (the path part of an address on
    /// <see cref="Host"/>, escaped as in the address), or null when it is not served: outside the page's folder, not a
    /// file, too large, or unreadable.
    /// </summary>
    public (byte[] Bytes, string MimeType)? Resolve(string path)
    {
        string relative;
        try { relative = Uri.UnescapeDataString(path.Split('?', '#')[0]).TrimStart('/'); }
        catch (UriFormatException) { return null; }
        if (relative.Length == 0 || relative == Name) return _markdown ? Render() : Read(_source, MimeType(Name));
        if (_folder is null || relative.Contains('\0') || relative.Contains(':') && OperatingSystem.IsWindows()) return null;
        string full;
        try { full = Path.GetFullPath(Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        if (!Inside(full)) return null;
        try
        {
            var file = new FileInfo(full);
            if (!file.Exists) return null;
            // A link inside the folder that leads out of it is not followed.
            if (file.LinkTarget is not null && (file.ResolveLinkTarget(returnFinalTarget: true)?.FullName is not { } target || !Inside(target))) return null;
            if (file.Length > MaxBytes) return null;
            return (File.ReadAllBytes(full), MimeType(full));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private bool Inside(string full)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string folder = _folder!.EndsWith(Path.DirectorySeparatorChar) ? _folder : _folder + Path.DirectorySeparatorChar;
        return full.StartsWith(folder, comparison);
    }

    /// <summary>The Markdown file as a page: its text decoded as the viewer decodes it, drawn, and served as HTML.</summary>
    private (byte[], string)? Render()
    {
        if (Read(_source, "text/markdown") is not { } file) return null;
        var bytes = file.Item1;
        var guess = TextDecoding.Detect(bytes.AsSpan(0, Math.Min(bytes.Length, 64 * 1024)));
        string text = guess.Encoding.GetString(bytes, guess.PreambleLength, bytes.Length - guess.PreambleLength);
        return (Encoding.UTF8.GetBytes(Markdown.ToPage(text, Name)), "text/html");
    }

    private static (byte[], string)? Read(IContentSource source, string mime)
    {
        try
        {
            long length = source.Length;
            if (length > MaxBytes) return null;
            using var buffer = new MemoryStream(length > 0 ? (int)length : 64 * 1024);
            var chunk = new byte[64 * 1024];
            for (long offset = 0; buffer.Length <= MaxBytes;)
            {
                int n = source.Read(offset, chunk);
                if (n <= 0) break;
                buffer.Write(chunk, 0, n);
                offset += n;
            }
            return buffer.Length > MaxBytes ? null : (buffer.ToArray(), mime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The type a browser expects for a file, by its extension.</summary>
    public static string MimeType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".htm" or ".html" or ".shtml" => "text/html",
        ".xhtml" or ".xht" => "application/xhtml+xml",
        ".css" => "text/css",
        ".js" or ".mjs" => "text/javascript",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".otf" => "font/otf",
        ".txt" => "text/plain",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".wav" => "audio/wav",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// Whether a file is a web page: by its extension, or, for another name, by beginning (after a byte-order mark and
    /// white space) with a doctype or an html element.
    /// </summary>
    public static bool IsHtml(string name, ReadOnlySpan<byte> prefix)
    {
        if (Path.GetExtension(name.TrimEnd('/', '\\')).ToLowerInvariant() is ".htm" or ".html" or ".xhtml" or ".xht" or ".shtml") return true;
        if (prefix.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) prefix = prefix[3..];
        int i = 0;
        while (i < prefix.Length && prefix[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') i++;
        var start = Encoding.ASCII.GetString(prefix[i..Math.Min(prefix.Length, i + 15)]).ToLowerInvariant();
        return start.StartsWith("<!doctype html", StringComparison.Ordinal) || start.StartsWith("<html", StringComparison.Ordinal);
    }
}
