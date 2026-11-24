# I654 — A PDF that cannot be shown gets a message with a doubled period

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (b24). The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/b24_pdf.txt`). **Status:** open, Low.

## Observation

On the macOS rc.2 package (`FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed…`), the viewer said:
- "This PDF page cannot be shown: File not in PDF format or corrupted.. Text and Hex (F4) show its bytes; Info (Ctrl+I) describes the file."
- for an encrypted PDF: "…Password required or incorrect password.. Text and Hex (F4)…".

`src/FileCat.App/Views/ViewerWindow.cs:752` appends ". Text and Hex (F4)…" to the PDF library's message, which already ends with a period. The code is shared, so every platform shows the same text.

## Remedy

Drop a trailing period from the library's message before appending the rest. Show the message otherwise unchanged.
