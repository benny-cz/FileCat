# S1 implementation — execution results

> Final verification completed on 17 October 2026. All 321 added Core/App test cases passed. The complete suites retain exactly the original baseline failures and platform skips; there are no new failures, removed cases or changed outcomes among comparable existing cases.

## Scope and environment

This report records the first high-severity implementation batch against the original FileCat baseline checkout. The [implementation map](UX_UI_IMPLEMENTATION_STATUS.md) covers all 13 S1 findings in the supplied audit and identifies the remaining acceptance limits. FCUX-009 is an interim full-line-detail correction; FCUX-010 still requires native accessibility acceptance. Medium and low findings remain a separate backlog except for the explicitly documented adjacent Settings and clipboard-export corrections.

The original and modified sources were built and tested in separate local checkouts using the pinned **.NET SDK 10.0.101**, **Release / net10.0**, **Avalonia 12.1.1**, and **xUnit v3 3.2.2**. The host is **Linux x64 running as root**; App UI tests use Avalonia's headless backend. Dependency versions, package lock files and SDK pinning were preserved. The locked package restore succeeded.

| Build | Warnings | Errors | Result |
| --- | ---: | ---: | --- |
| Original complete solution | 0 | 0 | Passed |
| Final complete solution | 0 | 0 | Passed |

The final build includes the last Find focus, comparison boundary, safe-confirmation test and command-icon corrections. Its captured elapsed time was **18.47 seconds**. No production or test source changed after that build. Validation documentation and packaging metadata are added afterward.

## Complete regression results

| Suite | Original passed | Original failed | Original skipped | Final passed | Final failed | Final skipped | Final total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Core | 4,247 | 1 | 112 | 4,395 | 1 | 112 | 4,508 |
| App | 1,577 | 31 | 125 | 1,750 | 31 | 125 | 1,906 |

The complete Core run added **148 executed cases, all passing**. It has no removed cases, no changed outcomes among comparable existing cases, and no failure absent from the original-source baseline. The complete App run added **173 executed cases, all passing**, also with no removed cases, no changed outcomes among comparable existing cases, and no failure absent from the original baseline. Together, the two suites executed **321 added passing cases**. The full App run completed in **13.30 minutes**; it has exactly the same 31 failing case names and 125 skipped cases as the original run.

These are complete **Core and App project** runs. They are not a native desktop or live-service acceptance run. The separately provided Windows, remote integration, screenshot and smoke/probe projects are not claimed as executed here. Existing platform-specific skips are reported as skips, and passing headless tests do not establish native keyboard, clipboard, screen-reader or filesystem behavior.

## Focused gates and what they verify

Focused runs overlap the complete suites and each other. **Do not add their counts to the complete-suite totals.** They are retained to make the affected behavior and intermediate verification independently reviewable.

| Recorded gate | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| Clipboard, viewer/editor search and checksums | 98 | 0 | 0 | 98 |
| Recovery and Undo affected App regressions | 231 | 1 | 8 | 240 |
| Dialogs, Settings, bulk-rename scope, synchronization and operation UI | 45 | 0 | 0 | 45 |
| Final selection, comparison, Find, toolbar and Working Set integration | 112 | 0 | 2 | 114 |
| Core marked-item paging and cached metadata | 6 | 0 | 0 | 6 |

The one recovery-gate failure is the existing root/POSIX journal-permission assertion described below. Its 27 cases in `InterruptedTransferIntentUiTests`, `UndoLifecycleUiTests`, and `DialogFocusOwnershipTests` all passed. The two final-integration skips are existing Windows drive-letter PanelKeys cases. That final gate includes 67 comparison cases, 31 selection/accessibility cases, eight Find cases, five runnable PanelKeys cases, and one Working Set case.

Coverage verifies outcomes as well as UI state:

- **Operation identity and effects:** controllable clipboard reads/publications and delayed reviews assert the submitted Copy/Move kind, captured destination, complete paths, and actual source/destination manifests. Recovery tests exercise persisted filters and mappings, legacy/unsupported intent, conflicts, aliases, and the filtered same-volume move traversal. Undo tests verify exclusive reservation, tracked admission, cancellation boundaries, retained per-action evidence, changed destinations, history removal and durable dependent-rename recovery.
- **Selection and consequential review:** real key/pointer events exercise range ownership across sorting, filtering, tabs and focus. Bulk-rename tests inspect hidden-mark disclosure and counter changes, then verify actual renamed files and the original navigation origin. Held background decisions and queued focus callbacks cannot replace an unfinished review or turn an unrelated Enter into approval.
- **Settings semantics:** round trips include empty argument tokens, literal quotes/backslashes, Unicode, pipe characters, exclusion masks and structured launch properties. UI tests keep invalid drafts in the same session and inspect the actual non-executing `ToolLauncher.Plan` and persisted settings, rather than launching user-configured tools.
- **Content correctness and lifetime:** backward-search fixtures include distant and chunk-boundary matches, UTF-16 progress, overlapping matches, malformed sequences and cancellation. Caller tests hold reads and change queries, modes, sources or ownership. Checksum tests distinguish one selected byte from a whole file, compare complete known hashes/counts, retain partial/error/revision evidence, and require explicit Copy before changing the clipboard.
- **Decisive comparison evidence:** tests use real laid-out viewports at **640, 980 and 1200 DIPs** with 60-, 80-, 200- and 10,000-character lines differing at the last character. They assert changed-glyph visibility, keyboard/pointer navigation, wrapping, synchronized scrolling, lifetime ownership, and complete part reassembly. The exact accent-at-part-boundary case is covered; an oversized combining sequence must remain bounded and disclose partial-element display.
- **Accessibility model:** tests exercise stable virtual item identity, focus versus mark state, action/selection/scroll contracts, detached/stale peers, bounded peer creation, coalesced cached events and a complete paged marked-item review. Same-named result items retain their original location. Accessibility queries do not trigger synchronous provider metadata reads. These are headless model and contract checks; the native bridge is a separate gate.

The [machine-readable result record](UX_UI_TEST_RESULTS.json) contains the executed new case groups, outcome counts, exact failure names/messages, run times and TRX hashes. It also records the original-to-final comparison method.

## Failures reproduced in the original sources

| Group | Cases | Observed evidence and practical limit |
| --- | ---: | --- |
| Core device-file expectation | 1 | `ChecksumManifestBoundedInputTests.Unix_zero_length_device_cannot_escape_the_explicit_manifest_budget` expects exactly `InvalidDataException`; the reader rejects the device with `VerificationFileReader.NotAFileException`. The same mismatch occurs in the original checkout. The reader's refusal was preserved; the unrelated assertion was not weakened. |
| App picture/worker families | 30 | The same cases fail in the original root-run Linux environment. Worker diagnostics include the deliberate refusal to run with elevated rights when no ordinary user/group identity is available; related picture cases report unavailable images or wait failures. This environment cannot establish native picture-worker acceptance. No worker privilege check was bypassed. |
| App journal close permission | 1 | `InterruptedDemandTests.A_failed_journal_close_preserves_the_durable_source_manifest` expects a POSIX permission denial, but this root process can bypass the fixture's mode-bit restriction. The failure was separately reproduced on the unmodified sources. An ordinary-user execution remains necessary. |

The App picture/worker failures span `ShellPictureUiTests`, `PictureViewerTests`, `PictureRequestBoundsTests`, `PictureAdmissionTests`, `PictureRevisionStatusTests`, `PictureDeviceDemandTests`, `PictureLifetimeTests`, and `LinuxPicturePolicyTests`. Exact parameterized case names and captured failure messages are included in the JSON and original/final TRX files. The evidence identifies the observed environment and failure families; it does not claim that every related timeout has been independently diagnosed beyond the baseline comparison.

## Integration issues found and corrected during this work

The first complete modified App run exposed seven failures absent from the original-source baseline. Five existing Find tests showed that the window's queued startup callback could take focus from Save Search or Duplicates. The callback now yields to active dialogs, newer focus, deactivation and closure, with three additional ownership tests. The existing toolbar invariant caught the missing icon for the new Review marked items command; its icon now uses the established vector/theme system. The Working Set deletion test was updated to deliberately focus **Delete set**, since consequential confirmations now correctly start on Cancel. Its file/reference assertions are retained. All seven affected existing tests pass in the final focused gate. They also pass in the final complete App run, whose existing-case outcome comparison is unchanged from the original baseline.

An earlier unrestricted Core run also produced three memory failures in existing page-cache and recovery-fuzz tests on this runner. The final complete Core run uses **four concurrent xUnit collection threads** and the conservative scheduling algorithm. All three pass under that configuration; the original exception-type mismatch remains. Product memory limits, test assertions and test data were not reduced. The exact [runner configuration](ux-s1.runsettings) is included, using xUnit's documented [RunSettings interface](https://xunit.net/docs/config-runsettings).

The earlier full runs were retained as diagnostic evidence for this historical comparison and are explicitly superseded by the final complete runs. Focused success is not substituted for the final regression comparison.

## Reproduction

Install the SDK specified by `global.json`, then run from the source root:

```sh
dotnet restore FileCat.slnx --locked-mode
dotnet build FileCat.slnx -c Release --no-restore --disable-build-servers
dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-build --no-restore --settings docs/validation/ux-s1.runsettings --logger 'trx;LogFileName=core.trx' --results-directory test-results/core
dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj -c Release --no-build --no-restore --blame-hang --blame-hang-timeout 90s --blame-hang-dump-type none --logger 'trx;LogFileName=app.trx' --results-directory test-results/app
```

Use an ordinary desktop user for the native runs. The recorded container execution used a 1.5 GiB per-process managed GC heap cap (`DOTNET_GCHeapHardLimit=0x60000000`) and a task-local SDK/package installation. Those environment details describe the recorded run; they do not change the repository's runtime requirements or product policy.

### Reviewing the implementation

Use the Git history on `main` to inspect the implementation and tests. Run the
commands above against a checkout of the desired commit. The current validation
state is recorded in the [implementation status](UX_UI_IMPLEMENTATION_STATUS.md)
and [session workflow](UX_UI_SESSION_WORKFLOW.md).

## Remaining acceptance and next severity work

- **Clipboard:** verify native exchange and replacement timing on each supported desktop backend. External/unverifiable data intentionally imports as Copy; native preferred/performed Move-effect interoperability is outside this batch's exchange contract.
- **Recovery and Undo:** exercise native identities, Windows Recycle Bin and cross-volume behavior, genuine ordinary-user permission failures, actual process interruption, and desktop report/clipboard/close interactions. Cancellation is not a promise of rollback. Registry inverses retain their guarded Registry job and issue path; the new complete typed per-action Undo report applies to filesystem inverses.
- **Accessibility:** execute the audit's native inspector and keyboard/screen-reader protocol with the supported Windows, macOS and Linux assistive technologies. Large selection has a complete paged review; it does not create an unbounded native array of item peers. FCUX-010 native acceptance remains open.
- **Comparison:** the current detail fields expose full source lines in bounded parts, but FCUX-009 remains interim. Full-document variable-height text comparison, general text selection/search and the native long-line experience require the later shared comparison surface. The detail's bounded extension does not guarantee whole-element reveal for an exceptionally large grapheme; partial display is explicitly disclosed and every part remains accessible.
- **Layout and input:** validate native focus/automation and supported display scaling/font sizes. Headless checks report DIPs and laid-out bounds, not native physical-pixel or assistive-technology results.

Continue through the S2 audit order after the remaining S1 acceptance work: shared modal reachability (FCUX-011), viewer geometry/selection (FCUX-012), viewer copy correctness (FCUX-076), right-side binary keyboard selection (FCUX-072), and viewer accessibility events (FCUX-073), followed by the rest of the individual findings. Adjacent code changes do not imply that those findings are closed.

## Evidence integrity

The [structured test results](UX_UI_TEST_RESULTS.json), runner settings and this
report are tracked in Git. The first batch compared original and modified
checkouts with separate complete builds and test runs. Current verification
should use the checked-out source and the test commands above; historical run
counts do not qualify later commits.

For the regression comparison, test display-name groups are compared as outcome multisets, retaining repeated/truncated theory-case counts. VSTest IDs include checkout paths and therefore cannot directly identify the same test between the two isolated checkouts. One unchanged Core PE-inspection case reports the path of its own test assembly; its reviewed display-name alias and reason are recorded explicitly in JSON. No arbitrary name normalization or ignored new failures are used.
