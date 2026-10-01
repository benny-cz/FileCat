# E-I25 — Markdown files drawn as pages

Release issue I25 (owner-reported; required for 1.0.0 by DEC-11). Preliminary automated evidence on a development build.

## What was built

- **Renderer (`FileCat.Core/Content/Markdown.cs`, no new dependency):** CommonMark's blocks — ATX and setext
  headings, paragraphs with soft and hard breaks, fenced and indented code, block quotes with lazy lines, ordered (with
  start) and bullet lists nested, tight or loose, thematic breaks, reference definitions — and inlines — code spans,
  emphasis paired by CommonMark's delimiter rules in linear time (`snake_case` stays), links and pictures (inline and by
  reference), autolinks, character references, backslash escapes; with GitHub's tables (alignment, escaped pipes),
  task lists, strikethrough, bare web addresses and heading anchors.
- **Containment:** the file is untrusted text. Its HTML never reaches the page — it is shown as text, apart from a few
  formatting tags without attributes (`kbd`, `br`, `sub`, `details`, …) and comments, which stay hidden as on GitHub.
  Links lead only to the web, mail or within the page; any other destination (`javascript:`, `data:`, `file:`, other
  files) is shown as text with the address on hover. Pictures load only from the file's own folder; a picture from the
  web is never requested (its alternative text shows, marked "Not loaded"). The page carries a Content Security Policy
  (`default-src 'none'; img-src 'self'; style-src 'unsafe-inline'`), on top of the page engine's own containment
  (scripts off, every request outside the folder refused, §16.1). Files over 4 MiB of text are not drawn (the page says
  so; Text shows them); nesting is bounded (32 block levels, 16 inline levels).
- **Viewer:** F3 on a Markdown file (`.md`, `.markdown`, `.mdown`, `.mkd`, `.mkdn`, `.mdwn`) opens it drawn, as a web
  page opens; F4 shows its text; the status line says what the page is and is not. Where no page engine is available,
  the text is shown with the reason.

## Tests (E-I25-T1)

- `MarkdownTests` (44): blocks and inlines as CommonMark reads them; code kept exactly; lists, quotes and tables; links
  only to the web, mail or the page; pictures only from the folder; nine hostile HTML/URL inputs leave no element but
  the renderer's own (no handlers, no unsafe `href`/`src`); hostile runs (50,000 unmatched `*`, `_`, `[`, `` ` ``, `>`,
  `<`, 2,000 nested list markers) drawn in moments; the page's policy and title; a file served as its page with a
  picture from its folder and nothing from outside it.
- `ViewerInfoTests.A_markdown_file_opens_drawn_and_without_an_engine_as_its_text` (App, headless).
- `PageViewTests.A_markdown_file_is_drawn_with_its_pictures_and_asks_the_web_for_nothing` (Windows, real WebView2): the
  page loads with the file's name as its title (its embedded script is text), the picture beside it is served, and no
  request at all leaves (no web picture requested). A picture of the page as WebView2 drew it:
  `i25-markdown-webview2.png` `caeb25c8745a454023811bf2aba40bbc39f35775c6b2498b86d95a8c76c507ae`.

## Limitations

- Not every CommonMark corner case (link destinations with nested brackets, some list-indentation rules, HTML blocks
  as such); GitHub extensions beyond tables, task lists, strikethrough and bare addresses (footnotes, alerts, math,
  Mermaid) are drawn as text.
- Drawn pages follow the system's light or dark scheme, not FileCat's theme (I31).
- Checked here in WebView2 (Windows 11); WebKitGTK and WKWebView use the same page and containment, not yet run with a
  Markdown file.
