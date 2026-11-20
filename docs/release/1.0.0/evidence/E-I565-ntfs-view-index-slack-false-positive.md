# I565 — NTFS view-index slack false positive

**Status:** product correction committed at `3a326cb06c08a278fcb47e5af92972344d491a08`; exact pushed-source CI run [41099849377](https://github.com/benny-cz/FileCat/actions/runs/41099849377) passed all five required jobs. Later documentation-only run [41100432774](https://github.com/benny-cz/FileCat/actions/runs/41100432774) also passed them. This is source-level evidence, not final-candidate qualification.

## Failure and consequence

Main [run 41092392016](https://github.com/benny-cz/FileCat/actions/runs/41092392016) at docs-only `1c562b56` passed the producer, Windows x64, Windows ARM64 and macOS jobs. Ubuntu Core had **1 failed / 4,905 passed / 120 skipped**: `NtfsViewIndexTests.Entries_left_behind_are_carved_and_copies_of_ones_in_use_left_out` at line 132 expected one entry and received two. The unexpected carved object-ID entry had record `640965537441`, sequence `19076`, and `InSlack=true`. The artifact-retention step also failed only because the preceding test failure left its optional directories absent. Tag-only package and draft jobs skipped on main by design. The immediately later docs-only run at `229d4c96` passed Ubuntu, which establishes intermittence, not a fix.

The last-entry marker in an NTFS `$O` view-index node can precede a complete leftover entry for a system record. The old carver rejected that complete entry because its record number was below 16, then interpreted the first 16 bytes of its *intact header* as a headerless object-ID key. Depending on the following GUID bytes, this fabricated an apparently plausible extra recovered record. It could misreport deleted-file metadata; no source write or recovery output was observed in this failure.

## Bounded correction and controls

The source carver now recognizes a structurally complete leftover entry independently of its record plausibility. It advances over the entry's declared length, adds it only if plausible, and tries the headerless-key path only when no complete entry is present. This also advances over the actual 96-byte subnode form rather than assuming 88 bytes.

The regression fixture uses a fixed object ID whose first eight bytes create a plausible fake reference under the old code. On Windows x64 Release, the focused test **failed before the product change**, returning the live entry plus false record 100/sequence 0. After the change, all seven `NtfsViewIndexTests` passed (7P/0F/0S). Existing controls in that class still cover genuine headerless `$O`/`$R` entries, complete leftovers, copies of live entries, damage and bounded malformed inputs.

## Stop and remaining gate

The exact pushed-source run 41099849377 and later docs-only run 41100432774 passed producer, Windows x64, Windows ARM64, Ubuntu and macOS. Tag-only packaging and draft jobs skipped by design on main. Retain I565 as remediated at source scope and leave installed/frozen-candidate NTFS recovery qualification under REL-I106-RECOVERY. The later I566 clipboard change is a documented consequential reason to run a new exact-source CI gate; it does not reopen the NTFS-focused scenario. Do not rerun passed unrelated scenarios without another consequential trigger.
