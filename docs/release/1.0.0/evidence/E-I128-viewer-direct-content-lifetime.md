# E-I128 — viewer direct-content calls retain the file through close

Preliminary V12 / AI-03 remediation; no candidate qualification. Baseline production producer
`9d332825237ffab2bb6f329e4a454fe78e3239e7`; execution resumes from clean main
`8b6a52a70152602b9325c5c7d538564ca090d889`. Private evidence is under
`FileCatReleaseEvidence/viewer-direct-content-20261004` in the existing C-drive evidence root.

The owner's elevated Codex restart did not restore Computer Use 26.930.41038. Prescribed initialization exits
before any target or input; one retry reports `windows sandbox failed: helper_unknown_error: setup refresh had errors`.
Exact results are retained in `elevated-computer-use.json`, SHA-256 `997fbcf89685eef4705de44a1a8347cd9a0ce3504638d21c82991931d2113f44`.
The shell identity observed at 17:20:00 UTC is Windows 10.0.26220.0, marek, **unelevated**, despite the reported
elevated desktop launch (`shell-identity.json`, `fc4c4c0429765d5b2ae521e154385dcf5b4ac9126f090fcb2e14eac1aaba7e80`). VMware command execution remains
available. Both guests stay running. No USB work was performed; the existing source-change gate stays held.

## Reproduction before modification

An independent test assembly references the actual retained App/Core/vendor binaries from the clean 9d33282
SDK-free package. Its original input manifest is rechecked against its recorded hash
`0972c6da15f0eee9b68144378c8c53e6e91065a02fcbf7ad8bcea1bbd23d577b`, clean producer identity and every copied DLL.
The headless test opens the actual ViewerWindow over an owned real FileContentSource, waits for initialization,
caches its pages and stops only the fixture's refresh timer. The actual line command, ShowInfoAsync, or HtmlPage
captured from ShowPage performs the held read. No native browser, desktop input or physical device is involved.

Four close-during-read cases fail on unchanged production: line, HTML, Markdown and Info. Each disposes its
source once while the read is active; that read then observes a disposed file. The line operation also returns
an uncaught `ObjectDisposedException`. Actual application termination was not observed. All four controls that
release the read and complete before closing pass; their line offset/content/inspection results are checked.
Every owned file's independent SHA-256 remains unchanged. Baseline v2 and the stronger identical v3 inventory
are both retained; v3 XML `121cac3cd72b5a90f73df4602a36381e1c59f5e319c5c4a21bfa4ca866dde078`. The initial probe compile error used an inaccessible
test-only status property; it is retained as a harness failure and has no product disposition.

## Correction and host validation

Line scanning borrows the source through PagedReader.WithSource, links cancellation to close, disposes its CTS
and rejects canceled or superseded progress/results. HTML/Markdown pages accept the reader owner and closing
token, retain the active resolution and refuse requests after close/cancellation, including adjacent assets.
Info borrows the source for the inspection and fallback length, observes cancellation around source calls and
rejects late reports. Encoding initialization also avoids starting a timer or a mode after close.
The existing source-only HtmlPage API and rendering/containment/size limits remain available.

The same eight-case probe passes with only FileCat.dll and FileCat.Core.dll replaced; every other input,
including the probe assembly, is byte-identical. Corrected XML `22ebf5a1b3d17f1244c30aa2ba533af5dd351218dc9533cd7f92c548c69c918d`. Close returns below
the fixture's three-second bound while the read is held, immediately retires cached pages, does not dispose
the source until the call returns, performs no further read and discards canceled page/line results.

| Working host inventory | Pass | Declared skips |
|---|---:|---:|
| Core affected | 102 | 0 |
| Headless App affected | 41 | 0 |
| Core full | 754 | 46 |
| App full | 290 | 21 |

Eight App regressions cover all four held/complete paths; two Core regressions cover owner-close refusal of
main and adjacent files and cancellation of an otherwise open Markdown reader. The two retained working
captures contain 1,096 files, each with twenty-five source/build inputs (21 C# files, two projects and two
properties files). All bytes, XML case IDs/multiplicity/counts, complete probe observations and selective
production replacements independently verify in `independent-working.json`, SHA-256
`593e1ac17b2e47bbe3e0e8b86d151789b3abb2c7fd4fc80142f686593cd2ca52`. Working source is the recorded 8b6a52a base plus the four-file overlay;
these are preliminary host results, not a clean committed build.

Clean CI and SDK-free guest revalidation are pending. Native engine callbacks/input/frame timing, actual hung
hardware, aggregate picture/page/other queues and exact-candidate qualification remain open. The former
viewer passes do not qualify this direct-source close race. Overall **NO-GO** remains; no stable publication.
