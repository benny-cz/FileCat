# Archive cleanup drawer follow-up

This follows `b9c5b5c9` and the desktop findings in [the retained cleanup checkpoint](RETAINED_ARCHIVE_CLEANUP_UI.md).
It changes presentation and final report wording; native review, approval, journal sequencing, deletion authority and
writer-profile admission are unchanged. No dependency or codec is added. It is development validation, not the final
independent review requested after the implementation and RAR phases.

## Findings and fixes

- Completed report rows exposed the record's entire `ToString()` through accessibility, including journal paths and
  report contents. The row now names itself with its concise existing display text. Its explicit Open report button
  remains separately named.
- After successful cleanup removed the interrupted row, the empty-history drawer reverted to a 75 DIP recovery area
  and partly clipped the report button. The extra empty-history space now also covers reviewed reports, and the first
  report action is minimally scrolled into view when no interrupted row is selected.
- Idle archive guidance still instructed users to keep files and open only a report even when native review was
  available. Supported rows now explain the retained-file review and that cleanup keeps the archive. Unsupported rows
  retain the conservative guidance. The dialog calls its two summary lines **Cleanup summary**, avoiding an implication
  that they are selectable alternatives.
- A successfully closed report retained its earlier not-yet-confirmed heading above a confirmed footer. The final
  successful report is now rendered again with a completed heading and confirmed closure; the earlier saved unconfirmed
  report remains appropriate if the final save fails. Its existing warning behavior is retained.

Three geometry/accessibility cases reproduce the exact completed-report announcement state at 1280×800, 900×640 and
640×420. Before the fix, the row name has 689 characters instead of its 73-character display, and the button extends
from 59 to 88 DIP in a 75 DIP viewport at the two larger sizes. Both initial and exact-announcement failed reproductions
are retained. Existing approval cases now also check actionable guidance and a consistent closed report; the two real
Windows native UI cases independently check the final report.

## Candidate identities and validation

The v4 source has all presentation fixes and six changed files over `b9c5b5c9`. The v5 source corrects one test analyzer
warning by using the predicate overload of `Assert.Single`; all production source bytes are identical. The final host build
and publishes pass with no warnings/errors. Host work consists of builds and artifact checks; live/UI tests run only
inside the Windows/Ubuntu VMs and on the Mac.

The full v4 UI lane passes 276 cases on both Mac and Ubuntu, with two explicit Windows native UI skips. Each platform
also skips the 159 Windows-only guards. The final v5 focused lane passes 31 cleanup/drawer cases on each, with two native
UI skips and no analyzer warning. Both lanes perform an idle census. The Mac's temporary until-reboot helper remains
active (PID 36765 at the observed census), along with its caffeinate assertions.

The Windows candidate is derived by comparing every complete test/runtime manifest row against v3, producing a
71-file delta. Complete trees still contain 476 framework-dependent test/helper and 469 app/probe files. The rebuilt
application SPDX matches all 261 actual application files, 34 reviewed NuGet packages and one runtime package, with
235 restored files matched to their source archives.

Windows verifies all 476 test/helper, 469 administrator runtime, 469 standard-user runtime and 261 desktop app files.
It passes 276 full UI cases (two opt-in native skips), the two separate actual native UI cases and all 227 administrator
native cases. The genuine loaded-profile standard account passes 33 focused cleanup/drawer cases and 222 native cases
with five explicit administrator-only skips. Its medium-integrity token excludes the Administrators SID; before/after
observations and exit 0 are retained. No skipped case counts as passed.

Raw evidence is retained under the private worktree's ignored `.tmp` directory.

| Artifact | SHA-256 |
| --- | --- |
| `archive-cleanup-ui-source-v4.tar.gz` | `9B5A4864F2D28A83F8D465703FB2B08A7DF98E31907D2A72E0C438A945E4F776` |
| `archive-cleanup-ui-source-v5.tar.gz` | `686CD6C73D502A3E25D4C2CFCC8E8C2BD9016D458DFE8060234FAA9B1C8C0CA6` |
| `archive-cleanup-ui-tests-v5.json` | `DFBF43001EC4725B90E4A50CC249B7D87D09ED37449D6B37E4F09B793512CEC8` |
| `archive-cleanup-ui-runtime-v5.json` | `DFC809DB5C8295AB7696707E4A94007293D8A8765EE6AA1CC75836B6B8AA68B3` |
| `archive-cleanup-ui-windows-v5.tar.gz` | `C8BF5DCF5A94470CB11EADA81DDFB9B1616A713860292C9C3DD3166506F97ED0` |
| `archive-cleanup-ui-windows-evidence-v5.tar.gz` | `47AE850824E79D2014D1062D9A96FC31B7C70807A43475F528808A17B534BD85` |
| `archive-cleanup-ui-artifacts-v5/application.spdx.json` | `6A52F3A38E5AE4DF3F9EF5ED80149F636EAC135CE6824E8FF1AC7907D436EF8D` |
| `archive-cleanup-ui-mac-evidence-v5.tar.gz` | `62A2DF1281FFE74768A458844BC5C379E9109907F4D00461986B7F51641C1124` |
| `archive-cleanup-ui-ubuntu-evidence-v5.tar.gz` | `3653EDA49F109658C57FE075B5A2BE4FBD3DF7720EEE9C1899A56643475B92EB` |
| `archive-cleanup-guest-desktop-evidence-v5.tar.gz` (65 payload files plus manifest) | `0FED503A469EA7A452B4BAE9BD7105BD434A8F7A21F2259F7C15DA5A67394EC1` |
| `archive-cleanup-guest-desktop-packet-v5.tar.gz` (19 payload files plus manifest) | `7058CB6203C8B8D2D927261D1DC7B579E6F3CBFF69F95A189784C46514C946EE` |

## Actual final Windows guest desktop

A fresh `profile-v5` and actual native crash fixture are created under the existing guest interactive account in
session 2. The application and both complete helper trees are hash-checked before launch. All UIA, Enter, foreground/
clickable-point checks and capture use the guest; no host desktop input or activation is used.

The idle guidance and **Cleanup summary** heading are observed. Cancel retains focus. Preview and guest-only Enter
cancellation preserve all six files and the exact 23-record journal. A second review scrolls all six full paths into
view before explicit approval. Cleanup removes exactly those six files and preserves all noncatalog fixture files,
the original closed journal copy, current archive SHA-256
`787AA517696A3936C77FE3743E7720A9155B0486723A3EFA670A736F0C52DED1` and raw origin SHA-256
`F8076762E3EAA0FB12C310E41FF8E1A7E08CAA7FE00AA236011E89D359214C79`.

The independent observer checks 38 valid CRC-framed records, completed recovery with deletion mask 63/absence mask 0,
one terminal record and a closure-confirmed report without the obsolete not-yet-confirmed heading. The actual native
row name is the 73-character display, with no record dump. The entire 29-pixel report action is visible. Its button
opens the read-only viewer, whose completed heading and confirmed footer are visually checked.

All owned FileCat/report windows close normally with exit 0; the subsequent census is free of FileCat, dotnet and test
hosts. Nine inactive author-verified tasks belonging only to this validation are removed. The owner exports 65 payload
files plus their inventory, including the raw origin bytes; every count, length and hash matches after guest transfer.
The separate packet retains the captures, guest export, exact manifests/SPDX/source and scoped control/setup drivers.

## Limits

This addresses the reproduced empty-history drawer cases; it is not complete all-state accessibility or platform
qualification. Native restoration actions, Unix cleanup/recovery/publication, broader resources/metadata/security/
aliases/durability, profile registration, remaining formats/workflows, package/fuzz gates, RAR feasibility and the final
independent review remain unfinished. No writer profile is enabled.
