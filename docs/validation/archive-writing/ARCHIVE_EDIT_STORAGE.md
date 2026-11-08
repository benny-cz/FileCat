# Retained edit-session storage checkpoint

This component is in progress and unregistered. The application still constructs the legacy store;
the native factory is used explicitly by the platform tests. The complete plan, all-platform acceptance,
and release qualification remain open.

`03ec2201` adds retained native directory authority and bounded visible recovery records. `df4ec125`
fixes the Darwin ARM64 variadic creation-mode ABI and adds exclusive new-record publication.
`6b7a80a3` connects private native session creation, an interrupted-creation marker, persisted directory
identities, exclusive immutable state generations, stale/concurrent-save rejection and complete bounded
revision-chain review. `c68ec836` adds held working-file reads with fresh native name/version checks,
legitimate atomic editor replacement, private Windows ACL inheritance and native storage schema 3.
Existing schema 1/2 records remain readable without rewriting them merely by listing.

`b35d5992` fixes Ubuntu's normal editor replacement under umask 0002, preserves schema 3 after
verified archive acknowledgement, keeps unreadable interrupted-creation markers visible, and bounds
native modification-time conversion. The Unix adapter tests cover all ten format/password combinations.

The initial `session.json` stays intact. New state records claim the next fixed generation without
replacement and link to the prior record's SHA-256. Every generation is reviewed, with limits of
4,096 revisions and 32 MiB of aggregate metadata. An unknown version, missing generation, damaged
ancestor, stale caller or uncertain publication retains the working folder for review. These hashes
detect inconsistent state; they are not authentication against a process with equivalent user authority.

On Windows, new directories have protected owner/SYSTEM ACLs whose permissions inherit to normal
editor-created files. File admission accepts only those two principals, a single regular non-reparse
file and the expected retained parent namespace. On Unix, directories remain creator-owned 0700;
normal editor replacement may produce ordinary read/write permission bits through 0666 within that
private namespace; the retained 0700 directory prevents other users from traversing to the file. Working
admission checks owner, type, mode, link count, native metadata/version and the current relative name.

## Evidence

The [checkpoint](edit-storage-checkpoint.json) preserves thirteen verified bundles, 219,524,260 bytes,
including every initial failure. The static verifier binds Git source bytes, compiler snapshots,
DLL/PDB checksums, test results and final process censuses:

```powershell
python -B -X utf8 eng/validation/archive-writing/verify_edit_storage_checkpoint.py
python -B -X utf8 eng/validation/archive-writing/verify_edit_storage_checkpoint.py --platform mac --folder awe-storage-v5
```

| Focused gate, `b35d5992` / v5 | Windows | Mac | Ubuntu |
|---|---:|---:|---:|
| Bounded record/recovery parser | 19 | 19 | 19 |
| Native private-directory primitives | 16 | 16 | 16 |
| Native session storage and working reads | 14 | 19 | 19 |
| Connected edit/session/password Core selection | 100 passed / 10 Unix-only skipped | 110 | 110 |
| Connected application edit selection | 190 | 190 | 190 |

The focused packets bind 22 changed C# inputs and 15 DLL/PDB pairs on Windows, and 20 inputs and
12 pairs on each Unix platform. They are not complete Core or native desktop acceptance runs. Every
final census has zero owned instances and the Mac preserves wake helper 36765. Windows v5 was
initially refused before source application by the sharing check when another tester resumed. That
refusal is preserved; the identical frozen packet later completed successfully when the VM was idle.

The initial Windows lane used an inaccessible administrator-profile fixture; subsequent test roots
use the standard account's own temporary directory. Darwin's native creation mode required the ARM64
variadic ABI correction. Remaining early failures covered exact invalid-Unicode exception shape and
unnecessary Windows directory DELETE access. Mac `awe-storage-v3` passed all native gates but exposed
two old assertions that expected oversized records to be hidden; the corrected assertions require
visible readonly recovery and refusal of Save/Discard.

Ubuntu v4 passed 13/14 storage cases and rejected the normal atomic editor save: its inherited umask
0002 creates a group-writable replacement. The actual v5 recipe passes that case and explicit 0640,
0664 and 0666 variants while still refusing a working directory changed from 0700 to 0755. Both Unix
platforms also pass interrupted-marker substitution and all ten native-store archive acknowledgement
cases. This validation does not qualify native snapshot cleanup or archive publication.

## Remaining implementation and qualification

Native immutable commit/save-copy capture, exact owned snapshot cleanup, native discard/new-child
reconciliation, old-record adoption and application composition are still required. The current
legacy snapshot and recursive discard implementations do not establish these native ownership gates.
Do not enable this factory or new write profiles before those operations are connected and qualified.

Follow-up `cc5ea3a9` makes repeated commit-copy disposal return before touching a reused pathname and
adds a replacement-preservation regression. It compiles cleanly; live validation is queued with the
next snapshot packet and is not part of this frozen v5 evidence. Include `EditCommitCopyTests` in that
packet's focused selector; the current `EditSession` selector alone does not select that class.

Complete directory/record/artifact substitution, interrupted creation/publication/cleanup, editor
concurrency, origin/metadata, cancellation and restart cases on all three platforms. Then rerun complete
regressions, actual native editor/UI/accessibility acceptance, Unix mutation/confinement and the
remaining fuzz/resource/RID/package gates before the independent final review. Optional XZ/ISO work
retains its existing plan priority.
