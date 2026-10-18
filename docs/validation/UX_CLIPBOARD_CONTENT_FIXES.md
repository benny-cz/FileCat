# Clipboard intent and viewer content corrections

Scope: audit findings **FCUX-069**, **FCUX-070**, **FCUX-007**, and **FCUX-008**.

## Behavior and ownership

### Clipboard Copy/Cut/Paste

- Move authority belongs to the exact clipboard publication still owned by this running FileCat instance. It does not follow historical path membership. The entire materialized file list must match the publication, using FileCat's ordinal resource identities; a subset, an extra item, or a case change does not authorize Move.
- Clipboard formats are materialized from one caller-owned data-transfer object, which is disposed after materialization. In-process ownership is checked before and after the asynchronous reads and again after destination admission. Copy/Cut preparation has an invocation revision, and native writes are serialized so an older delayed write cannot replace a later publication.
- Another application's file publication, a restart, and a backend without in-process ownership support import as Copy. The unsupported-Cut fallback is explained immediately. FileCat does not claim to export or import a native preferred/performed Move effect.
- File Paste captures the destination location before its first await. Switching panels, moving the tab, or navigating the same tab cannot redirect its job. Closing the invoking tab cancels the pending Paste. A vanished original destination is reported instead of recreated or redirected.
- Text-path Paste also captures the starting location, but a later navigation invocation, newer Paste, or tab closure supersedes it. The navigation generation changes before provider preparation, so a still-pending newer navigation also wins.
- An in-flight Cut publication admits at most one Move. A completed Move cannot replay. A canceled, failed, or partially completed Move requires review in Operations or a fresh Cut of the remaining originals. Job completion updates its own publication and cannot consume a newer Copy/Cut.
- Clipboard export accounts for the complete selection: unsupported or unconvertible selected items leave the previous clipboard intact and explain the failure. This covers the clipboard branch of FCUX-071; the drag-export finding remains separate.

### Viewer and editor text search

- Both callers use the shared complete reverse text search. Buffers remain bounded; each missed window moves strictly toward byte zero. UTF-16 continuation advances by an encoding unit instead of returning to the same odd restart.
- Matches may overlap. A previous match has an exclusive upper bound on its start, so a match that begins before the cursor and crosses it remains eligible.
- Decoding includes short context before each window. A window inside a valid UTF-8 sequence or UTF-16 surrogate pair does not invent a replacement-character match. Genuine malformed sequences retain exact match offsets in the tested UTF-8/UTF-16 cases.
- Literal case-insensitive search and visible text highlighting use `OrdinalIgnoreCase`. This avoids culture-dependent collation changing the literal span or required overlap.
- Continuation belongs to the current query, mode, encoding, case/byte options, reader, and source/report context. Manual movement and context changes invalidate the old continuation; revealing a current hit preserves it.
- Stop search is visible during work. Cancellation, read failure, and an exhausted readable range produce distinct outcomes. Stale workers cannot replace a newer request's hit or status.
- Query and option changes invalidate synchronously through property changes, so queued routed text events cannot cancel the search just requested. Navigation events from inactive content controls do not alter the visible surface's search.
- Source polling starts only after initial encoding detection completes. The timer's interval/callback constructor starts immediately in the pinned Avalonia version, so the viewer now constructs an initially stopped timer and starts it at the actual opening-complete boundary. Closing during detection prevents a later timer start or encoding publication.

### Viewer checksums

- Checksum opens a scope review panel. Compute starts the operation. Whole file is an explicit option; an actual one-byte selection is eligible for selected-range hashing.
- Scope and source generation are captured before work. Caret movement does not change the requested range. The panel retains complete selectable SHA-256 and CRC-32 values, actual/expected byte counts, source identity, and revision evidence independently of transient cursor status.
- Copy results is explicit. Opening the panel, computing, cancellation, partial reads, and source changes leave existing clipboard contents intact. A failed explicit Copy reports the error while retaining the result.
- Long computations show bytes read and a visible Stop action. Escape stops active work even when the result text box has focus. The worker checks cancellation at supported source-call boundaries.
- A short read or read failure is visible with the partial count and qualified hashes. Revision changes observed before/after reading prevent publication of a mixed-content hash. Sources without revision evidence are visibly qualified; length and modification time remain weak evidence, not an immutable snapshot guarantee.

## Automated verification

The focused tests use real FileCat command and window code. Clipboard tests supply controllable asynchronous clipboard publications, while their transfer jobs operate on disposable real folders and assert actual operation kind, captured destination, destination manifests, and original-file presence.

- `ClipboardIntentTests`: publication replacement, Cut subset replacement, intervening text/image/empty data, same-path Copy, restart, distinct-case paths, overlapping writes/reads, unsupported ownership fallback, admission/lifecycle failure, partial/canceled/complete Move, replaced publication completion, immutable destination, tab closure, stale text navigation, and all-or-nothing mixed-provider export.
- `ContentSearchBackwardTests`: files over 12 MiB, distant/chunk-spanning hits, UTF-16LE/BE BOM and no-BOM cases, overlaps, strictly decreasing progress, cancellation, read failure, valid split scalar versus real malformed input, and out-of-range alignment.
- `ViewerSearchScopeTests`: the real viewer and Latin-1 editor callers, reverse match order, UTF-16 completion, overlapping successive next/previous searches, manual movement/query changes, encoding/mode/options/report invalidation, visible read failure, and held initial reads proving polling begins after detection and remains stopped after close.
- `ViewerSearchLifetimeTests` and `HexEditorSearchLifetimeTests`: retained ownership/cancellation/close tests; the Stop scenario activates the actual visible button.
- `ViewerChecksumScopeTests`: one-byte and whole-file options, review before computation, immutable selection, full selectable values at actual window widths of 480 and 980 device-independent pixels (DIPs), explicit Copy and Copy failure, partial/error reads, revision change, visible Stop, Escape from results, and missing revision evidence. Width is established before showing the window, and the tests assert both the actual window size and result bounds.
- `ViewerChecksumLifetimeTests` and `ViewerChecksumRevisionTests`: retained request/source lifecycle assertions, updated to inspect the visible result and verify clipboard preservation.

Core search tests can be run with:

```sh
dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~ContentSearchBackwardTests
```

UI tests require the repository's existing Avalonia headless test setup. Run the corresponding class filters after building `tests/FileCat.App.Tests/FileCat.App.Tests.csproj`.

### Recorded focused results

The final focused App run passed **98/98** cases with no failures or skips in 20 seconds; its recorded result file was `clipboard-content-passing.trx`. The **26** `ContentSearchBackwardTests` cases also passed in the complete final Core run. Together these findings have **124 passing cases**; these counts overlap the complete suites and do not claim that the entire repository or every native platform passed.

| Test class or group | Passing cases |
| --- | ---: |
| `ClipboardIntentTests` | 34 |
| `ContentSearchBackwardTests` (Core) | 26 |
| `ViewerSearchScopeTests` | 25 |
| `ViewerSearchLifetimeTests` | 8 |
| `HexEditorSearchLifetimeTests` | 8 |
| `ViewerChecksumScopeTests` | 13 |
| `ViewerChecksumLifetimeTests` | 6 |
| `ViewerChecksumRevisionTests` | 4 |

The App command used the existing headless setup and a bounded hang diagnostic:

```sh
dotnet test tests/FileCat.App.Tests/FileCat.App.Tests.csproj --no-restore --no-build -c Release --disable-build-servers \
  --filter 'FullyQualifiedName~ClipboardIntentTests|FullyQualifiedName~ViewerSearchScopeTests|FullyQualifiedName~ViewerSearchLifetimeTests|FullyQualifiedName~HexEditorSearchLifetimeTests|FullyQualifiedName~ViewerChecksum' \
  --blame-hang --blame-hang-timeout 90s --blame-hang-dump-type none \
  --logger 'trx;LogFileName=clipboard-content-passing.trx'
```

The solution build and overall regression comparison are recorded separately in `UX_UI_IMPLEMENTATION_STATUS.md`.

## Verification boundaries

These tests are headless component and file-system integration checks. They do not establish native Explorer/Finder/Linux clipboard exchange, desktop clipboard-manager behavior, native keyboard/pointer routing, screen-reader announcements, or visual quality at every platform scale.

FCUX-012 remains a separate S2 item for horizontal access and selection identity during reflow. The search tests assert exact byte hit positions and continuation behavior; they do not certify long-line glyph reveal across native content surfaces.

Avalonia 12.1.1 documents `TryGetInProcessDataAsync` as returning the exact currently owned transfer on Windows, macOS, and X11; unsupported backends return null. The implementation uses this capability for safe same-process Cut and explains Copy fallback elsewhere. Native release verification should repeat Cut A → external Copy A, Cut A+B → external Copy A, intervening text/image ownership, repeated/partial Move, and delayed Paste with navigation on each supported backend, asserting actual job kind, destinations, and source survival.

Sources used for the API contract: the pinned Avalonia 12.1.1 XML reference documentation for `IClipboard` and `IAsyncDataTransfer`, and the official [Avalonia IClipboard reference](https://docs.avaloniaui.net/api/avalonia/input/platform/iclipboard). Native preferred/performed transfer effects are intentionally outside this implementation's supported exchange behavior.
