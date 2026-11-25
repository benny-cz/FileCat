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

## Fix

Investigated and fixed on 2026-11-24 on the development PC (Windows 11, 24 logical processors, WebView2 158.0.4370.33; CI had 157.0.4341.46).
- **How the runs ran.** All runs below were at medium integrity, at below-normal priority, with hidden host windows. Other sessions kept the PC 70 to 85 % busy meanwhile (the median of samples taken every 10 s).
- **"Two processors."** Some runs confined the test process, and with it its WebView2 processes, to two of the 24 logical processors. One or two busy loops ran on the same two, at the same priority: a loaded small machine, without loading the rest of the PC.

### Diagnostics added

`WebView2Page` now keeps a timeline of its last 400 events: the milliseconds since the view was made, the managed thread, and what happened. It records:
- each Show, the environment and controller starting and made (with the browser's process ID), and the folder's browser processes as WebView2 lists them;
- each navigation with its ID: accepted, refused or handed to another viewer; its content loading; DOM content loaded; and its completion with status and HTTP code;
- each of the page's own requests: its resource context, path and size, the response set and the deferral completed, or the step that failed, with the exception's type and HRESULT; each response WebView2 says it received;
- process failures, browser exits, size and host-window visibility, the start's waits and retries, and what the new watch below decided.

Tests print it, and the CI test's timeout message includes it. When the view has to act on a page's behalf, FileCat's diagnostics log gets a "Page view (I648)" warning with the timeline since the page was shown. Addresses and paths in it are hashed unless diagnostic mode is on.

### What was observed

**A normal load**, from the timelines: the view's environment within 50 ms, its controller in 0.4 to 2.9 s (a browser start), the navigation, the document's request answered, content loading, DOM content loaded, the picture's request, the completion, and then a request for `/favicon.ico`. In every timeline read (several dozen), every answer and every deferral completion ran on the view's own thread.

**CI's failure, read again.** Its diagnostics said 2 page requests were started. In every timeline read, WebView2 asks for `/favicon.ico` right after it reports a completion. So in CI, Chromium most likely never finished the page at all, rather than finishing it and the report going astray. This is an inference: CI had no timeline then.

**Repeated opens before the fix.** The repeat test opens Markdown pages in rounds that cycle through five cases: three pages alone, two at once, and three open, closed, and one opened at once. All of a run's views share one data folder, as FileCat's viewers do.

| Run | Conditions | Pages | Outcome |
|---|---|---|---|
| In-process, 1,000 rounds | Whole PC | 1,800 | All completed. Longest 5.2 s; 99th percentile 2.6 to 3.8 s |
| CI's test, 150 fresh processes | Whole PC; a data folder per process | 150 | All passed |
| CI's test, 58 fresh processes | Two logical processors, shared with two busy processes | 58 | All passed |
| In-process, 16 rounds | The same two processors and busy processes | 28 | **5 stayed blank, with no completion, for 20 s.** Four had no controller yet, so there was nothing to picture; one drew its page's dark background and nothing else. The run ended when navigating one of them again failed with "CoreWebView2 members cannot be accessed after the WebView2 control is disposed" (0x8007139F) |
| In-process, 60 rounds, with the timeline | The same | 108 | **3 stayed blank for 40 s, their controller never made**; one of them got it after 37.8 s, while being shown again. 92 failed at once with ERROR_BUSY ("The requested resource is in use", 0x800700AA). 13 completed |
| In-process, 15 rounds, waiting up to 100 s | The same | 27 | **Blank for up to 70 s**: 7 of the 9 that completed waited 21 to 66 s for their controller, the longest 69.5 s in all. 18 failed with ERROR_BUSY |
| In-process, 20 rounds | Two processors, one busy process | 36 | All completed, the slowest after 15.9 s |

So the fault is reproduced: under load, a page view started while its folder's previous browser process is still exiting waits for its controller for tens of seconds or for good, or fails with ERROR_BUSY. Each round closes every view, so every round starts like that. With a data folder per process, the same load gave no failure in 58 runs.

**The exit takes time.** After the last view of a folder closes, WebView2's BrowserProcessExited came 0.3 to 1.5 s later on this PC. Under the heaviest load, it never came within 10 s. The exiting browser's processes were still listed, unreadable, and new views got their controller only about 70 s after the previous ones closed. Even on the whole PC, it showed: after the fix's watch but before the wait below was added, 1 of 1,800 pages, opened right after three others closed, got a controller in 38 ms, then waited 6.3 s for a renderer process.

**Suspected causes tested:**
1. **A request answered on the wrong thread, its error swallowed.** A probe answers a page's request off the UI thread, as would happen if WebView2 raised its request event with no synchronization context. WebView2 refused the response from that thread (InvalidOperationException, 0x80131509) but accepted the deferral's completion. It then showed its own error page and reported a failed completion within 0.35 s. A failure, not a stall: not this fault, on this runtime.
2. **A hidden host window.** Every local run used a hidden parent window, as CI's tests do, and thousands of pages completed. Hiding alone does not cause it.
3. **A race between Show and startup.** Two pages at once, and pages shown while their view was still starting, completed in every run on the whole PC.
4. **A lost completion.** With two processors and one busy process, a picture's response and the page's completion arrived 10 s after the picture was answered. The view's own timer had fired 6 s late, and the completion came 13 ms after the view's thread got to run again. So the test process was held up, not WebView2. No completion was seen lost outright. CI's stall, after both requests were answered, was not reproduced as such.

Not established: which of these mechanisms stalled the two Pages in the qualification VM.
- Batch 21's `links.md` matches the reproduced race: its viewer started right after three others had closed.
- Step R1's viewer was the first of a fresh FileCat. Whether another FileCat had closed just before is not recorded.

### The change

1. **The watch** (`PageCompletionWatch`). It follows each page shown and acts once the page has made no progress for 4 s, with none of its own requests still being answered:
   - Content loaded (DOM content loaded): the page counts as drawn.
   - Never began to load (no content loading): it is navigated once more. If that does not help either, the page waits and Refresh stays available.
   - Its renderer or browser process ended: it is navigated once more. If no new navigation starts, or that one's process ends too, the page is reported as not shown.
   - A look that comes more than 0.5 s late acts on nothing and looks again 0.25 s later. The process was held up, and WebView2's own reports may still be waiting to be handled.
   - A completion that does come is passed on, failures included. Only two kinds are dropped: one for a navigation that a newer one replaced (a retry or a Refresh), and a success for a page that already counts as drawn.
2. **The browser's exit** (`PageBrowserExits`). When the last view of a data folder closes, the next view of that folder waits for WebView2's BrowserProcessExited, at most 10 s, before it starts.
3. **A bounded start.**
   - A start that fails with ERROR_BUSY is tried again after 1 s.
   - Each start asks for its controller once, and waits for it until 90 s after the view began to start. An earlier version asked again after 15 s. Under the heaviest load, that only made three controllers, which all came together at 65 s, just after that version had given up at 60 s.
   - After 90 s, or on an ERROR_BUSY at the limit, the view says that WebView2 did not start.
   - Any other start failure is reported at once, as before. So is a navigation that WebView2 refuses outright.
4. **The viewer.** Refresh (F5) stays available while a page loads and works then. Before, it was disabled, and F5 answered "Wait for the current page to finish loading before refreshing". A completion reported before the page now shown was asked for no longer counts for it. After 10 s without the page finishing, the status line begins "The page has not finished loading; Refresh (F5) tries again."

Errors still show as errors. No failure report is turned into a success: the watch reports a page drawn only when its content loaded and no completion came at all.

### Tests

- **`PageCompletionWatchTests`**, 12 tests of the watch on a clock of the test's own:
  - drawn when quiet and not before;
  - nothing decided while one of the page's own requests is being answered, or while the browser starts;
  - one retry, and then left to Refresh;
  - a late failure still reported;
  - old navigations' reports dropped;
  - a process that ended.
- **`PageViewTests`**, two new real-WebView2 tests:
  - **The completion withheld.** A seam (`WithholdNavigationCompletedForTests`) drops WebView2's completion reports. The page must count as drawn exactly once, at least 4 s after it went quiet and within 10 s more. It took 4.0 s. Against the old code with the seam, it timed out after 49 s.
  - **Opened right after the last view closed.** The second view waits for the first browser to exit, 0.3 and 1.2 s in two runs here. Then it draws its page.
- **`PageCompletionRecoveryTests`**, 3 App tests through an engine that never reports a completion:
  - Refresh is enabled while the page loads, and reloads it.
  - The 10-second notice appears and goes once the page has loaded.
  - A completion reported before the next page was asked for does not count for that page.

  All three failed against the old code.
- **Opt-in tools:**
  - The repeat test (`FILECAT_PAGE_REPEAT` with rounds; `_WAIT`, `_FRESH`, `_DATA` and `_REPORT`) gave the tables here.
  - The wrong-thread probe needs `FILECAT_PAGE_PROBE=1`.
- **The CI test.** When the view had to go on without WebView2's completion, the test now prints the timeline.

**Results on the final code.**
- **Platform tests:** `PageViewTests` and `PageCompletionWatchTests`, 26 tests: 24 passed, and the 2 opt-in tests skipped. The probe, run on its own, passed.
- **App tests:** the 29 viewer and page classes, 299 tests, all passed. The App code has not changed since; the six page classes, 37 tests, passed again at the end.

**After the fix:**

| Run | Conditions | Pages | Outcome |
|---|---|---|---|
| In-process, 1,000 rounds, the watch only | Whole PC | 1,800 | All drawn: 1,799 by their completion, 1 by the watch's retry (the 6.3 s renderer wait above). Longest 7.8 s |
| In-process, 500 rounds, all but the start's last change | Whole PC | 900 | All completed, every one by WebView2's own report; the watch never acted. Medians 0.7 to 1.1 s, 99th percentile 2.3 to 3.6 s, longest 5.5 s: the wait for the exiting browser adds about a second where a round starts |
| In-process, 20 rounds, without the late-look rule and the start's last change | Two processors, one busy process | 36 | All drawn. The watch went on without the completion twice, 13 ms and 0.3 ms before it arrived late. The late-look rule now waits for such reports |
| In-process, 15 rounds, waiting up to 100 s, with the start's earlier 15-s retries | Two processors, two busy processes | 27 | No ERROR_BUSY. 25 drawn: medians 53 to 65 s by kind, the longest 79 s. The exiting browser never ended within the 10-s wait, and each retry's controller came together with the first one's. 2 failed at that version's 60-s limit, a moment before their controllers came |
| In-process, 15 rounds, waiting up to 120 s, the final code | Two processors, two busy processes | 27 | **All 27 drawn, none failed** (before: 9 drawn, 18 ERROR_BUSY). The first after 4.1 s, the others after 72 to 78 s: the exiting browser never ended within the 10-s wait, and each view's one controller came about 61 s after it was asked for |

### Residuals

- **CI's stall is not reproduced.** It came after both of the page's requests were answered, and its cause is unknown. The watch covers it if the page's DOM content loaded, which is likely for that small page, but not shown.
- **A FileCat started while the previous FileCat's browser is still exiting is not waited for.** Only views of the same FileCat know about that exit. The bounded start and its retries cover it.
- **Under the heaviest load, a page can still stay blank for up to 90 s while WebView2 starts.** The status line says so after 10 s; after 90 s the view says that WebView2 did not start, and shows the file's text instead. There, the pages that the old code failed at once with ERROR_BUSY now wait about a minute, and then draw.
- **Not on the installed candidate.** These runs used hidden host windows in test processes, on WebView2 158; nothing ran in the qualification VM.

### What the next installed candidate must check

In the qualification VM, with the candidate installed:
1. **Batch 21's sequence, 20 times.** Open the results document, the plan (from a link) and the release dashboard; close all three; press F3 on `links.md` at once. Each time, the Page must draw, normally within a few seconds. One that takes longer than 10 s must say so in the status line, and within 90 s it must draw or say why it cannot.
2. **Step R1, 20 times.** Close FileCat, start it again at once, and make the results document its first viewer.
3. **While a page is still loading.** Refresh must be enabled. After 10 s, the status line must say that the page has not finished loading. F5 must reload the page.
4. **After the runs.** Export diagnostics and keep any "Page view (I648)" warnings in `filecat.log`: their timelines show which stage stalled.

## Follow-up fix

**Found:** 2026-11-25, in the qualification VM, on the pre-rc3 build `d54e5a2b`, which has the fix above. Batch 34b ran step 2 of the check above 20 times, judging each Page by step 1's rule and reading the status line on every poll from 10 s on. All 20 Pages drew, 7 of them 10.1 to 11.9 s after F3. None of those 7 said in the status line that it had not finished loading, so the check failed. The batch counts a Page as drawn once the page's accessibility tree holds a hyperlink.

### The cause

- **The notice counted from when the page view had the page, not from the reader's request.**
  - The 10-s timer started in `PageView.Load` (`src/FileCat.App/Controls/PageView.cs`, lines 109 to 121 before the change), when the page view handed the page to the engine.
  - That happens in `ViewerWindow.ShowPage` (`ViewerWindow.cs`, lines 652 and 812), after the viewer window has been made and shown and has read and examined the file's first page, or later, when the native view is made. The reader's wait before that was never counted.
  - **Measured:** a probe in the VM on the same build (`probe-i648-timing.ps1`) ran step 2's steps eight times. The status line switched to page mode, the moment the page view gets the page, 0.39 to 0.55 s after F3 in five runs and 1.9 s in one. In the other two, the probe's own accessibility searches stalled.
  - So if batch 34b's runs were alike, the notice was due 10.4 s or more after F3. Four of the seven slow Pages (10.1 to 10.3 s) had drawn before that, and a fifth (10.4 s) about when it came.
- **The wait for the previous browser was not the uncounted part in step 2.** Each run there is a fresh FileCat, and `PageBrowserExits` knows only its own process's views, so none of them waited.
  - Where a view does wait (one opened as its folder's last one closed, as in step 1), the wait (`WebView2Page.WaitForExitingBrowserAsync`, at most 10 s) ran inside the notice's time.
  - But the notice then said "Refresh (F5) tries again", which cannot hurry the wait: until the view has started, Refresh only changes the page it will navigate to.
- **Not settled for the other two slow Pages** (11.0 and 11.9 s). That build logs nothing about a page that finishes by itself, so whether FileCat had finished them before their notice was due is not known. The probe found another way such a run can arise (see "In the qualification VM" below): the hyperlink can come seconds after FileCat has finished the page and its text shows.

### The change

1. **Counted from the request.**
   - The time the reader asked for the viewer goes with it: F3, or a Markdown link's click or its approval. `ViewerLauncher` sets `ViewerWindow.AskedAt`, and the viewer passes it to `PageView.Show(page, askedAt)`.
   - The notice is due 10 s after that, or at once if that time has passed.
   - A page asked for later (Refresh, the Page button) counts from that moment.
2. **What the page waits for.**
   - `WebView2Page` says when it starts and stops waiting for its folder's previous browser process (`WaitsForPreviousBrowser`, `WaitChanged`).
   - While it waits, the notice reads "The page has not finished loading: it waits for the previous page's browser to close." Once the wait ends, it reads "The page has not finished loading; Refresh (F5) tries again." as before.
3. **The log.**
   - When the notice comes, `filecat.log` gets a "Page view (I648)" warning. It says how long after the request and after the view had the page, whether the view still waits for the previous browser, and gives the view's timeline since the page was shown.
   - When that page then ends, an Info line says how long after the request it finished or failed.

Unchanged: one report per page, Refresh while a page loads, the watch's 4 s, the 10-s wait for the previous browser, and the 90-s start limit.

### Tests

- **`PageCompletionRecoveryTests`** (App), two new tests:
  - **The reader's wait counts.** The viewer is asked for, and its file's first read takes 2 s. Then the engine waits for the previous browser, and the page loads at once after the wait. With a 3-s notice:
    - the status line must say that the page waits for the previous page's browser 3 s after the request, and sooner than 4.5 s after the page view had the page;
    - once the wait ends, it must say that Refresh tries again;
    - the page must count as loaded once.

    Against the old code, it failed: the status line said so 5.48 s after the viewer was asked for, and the engine had had the page since 2.46 s. It passes with the change.
  - **A page asked for 1.5 s before the page view had it** says so 0.5 s later, with a 2-s notice. A Refresh then counts its whole time again.
- **`PageViewTests`** (Platform, the real WebView2): the test of a view opened right after its folder's last one closed also checks that the view reports its wait's start and end, and that the log entry for a slow page names the wait.
- **Runs on the development PC**, all at medium integrity:
  - App, the 41 viewer and page classes: 380 tests, 375 passed, and the 5 WebKitGTK tests skipped (Linux only).
  - App, the whole suite: 4,165 tests, 3,833 passed, 332 skipped, none failed.
  - Platform, `PageViewTests` and `PageCompletionWatchTests`: 26 tests, 24 passed, and the 2 opt-in tests skipped.

### In the qualification VM

A build of the fix's commit, `238f82b2`, was deployed as `C:\FQ\pre-rc3\win-x64-i648b`; the commit that adds this section changes no code. Afterwards, the harness pointed at `win-x64-r4` (`d54e5a2b`) again.

**Batches 34b and 34 passed, but no Page in them was slow.**
- Batch 34b: 20 of 20 drew, 19 within 5 s, none after 10 s.
- Batch 34: in A, 20 of 20 drew, in 0.4 to 0.5 s; in B, 20 of 20 drew, in 4.3 to 4.8 s.
- Neither left a "Page view (I648)" warning.
- The VM was faster than when batch 34b failed: on `d54e5a2b` too, the probe above saw the first hyperlink 4.2 to 6.3 s after F3 in its three undisturbed runs. So these batches do not show the notice at work.

**Under load, the notice at work.** A probe (`probe-i648-load.ps1`) ran four busy loops on the VM's four processors throughout and measured each Page without whole-tree accessibility searches, on both builds:
- **A, six times in one FileCat:** the results document was opened, drawn and closed, then `links.md` was opened at once.
  - The FileCat's first Page drew (its first hyperlink) in 3.7 s with the fix and 7.4 s on `d54e5a2b`. Every view after it waited the whole 10 s for the previous browser, on both builds. The fix's log puts each of those pages' end 10.3 to 10.6 s after F3.
  - With the fix, those 11 Pages said that they had not finished loading 10.0 to 10.3 s after F3. Each of the 11 log warnings reads "10.0 s after it was asked for".
  - The first `links.md` said "it waits for the previous page's browser to close" at 10.02 s, then "Refresh (F5) tries again" at 10.21 s, once its wait had ended.
  - On `d54e5a2b`, the 11 said "Refresh (F5) tries again" 10.05 to 10.21 s after F3, whether or not they were still waiting.
  - Refresh was enabled whenever the probe read it while a Page loaded, on both builds.
- **B, six fresh FileCats:** step 2's results document.
  - With the fix, FileCat finished every one within 10 s of F3: its status line never said otherwise, and its log has no entry. The page's text showed 4.0 to 4.8 s after F3.
  - Yet the first hyperlink came 6.6 to 10.4 s after F3, and 3.8 to 10.8 s on `d54e5a2b`.
  - One run on each build (10.36 s and 10.84 s) is one that batch 34b would count as a slow Page that did not say so.
  - The probe's list of notices for some B runs holds the page's own text, which its pattern also matched; FileCat's status line said nothing of the kind there.

### Residuals

- **Batch 34b's measure of a drawn Page.** For the 318 KB results document under load, the hyperlink came up to about 6 s after the page's text showed and after FileCat had finished loading it. Such a run fails batch 34b's rule, though the status line has nothing true to say. A check of the notice needs a page that FileCat itself has not finished, or a measure of the page as drawn, such as its pixels.
- **The original failure is only partly explained.** The counting gap is measured; whether FileCat had finished the 11.0 and 11.9 s Pages is not known. This build logs a page that has not finished 10 s after the request, and when it ends.
- **The exit under load.** Under the four busy loops, every view opened within about 2 s of its folder's last one closing waited the whole 10 s for the old browser. The wait is bounded and now said; why the exit takes that long under load was not studied.
- **CI.** The two new App tests give their timing about 1.5 s of margin. They have run on the development PC only, not yet on CI's three systems.
