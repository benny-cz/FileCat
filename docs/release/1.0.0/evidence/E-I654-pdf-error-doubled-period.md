# I654 — A PDF that cannot be shown gets a message with a doubled period

**Found:** 2026-11-24 by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (b24). The harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/b24_pdf.txt`). **Status:** open, Low.

## Observation

On the macOS rc.2 package (`FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed…`), the viewer said:
- "This PDF page cannot be shown: File not in PDF format or corrupted.. Text and Hex (F4) show its bytes; Info (Ctrl+I) describes the file."
- for an encrypted PDF: "…Password required or incorrect password.. Text and Hex (F4)…".

`src/FileCat.App/Views/ViewerWindow.cs:752` appends ". Text and Hex (F4)…" to the PDF library's message, which already ends with a period. The code is shared, so every platform shows the same text.

## Remedy

Drop a trailing period from the library's message before appending the rest. Show the message otherwise unchanged.

## Fix

Commit `5d0edc429` on branch `fix/i649-i653-i654`.

`ViewerWindow` now passes the decoder's message through `WithoutFinalPeriod` before its own sentence. The helper drops exactly one final period and leaves the message otherwise as it came, so an ellipsis keeps two of its three dots. The message now reads: "This PDF page cannot be shown: File not in PDF format or corrupted. Text and Hex (F4) show its bytes; Info (Ctrl+I) describes the file." The same line serves pictures: FileCat's own decoder messages there end without a period and read as before.

**Tests.** `PdfFailureMessageTests` holds 7 headless App tests:
- the messages for a malformed PDF and for the encrypted `owned-encrypted.pdf`, compared in full;
- five cases of the helper: a final period, none, an ellipsis, a space before the period, and an empty message.

**Results**, at medium integrity on Windows 11. The TRX files are kept on the development PC, in `%LOCALAPPDATA%\Temp\fc-a0267\`.
- **Before**, against the unchanged code: both message tests failed, on "corrupted.." and "incorrect password..". `before-app.trx`, SHA-256 `8362f5cddbea5f55c53b7b7755215d22da393c14315453900477f10a465dab9d`.
- **After**: 7 of 7 passed. `after-app.trx`, `e2148f3a31b8a3c8cc739e5fc33d2be70b1de87af87bdada178fd97c2860463c`.
- **Mutations**:
  - Trimming every final period failed the ellipsis case (`mutation-a.trx`, `d3c7adb551d5151f567dcf7df657b570c48e94174f29ae6f0a8d0bd7b120615f`).
  - Keeping the message whole failed both message tests and three helper cases (`mutation-b.trx`, `4ad05bfeaf06f23dd3351e92a7eb47dfcbe855a7bb0fe7ce0b31aaaba273e63a`).
- **Regression**: the whole App test suite on the three fixes ran 4,003 tests: 3,709 passed, 0 failed, and 294 were skipped as their environment requires (`full-app.trx`, SHA-256 `faec871c6108e203823cdb9b3121827f9d94f598ca619240e361976d8f0a39bc`, 38 min).

**Not changed: the same pattern elsewhere.** Five notifications append to an exception's message in the same way, so a .NET message that ends with a period doubles it there too: `MainViewModel.EditSessions.cs:789` and `:1091`, and `MainViewModel.Registry.cs:365`, `:566` and `:606`. They are outside this record.

**Not verified here.** No installed build was run; macOS and Linux run these tests only in CI.

**The next installed candidate must check** that the viewer's message for a damaged PDF and for an encrypted PDF ends the library's sentence with a single period.
