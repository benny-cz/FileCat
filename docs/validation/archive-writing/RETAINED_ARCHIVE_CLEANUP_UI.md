# Retained archive cleanup action

This follows `70b332cd`. It connects the existing exact native NTFS cleanup coordinator to an explicit application
review and approval action. It does not enable an archive writer, register the unqualified preparation/publication
bridge, or add a restoration action. Other platforms retain read-only reports until their native adapters qualify.

## Behavior and authority

For a complete supported TAR-family update/catalog, Windows offers **Review retained files…** in the interrupted
operations drawer. The row is advisory. On invocation, the application refreshes the journal, retains its actual
native single-link regular object and parent, derives a bounded current SHA-256 through that same handle, and reopens
the existing strict checksummed recovery reader. The native adapter then independently reviews the exact archive,
provenance and all six catalog objects. An old row, displayed path or report never authorizes deletion.

The held native scope survives the approval dialog. The dialog identifies the archive being kept, explains which
version it currently matches, lists the files to remove and separately describes observed absences. It warns when
removal includes the old-data backup. Cancel has initial focus; Enter cancels. Only explicit **Keep archive and clean
up** approval starts the shared object-bound coordinator. All journaled deletion/absence distinctions and prior
reconciliation prefixes are preserved. Linked restoration records are reported as not inspected.

Preview/cancel does not append to the journal or delete artifacts. Cancellation during acquisition cannot leak an
already acquired lease. After approved work starts, completed effects remain reportable even if stopping or IO failure
interrupts the action. Unrecorded unlinks require a fresh review; uncertainty is not described as successful deletion.
The review report is saved before closing recovery tracking, then updated to confirm closure. A failed final report
update remains a visible warning. Mixed, damaged, incomplete, unsupported and legacy archive records keep their
conservative report-only behavior; the pre-existing generic recovery guards remain in force.

## Desktop defect reproduced and fixed

The actual Windows guest exposed a clipped cleanup action: the recovery viewport received only 75 DIP of a 300 DIP
drawer, although no operation history needed the remaining space. UI Automation misleadingly reported the clipped
button as not offscreen. Three geometry checks reproduced this at 1280×800, 900×640 and 640×420.

With empty history, the selected archive review now uses more drawer space. After layout settles, the drawer minimally
scrolls its idle cleanup action into view. Ordinary history/recovery allocation and the existing Stop visibility logic
are retained. The three reproductions and all eighteen neighboring drawer-layout checks pass on the fixed build.

## Final candidate and automated validation

No codec/package version, lockfile or dependency licence changes. The App test project adds a non-output reference to
the existing crash harness. The Windows assembly grants the trusted application access to its internal native adapter.
The candidate retains the existing 261-file application SPDX inventory: 34 NuGet packages, one runtime package and
235 application/dependency file rows matched against their source restore artifacts.

Raw evidence is retained in the private worktree's ignored `.tmp` directory. The v3 source contains the exact thirteen
production/test/project files over `70b332cd`. The runtime and test manifests cover complete output trees; the Windows
delta was selected by comparing every row, then all 476 framework-dependent and 469 application/probe files were
verified in the guest, all 469 again under the standard-user assets, and all 261 actual desktop application files.

| Artifact | SHA-256 |
| --- | --- |
| `archive-cleanup-ui-source-v3.tar.gz` | `B5B0B77EC8F6ECB7FFD5D2927EA4D4EE286DE67850F295D23E13D1C2371E6451` |
| `archive-cleanup-ui-tests-v3.json` | `1C0C398B82CB072262AB728B2534DBF2CB204BB78D3A616C5CEFAE5B2E4C1CF6` |
| `archive-cleanup-ui-runtime-v3.json` | `83B4342D9CCF6063C9235167080BC64EA263289286EB406E94616FC30FD9FA23` |
| `archive-cleanup-ui-windows-v3.tar.gz` | `6F1AAFAE9D90D67FAAD751ABC85DD10F06E944AB939552CC584B6B839FEC271D` |
| `archive-cleanup-ui-windows-evidence-v3.tar.gz` | `7BE1E4811BE8EA2F6AA8C06B67956B1922AFCB4642B264F1A0F0AF16763B3BED` |
| `archive-cleanup-ui-artifacts-v3/application.spdx.json` | `BEA819A02D1E6E3D6FE411FD0DB3DDF48275106095F58036E2D0F387AAF2D51C` |
| `archive-cleanup-ui-mac-evidence-v3.tar.gz` | `EAA33580BEF39B3737CCD7AB8FCDC4683E4593371D1CA813C932D77C0EE2DC7F` |
| `archive-cleanup-ui-ubuntu-evidence-v3.tar.gz` | `9C86A00BA01313F6FF58A71E3A7FBE42715AA2583EF4AA3D55B274706CD97243` |
| `archive-cleanup-guest-desktop-evidence-v3.tar.gz` (40 payload files plus manifest) | `CF994CFA00078EA1E5E2DE6E83BFAE9EF9E02A79F9761718E74591EDF410A726` |
| `archive-cleanup-guest-desktop-packet-v3.tar.gz` (guest export, captures, scoped setup/action drivers) | `32BD955871AC463DE96FC30CFC8D40FA6E95786528C1B9F41B6354BA880DF173` |

Ten portable new-action cases cover the three pointer geometries plus approval, Cancel, changed target, stopping after
a confirmed deletion, failure after unlink, cancellation during lease acquisition and absent-adapter/stale-row guards.
Two Windows native UI cases create a real interrupted committed update by killing its parent, then exercise the
production adapter through the actual Avalonia approval dialog. They check Cancel preservation and approved cleanup,
raw origin/archive preservation, deletion masks, terminal journal records and report closure. These in-process tests
use the headless windowing platform; the OS desktop journey is separate evidence below.

Mac and Ubuntu each build/restore successfully and pass 273 UI/edit/archive/drawer cases, with two explicit Windows
native UI skips. Each separately skips all 159 Windows-only guards. Mac's focused post-fix lane passes 28 portable
cleanup/drawer cases. Failed clipping reproductions are retained rather than overwritten by passing evidence.

Windows 11 passes the same 273 UI cases (two native opt-in skips), the separate two real native UI cases and all 227
administrator native cases. The loaded-profile standard account passes all thirty focused cleanup/drawer cases and
222 native cases with five explicit administrator-only skips. Its raw token observation excludes the Administrators
SID and records medium integrity before/after, runtime 10.0.14 and exit 0. No skipped case counts as passed.

## Actual Windows guest desktop

The complete 261-file candidate is run under the existing guest interactive account in session 2, with a private
profile and a disposable fixture made by a genuinely killed native update parent. The UIA driver, keyboard Enter,
foreground/hit checks and screen capture all execute against the guest. No host window activation/input is used.
The separate standard-user lane above establishes its raw-token claim; the desktop account is not substituted for it.

The fixed action is fully visible. Its accessible Invoke target is independently hit-checked through the guest's
current clickable point. The real dialog warns about backup removal, shows the exact archive/version and retains
Cancel focus. A native accessibility ScrollPattern exposes all six paths at the bottom while approval remains visible.
Guest-only Enter is allowed only when both focused control and foreground still match the exact owned FileCat window.
It cancels; the dialog disappears, all six files remain, and the archive/origin/journal/all fixture hashes are unchanged.

After reopening, explicit approval removes exactly the six catalog objects. The archive remains SHA-256
`FB32ACE384479268CA185EB5496F9CC367E043F5031270E29B16C3FF3B6807F8` and its raw origin mark remains
`F8076762E3EAA0FB12C310E41FF8E1A7E08CAA7FE00AA236011E89D359214C79`. The recovery journal advances from 23 to 38
independently CRC-checked records, with completed recovery, deletion mask 63, absence mask 0 and one terminal record.
The saved report confirms closure. All noncatalog fixture files and the original closed journal copy are unchanged.
The retained report opens in the application's read-only report viewer. All owned FileCat/report windows close
normally with exit 0, followed by a census showing no FileCat, test host or dotnet processes. The owner exports forty
payload files plus their manifest; all counts, lengths and hashes are independently checked after guest-to-host copy.

Two observer failures are retained: an administrator read lacked access to creator-private files; an owner read then
omitted sharing delete access required by native retained cleanup handles. The read-only observer was corrected to run
as the owner and share read/write/delete. Production ACLs, retained handles and deletion authority were unchanged.

This desktop run also identifies follow-up drawer defects: report rows expose record `ToString()` as their accessible
name, a completed report button is partly clipped when the old compact recovery allocation returns, and the idle archive
guidance still points only to the report. The final report also retains its pre-closure heading above a closure-confirmed
footer. These findings are queued for the next development iteration; this is not the
final independent review and does not claim the drawer is free of known findings.

## Qualification limits

Complete native restoration UI, Unix cleanup/recovery/publication, wider metadata/security/alias/durability/resource
coverage, production profile registration, full workflow/platform/package/fuzz qualification, RAR feasibility and the
final independent review remain open. This checkpoint completes no AW packet or release gate by itself.
