# E-I108-P1 — finalized copy totals and scoped thumbnail evidence

I108/V03/V16/V24. Preliminary observer corrections, base `36ee8248453ecc91cc11476918b0a3d3ed8ccd5f` plus an
exact two-test-file working overlay. **Affected host checks pass; successor CI pending.** No production copy,
progress, Shell policy, deadline, isolation or helper recovery behavior changes. No USB access or candidate pass.

Documentation c162481 CI [37151556209](https://github.com/benny-cz/FileCat/actions/runs/37151556209) retains two
failures: macOS sees BytesTotal zero at the held copy boundary; ARM64 sees two helper starts instead of one.
Windows x64 and Ubuntu pass. Complete metadata/log are retained; hashes are recorded in E-I106-P2.
The subsequent unchanged-observer 36ee824 CI passes all four lanes. An intermittent passing rerun is not the
remedy for the original observer defects.

Copy discovery runs on a separate worker. Holding CopyFile after it returns prevents read-back progress, but
does not finalize discovery totals. The corrected observer waits, within thirty seconds, for both the held copy
checkpoint and TotalsFinal before asserting all copy bytes done, zero verification bytes, three-file-size work
and exact one-third progress. Release, completion and two-file-size verification assertions remain intact.

The thumbnail test previously equated a global helper-start count with successful quick view. Replacement after
a contained handler failure is supported behavior, and other Shell requests share that client. The reason for
ARM64's extra start is not established. This UI case now requires its exact picture's non-null cached helper
answer, expected bitmap dimensions, valid BGRA length/gradient content, visible Shell-thumbnail caption and
low-integrity helper. Separate native client tests retain exact one-helper reuse, request sharing and
hang/crash containment assertions; those nine cases pass, zero skips. No failure is hidden by a retry or new skip.

An attempted stronger displayed-byte comparison fails on the host's existing mock headless renderer. Its source,
DLL, full failing App TRX/log remain retained. Avalonia's tagged
[headless implementation](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.1/src/Headless/Avalonia.Headless/HeadlessPlatformRenderInterface.cs)
uses bitmap stubs; [official testing guidance](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform)
requires a real drawing backend for rendered-image validation. This suite keeps its existing renderer and reports
the resulting scope explicitly: helper answer and UI binding, **not rendered pixels**. Final native UI/FQ remains open.

Private root `artifacts/release-evidence/i108-observers-20261003`. Full host Core passes **700/746, 46 skips**,
all eight progress cases included. Corrected full App passes **242/263, 21 skips**. Targeted Shell UI passes 2/2;
native Shell client passes 9/9. Independent direct XML/source checking retains skip inventories and 22 files.
The verifier's initial expected-name typo is retained and corrected against the actual test inventory.

| Private evidence | SHA-256 |
|---|---|
| Exact two-file source manifest | `312afc8bb88876c6727b4753a6f9e8487eecf287cfd13761e035d4d8a461cc7e` |
| Full Core TRX | `69aca695484fea4ce2d9f76c64cf8759c576aa335180632ed1a9f3843accba33` |
| Corrected full App TRX | `3d41e119c51b122be5c8d6bdceeddab4e27bb78c297ace71c3042b90227ac606` |
| Targeted Shell UI TRX | `ee0ba84680fea25d974917ab6afe33371347ddb7a800022f5f6e794ac1e05582` |
| Native Shell client TRX | `9e9f6a59ad47bc4490309de5eacba4b9b5da7e79850376426cc3131312f97880` |
| Retained unsupported pixel-control App TRX | `c176e4accb55d8de1405c3b91c74976a2cfb249a72abc0665ee2a1e223cf8ee5` |
| Independent accounting/source/inventory | `c5975982f016b69541734ed5d73028bf77d0894ee630a318475f0e28419b604a` |

I108's successor CI remains required. Historical G6 attribution, I106's broader availability and candidate gates
remain open; recommendation **NO-GO**.
