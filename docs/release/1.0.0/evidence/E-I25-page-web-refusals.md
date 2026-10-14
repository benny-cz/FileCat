# E-I25 — what a rendered page may not reach, in the real Windows engine

I25/V10/V24/D-51, 2026-10-15. **Two new controls pass in the real engine on two machines.** The page view's policy was implemented and described; this measures it.

## What was missing

I25's remaining page scope named "fonts/CSS/permission/download/popup workflows". The policy is in `WebView2Page`: scripts, web messages, script dialogs, devtools, host objects, autofill and browser accelerators are off; `AddWebResourceRequestedFilter("*", All)` refuses anything not on the reserved host with 403; navigation away is cancelled; new windows, downloads, external URI schemes and permission requests are refused. The existing controls covered a script, an image beside the page and one outside its folder. Styles, fonts, frames, a navigation the page asks for, and a download it sends itself were not measured.

## The controls

**A page asks the web for styles, fonts, frames and its next page in vain.** A local page carries `<link rel=stylesheet href="https://styles.example.com/theme.css">`, an `@font-face` whose `src` is `https://fonts.example.com/far.woff2`, `<iframe src="https://frames.example.com/inner.html">`, `<img src="https://pictures.example.com/remote.png">` and `<meta http-equiv="refresh" content="0; url=https://next.example.com/elsewhere.html">`. Measured: all four resources are refused by host, the refresh is **cancelled** and arrives as its own navigation completion with `OperationCanceled`, the page keeps its own title, and nothing from `example.com` is ever served. The control records every navigation in order, because the refused refresh completes after the document itself loads.

**A page that sends itself a download saves nothing.** Scripts are off, so the only way left to a page is to navigate to a download: it refreshes to a file beside it, served as `application/octet-stream`. Measured: `/page.html` and `/payload.bin` are served as the page's own resources, the download is cancelled and arrives as `ConnectionAborted`, and no `payload.bin` appears in the test folder or in the user's Downloads folder.

## Where they ran

| Machine and session | Result |
|---|---|
| Development host, elevated | Both controls pass; the four existing WebView2 controls also pass **unskipped**, with the installed runtime (155.0.4283.45) |
| Disposable Windows 11 VM, signed-in medium session | Both controls pass; same refusal lists and completion reasons |
| Same VM, elevated in that session | Both controls pass |
| Owner's Mac (macOS 27.0.1, Apple Silicon), over SSH | The macOS smoke tool passes at this commit: the HTML page loads with its own title, so its script did not run, one remote request is refused; the Markdown page loads as `README.md` with nothing requested from the web; 12 of 12 engine lifecycles show both titles and no event arrives after disposal |

The full Windows Platform suite passes **454 tests with 36 explicit skips** on the host.

## What this does not establish

Popups and permission prompts remain unmeasured: with scripts off, a page cannot open a window or ask for a permission without a human click, so those two refusals stay code-level only. Linux (WebKitGTK) and macOS (WKWebView) keep their own harnesses — the macOS smoke tool and `LinuxPageEngineTests` — and are not re-measured here. Fonts loaded from the page's own folder, print and zoom behaviour, the native engines' own updates, and candidate qualification remain. I25 stays open.
