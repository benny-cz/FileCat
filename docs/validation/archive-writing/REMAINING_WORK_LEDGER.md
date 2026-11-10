# Archive writing remaining-work ledger

Dated 10 November 2026, branch `codex/archive-writing`, updated after the v14f/v15 candidates (AW7, AT18, cross-job admission, worker ZIP). This is a working queue derived
from the plan, [the implementation status](IMPLEMENTATION_STATUS.md) and the checkpoint records. It does not
qualify anything; each item closes only with its own evidence entry in the status document. Update the
"State" column when an item moves. Nothing new is registered in ordinary application startup.

## Platform position

| RID | Worker boundary | Existing-archive update | Recovery/cleanup | Current route decision |
|---|---|---|---|---|
| win-x64 | LPAC + job object, development evidence under a real standard account | NTFS TxF same-object update, crash/restoration matrices | Object-bound NTFS cleanup and review UI | First candidate for enablement once its remaining gates close |
| linux-x64 | cgroup/seccomp/Landlock lane exists but is not wired into the pipelines | Exchange primitive only (`b75e6dfd`) | Exact unlink refused; no Unix recovery adapter | Candidate after Unix replacement, confined roles and package routes |
| osx-arm64 | No enforceable hard memory ceiling (RLIMIT_AS EINVAL, footprint setter KERN_NO_ACCESS, VM provider above 1 GiB) | Same as Linux | Same as Linux | Not qualifiable under the current plan wording; stays read-only unless the owner re-scopes the memory gate |
| win-arm64 / linux-arm64 | Never run on real hardware (linux-arm64 only inside the Mac VM guest) | — | — | Disabled: no test machine |

## Work queue (in execution order)

| # | Item | Plan refs | State |
|---|---|---|---|
| 1 | Unix replacement transaction: two-parent journal facts, outer metadata application (mode/gid/ACL/xattr/flags/birth where representable), exchange integration, reconciliation, recovery classification, process-kill matrix | §7.1–7.6, AW2, AT06–AT10 | Implemented and passing on Mac/Ubuntu (16 kill points); preparation-phase crash cleanup open |
| 2 | Unix cleanup policy: exact-content destruction through held descriptors, name removal limited to the creator-private directory with pre/post identity checks, honest retention otherwise | §7.6, INV-19, R04, R21 | Decided ([record](UNIX_PRIVATE_CLEANUP_DECISION.md)) and implemented for update and creation transactions (`8872044c`); an unpublished transaction (failure or cancellation before publication) is discarded exactly, live by the job and through reviewed recovery after a crash, on Unix and Windows (`38e56944`) |
| 3 | Production Unix job scope (create/update/move) and Linux confined worker roles wired into all pipelines | AW1, AW2, §10.2 | Update scope, application worker roles and recovery review done (`48f5ed22`); creation in the private folder done (`8872044c`); confined Linux application workers on every pipeline (`2acacfe3`); F6 move into native Unix archives with rename-aside source removal (`38e56944`) |
| 4 | R21: legacy edit-session cleanup by pathname (Windows bound deletion, Unix retain-and-report) | R21, AT10/AT14 | Legacy cleanup sites bound (`38e56944`): Windows deletes through handles bound to the recorded identity or verified directory handles, Unix only in folders no other user can change while the name binds the recorded identity, anything else kept and logged. Production composition of the native edit store (adoption, active/retained routing, old-folder retirement) remains open: [boundary](LEGACY_EDIT_ADOPTION_BOUNDARY.md) |
| 5 | Re-run never-executed Ubuntu queued checks (marked creation `05a86d83`, exact-origin update stage `320f43c6`, restoration preparation `aee8a384`, AW0/R22 benchmark) and a current full Ubuntu regression; soak the unexplained worker-monitor refusals | AT19, R22 | Open |
| 6 | Worker-backed ZIP transform and global (cross-job) budgets | AW2 task 5, §9.2 | Cross-job bounds done: one archive worker at a time (eight waiting), 16 input leases, and one profiled archive job preparing at a time ("Waits for … archive changes are made one at a time"). ZIP worker profile implemented ([design](ZIP_WORKER_PROFILE_DESIGN.md)): strict catalog, raw copy of unchanged members, descriptors for new payloads, fresh verification, `zip-archives` fuzz target, legacy comparison; native jobs through the shared matrix. Open: its three-platform evidence, and switching the ZIP provider and UI to it when a ZIP route is qualified |
| 7 | Missing fuzz targets FZ04–FZ08, FZ10–FZ12 and write-side targets; FZ09 parser floors; per-family 24 CPU-hour and shared 24 CPU-hour totals on the final candidate | §13.3, §13.6 | Open; read-side floors and three-RID structured replay exist |
| 8 | UX completion: operation-center phases and "Finishing safely", all-surface command matrix, §11.7 microcopy, restoration action, native edit-store composition, member/edit edge cases | §11, §12 | Phases, "Finishing safely", §11.7 microcopy (including a full drive), combined review, existing-ZIP question, pack estimate and "Open archive" done (`2acacfe3`); A4 usable operation center during a scan, A16 "Show working copy" and B8 read-back done (`38e56944`); open: the C runs ([UX_GAP_AUDIT.md](UX_GAP_AUDIT.md)) and the native edit-store composition (row 4) |
| 9 | Accessibility and rendering: NVDA, Orca, VoiceOver; 100–200 % scaling, 640×480, themes; 50 ms UI budget | §11.8, AT16 | Create dialog at 640×480 with 100–200 % text: actions inside, long path contained, focus reveals the last field (`ArchiveDialogViewportTests`). Runners ready: `win-nvda-archive-writing.ps1`, `linux-orca-archive-writing.py` (qualification host). Open: their runs, themes and the 50 ms budget measured live. macOS VoiceOver for writing is moot while macOS writing cannot be qualified (platform position) |
| 10 | AT08 disk-full/partial-IO faults, AT18 scale/resource measurements | AT08, AT18, §9.2 | AT08 on real small volumes on all three platforms (APFS image and ext4 loop `2acacfe3`, NTFS VHDX `38e56944`): the space review refuses before writing, a drive filled mid-rebuild fails cleanly with the original intact and the unfinished files discarded; an injected stage write failure is a typed refusal. Open: destination denial and quota on a real volume, close/flush failure on a real volume. AT18: the limit run passes on Mac and Ubuntu after the review in [AT18_SCALE_REVIEW.md](AT18_SCALE_REVIEW.md) (Mac create 518 s, update 151 s), and the payload extremes pass on the Mac; Windows numbers, a retained-heap figure and Ubuntu payloads open |
| 11 | Documentation: README, ADR-07/06/01 amendments, help text, release notes, downgrade note | §2.2, §16.2 | ADR-07 facts corrected and proposed amendments recorded in ADR-07, ADR-06 and ADR-01 (not decided). README, CAPABILITIES and help stay accurate (only ZIP writable) until a route is qualified; the text to publish then, with the downgrade note, is in [RELEASE_TEXT_DRAFT.md](RELEASE_TEXT_DRAFT.md) |
| 12 | Packaging: per-RID SBOM/notices on actual payloads, worker packaging, installed/portable candidates, release manifest | §4.4, §16.1, AT20 | Open (development payload validators pass) |
| 13 | Availability composition: register only qualified format/RID/filesystem routes with typed unavailable reasons | §16.1, §16.3 | Mechanism done: `ArchiveWriteRoutes` composes only listed format/runtime/file-system routes, each with typed refusals, rechecked at execution; the list is empty. The qualification host composes every backend for interface runs. Open: the per-route qualification decisions |
| 14 | Final independent UX/UI/function/security review, fixes and re-validation | Status "Required independent review" | Open |
| 15 | AW7 standalone GZ/BZ2/ZST (release R3): one-file compression, explicit sole-payload replacement and edit commit | AW7, §8.5, §11.6 | Implemented ([rules](STANDALONE_COMPRESSION_DESIGN.md)); the review of an existing compressed file admits its one payload directly (fixed after v14d). Passes the Mac v14f full run and the real Unix worker UI test; Windows and Ubuntu evidence with the next candidate; fuzz target `standalone-streams` |

## Known gaps that are not implementation tasks

- 7z: Deflate64 encoding, plugin methods, solid, multipart and SFX output are not offered; header coder range limited.
- RAR5: no RAR7/large-dictionary, filters, legacy encoders, solid or multipart output; AW10b/AW10c fallbacks were never
  activated because AW10a reached a technical go.
- TxF availability must be checked per volume and refused cleanly; Microsoft documents TxF as possibly removed in future.
- Power-loss durability is not claimed anywhere (INV-20).
- Unix publication is an atomic exchange, not compare-and-swap; a late different occupant is retained and reconciled.
