# Journaled NTFS content update integration

6 November 2026. This is an **unregistered implementation**, not release qualification. New writer profiles remain
disabled. It advances the [native feasibility probes](WINDOWS_TRANSACTIONAL_CONTENT_SPIKE.md); it does not complete the
[archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

## Implemented behavior

`WindowsArchiveFile.ContentTransaction` consumes the reviewed original's normal deny-write source lease and transfers
its counted parent lifetime into an explicit, finite native transaction. The held local NTFS volume must advertise
transaction support. Native identity, bytes, queried security metadata and origin must still match the reviewed
original after the transactional open. No normal source lease's sharing contract has been relaxed.

A separate creator-private snapshot retains and verifies all old archive bytes before private mutation. A failed
snapshot attempt cannot retry over a partially claimed artifact. The snapshot receives the original's declared zone
in canonical form; complete original mark bytes and URLs remain in the scoped provenance evidence. Its stream
inventory is checked. This is an old-data copy with captured evidence, not a clone of all original filesystem metadata
or permission to restore a pathname.

The output must be a separate sealed native stage from the confined transform and fresh verification pipeline, bound
to the exact original observation. Preparation, input and sidecar observations are rereviewed; complete ordered
provenance is matched to the original and every input, including records after a maximum zone. The original mark's
URLs are retained and its declared zone can only increase. The private transactional data copy is bounded and hashed.
Creation time, attributes, link count and the queried native security subset must remain preserved.

After closing the private writer, a same-transaction deny-write read pin spans commit and adoption of a normal native
reader. The published observation must match the private output's identity, bytes, origin and metadata. Disposing the
directory owner does not invalidate outstanding children. Cancellation before commit causes no public change; after
a possible commit the code finishes observations without cancellation and reports uncertain failures as NeedsReview.
Disposal closes handles and never deletes evidence.

`WindowsArchiveContentUpdate` connects these primitives to a separate `archive-content-update` recipe, version 1,
cryptographically bound to the exact completed preparation facts. It durably records:

1. The original-byte snapshot reservation, before creation, with the original metadata and required-origin digests.
2. The fresh snapshot identity and actual queried security digest, before transaction opening/copying.
3. The native transaction GUID, before old-data copying.
4. The sealed snapshot's independent identity, length and original-byte checksum.
5. The verified private output and metadata observation.
6. Commit intent, before the native commit.
7. Actual observed publication, with every artifact retained.

Normal preparation facts are frozen once this recipe begins. The original object becomes the published object;
existing replacement facts are never reinterpreted as a stage rename. Strict bounded parsing rejects changed scope,
missing/reordered transitions, aliases, changed bytes, unknown recipes/versions/fields and stale live snapshots.
Recovery exposes the recorded snapshot and native transaction in the read-only report. Every current outcome still
requires archive-specific review; generic cleanup, restore, closure and replay remain unavailable.

Space accounting separately charges the old-data copy and a bounded private data shadow, in addition to the existing
stage, evidence, other known snapshots and safety margin. A fresh held-volume capacity review precedes reservation.
This is an estimate, not an allocation reservation or proof of bounds on NTFS's internal transaction-log overhead.

## Validation

Actual disposable Windows 11 VM, .NET 10.0.14, medium test parent; no skips in the final native lane:

| Lane | Passed | Skipped | Scope |
| --- | ---: | ---: | --- |
| Windows common archive/edit regression | 1,355 | 212 | 1,567 discovered cases |
| Windows recovery/archive UI regression | 242 | 0 | Includes content intent, commit and mixed-history reports |
| Windows native storage control | 128 | 89 | Native/explicit lanes deliberately skipped here |
| Final Windows LPAC/application/native lane | 88 | 0 | 68 existing cases plus 20 actual content-update cases |
| Mac common archive/edit regression | 1,502 | 65 | .NET 10.0.12 |
| Mac recovery/archive UI regression | 242 | 0 | Actual macOS arm64 process |
| Ubuntu common archive/edit regression | 1,515 | 52 | .NET 10.0.14 |
| Ubuntu recovery/archive UI regression | 242 | 0 | Actual Ubuntu x64 process |
| Ubuntu native containment | 61 | 1 | Non-Linux platform-refusal guard skipped on Linux |

The 20 Windows cases rebuild and independently verify real TAR, gzip, bzip2 and zstd archives in separate confined
workers. They cover commit, rollback, abandonment, canceled commit, closed backup/output refusal, replay/forged-review
refusal, zone reduction refusal, parent-owner disposal, snapshot provenance, retained artifacts and all four journaled
formats. Public bytes and origin remain old before commit; committed files preserve original native identity/security
and are independently reread through TAR/decompression readers.

The first native run passed 84/88: the four journaled cases caught an identity-versus-digest contract mismatch before
opening the content transaction. `ReviewCreation` returns an identity. The corrected implementation separately hashes
the actual queried native security state; the full 88-case native replay then passed. The original failed evidence is
retained. The earlier 13-case primitive-only replay also passed.

Each Unix broad run passed independent readback of 12 zstd, 11 bzip2, 11 gzip and 8 exact-timestamp fixtures. The final
Windows-only fix was compiled again on both Unix platforms; all 20 Windows-only cases correctly skipped. Those skips
are platform guards, not Unix transaction qualification. No FileCat/test processes remained on any guest after testing;
the Mac's temporary until-reboot keep-awake assertions remain active.

The refreshed development Windows app has an SPDX 2.3 inventory of 261 exact files, with all 261 SHA-256 values
independently rechecked against disk: 34 reviewed NuGet packages and one runtime pack, with 235 restored files matched
to their archives. No dependency was added. This is a dirty-source development artifact, not release qualification.

## Evidence identities

Raw evidence is retained under the private worktree's `.tmp/test-results/archive-writing/content-update-v1` and
`content-update-artifacts-v1`; guest archives remain in this task's validation directories.

| Artifact | SHA-256 |
| --- | --- |
| Initial 13-file source delta | `7ACDF9B5D9AEA81FA0D5EE42B96DF8C6882F9ED947726E34DD148883E6EAF884` |
| Final 13-file source delta | `A2DF8A8599A113E5400F3CFC9C3ABBCCE3F31BDC92344351E2E27083735CAAC7` |
| Final Windows binary overlay | `D6BDC07A010087E4A9BF36449C1A473BDCBCB3351FB12BCA60DDF97CE025BAFE` |
| Windows first run, including failures | `ADFC4E41753324A1B0A3B31D70AC97B7B39C05EF13349CBAAADDFA1E650FEC20` |
| Windows corrected native replay | `21DF8DECB849BF8A25073BDA65815B18E0E65083D3141E64B9567ED4729B9834` |
| Mac broad/oracle evidence | `2C8A99C60B5A48D7592BB2923408AACEE7D66FA9DD0C549D58450026F365B285` |
| Ubuntu broad/native/oracle evidence | `A1DB5AA219EDD6FF701190FCC1F7448B277A83540C6CB877BCD2D59EA7798C55` |
| Mac final compile/guard evidence | `64626B7CD12151471D48B31ED5A5C2BAB5F98AC25E52910C45EE65B49C7795CB` |
| Ubuntu final compile/guard evidence | `D1B629BC57384426D74166A489B7CE312F792F65AE9D83E24993A6D71799B351` |
| Final app SPDX inventory | `A046301F717F14B595E79D305411A897912E54641F428FEA8AC62AA6C3B97E8B` |

## Remaining gates

The complete process/system-crash matrix, active native recovery/rollback/cleanup, transaction availability/deprecation
handling, broader metadata/resource/timeout qualification and the full hard-link requirement remain unfinished. This
primitive admits single-link originals and must not silently modify other names. The earlier seeded audit/resource/
central-policy probes establish a candidate; this integration's actual end-to-end fixtures do not expand their policy
coverage. No claim is made to qualify every filesystem security mechanism, hostile equivalent-privilege races or
power-loss durability. Unix publication/update routes, complete capture/global budgets, remaining formats, production
UI integration and the rest of the controlling plan remain open. RAR analysis/feasible implementation still follows
the implementation loop and precedes the final independent review, as requested.
