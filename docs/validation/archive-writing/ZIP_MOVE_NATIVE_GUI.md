# Native ZIP move follow-up

This follows `b8077b68` and `LOCAL_MOVE_INTO_ZIP.md`. It fixes defects found while driving the actual desktop app.
The existing ZIP route remains the only enabled archive writer; no new profile or AW packet is qualified here.

## Fixes found by desktop testing

Editing an existing archive target from `destination.zip` to `destination.zip/renamed.txt` previously fell through to
filesystem interpretation. The dialog promised to create a filesystem folder below the archive file. Both affected
native runs were canceled before starting that transfer; the original data stayed intact.

The edited destination now keeps the selected ZIP context. A device worker parses the archive path and checks its
index for explicit or implicit folders. A single item without a trailing separator maps to a chosen member name when
its archive parent exists. Existing folders, explicit new-folder paths and multiple-item folder targets keep their
folder meaning. A file blocking a folder and nested read-only archives receive errors. The accepted interpretation is
captured in the transfer draft and used by submission. Ordinary filesystem-target interpretation retains its existing
route. This is advisory legacy-ZIP preparation, not the new formats' complete capture/admission boundary.

The archive conflict dialog previously focused/defaulted to Replace. Its default is now Skip existing, so Enter cannot
silently replace old member data. Explicit Replace remains available and was tested separately. The single-conflict
explanation and move summaries now use correct singular nouns, including `1 file` and `1 source item was`.

## Automated checks

`ArchiveMoveUiTests` now has 17 cases: ordinary and queued F5/F6 jobs; chosen member names, payloads, deletion and pane
refresh; safe conflict Enter and explicit replacement for copy and move; and seven destination-mapping cases covering
implicit folders, existing files, new folders, multi-item targets and nested archives. Fourteen cases were added to
the existing three. The neighboring preparation, destination-validation and scope suite totals 85 cases.

| Platform | Core/native real-file checks | Headless UI/preparation/destination/scope |
|---|---|---|
| Windows 11 VM | 13 passed/28 skipped core; 19 move and 18 adjacent native checks passed/1 skipped | 85 passed |
| Mac arm64/APFS | 40 passed/1 skipped | 85 passed |
| Ubuntu x64/ext4 | 40 passed/1 skipped | 85 passed |

The Windows suites used .NET 10.0.14 and the existing medium-integrity harness. Every launch record reports 12288
before/8192 after and exit code 0. The fresh `move-gui-results-v1` directory retains `v6` in its Windows TRX/facts
basenames because of the runner template; these are the new 85-case results, not the previous 71-case run. Host Windows
also passed the 17 focused cases and the full 85-case neighboring suite.

## Native desktop journeys

Ubuntu was driven through its actual X11 window with native keyboard events, and observed through authenticated
VMware framebuffer captures. The first run exercised F6 file move, F5 copy, conflict skip/replacement, Escape,
extra-xattr retention and Queue on a folder containing a nested file and an empty directory. The fixed build replayed
file move, conflict Enter and the chosen member name. The corrected name preview and final `renamed.txt` are visible
in its retained screenshots. The completed job summary shows `moved 1 file and 0 folders`.

Mac was driven through native keyboard/accessibility controls. The fixed build completed file move, F5 copy, safe
conflict Enter, explicit Replace, chosen member name, Escape after editing that name, additional-xattr retention and
Queue on the nested/empty-folder tree. The source pane refreshed and focused the remaining row. Accessible names,
field values, button states, mapping text and final pane contents are retained as native AX observations. The folder
job reports `moved 1 file and 3 folders`.

Python's independent ZIP reader checked CRCs, exact payloads and member names against the actual source namespace.
The fixed-run verifier also checked checksummed journal records, the selected source paths and terminal job states.
Skipped inputs and the extra attribute remained; successful move sources disappeared; copies remained. Escape kept
the archive's whole-file digest unchanged. The Queue button was used with no competing job active; queue-behind-active
operation behavior is not established by these native journeys.

One Mac post-action focus query returned no element after Enter. The move was not retried: fresh ZIP/source and
terminal-journal observations proved it had already completed. The driver was changed to report that observation
state explicitly and to activate the owned app before each key. An earlier foreground query was malformed; the
corrected query showed System Settings, so the earlier Mac-lock inference is not acceptance evidence.

The host Windows desktop helper could not activate VMware or the task-owned FileCat window and returned unusable
captures. That attempt counts as no native Windows GUI acceptance. Its exact owned instance was closed; its fixture
archive digest stayed unchanged. Windows native desktop acceptance remains open. Mac screenshots were unavailable
through SSH (`could not create image from display`), so the Mac evidence establishes keyboard/AX/functionality rather
than visual layout acceptance.

Ubuntu's session/display were restored through its native session and compositor APIs when idle lock/display power
prevented capture. Its temporary idle inhibitor was stopped after testing. Both native app PIDs and all guest test
processes were checked for exit. The Mac's existing sleep assertions were retained; a task-owned temporary loop
refreshes user activity every 30 seconds until reboot. The final power snapshot and helper process observation are
retained. No permanent lock/security preference was changed.

## Retained evidence

Raw evidence is in ignored `.tmp/test-results/archive-writing/move-gui-v1` and the task-owned guest directories.
Fixture launch records identify actual app/core hashes and OS versions; the source overlay is the five-file delta
over the preceding source checkpoint. Native runs do not establish reproducible release/package identities.

- Fixed source overlay: `53159E85E95CD1F0F410C40BFCE7303B5C9943D4B0FDAABB1BEB99C0248FF0B4`.
- Windows managed overlay: `1CAC1989E049AF7F30C491A0C89F28708F8312AD114E5AD2B29F1C4FD12A76F4`.
- Windows fixed-run evidence: `608031B50C7023182628B2549F43359A55AB1E65DD1742AF40BA9A1A5A7C5738`.
- Mac final native/regression evidence: `17E04F3D31569E5D316B35A55CDCE7A5CB7D8CB778AC1993D9AD44A4342DC947`.
- Ubuntu final native/regression evidence: `29F425D3BD7E9206FD3D5CADB15A95E6BBC1D004A6FB28E273032BDB64B905B5`.
- Mac initial native evidence: `6CE385F0F40239D71BDF007E66C8094982FE140F07968F6A49A52BA61E27660E`.
- Ubuntu initial native evidence: `89D28781AA78E4BE459E53D6FE81968553B8C5DC5DBCA4DB9F4D329659E0CEE0`.
- Ubuntu incorrect-name preview image: `438DBCB2FF2BC42A5E4B2DE885DD62082B12B39D4DD7AF97CBC5266319C8EF8D`.
- Ubuntu corrected-name preview image: `A3549292DAFC004132B355DBB009F9117B0255B0F8F3CE3AAF11C6BB0F877683`.

The refreshed win-x64 development application's SPDX inventory is retained at ignored
`.tmp/test-results/archive-writing/move-gui-artifacts-v1/application.spdx.json`, SHA-256
`208059ED3951D4C0E6F19ADF7E296CC8701211C17E6D65CC6B7035D97E252073`. All 261 file hashes were independently rechecked;
34 reviewed NuGet packages and one runtime pack contribute 235 files matching their reviewed archives. No dependency
was added or upgraded. Its explicit source state is base `b8077b68b2fc13df0f1724d67abed5f2c2a9c829` plus dirty-state tar
SHA-256 `4f53e05851bae2de35c040bffcefcc3e5d9d38cf5ec0b572ec7bb9fd5a6b4db7`. It does not assert a reproducible commit
identity. Final report edits follow the capture and do not change the tested runtime inputs.

Native Windows GUI, complete Mac visual acceptance, scaling/viewport/accessibility matrices, full metadata/namespace/
crash/scale qualification, remaining formats and production integration are still required. The complete implementation
loop, subsequent RAR phase and final independent UX/UI/functional/security review remain unfinished. Keep the goal active.
