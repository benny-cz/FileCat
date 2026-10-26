# UX/UI acceptance ledger

Updated 26 October 2026. This is the **one authoritative ledger of required
acceptance still open** for the [76-finding audit](FileCat_UX_UI_Audit.md) and
the separate queued bugs. A row is a finite consequential scenario, not a
Cartesian product of every platform, scale, theme and provider. The
[implementation status](UX_UI_IMPLEMENTATION_STATUS.md) preserves dated,
build-specific passing and failing evidence. The
[session workflow](UX_UI_SESSION_WORKFLOW.md) governs closure and justified
repeats. [UX_UI_REMAINING.md](UX_UI_REMAINING.md) is only a severity-ordered
index into this ledger.

Statuses: **Open** means the exact boundary has no qualifying result;
**Failed** needs diagnosis or a fix; **Blocked** names an external
prerequisite. “Unrun” in the tested-build column means no qualifying run for
that scenario, even when related code and component tests passed. An evidence
link establishes the *related* tested subset, not the open boundary. Each
scenario closes independently; another open row for the same finding does
not invalidate it. The audit-wide CI gate is separate below.

## S1 — consequential operation and inspection routes

| Stable scenario | Finding | Status | Tested build and environment | Evidence | Exact remaining requirement |
| --- | --- | --- | --- | --- | --- |
| <a id="ac-s1-069b"></a>AC-S1-069B | FCUX-069 | Open | Finder/GNOME external Move effect unrun | [clipboard history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | Exchange one file with Finder and Files as Move if supported, or expose an explicit refusal; verify source/target manifests and no silent Cut reuse. |
| <a id="ac-s1-070"></a>AC-S1-070 | FCUX-070 | Open | Controlled delayed-task tests passed; native backend timing unrun | [clipboard history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | Hold a native clipboard read, change the active tab and target, then release it on Windows, Ubuntu and macOS; Paste must use its captured destination or explain refusal. |
| <a id="ac-s1-004"></a>AC-S1-004 | FCUX-004 | Open | Copy/Move/Delete entry and return passed on all three desktops | [modal history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | At narrow size, exercise one Replace conflict and one Mirror/recovery approval: leave and return focus, Cancel by pointer/keyboard, and hear the consequential choice and full target with NVDA, Orca and muted VoiceOver. |
| <a id="ac-s1-007"></a>AC-S1-007 | FCUX-007 | Open | Local Text/Hex Stop passed on Windows and Ubuntu at 100/150/200%; Mac Retina passed | [Stop history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | On a held remote Text read and a Hex/provider error, change query/mode, invoke Stop and verify prompt cancellation, truthful incomplete count, no stale result and spoken Stop/error feedback. Do not rerun the completed local Text/Hex Stop matrix. |
| <a id="ac-s1-008"></a>AC-S1-008 | FCUX-008 | Open | Local narrow and mid-read cases passed; remote revision case unrun | [checksum history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | Hash a delayed remote file while bytes/revision change; publish no mixed digest. Repeat once without strong revision evidence and show its visible qualification in one narrow state not already covered. |
| <a id="ac-s1-009"></a>AC-S1-009 | FCUX-009 | Open | Windows long-line and Ubuntu keyboard route passed; pointer/font fallback unrun | [comparison history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | On Ubuntu with enlarged nondefault font and a 2,000-character changed line, pointer-select/Copy the suffix, navigate Next difference and hear the correct changed-line identity. |
| <a id="ac-s1-010"></a>AC-S1-010 | FCUX-010 | Open | Local NVDA/Orca/VoiceOver routes passed; provider route unrun | [list history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | Inspect bounded virtual rows in one Windows archive and one Ubuntu delayed SFTP/result listing; verify focus versus marks, original location and one real action through native accessibility. Mac's spoken Previous/Next full-path navigator is an accepted equivalent for missing preview-row children. |

FCUX-001, 002, 003, 005, 006 and 018 have no new native row: their consequential
subcases are accepted in the dated status history. The final CI gate still
applies to the audit.

<!-- ACCEPTED-FINDINGS-BEGIN -->
Fully accepted original findings with no required scenario left:
FCUX-001, FCUX-002, FCUX-003, FCUX-005, FCUX-006, FCUX-018, FCUX-028,
FCUX-034 and FCUX-035. Their build-specific evidence is in the status
history; this inventory only prevents an old pending note from reopening
them.
<!-- ACCEPTED-FINDINGS-END -->

## S2 — shared desktop boundaries

| Stable scenario | Finding | Status | Tested build and environment | Evidence | Exact remaining requirement |
| --- | --- | --- | --- | --- | --- |
| <a id="ac-s2-011"></a>AC-S2-011 | FCUX-011, UXBUG-010/011/016 | Open | Signed `be7b2a30` Mac Retina 640×480 Copy X/backdrop passed on 26 Oct after clearing an occluding macOS alert | [dialog history](UX_UI_IMPLEMENTATION_STATUS.md#uxbug-016--pointer-dismissal-for-overlay-dialogs) | Use one long-path Undo/recovery review and one Registry/Find form at minimum/enlarged text: full paths wrap and are emphasized, actions remain reachable, X and backdrop cancel without mutation. Pairwise desktop coverage suffices. |
| <a id="ac-s2-012"></a>AC-S2-012 | FCUX-012/073/076 | Open | Mapping/reflow tests passed; native Copy round trip unrun | [viewer map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Select multibyte Text and Hex ranges, resize/enlarge font, then Copy; verify exact source text/bytes, viewport reveal and read-only value/selection announcements on Windows and one Unix backend. |
| <a id="ac-s2-013"></a>AC-S2-013 | FCUX-013/014/049/050/051 | Open | Palette/component contrast checks passed; full native composition unrun | [theme map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | In one high-contrast and one expressive palette inspect selected/search/danger/hidden-name glyph contrast, transparent picture content and one complete animation cycle. Toggle reduced motion while active on Windows, GNOME and macOS; verify content stays fixed and the preference is announced. |
| <a id="ac-s2-071"></a>AC-S2-071 | FCUX-071/075 | Open | Provider/model refusal tests passed; external native drop unrun | [drag map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Drag a complete local/ZIP selection to an OS file manager and a mixed/disconnected selection to a refused target; verify effect/cursor, exact manifest, no partial publication and unchanged source. Use clipboard as an equivalent if a backend has no supported drag effect. |
| <a id="ac-s2-015"></a>AC-S2-015 | FCUX-015/016/017, UXBUG-009/014 | Open | Headless issue/history tests passed; combined native route unrun | [operation map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | With eight finished jobs and a ninth running, Details selects/focuses the running row; append an issue and hear it. Complete a real transfer and inspect average speed in both finished info and copied report at narrow size. |
| <a id="ac-s2-019"></a>AC-S2-019 | FCUX-019/020 | Open | Windows long-path Undo and interruption passed; narrow spoken decision unrun | [Undo map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | In one narrow interrupted-operation review, choose Keep history/Cancel by keyboard and screen reader, inspect every complete inverse path in the retained report, and prove source/journal/prior report survive. |
| <a id="ac-s2-021"></a>AC-S2-021 | FCUX-021 | Open | Sync group tests passed; native interruption unrun | [sync map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Start a reviewed sync with one copy and one removal, interrupt after one child effect, Stop the group and inspect exact child states/report; no unapproved later mutation. |
| <a id="ac-s2-022"></a>AC-S2-022 | FCUX-022/023 | Open | Held connector/capacity tests passed; native disconnect unrun | [navigation map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | On a delayed/disconnected mount, navigate twice before the first reply and query free space; only the current location/volume owns the panel, stale capacity cannot overwrite it, and the error is announced. |
| <a id="ac-s2-024"></a>AC-S2-024 | FCUX-024/025 | Open | Workspace race and 40-tab component tests passed; native process race unrun | [workspace map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Race Save as on the same name in two isolated processes; require explicit replacement and no overwrite after Cancel. Restore 40 tabs/four panels at narrow scale and verify targets/focus by keyboard/AT. |
| <a id="ac-s2-026"></a>AC-S2-026 | FCUX-026/027 | Open | Horizontal/sort component tests passed; native touchpad/AT route unrun | [list map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | At 150% with enlarged row font, reveal/sort a clipped column by touchpad or wheel, change metadata order while focus stays on the same item, and hear item/column identity. |
| <a id="ac-s2-029"></a>AC-S2-029 | FCUX-029 | Open | Orca/NVDA/Mac first and repeated notices passed; scaled clipboard/admin timing unrun | [notification map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Publish a clipboard warning and elevated-administrator notice in a narrow scaled window; verify copy/history/action accessibility and timed disappearance of the admin toast without losing history. |
| <a id="ac-s2-030"></a>AC-S2-030 | FCUX-030/031 | Open | Windows basic splitter passed; nested scaled route unrun | [panel map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | At 150%/200%, resize a nested four-panel split by keyboard and pointer, retain visible list capacity and announced share, then cancel/restore exact geometry. One Windows and one Unix arrangement cover the pairwise risk. |
| <a id="ac-s2-032"></a>AC-S2-032 | FCUX-032/033 | Open | Windows filesystem ancestor menu passed; nonfilesystem spoken route unrun | [breadcrumb map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | From archive/remote tab at narrow scale, open ancestor menu by keyboard, hear full target, use Open here/new tab/Copy path, return focus to the original list and preserve newer tab intent. |
| <a id="ac-s2-036"></a>AC-S2-036 | FCUX-036/037 | Open | Windows picker/Cancel/Browse keyboard passed; protected-profile route unrun | [Settings map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | In enlarged Settings, edit a protected tool profile with spaced executable path and failed import; Cancel/reopen proves byte-exact draft preservation, then inspect toolbar availability/tooltip and spoken reason. |
| <a id="ac-s2-038"></a>AC-S2-038 | FCUX-038/039/040 | Open | Mac 3,001-row review and bounded 50,000-row headless run passed; slow-provider change unrun | [rename map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Hold device-scheduled validation while changing rename rule/tab/editor result; only newest 3,001-row mapping can be approved, last collision stays reachable, counter error is spoken and Cancel/shutdown stay responsive. Native 50,000-row repetition is optional. |
| <a id="ac-s2-041"></a>AC-S2-041 | FCUX-041, UXBUG-015 | Open | Windows/Ubuntu/Mac inline destination error passed; live race unrun | [transfer map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | On slow network target change/remove the destination during validation and correct an invalid localized speed in the same Copy form; submit only current mapping, with visible/spoken errors and no stale job. |
| <a id="ac-s2-042"></a>AC-S2-042 | FCUX-042/043 | Open | Recovery state/component tests passed; native delayed-device drawer unrun | [operation map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Pause journal preparation on slow device, resize drawer and Stop before approval; show phase/retained journal, keep active recovery row visible and hear status at 150% narrow size. |
| <a id="ac-s2-044"></a>AC-S2-044 | FCUX-044/045/046 | Open | Find scope/completion component tests passed; native provider origin unrun | [Find map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Open Find from SFTP, Registry and result set; each names/requests actual scope. At narrow size use context shortcut, stop search, distinguish no-match from incomplete and hear qualifier. |
| <a id="ac-s2-047"></a>AC-S2-047 | FCUX-047 | Open | Fractional-scroll component tests passed; native precision device unrun | [scroll map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Deliver ten 0.1-delta events, one 1.0 event and reverse deltas on a real precision device in FileList/Text/Hex/Compare; compare aggregate movement and one-row paging. Stop after one device/backend pair plus contrasting wheel check. |
| <a id="ac-s2-048"></a>AC-S2-048 | FCUX-048 | Open | Context component tests passed; native provider-safe route unrun | [context map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Right-click blank space on read-only archive and disconnected/virtual view, then keyboard-open same menu; only location-safe actions appear, marks/focus persist and captured location is announced. |
| <a id="ac-s2-052"></a>AC-S2-052 | FCUX-052/053/055 | Open | Local refresh/zoom/quick-view tests passed; remote rollback unrun | [viewer map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Change archive/remote source during slow Quick View/Viewer refresh; keep old snapshot until complete, show stale/failure on interruption, preserve valid explicit zoom and offer reachable Cancel/Retry/full viewer in small pane. |
| <a id="ac-s2-054"></a>AC-S2-054 | FCUX-054 | Open | Parser/layout tests passed; native Go to unrun | [Go to map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | In native 480×300 viewer enter invalid then valid offset/line by keyboard and pointer; hear inline reason, retain draft, disclose nonseekable remote-line limitation before scanning. |
| <a id="ac-s2-056"></a>AC-S2-056 | FCUX-056/072 | Open | Compare/Hex range component tests passed; native range route unrun | [compare map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | At 640×320 select/Copy a changed source line and binary byte range through pointer/keyboard, then Find/Next; verify exact clipboard bytes/text and native AT range/status speech. |
| <a id="ac-s2-057"></a>AC-S2-057 | FCUX-057 | Open | Privacy wording tests passed; native manual check unrun | [Settings map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | At enlarged text hear automatic versus manually requested update/network wording, toggle automatic checks, then make one explicit manual check without implying the toggle blocks it. |
| <a id="ac-s2-md"></a>AC-S2-MD | UXBUG-001/002/004/008/012 | Open | Native Page search/resize passed on Windows, Ubuntu and Mac; residual AT/scale route unrun | [Markdown/search map](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | At one untested scale with a wide table and narrow viewer, keyboard/screen reader traverse table-local scroll, prose reflow, Find status, Previous/Next and Mac Page focus return. Windows/macOS exact totals are optional until a native count API exists; truthful lower bounds passed. |
| <a id="ac-s2-window"></a>AC-S2-WINDOW | UXBUG-005/007 | Open | Windows maximize/restore and Registry/Advanced/Ignored resize passed; display-change unrun | [window history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | Move viewer and one Find/Registry form between two display/DPI settings, resize/close/reopen and verify normal bounds, input reachability and spoken resize help. |
| <a id="ac-s2-unix"></a>AC-S2-UNIX | UXBUG-006 | Open | Windows NTFS stream deletion passed; Unix attribute unrun | [Streams history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | On Ubuntu or macOS delete one disposable extended attribute through FileCat; parent count updates on return without reload and sibling attributes stay. |
| <a id="ac-s2-keybar"></a>AC-S2-KEYBAR | UXBUG-018 | Open | Ubuntu 200% physical-key state passed on `bdb618d9`; popup/Find visual unrun | [modifier history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | Capture Shift+F10 popup and Alt+F7 Find at 200% while key-bar layer returns to normal after dismissal; verify no clipped actions. This is visual closure, not a rerun of modifier logic. |

FCUX-028's Mac 1× inline Filter, Windows scaled Filter and Ubuntu scaled
Filter/Orca routes are accepted; there is no Filter row here. FCUX-034/035
are accepted Settings safety subcases. Their dated results stay in the
implementation status.

## S3 — focused usability routes

| Stable scenario | Finding | Status | Tested build and environment | Evidence | Exact remaining requirement |
| --- | --- | --- | --- | --- | --- |
| <a id="ac-s3-058"></a>AC-S3-058 | FCUX-058/074, UXBUG-013 | Open | Toolbar/Places tests passed; native four/six-panel pinning unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | In four/six panels at 150%, pin/unpin a place and open Help from command search by keyboard; inspect More, complete locations and spoken action names. |
| <a id="ac-s3-059"></a>AC-S3-059 | FCUX-059/060 | Open | Density/hierarchy component tests passed; enlarged native route unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | At minimum and maximized size switch Compact/Comfortable and enlarged content font; viewer/editor Save, Find, More and modes stay reachable by pointer/keyboard/AT. |
| <a id="ac-s3-061"></a>AC-S3-061 | FCUX-061/062 | Open | List/tab tests passed; native provider collision unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | With same-named local/archive/remote tabs and a clipped value, inspect full value by hover/keyboard, drag/switch tabs and hear distinct provider/lock identity. |
| <a id="ac-s3-063"></a>AC-S3-063 | FCUX-063 | Open | Settings/column tests passed; native disabled-help unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Disable a parent option and reach subordinate field/column limit by pointer/keyboard/AT; retain draft and hear specific disabled reason. |
| <a id="ac-s3-064"></a>AC-S3-064 | FCUX-064 | Open | Geometry tests passed; native mixed-monitor route unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Maximize/restore/move main window between monitors/DPI on Windows and one Unix manager; reopen at valid normal bounds without trapped controls. |
| <a id="ac-s3-065"></a>AC-S3-065 | FCUX-065/066 | Open | Workspace/tab guard tests passed; native one-panel route unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Restore valid one-panel workspace, attempt F5/F6 and last-tab middle-click/keyboard close; explain missing target, preserve history and allow continued work. |
| <a id="ac-s3-067"></a>AC-S3-067 | FCUX-067 | Open | About tests passed; native clipboard/AT unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | Copy support details and open displayed settings/data folders from About by keyboard/AT; dialog stays until Close and copied paths match display. |
| <a id="ac-s3-068"></a>AC-S3-068 | FCUX-068 | Open | Queue-order tests passed; native timing unrun | [S3 map](UX_UI_IMPLEMENTATION_STATUS.md#s2-continuation) | With one running and two queued jobs, move later queue item earlier by pointer/AT; explain disabled movement for first/fixed sync child and preserve execution order. |

## Queued defects and audit-wide integration

These are separate from the original 76-finding scope. A new defect does not
reopen an unrelated accepted audit scenario.

Queued UXBUG-003 (PE thumb freeze), UXBUG-017 (cropped scaled icons) and
UXBUG-020 (Hex resize/scroll selection) have passing dated native results in
the implementation status and no current required rerun. UXBUG-018's
modifier state also passed; only its distinct popup/Find visual row remains.

| Stable scenario | Finding | Status | Tested build and environment | Evidence | Exact remaining requirement |
| --- | --- | --- | --- | --- | --- |
| <a id="def-019a"></a>DEF-019A | UXBUG-019 | Open | VM `663b6fdd` strict plus native exact-byte recovery passed; owner's G: unrun | [26 Oct VHD history](UX_UI_IMPLEMENTATION_STATUS.md#changes-and-verification-map) | If isolated non-workstation input is available, repeat owner's original elevated G: admission and off-source recovery; verify no false “other FileCat processes” refusal, exact bytes and no G: write. VM acceptance stays valid without this reconfirmation. |
| <a id="gate-ci-final"></a>GATE-CI-FINAL | Audit-wide | Open | `663b6fdd` five lanes green; `49041bd3` and docs-only `d2c14636` running on 26 Oct | [green CI](https://github.com/benny-cz/FileCat/actions/runs/40390006844) | After last implementation change require producer, Windows, Windows ARM64, Ubuntu and macOS CI green on that exact code head. Documentation-only commits do not invalidate passed native scenarios. |

## Deliberately outside required closure

- A second **native** 50,000-row rename run after bounded 50,000-row
  headless and 3,001-row native collision tests; repeat only for a measured
  performance regression. A redesigned full search-results surface is
  optional after complete and truthful current navigation.
- Exact Page totals on WebView2/WKWebView while their native interfaces only
  support a directional lower bound. Retain the truthful label.
- A Cartesian product of platforms, palettes, scales, providers and inputs.
  The audit calls for pairwise coverage and focused risk combinations and
  accepts an equivalent route with the same consequential outcome.
- A separate SFTP server that does not advertise atomic rename. The current
  `TryReplace=false` adapter exercised the same non-atomic product branch over
  real OpenSSH file operations and four consequential server-response faults;
  live SFTP interruption/recovery review passed separately. A different
  server implementation is a useful interoperability refinement, not another
  FCUX-001 completion gate absent a failure with the accepted branch.

## Repeat authorization record

Before repeating a passed scenario, record its stable ID, the relevant
implementation/dependency change, new regression, invalid evidence,
uncovered required boundary, or bounded reliability investigation, plus a
stopping condition. An open parent finding or unrelated/documentation-only
commit is insufficient.

| Date | Scenario | Reason and stop |
| --- | --- | --- |
| 26 Oct 2026 | DEF-016A | First new Mac narrow Copy X click was intercepted by an unrelated iCloud/FaceTime alert, making that result invalid. After clearing the alert, one controlled retry on the same signed build passed X and backdrop cancellation with unchanged fixture (`mac-dialog-pointer-copy-narrow-be7-retry-20261026.json`). Investigation closed; no broad pointer rerun authorized by that invalid result. |
