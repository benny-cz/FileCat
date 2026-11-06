# Windows resource-policy observations, 6 November 2026

Development checkpoint over `97bbf715`. This advances outer metadata capture required by the
[archive-writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md). Existing-archive metadata application
and replacement remain unfinished; no writer profile is enabled and no release qualification is claimed.

## Capture and mutation guards

The held-file security query now requests owner, group, DACL, integrity label, resource attributes and the central
access policy identifier together (`SECURITY_INFORMATION = 0x77`). Microsoft documents resource attributes and CAP
identifiers as readable with `READ_CONTROL`, unlike the complete audit SACL. The production reader neither enables
privileges nor retries with a smaller mask when a component cannot be captured. See Microsoft's
[security-information requirements](https://learn.microsoft.com/en-us/windows/win32/secauthz/security-information).

The bounded normalized descriptor, including returned resource/scoped-policy ACE bytes, remains part of the complete
metadata digest. Its hash also includes the explicit query mask, so the old `0x17` scope cannot be mistaken for the
new scope even when both return no resource-policy ACEs. Live native creation witnesses already serialize that mask
and descriptor. No layout reinterpretation or deserialized publication authority is introduced.

Retained input reviews and existing native rename/publication guards therefore detect a resource-claim value change
even when identity, payload hash, creation time, mtime, DACL and origin are unchanged. A late change on the owned stage refuses
the final-name claim and retains the stage. These observations do not interpret claims as authorization or establish
that unqueried security state is absent.

## Native fixtures

Tests repeatedly change an already-present string resource claim and check that the complete metadata changes while
payload/creation-time/mtime/DACL/origin remain equal. Another test changes a claim after a live creation review and proves that
publication refuses before changing the namespace. Establishing a claim before the baseline is intentional: the first
assignment normalized inherited DACL flags on the Windows VM. The final test retains the exact DACL comparison.

Two actual NTFS files contain both a resource-attribute ACE and distinct scoped-policy identifier ACEs. A medium-
integrity reader opens them through the ordinary retained source route, whose requested handle rights exclude
`ACCESS_SYSTEM_SECURITY`, then observes the exact identifiers and unchanged payload. This tests native CAP metadata
capture, not domain policy installation or effective CAP enforcement.

The explicit validation helper `eng/validation/archive-writing/make-windows-resource-policy-fixtures.ps1` requires an
available caller privilege to prepare those fixtures; it never elevates the reader or changes machine policy. It
atomically creates a new private directory, refuses existing paths, creates files exclusively and keeps them open
without delete sharing. Privileged metadata changes use `ReOpenFile` on each created object's live handle and
`SetSecurityInfo`; they do not resolve the file path again. The helper restores its token privilege and closes its
handles. This follows the [ReOpenFile contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-reopenfile)
and introduces no production broker command.

## Validation

| Environment | Core archive/edit/timestamp replay | UI replay | Additional native evidence |
|---|---|---|---|
| Windows 11 x64 VM / NTFS, .NET 10.0.14 | 1,314 passed, 212 skipped; 1,526 total | 239 passed | Storage: 128 passed, 57 skipped; 185 total. LPAC: all 68 passed. Final handle-prepared fixtures: all 14 metadata tests passed |
| macOS 27.0.1 ARM64 / APFS, .NET 10.0.12 | 1,461 passed, 65 skipped; 1,526 total | 239 passed | Native Unix retained/publication/crash coverage is included in Core |
| Ubuntu x64 VM / ext4, .NET 10.0.14 | 1,474 passed, 52 skipped; 1,526 total | 239 passed | Native containment: 61 passed, one skipped; 62 total |

No final failures. The Windows launcher facts record integrity 12288 before / 8192 after and exit zero. All eight
marked-creation application/probe cases also passed with the expanded query. The final metadata replay used fresh
handle-prepared CAP fixtures and a medium reader. An initial host focused run passed 15 tests; a later optional host
replay deferred when another implementor started work. Ubuntu initially deferred until its other process exited.
Integrity facts alone do not qualify a standard non-admin account or an installed release package.

Mac and Ubuntu independent readers each checked 12 TAR.ZST, 11 TAR.BZ2, 11 TAR.GZ and eight exact-timestamp outputs.
The runtime changes are Windows-only; the Unix runs also confirm cross-platform compilation and regression behavior.
All owned FileCat/test instances exited. The Mac's temporary activity/display assertions remain until reboot.

## Evidence and limits

Raw evidence remains in ignored `.tmp/test-results/archive-writing/resource-policy-v1` and task-owned guest folders.
The initial fixture-helper compatibility/restore-buffer failures and first Windows DACL-baseline failure are retained.
Final changes after the broad source capture affect only Windows tests and validation setup; production sources were
independently checked unchanged.

- Broad runtime source delta: `68718C884230AA8257F4047549043B31EE6E649F1DDEEC2CD0AE091AE3E83EC1`.
- Final source/test/setup delta: `A462414D78D97B074C338019B7A6E9D3370DFB1BD78E9721BE0B1BCAFADAE30C`.
- Final Windows managed overlay: `1B3E8AC89B2CDFED3135449EBBF1135B42283E959B8A7EBEF7C48EC55301BFAF`.
- Windows initial broad/Core/UI evidence: `EFC6FE40195ED71A5B9BF0032E2AEDC680F85E5C300C995C50D57970FF66CB43`.
- Windows corrected storage/native evidence: `06686589AA7FF7D1E29BE9A23BA4BB0561B4987DEEE4A7233FAAA8B67694E1E2`.
- Windows final handle-fixture evidence: `3F644C04E3D482711EA5669BCD3F2DF8AFD732EF8DF7DF4A6EC8D51C8C44AB74`.
- Mac evidence: `45A244AE7D854D87EBCBC1C7F109C31A1D812069E6764FC3135F834B4517D4D9`.
- Ubuntu evidence: `0680C23070833223B72CE8DBDB4DD203D517CB22520C543F53E40D25BD36E249`.

The win-x64 development SPDX inventory is retained at ignored
`.tmp/test-results/archive-writing/resource-policy-artifacts-v1/application-final.spdx.json`, SHA-256
`112E2CC7F4E5B612D27ECF8E8106E0C044A73ED6463CCD493066AAE08B2A9530`. All 261 payload hashes were independently
rechecked; 34 reviewed NuGet packages and one runtime pack contribute 235 files matching their reviewed archives.
No dependency changed. Its source state explicitly records the base commit plus the final delta checksum above;
it is not a reproducible release identity, and final report edits follow capture.

Audit SACL and other unqueried security-policy admission, actual metadata application, reviewed replacement/retirement/
rollback/cleanup/recovery, full source capture/budgets, Mac hard-memory containment, remaining engines and complete
GUI/fuzz/package qualification are still required. The complete implementation loop, subsequent RAR analysis/feasible
implementation and final independent review remain active.
