# E-I132 - quick-search focus is observed when Escape cancels it

**Classification:** preliminary validation repair; no production change or candidate qualification.
**Baseline:** clean 545d627, CI [37232322100](https://github.com/benny-cz/FileCat/actions/runs/37232322100).
**Correction:** `5a11100bddbe3c17ee7fe8739678be221b444ec7`, only `QuickSearchTests.cs` changes.
**Status:** controlled/full host validation and all four clean CI jobs pass; verified preliminarily.

## Failure and controlled evidence

The baseline's macOS App inventory has 297 passes, one failure and 43 declared skips. The Escape
test records pending search/focus 1000 before calling the headless input helper; its final assertion
observes focus 0. The run does not trace whether the accepted match applied before or after the
actual cancellation boundary. Its precise historical timing remains unavailable.

An independent four-case probe uses the original seven pinned production DLLs from clean fdb17b4.
It holds the real background-search continuation, then releases it before or after keyboard Escape.
When released before Escape, the accepted `b.txt` match legitimately moves focus from 1000 to 0;
the early observer fails (expected 1000, actual 0), while the cancellation observer passes. Both
observers pass the after-Escape control: canceled text/queued keys stay discarded and focus remains
1000 after the held answer is released. All four retain one mark. One controlled old-observer failure
and three passing controls are retained; no production DLL changes between them.

The test now samples focus when QuickSearch becomes null during cancellation, and still requires
that focus/text/pending state remain unchanged after the old work finishes. Two forced timing
controls require exact focus 0 for a completed match and 1000 for an answer released after Escape;
both use the actual background queue and headless keyboard route. The synchronization context is
restored immediately after starting the controlled work. No timeout or production guard is relaxed.

## Host validation and exact provenance

Working base `11a9b94388bee4b3c69ea1287d16eeec0fd90235`, one-file test overlay SHA-256
`cb177324225584ea16502fb1f55879130ad32bcc529e0e5c4f6ce7dfc0a77717`.
The final verifier confirms canonical source equality to the correction commit and that no
production source changes. All original/new case names and execution IDs are checked.

| Check | Pass | Skips | Results SHA-256 |
|---|---:|---:|---|
| Controlled probe | 3; one deliberate old-observer failure | 0 | `f33e39376ed85bae1d811c81a9790d58429f0284b470315e8585cc6d49cfc9be` |
| All quick-search App cases | 9 | 0 | `a5076d5174881d700a307e51ba027dc01130b5983af5b18b28ec1e6f0120134f` |
| Full App | 322 | 21 | `cdf4fb971d0670dde7e933219785089e4fb1fee21dd65bbddfd458536a435397` |
| Corrected Git fixture setup control | 1 | 0 | `e3cdea9134b5f1b10cf50e3c47d89a2edf80dec5d92e0ca3984fb335cdb91b4a` |

Private roots in the authorized second workspace's `FileCatReleaseEvidence`:

- `quick-search-escape-checkpoint-20261004`: source snapshots, probe/controller, pinned inputs,
  failed compile logs, all four observations/XML, working source and complete host TRX inventories.
  `independent-working.json` SHA-256 `567b8020982b2a5a966f69c73d25fbc04215c360dd9aa581b2ff4d2de5b99e28`.
- `ci-545d627-failure-20261004`: complete failed job log, run/jobs/artifact metadata and raw macOS TRX.
  Artifact 11314815255, 104,821 bytes, server/ZIP SHA-256
  `41f540f6110080aba89f21cf572d618f5f5b650b59130042559d15cf9dc7bc26`.
  Independent full failed inventory SHA-256
  `374dd21ba27dc104059211280ca75c958a0079c10fb7834bb4abbebabcaca601`.

The first host compile failed on an ambiguous namespace import; the alias repair and original logs
are retained. The first full host run has 321 passes, one Git fixture setup failure and 21 skips:
Git rejects the harness's long temporary path before FileCat is exercised. A short owned temp root
fixes that precondition; the unchanged build passes the setup control and all 322 runnable App cases.
The short root is verified absent at 2026-10-04T22:26:43Z. All original failures stay failed and retained.

## Clean correction CI

[Run 37240337432](https://github.com/benny-cz/FileCat/actions/runs/37240337432) completes successfully
at the exact correction commit. All four required jobs pass; the three tag/manual package jobs are
declared skipped. Windows ARM64's App suite, package startup/drawing and installer compilation pass.
That lane retains log totals, not a per-case TRX inventory; it is not physical ARM64 qualification.

The six direct TRX inventories retain every test name/ID, execution ID, outcome and skip reason.
Each App inventory exactly matches the full 343-case host inventory, with all nine quick-search cases
passing without skips on Windows, Ubuntu and macOS, including original Escape and both forced controls.

| CI App lane | Pass | Declared skips | TRX SHA-256 |
|---|---:|---:|---|
| Windows | 328 | 15 | `48182351793dfa345dd04da4cc96b60f3e00bfb157a51dfc221ffa678a5a5239` |
| Ubuntu | 300 | 43 | `e0237e1be61ec8447a11e62b8dfa4e0a99ed2d3692ac83bda34e29bd08de6bca` |
| macOS | 300 | 43 | `02c562484f762d31dee751a0c90ae84f38a55c523f3907e1d9b3d50e3993f77a` |

Four downloaded ZIPs match their server SHA-256 digests, byte sizes and exact run/source association.
Extraction checks duplicate/traversal/symlink members; the independent report pins every extracted file.

| Artifact | ID | Bytes | Server/ZIP SHA-256 |
|---|---:|---:|---|
| Windows test results | 11316807438 | 400,814 | `f45186b37b99150cf1e110ce2285c6fe8eafb0342e0c492ed6547b1ae216234b` |
| Ubuntu App results | 11316732783 | 103,114 | `a766b9a22e452ac1e391fabdfc793ca2ba498a8135ef7beeac2fe5ba851e03b9` |
| macOS App results | 11317186622 | 104,993 | `12d56a96d285b55961652c7e53af948bb8c6893ba23330a137c87b576d3f541e` |
| ARM64 screenshot | 11316804164 | 103,614 | `fc8dbda9450e7763274e8342022738fc0f2168179172f2cfc752efe0d794d6fd` |

Private `ci-37240337432` retains full run/job/artifact metadata, complete job log, command exits,
original ZIPs, six TRX files and the screenshot. `independent-ci.json` SHA-256
`6b50257c52ce896423855a40da26739856fe5440cf7f28ac89dd103a01dc431f`, verified at
2026-10-04T22:44:45Z. The original failed run remains failed; clean success does not supply its
unobserved historical event timing or native input/frame/AT evidence.

The unchanged-source documentation successor 11a9b94 independently passes all four CI jobs; this does
not repair the invalid checkpoint. The correction's clean CI now passes; applicable native/candidate work remains.
E-V12-N1's eighteen native-data executions remain valid preliminary evidence because this change
only corrects test observation. Both VMs stay running; G: is untouched and its source-change gate
stays held. Native input/frame/AT and final candidate qualification remain; overall **NO-GO**.
