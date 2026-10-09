using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FileCat.Core.Content;

/// <summary>
/// Markdown drawn as a page in the viewer (release issue I25): CommonMark's blocks and inlines with GitHub's tables, task
/// lists, strikethrough and bare web addresses — enough for READMEs, notes and documentation. Built in, so it adds no
/// dependency. The file is untrusted: its HTML never reaches the page (it is shown as text, apart from a few formatting
/// tags without attributes, and comments, which stay hidden), links lead only to the web, mail or within the page, and
/// pictures load only from the file's own folder; the page's policy forbids scripts and everything else, on top of the
/// viewer's own containment (§16.1).
/// </summary>
public static partial class Markdown
{
    /// <summary>Larger files are not drawn: their text is shown instead.</summary>
    public const int MaxChars = 4 << 20;

    private const int MaxDepth = 32;

    public static bool IsMarkdown(string name) =>
        Path.GetExtension(name.TrimEnd('/', '\\')).ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd" or ".mkdn" or ".mdwn";

    /// <summary>A whole page: the document drawn, its style, and a policy that allows nothing but itself and its pictures.</summary>
    public static string ToPage(string markdown, string title)
    {
        var body = markdown.Length > MaxChars
            ? $"<p class=\"note\">This file is {markdown.Length / (1 << 20)} MB of text, more than FileCat draws as a page; the Text mode shows it.</p>"
            : ToHtml(markdown);
        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'self'; style-src 'unsafe-inline'">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{Encode(title)}}</title>
            <style>{{Style}}</style>
            </head><body><article>
            {{body}}
            </article></body></html>
            """;
    }

    /// <summary>The document's HTML (no page around it).</summary>
    public static string ToHtml(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(ExpandTabs).ToList();
        var renderer = new Renderer();
        renderer.CollectReferences(lines);
        var html = new StringBuilder();
        renderer.Blocks(lines, html, 0);
        return html.ToString();
    }

    private static string ExpandTabs(string line)
    {
        if (!line.Contains('\t')) return line;
        var sb = new StringBuilder(line.Length + 8);
        foreach (char c in line)
        {
            if (c == '\t') sb.Append(' ', 4 - sb.Length % 4);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Text for the page: only what HTML gives meaning to is escaped (the page is UTF-8, so letters stay letters).</summary>
    private static string Encode(string text)
    {
        if (text.AsSpan().IndexOfAny("&<>\"'") < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        foreach (char c in text)
        {
            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    private static int Indent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        return i;
    }

    private static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})\s*([^`\s]*)[^`]*$")]
    private static partial Regex FenceOpen();

    [GeneratedRegex(@"^ {0,3}(#{1,6})(?:[ ]+(.*?))?(?:[ ]+#+)?[ ]*$")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"^ {0,3}([-*_])(?:[ ]*\1){2,}[ ]*$")]
    private static partial Regex ThematicBreak();

    [GeneratedRegex(@"^( {0,3})([-*+]|\d{1,9}[.)])( {1,4}(?! )|[ ]*$)")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"^ {0,3}=+[ ]*$")]
    private static partial Regex SetextOne();

    [GeneratedRegex(@"^ {0,3}-+[ ]*$")]
    private static partial Regex SetextTwo();

    [GeneratedRegex(@"^ {0,3}\[([^\]]{1,999})\]:[ ]*<?([^\s>]*)>?(?:[ ]+(?:""([^""]*)""|'([^']*)'|\(([^)]*)\)))?[ ]*$")]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$")]
    private static partial Regex TableDelimiter();

    [GeneratedRegex(@"^ {0,3}<!--")]
    private static partial Regex CommentStart();

    private sealed partial class Renderer
    {
        private readonly Dictionary<string, (string Url, string? Title)> _references = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _slugs = new(StringComparer.Ordinal);

        public void CollectReferences(List<string> lines)
        {
            bool fenced = false;
            string? fence = null;
            for (int i = 0; i < lines.Count; i++)
            {
                if (FenceOpen().Match(lines[i]) is { Success: true } f)
                {
                    if (!fenced) { fenced = true; fence = f.Groups[1].Value; continue; }
                    if (lines[i].Trim().StartsWith(fence![0]) && lines[i].Trim().Length >= fence.Length && lines[i].Trim().All(c => c == fence[0])) { fenced = false; continue; }
                }
                if (fenced) continue;
                if (ReferenceDefinition().Match(lines[i]) is { Success: true } m)
                {
                    string label = Normalize(m.Groups[1].Value);
                    string? title = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Success ? m.Groups[5].Value : null;
                    _references.TryAdd(label, (m.Groups[2].Value, title));
                    lines[i] = "\0ref"; // consumed; drawn as nothing
                }
            }
        }

        private static string Normalize(string label) => Regex.Replace(label.Trim(), @"\s+", " ").ToUpperInvariant();

        public void Blocks(List<string> lines, StringBuilder html, int depth)
        {
            int i = 0;
            var paragraph = new List<string>();
            void Flush()
            {
                if (paragraph.Count == 0) return;
                html.Append("<p>").Append(Inline(string.Join('\n', paragraph).Trim())).Append("</p>\n");
                paragraph.Clear();
            }
            while (i < lines.Count)
            {
                string line = lines[i];
                if (line == "\0ref") { Flush(); i++; continue; }
                if (IsBlank(line)) { Flush(); i++; continue; }
                int indent = Indent(line);

                // Indented code (not inside a paragraph).
                if (indent >= 4 && paragraph.Count == 0)
                {
                    var code = new List<string>();
                    while (i < lines.Count && (Indent(lines[i]) >= 4 || IsBlank(lines[i])))
                    {
                        code.Add(lines[i].Length >= 4 ? lines[i][4..] : "");
                        i++;
                    }
                    while (code.Count > 0 && IsBlank(code[^1])) code.RemoveAt(code.Count - 1);
                    html.Append("<pre><code>").Append(Encode(string.Join('\n', code))).Append("</code></pre>\n");
                    continue;
                }
                if (indent >= 4) { paragraph.Add(line); i++; continue; } // a lazy line of the paragraph

                if (FenceOpen().Match(line) is { Success: true } fence)
                {
                    Flush();
                    string marker = fence.Groups[1].Value;
                    string language = fence.Groups[2].Value;
                    var code = new List<string>();
                    i++;
                    while (i < lines.Count)
                    {
                        string t = lines[i].Trim();
                        if (Indent(lines[i]) < 4 && t.Length >= marker.Length && t.All(c => c == marker[0])) { i++; break; }
                        code.Add(lines[i].Length > indent && Indent(lines[i]) >= indent ? lines[i][indent..] : lines[i].TrimStart());
                        i++;
                    }
                    html.Append("<pre><code");
                    if (language.Length > 0) html.Append(" class=\"language-").Append(Encode(WebUtility.HtmlDecode(language))).Append('"');
                    html.Append('>').Append(Encode(string.Join('\n', code))).Append("</code></pre>\n");
                    continue;
                }
                if (CommentStart().IsMatch(line))
                {
                    // An HTML comment is not shown, as in any Markdown renderer.
                    Flush();
                    while (i < lines.Count && !lines[i].Contains("-->")) i++;
                    i++;
                    continue;
                }
                if (AtxHeading().Match(line) is { Success: true } heading)
                {
                    Flush();
                    Heading(heading.Groups[1].Value.Length, heading.Groups[2].Value, html);
                    i++;
                    continue;
                }
                if (paragraph.Count > 0 && SetextOne().IsMatch(line))
                {
                    Heading(1, string.Join('\n', paragraph), html);
                    paragraph.Clear();
                    i++;
                    continue;
                }
                if (paragraph.Count > 0 && SetextTwo().IsMatch(line))
                {
                    Heading(2, string.Join('\n', paragraph), html);
                    paragraph.Clear();
                    i++;
                    continue;
                }
                if (ThematicBreak().IsMatch(line))
                {
                    Flush();
                    html.Append("<hr>\n");
                    i++;
                    continue;
                }
                if (line.TrimStart().StartsWith('>') && depth < MaxDepth)
                {
                    Flush();
                    var quoted = new List<string>();
                    while (i < lines.Count)
                    {
                        string q = lines[i];
                        if (q.TrimStart().StartsWith('>') && Indent(q) < 4)
                        {
                            string rest = q.TrimStart()[1..];
                            quoted.Add(rest.StartsWith(' ') ? rest[1..] : rest);
                        }
                        else if (!IsBlank(q) && quoted.Count > 0 && !IsBlank(quoted[^1]) && !StartsBlock(q)) quoted.Add(q); // lazy continuation
                        else break;
                        i++;
                    }
                    html.Append("<blockquote>\n");
                    Blocks(quoted, html, depth + 1);
                    html.Append("</blockquote>\n");
                    continue;
                }
                if (ListMarker().Match(line) is { Success: true } item && depth < MaxDepth &&
                    (paragraph.Count == 0 || !IsBlank(line[item.Length..]) && (!char.IsDigit(item.Groups[2].Value[0]) || item.Groups[2].Value.StartsWith('1'))))
                {
                    Flush();
                    i = List(lines, i, html, depth);
                    continue;
                }
                if (paragraph.Count == 0 && i + 1 < lines.Count && line.Contains('|') && TableDelimiter().IsMatch(lines[i + 1]) &&
                    Cells(line).Count == Cells(lines[i + 1]).Count)
                {
                    i = Table(lines, i, html);
                    continue;
                }
                paragraph.Add(line);
                i++;
            }
            Flush();
        }

        /// <summary>Whether a line begins a block that ends a lazy paragraph continuation.</summary>
        private static bool StartsBlock(string line) =>
            FenceOpen().IsMatch(line) || AtxHeading().IsMatch(line) || ThematicBreak().IsMatch(line) || ListMarker().IsMatch(line) || line.TrimStart().StartsWith('>');

        private void Heading(int level, string text, StringBuilder html)
        {
            string inline = Inline(text.Trim());
            string slug = Slug(WebUtility.HtmlDecode(Regex.Replace(inline, "<[^>]*>", "")));
            html.Append("<h").Append(level).Append(" id=\"").Append(Encode(slug)).Append("\">").Append(inline).Append("</h").Append(level).Append(">\n");
        }

        /// <summary>A heading's anchor, as GitHub makes them: lowercase, punctuation dropped, spaces as dashes, repeats numbered.</summary>
        private string Slug(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
                else if (c == ' ') sb.Append('-');
            }
            string slug = sb.Length == 0 ? "section" : sb.ToString();
            if (_slugs.TryGetValue(slug, out int n)) { _slugs[slug] = n + 1; return slug + "-" + (n + 1); }
            _slugs[slug] = 0;
            return slug;
        }

        private int List(List<string> lines, int i, StringBuilder html, int depth)
        {
            var first = ListMarker().Match(lines[i]);
            bool ordered = char.IsDigit(first.Groups[2].Value[0]);
            char delimiter = first.Groups[2].Value[^1];
            if (ordered)
            {
                int start = int.Parse(first.Groups[2].Value[..^1], System.Globalization.CultureInfo.InvariantCulture);
                html.Append(start == 1 ? "<ol>\n" : $"<ol start=\"{start}\">\n");
            }
            else html.Append("<ul>\n");
            var items = new List<List<string>>();
            bool loose = false;
            while (i < lines.Count)
            {
                var m = ListMarker().Match(lines[i]);
                if (!m.Success || char.IsDigit(m.Groups[2].Value[0]) != ordered || m.Groups[2].Value[^1] != delimiter) break;
                int contentIndent = m.Groups[3].Value.Trim().Length == 0 && lines[i].Length <= m.Length ? m.Groups[1].Length + m.Groups[2].Length + 1 : m.Length;
                var content = new List<string> { lines[i].Length > m.Length ? lines[i][m.Length..] : "" };
                i++;
                bool blankBefore = false;
                while (i < lines.Count)
                {
                    string l = lines[i];
                    if (IsBlank(l)) { blankBefore = true; content.Add(""); i++; continue; }
                    if (Indent(l) >= contentIndent) { content.Add(l[contentIndent..]); i++; blankBefore = false; continue; }
                    if (!blankBefore && !StartsBlock(l) && !IsBlank(content[^1])) { content.Add(l.TrimStart()); i++; continue; } // lazy line
                    break;
                }
                while (content.Count > 0 && IsBlank(content[^1])) content.RemoveAt(content.Count - 1);
                if (content.Skip(1).Any(IsBlank)) loose = true;
                items.Add(content);
                if (i < lines.Count && blankBefore && ListMarker().IsMatch(lines[i])) loose = true;
                if (i < lines.Count && !ListMarker().IsMatch(lines[i])) break;
            }
            foreach (var content in items)
            {
                html.Append("<li");
                if (content.Count > 0 && TaskBox().Match(content[0]) is { Success: true } box)
                {
                    bool done = box.Groups[1].Value is "x" or "X";
                    html.Append(" class=\"task\"><input type=\"checkbox\" disabled").Append(done ? " checked" : "").Append("> ");
                    content[0] = content[0][box.Length..];
                }
                else html.Append('>');
                var inner = new StringBuilder();
                Blocks(content, inner, depth + 1);
                string itemHtml = inner.ToString();
                // A tight list shows its paragraphs as plain lines.
                if (!loose) itemHtml = Regex.Replace(itemHtml, @"<p>(.*?)</p>\n", "$1\n", RegexOptions.Singleline);
                html.Append(itemHtml.TrimEnd('\n')).Append("</li>\n");
            }
            html.Append(ordered ? "</ol>\n" : "</ul>\n");
            return i;
        }

        private int Table(List<string> lines, int i, StringBuilder html)
        {
            var header = Cells(lines[i]);
            var aligns = Cells(lines[i + 1]).Select(c => c.StartsWith(':') && c.EndsWith(':') ? "center" : c.EndsWith(':') ? "right" : c.StartsWith(':') ? "left" : null).ToList();
            html.Append("<table>\n<thead><tr>");
            for (int c = 0; c < header.Count; c++) Cell("th", header[c], aligns[c], html);
            html.Append("</tr></thead>\n<tbody>\n");
            i += 2;
            while (i < lines.Count && !IsBlank(lines[i]) && lines[i].Contains('|') && !StartsBlock(lines[i]))
            {
                var row = Cells(lines[i]);
                html.Append("<tr>");
                for (int c = 0; c < header.Count; c++) Cell("td", c < row.Count ? row[c] : "", aligns[c], html);
                html.Append("</tr>\n");
                i++;
            }
            html.Append("</tbody>\n</table>\n");
            return i;
        }

        private void Cell(string tag, string text, string? align, StringBuilder html)
        {
            html.Append('<').Append(tag);
            if (align is not null) html.Append(" style=\"text-align:").Append(align).Append('"');
            html.Append('>').Append(Inline(text.Trim())).Append("</").Append(tag).Append('>');
        }

        /// <summary>A table row's cells: split at pipes that are not escaped or inside code.</summary>
        private static List<string> Cells(string line)
        {
            string t = line.Trim();
            if (t.StartsWith('|')) t = t[1..];
            if (t.EndsWith('|') && !t.EndsWith("\\|")) t = t[..^1];
            var cells = new List<string>();
            var cell = new StringBuilder();
            int ticks = 0;
            for (int k = 0; k < t.Length; k++)
            {
                char c = t[k];
                if (c == '`') ticks ^= 1;
                if (c == '\\' && k + 1 < t.Length && t[k + 1] == '|') { cell.Append('|'); k++; continue; }
                if (c == '|' && ticks == 0) { cells.Add(cell.ToString()); cell.Clear(); continue; }
                cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }

        // ---- inlines -----------------------------------------------------------------------------------------------

        private enum Kind { Text, Delimiter }

        private sealed class Node
        {
            public Kind Kind;
            public string Html = "";
            public char Char;
            public int Count;
            public bool CanOpen, CanClose;
            public readonly List<string> OpenTags = [], CloseTags = [];
        }

        public string Inline(string text) => Inline(text, 0);

        /// <summary>Link labels are drawn as inlines too: this deep, and no deeper (brackets nested by the thousand stay text).</summary>
        private const int MaxInlineDepth = 16;

        private string Inline(string text, int depth)
        {
            var closeOf = depth < MaxInlineDepth ? Brackets(text) : null;
            var nodes = new List<Node>();
            var sb = new StringBuilder();
            void Literal(string s) => sb.Append(s);
            void Push()
            {
                if (sb.Length == 0) return;
                nodes.Add(new Node { Kind = Kind.Text, Html = sb.ToString() });
                sb.Clear();
            }
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    char n = text[i + 1];
                    if (n == '\n') { Literal("<br>\n"); i += 2; continue; }
                    if (char.IsAsciiLetterOrDigit(n) || n == ' ' || n > 127) { Literal("\\"); i++; continue; }
                    Literal(Encode(n.ToString()));
                    i += 2;
                    continue;
                }
                if (c == '`')
                {
                    int run = Run(text, i, '`');
                    int close = FindRun(text, i + run, '`', run);
                    if (close >= 0)
                    {
                        string code = text[(i + run)..close].Replace('\n', ' ');
                        if (code.Length >= 2 && code[0] == ' ' && code[^1] == ' ' && code.Trim().Length > 0) code = code[1..^1];
                        Literal("<code>" + Encode(code) + "</code>");
                        i = close + run;
                        continue;
                    }
                    Literal(new string('`', run));
                    i += run;
                    continue;
                }
                if (c == '\n')
                {
                    // Two spaces before a line break make it a hard break.
                    int spaces = 0;
                    while (sb.Length > spaces && sb[sb.Length - 1 - spaces] == ' ') spaces++;
                    if (spaces >= 2) { sb.Length -= spaces; Literal("<br>\n"); }
                    else { sb.Length -= spaces; Literal("\n"); }
                    i++;
                    continue;
                }
                if (c == '<' && Autolink(text, i, out int end, out string? link))
                {
                    Literal(link!);
                    i = end;
                    continue;
                }
                if (c == '<' && Tag(text, i, out end, out string? tag))
                {
                    Literal(tag!);
                    i = end;
                    continue;
                }
                if (c == '&' && Entity().Match(text, i) is { Success: true } entity && WebUtility.HtmlDecode(entity.Value) is var decoded && decoded != entity.Value)
                {
                    Literal(Encode(decoded));
                    i += entity.Length;
                    continue;
                }
                if (closeOf is not null && (c == '[' || c == '!' && i + 1 < text.Length && text[i + 1] == '[') && Link(text, i, closeOf, depth, out end, out string? linked))
                {
                    Literal(linked!);
                    i = end;
                    continue;
                }
                if (c is 'h' or 'w' && BareAddress(text, i, out end, out string? bare))
                {
                    Literal(bare!);
                    i = end;
                    continue;
                }
                if (c is '*' or '_' or '~')
                {
                    int run = Run(text, i, c);
                    if (c == '~' && run != 2) { Literal(new string('~', run)); i += run; continue; }
                    char before = i > 0 ? text[i - 1] : ' ', after = i + run < text.Length ? text[i + run] : ' ';
                    bool left = !char.IsWhiteSpace(after) && (!char.IsPunctuation(after) && !char.IsSymbol(after) || char.IsWhiteSpace(before) || char.IsPunctuation(before) || char.IsSymbol(before));
                    bool right = !char.IsWhiteSpace(before) && (!char.IsPunctuation(before) && !char.IsSymbol(before) || char.IsWhiteSpace(after) || char.IsPunctuation(after) || char.IsSymbol(after));
                    Push();
                    nodes.Add(new Node
                    {
                        Kind = Kind.Delimiter,
                        Char = c,
                        Count = run,
                        // "_" does not emphasize inside words (snake_case stays).
                        CanOpen = c == '_' ? left && (!right || char.IsPunctuation(before)) : left,
                        CanClose = c == '_' ? right && (!left || char.IsPunctuation(after)) : right,
                    });
                    i += run;
                    continue;
                }
                Literal(Encode(c.ToString()));
                i++;
            }
            Push();
            Emphasis(nodes);
            var html = new StringBuilder();
            foreach (var node in nodes)
            {
                if (node.Kind == Kind.Text) { html.Append(node.Html); continue; }
                foreach (var t in node.CloseTags) html.Append(t);
                html.Append(Encode(new string(node.Char, node.Count)));
                foreach (var t in node.OpenTags) html.Append(t);
            }
            return html.ToString();
        }

        /// <summary>
        /// Pairs emphasis delimiters as CommonMark does, in linear time: each closer looks back for an opener of its kind,
        /// and a fruitless search sets how far later ones need look.
        /// </summary>
        private static void Emphasis(List<Node> nodes)
        {
            var bottom = new Dictionary<(char, int, bool), int>();
            for (int closer = 0; closer < nodes.Count; closer++)
            {
                var c = nodes[closer];
                if (c.Kind != Kind.Delimiter || !c.CanClose) continue;
                while (c.Count > 0)
                {
                    var key = (c.Char, c.Count % 3, c.CanOpen);
                    int floor = bottom.TryGetValue(key, out int b) ? b : -1;
                    int opener = -1;
                    for (int k = closer - 1; k > floor; k--)
                    {
                        var o = nodes[k];
                        if (o.Kind != Kind.Delimiter || o.Char != c.Char || !o.CanOpen || o.Count == 0) continue;
                        if (c.Char == '~' && o.Count != c.Count) continue;
                        if ((o.CanClose || c.CanOpen) && (o.Count + c.Count) % 3 == 0 && !(o.Count % 3 == 0 && c.Count % 3 == 0)) continue;
                        opener = k;
                        break;
                    }
                    if (opener < 0)
                    {
                        bottom[key] = closer - 1;
                        break;
                    }
                    var open = nodes[opener];
                    int use = c.Char == '~' ? 2 : open.Count >= 2 && c.Count >= 2 ? 2 : 1;
                    string tag = c.Char == '~' ? "del" : use == 2 ? "strong" : "em";
                    open.Count -= use;
                    c.Count -= use;
                    open.OpenTags.Insert(0, $"<{tag}>");
                    c.CloseTags.Add($"</{tag}>");
                    // Delimiters between the pair can no longer pair across it.
                    for (int k = opener + 1; k < closer; k++)
                        if (nodes[k].Kind == Kind.Delimiter) nodes[k].CanOpen = nodes[k].CanClose = false;
                }
            }
        }

        private static int Run(string text, int i, char c)
        {
            int n = 0;
            while (i + n < text.Length && text[i + n] == c) n++;
            return n;
        }

        private static int FindRun(string text, int from, char c, int length)
        {
            for (int k = from; k < text.Length; k++)
            {
                if (text[k] != c) continue;
                int run = Run(text, k, c);
                if (run == length) return k;
                k += run - 1;
            }
            return -1;
        }

        [GeneratedRegex(@"\G<((?:https?|ftp)://[^\s<>]*|mailto:[^\s<>]+)>", RegexOptions.IgnoreCase)]
        private static partial Regex AutolinkUrl();

        [GeneratedRegex(@"\G<([A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*)>")]
        private static partial Regex AutolinkMail();

        private static bool Autolink(string text, int i, out int end, out string? html)
        {
            if (AutolinkUrl().Match(text, i) is { Success: true } u)
            {
                end = i + u.Length;
                html = Anchor(u.Groups[1].Value, Encode(u.Groups[1].Value), null);
                return true;
            }
            if (AutolinkMail().Match(text, i) is { Success: true } m)
            {
                end = i + m.Length;
                html = Anchor("mailto:" + m.Groups[1].Value, Encode(m.Groups[1].Value), null);
                return true;
            }
            end = i;
            html = null;
            return false;
        }

        [GeneratedRegex(@"\G(?:<!--.*?-->|</?(?:b|i|em|strong|sub|sup|kbd|del|s|u|mark|small|br|details|summary)\s*/?>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex AllowedTag();

        /// <summary>A comment (dropped) or one of a few formatting tags without attributes (kept); any other HTML stays text.</summary>
        private static bool Tag(string text, int i, out int end, out string? html)
        {
            if (AllowedTag().Match(text, i) is { Success: true } m)
            {
                end = i + m.Length;
                html = m.Value.StartsWith("<!--", StringComparison.Ordinal) ? "" : m.Value.ToLowerInvariant().Replace(" ", "");
                return true;
            }
            end = i;
            html = null;
            return false;
        }

        [GeneratedRegex(@"\G(?:https?://|www\.)[^\s<]*[^\s<?!.,:*_~'"")\]]", RegexOptions.IgnoreCase)]
        private static partial Regex Bare();

        private static bool BareAddress(string text, int i, out int end, out string? html)
        {
            end = i;
            html = null;
            if (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '/' or '@' or '.')) return false;
            if (Bare().Match(text, i) is not { Success: true } m) return false;
            string address = m.Value;
            // Balanced: a closing parenthesis belongs to the address only when it opened one.
            while (address.EndsWith(')') && address.Count(ch => ch == ')') > address.Count(ch => ch == '(')) address = address[..^1];
            end = i + address.Length;
            html = Anchor(address.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + address : address, Encode(address), null);
            return true;
        }

        /// <summary>[text](destination "title"), ![alt](source), and the reference forms [text][label], [text][], [label].</summary>
        private bool Link(string text, int i, int[] closeOf, int depth, out int end, out string? html)
        {
            end = i;
            html = null;
            bool image = text[i] == '!';
            int open = image ? i + 1 : i;
            int close = closeOf[open];
            if (close < 0) return false;
            string label = text[(open + 1)..close];
            string? url = null, title = null;
            int after = close + 1;
            if (after < text.Length && text[after] == '(' && Destination(text, after, out int destEnd, out url, out title))
            {
                end = destEnd;
            }
            else
            {
                string key = label;
                end = after;
                if (after + 1 < text.Length && text[after] == '[' && closeOf[after] is var refClose and > 0)
                {
                    string inner = text[(after + 1)..refClose];
                    if (inner.Length > 0) key = inner;
                    end = refClose + 1;
                }
                if (!_references.TryGetValue(Normalize(key), out var found)) return false;
                (url, title) = found;
            }
            html = image ? Picture(url!, label, title) : Anchor(url!, Inline(label, depth + 1), title);
            return true;
        }

        /// <summary>
        /// Each "[" and the "]" that closes it (-1 for none), skipping escaped brackets and code spans: found once, so a
        /// text of a thousand unmatched brackets is read once rather than once per bracket.
        /// </summary>
        private static int[] Brackets(string text)
        {
            var closeOf = new int[text.Length];
            Array.Fill(closeOf, -1);
            var open = new Stack<int>();
            for (int k = 0; k < text.Length; k++)
            {
                char c = text[k];
                if (c == '\\') { k++; continue; }
                if (c == '`')
                {
                    int run = Run(text, k, '`');
                    int to = FindRun(text, k + run, '`', run);
                    k = (to > 0 ? to + run : k + run) - 1;
                    continue;
                }
                if (c == '[') open.Push(k);
                else if (c == ']' && open.Count > 0) closeOf[open.Pop()] = k;
            }
            return closeOf;
        }

        [GeneratedRegex(@"\G&(?:#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6}|[A-Za-z][A-Za-z0-9]{1,31});")]
        private static partial Regex Entity();

        private static bool Destination(string text, int open, out int end, out string? url, out string? title)
        {
            end = open;
            url = title = null;
            int k = open + 1;
            while (k < text.Length && text[k] is ' ' or '\n') k++;
            var dest = new StringBuilder();
            if (k < text.Length && text[k] == '<')
            {
                k++;
                while (k < text.Length && text[k] != '>' && text[k] != '\n') dest.Append(text[k++]);
                if (k >= text.Length || text[k] != '>') return false;
                k++;
            }
            else
            {
                int parens = 0;
                while (k < text.Length && !char.IsWhiteSpace(text[k]))
                {
                    if (text[k] == '(') parens++;
                    else if (text[k] == ')' && parens-- == 0) break;
                    if (text[k] == '\\' && k + 1 < text.Length && char.IsPunctuation(text[k + 1])) k++;
                    dest.Append(text[k++]);
                }
            }
            while (k < text.Length && text[k] is ' ' or '\n') k++;
            if (k < text.Length && text[k] is '"' or '\'' or '(')
            {
                char closeQuote = text[k] == '(' ? ')' : text[k];
                int start = ++k;
                while (k < text.Length && text[k] != closeQuote) k++;
                if (k >= text.Length) return false;
                title = text[start..k];
                k++;
                while (k < text.Length && text[k] is ' ' or '\n') k++;
            }
            if (k >= text.Length || text[k] != ')') return false;
            end = k + 1;
            url = dest.ToString();
            return true;
        }

        /// <summary>
        /// A link: to the web, mail or a place in this page, with its address shown on hover. Other destinations (other
        /// files, scripts, data) are not links here: the text is shown, with the address it named.
        /// </summary>
        private static string Anchor(string url, string inner, string? title)
        {
            string decoded = WebUtility.HtmlDecode(url).Trim();
            string tip = title is { Length: > 0 } ? title + " — " + decoded : decoded;
            if (SafeHref(decoded))
                return $"<a href=\"{Encode(decoded)}\" title=\"{Encode(tip)}\">{inner}</a>";
            return $"<span class=\"link\" title=\"{Encode(tip)}\">{inner}</span>";
        }

        private static bool SafeHref(string url)
        {
            if (url.StartsWith('#')) return true;
            int colon = url.IndexOf(':');
            if (colon <= 0) return false; // a relative address names another file, which the page does not open
            string scheme = url[..colon].ToLowerInvariant();
            return scheme is "http" or "https" or "mailto";
        }

        /// <summary>A picture from the file's own folder; one from anywhere else is not loaded (its alternative text shows).</summary>
        private static string Picture(string url, string alt, string? title)
        {
            string decoded = WebUtility.HtmlDecode(url).Trim();
            string altText = WebUtility.HtmlDecode(Regex.Replace(alt, @"[\\*_`~\[\]]", ""));
            bool local = decoded.Length > 0 && !decoded.Contains(':') && !decoded.StartsWith("//", StringComparison.Ordinal) && !decoded.StartsWith('/') && !decoded.StartsWith('\\');
            if (!local)
                return $"<span class=\"remote-picture\" title=\"{Encode("Not loaded: " + decoded)}\">{Encode(altText.Length > 0 ? altText : "picture")}</span>";
            string src = UrlPath(decoded);
            return $"<img src=\"{Encode(src)}\" alt=\"{Encode(altText)}\"" + (title is { Length: > 0 } ? $" title=\"{Encode(title)}\"" : "") + ">";
        }

        [GeneratedRegex(@"^\[([ xX])\][ ]+")]
        private static partial Regex TaskBox();

        /// <summary>A path as an address: what an address cannot hold is percent-encoded; what is already encoded stays.</summary>
        private static string UrlPath(string path)
        {
            var sb = new StringBuilder(path.Length + 16);
            for (int k = 0; k < path.Length; k++)
            {
                char c = path[k];
                if (c == '%' && k + 2 < path.Length && Uri.IsHexDigit(path[k + 1]) && Uri.IsHexDigit(path[k + 2])) sb.Append(c);
                else if (char.IsAsciiLetterOrDigit(c) || "-._~!$&'()*+,;=:@/".Contains(c)) sb.Append(c);
                else
                {
                    // Encode a complete Unicode scalar, not the two halves of a supplementary character.
                    int count = char.IsHighSurrogate(c) && k + 1 < path.Length && char.IsLowSurrogate(path[k + 1]) ? 2 : 1;
                    foreach (byte b in Encoding.UTF8.GetBytes(path.Substring(k, count)))
                        sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                    k += count - 1;
                }
            }
            return sb.ToString();
        }
    }

    private const string Style = """
        :root { color-scheme: light dark; --fg: #1f2328; --bg: #ffffff; --muted: #59636e; --line: #d1d9e0; --code: #f6f8fa; --link: #0969da; }
        @media (prefers-color-scheme: dark) { :root { --fg: #e6edf3; --bg: #0d1117; --muted: #9198a1; --line: #3d444d; --code: #151b23; --link: #4493f8; } }
        html { background: var(--bg); color: var(--fg); }
        body { margin: 0; font: 16px/1.6 -apple-system, "Segoe UI", "Noto Sans", Helvetica, Arial, sans-serif; }
        article { max-width: 880px; margin: 0 auto; padding: 24px 32px 48px; overflow-wrap: break-word; }
        h1, h2, h3, h4, h5, h6 { margin: 1.4em 0 0.6em; line-height: 1.25; font-weight: 600; }
        h1 { font-size: 2em; padding-bottom: .3em; border-bottom: 1px solid var(--line); }
        h2 { font-size: 1.5em; padding-bottom: .3em; border-bottom: 1px solid var(--line); }
        h3 { font-size: 1.25em; } h4 { font-size: 1em; } h5 { font-size: .875em; } h6 { font-size: .85em; color: var(--muted); }
        p, ul, ol, blockquote, pre, table { margin: 0 0 1em; }
        a, .link { color: var(--link); text-decoration: none; } a:hover { text-decoration: underline; }
        .link, .remote-picture { border-bottom: 1px dotted var(--link); cursor: help; }
        .remote-picture { color: var(--muted); font-style: italic; }
        code { font: .875em ui-monospace, "Cascadia Code", Consolas, "Liberation Mono", monospace; background: var(--code); padding: .2em .4em; border-radius: 6px; }
        pre { background: var(--code); padding: 16px; border-radius: 6px; overflow: auto; line-height: 1.45; }
        pre code { background: none; padding: 0; font-size: .85em; }
        blockquote { margin-left: 0; padding: 0 1em; color: var(--muted); border-left: .25em solid var(--line); }
        table { border-collapse: collapse; display: block; overflow: auto; }
        th, td { border: 1px solid var(--line); padding: 6px 13px; } th { font-weight: 600; }
        tr:nth-child(2n) { background: var(--code); }
        hr { height: .25em; border: 0; background: var(--line); margin: 24px 0; }
        img { max-width: 100%; }
        li.task { list-style: none; } li.task input { margin: 0 .3em 0 -1.4em; }
        ul, ol { padding-left: 2em; }
        .note { color: var(--muted); font-style: italic; }
        """;
}
