# Windows transactional content feasibility, 6 November 2026

Native investigation over `09a70c13`; **not a production backend or release qualification**. No write profile or
existing source-lease sharing contract changed. This evaluates a possible way to preserve security state that an
ordinary reader cannot fully capture, while satisfying the [archive-writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

## Measured Windows behavior

All 12 final owned-file probes passed on the disposable Windows 11 x64 NTFS guest, .NET 10.0.14. The controlled
launcher recorded integrity 12288 before / 8192 after and exit zero; this is not standard non-admin-account proof.

- Pending transactional data remained invisible to normal readers. Commit exposed the new complete bytes; rollback
  and closing an uncommitted transaction retained the original bytes and mtime.
- File identity, creation time, attributes, origin and the ordinary security snapshot remained preserved across data
  updates. Updating base data and increasing the existing Zone.Identifier declaration became visible together on
  commit; rollback retained both previous streams.
- A prior nontransactional writer prevented opening the transactional writer. While locked, the file rejected an
  external origin-stream writer and new hard-link creation; a successful pre-transaction hard-link control preceded
  that refusal.
- The ordinary deny-write source lease could **not** be opened while the transaction was pending, even after closing
  its data-write handle. The first attempt failed with a sharing violation; that result is retained and tested as
  a required refusal rather than weakening `WindowsArchiveDirectory.OpenSource`.
- A read-only handle opened **inside the same transaction** with `SHARE_READ` successfully bridged commit. While it
  remained open, the normal deny-write source lease could be acquired after commit and an outside writer remained
  blocked. This provides a candidate pin transition for later publication integration.

These observations agree with Microsoft's [TxF isolation and locking description](https://learn.microsoft.com/en-us/windows/win32/fileio/txf-basic-concepts).
They do not prove crash durability, filesystem-wide support or a complete archive transaction protocol.

## Audit/security evidence

The validation fixture helper gained an explicit audited-fixture mode. It creates three private owned files with
audit, resource-attribute and scoped-policy ACEs, then records bounded descriptor hashes using an elevated observer.
The observer explicitly requests owner/group/DACL/audit SACL/label/resource/CAP (`0x7f`) and verifies that an audit ACE
is actually returned. An initial backup-information-only query returned access denied; no weaker production query or
absence inference was added.

The medium transaction tests update only data through handles without `ACCESS_SYSTEM_SECURITY` or privilege-enabling
code. A separate privileged observation after commit, rollback and abandonment matched all three seeded security
descriptor hashes exactly. This demonstrates preservation of the seeded audit/security state; it does not qualify
every possible policy, encryption, alternate stream, storage filter or security descriptor.

The helper's normal non-audited mode also passed its regression: 14 existing metadata cases passed with the 12 TxF
cases explicitly skipped when opt-in was absent. Both Mac and Ubuntu compiled the Windows test project successfully;
all 26 metadata/probe cases took their non-Windows skip guard. These are compilation/guard checks, not Unix TxF claims.

## Candidate integration and remaining requirements

An in-place transactional update could preserve the original object's unreadable security fields by avoiding security
replacement altogether. It must remain a distinct typed publication recipe, not a reinterpretation of the current
retired-object facts. Before integration it requires:

- A verified private output and an exclusive, checksummed original-data snapshot retained before commit.
- Held parent/volume admission, exact original identity/version/origin rereview after acquiring the transactional
  lock, and a transaction GUID/phase recorded before any commit attempt.
- Complete all-input origin aggregation and transactional mark update, bounded copy/readback, cancellation and
  actual rollback/commit-state handling, and the measured transaction-read-pin transition.
- Honest retained-artifact/uncertain outcomes, exact-object recovery rules, ordinary-user and filesystem qualification,
  crash/power-loss/resource/alias probes and integration into the full application/worker pipeline.

Existing hard links cannot be silently updated through an in-place strategy. The complete hard-link requirement
remains open; no eligibility or product promise is established by the single-link probes. The plan's other native
replacement strategies still require implementation and qualification.

Microsoft [recommends alternatives and cautions that TxF may disappear](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createfiletransacteda).
Any eventual use must be explicitly qualified on the held volume and refuse when unavailable, with no unqualified
overwrite fallback. This investigation does not make TxF a required cross-platform dependency or enable it in FileCat.

## Retained evidence

Ignored `.tmp/test-results/archive-writing/archive-txf-v1` and the task-owned Windows guest retain initial failures,
TRX, launch facts, seeded before/after descriptors and the independent observer result.

- Source/test/helper delta: `C489FAC3181E644E80A3B082C285A649C5283689BCCA3D5F869D7760835CE8D4`.
- Final test assembly overlay: `E199B40BB1299E8979B0CF6570AD0195FC26F17A2CA3659AAC016C29582E63B4`.
- Final native/audit evidence: `D71FCC95442CC2985D23CCB14478186143B19666D400463141EC478838525773`.

All test-created FileCat processes exited. The implementation goal remains active, including full metadata/publication/
recovery work, the remaining engines and platform gates, subsequent RAR work, then independent review.
