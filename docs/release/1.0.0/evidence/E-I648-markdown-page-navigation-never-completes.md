# I648 — A Markdown Page sometimes stays blank: WebView2 never reports that the page finished loading

**Found:** 2026-11-24, during final qualification of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (checklist c10), on the installed candidate in the Windows 11 25H2 qualification VM ([FQ progress](E-FQ-RC2-20261124.md)), and the same day in hosted CI. **Status:** open. The cause is not known yet. The owner decides whether the next candidate waits for a fix.

## Observations

**Qualification VM, rc.2 installed:**
1. **Batch 21.**
   - **What was open before.** The results document's viewer, the plan's viewer (opened from a link) and the release dashboard's viewer (opened after the parent-folder review) had all been closed.
   - **What happened.** `links.md` (260 bytes) was then opened with F3. Its Page stayed blank, with Refresh disabled, for over a minute: six attempts to click its links between about 15:14:10 and 15:15:15 found none.
   - **The viewer's status line** read "Markdown drawn as a page … 260 bytes".
2. **Batch 21c, step R1.**
   - **What was open.** Nothing: this was a fresh FileCat, and the results document (317,996 bytes) was its first viewer.
   - **What happened.** About 6 s after F3, the Page was blank, with Refresh disabled. The run did not wait longer.
3. **Batches 21b, 21c (R2, R3), 21d and 21e.**
   - **What was opened.** About 60 further Pages: `links.md`, its seven one-link variants, the results document and the release dashboard (110,687 bytes, lines up to 18,534 characters). They included Pages opened right after other viewers were closed, two viewers open at once, and a viewer opened from a link.
   - **What happened.** Each drew within 3.0 s. Batch 21e alone opened 40 Pages in one FileCat, half with the viewer brought to the front while waiting and half without, and none stayed blank.

**Hosted CI.** PR #27's run 45417241533 failed its Windows job "build, all tests" in `PageViewTests.A_markdown_file_is_drawn_with_its_pictures_and_asks_the_web_for_nothing` after 55 s:

> WebView2 157.0.4341.46 did not report Markdown navigation completion. Served requests: /README.md, /pic.png. Environment created: True. Controller created: True. Accepted navigations: 1. Canceled navigations: 0. Page requests started: 2. Browser process failure: none.

PR #27's only product change is in `WindowsContextMenu.cs`. Recent failed `main` runs that were checked (45386566393, 45322995966) do not show this failure.

## What the source says

- **Ready only on completion.** `WebView2Page` (`src/FileCat.Platform.Windows/Html/WebView2Page.cs`) reports the Page as loaded only from WebView2's `NavigationCompleted`. Until then the viewer keeps Refresh disabled.
- **The request handler.** Each of the page's own requests is answered in `OnResourceRequested`:
  1. it takes a deferral;
  2. it resolves the file on the thread pool;
  3. it sets the response;
  4. it completes the deferral in `finally`.

  Exceptions it treats as "the view closed meanwhile" are swallowed.
- **The CI failure.** Both requests were answered: `Served` fires before the response is set. Still, no completion event arrived within 55 s, and no browser process failed.

Why the completion is lost is not established, and neither is whether F5 or switching away and back recovers the Page.

## Why it matters

The Page, the default view for Markdown, sometimes shows nothing, with Refresh disabled. The user can still switch to the Text view (F4). The fault came twice in about 62 Pages checked in the VM, and once in CI.

## Remedy (proposed)

1. **Find the cause.** Record WebView2's events with timestamps while the fault occurs: navigation starting, content loading, DOM content loaded, completion, each resource request and its deferral completion, and process failures. Do this in CI's repeat runs or a local loop under load.
2. **Make sure the Page cannot stay blank and disabled.**
   - Treat a page whose own requests were all answered and whose content has loaded as drawn, or navigate again after a bounded wait.
   - Keep Refresh available while a page is loading.

This changes product source, so it reaches users only in a candidate after rc.2.
