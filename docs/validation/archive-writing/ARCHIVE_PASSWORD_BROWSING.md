# Password browsing and read ownership

Status: connected component matrix passed on Windows, Mac and Ubuntu. The
[checkpoint](password-browsing-checkpoint.json) binds the exact tested source and binaries and preserves failures.
No new archive write profile is registered or qualified by this component.

RAR5 and 7z navigation can request a masked password before listing. Header encryption is checked during
opening; data-only protection also requires a bounded complete encrypted-member read. Incorrect input or
damage allows retry or cancellation. Older RAR encryption reports its unsupported reading limitation.
The existing encoded-password readers and monotonic KDF budgets are reused; this adds no encoder or dependency.

An unlocked index belongs to the exact opened source and its native change evidence. Its password is held
only by the reader. There is no filename password cache, location credential token, settings value or saved
workspace credential. Leaving the last tab/view/operation immediately unpublishes the session. Reader/key
disposal runs off the UI thread after borrowed content returns. A changed source requires new admission.

Navigation preparation now carries runtime ownership through success, abandonment, cancellation and tab
closure. Nested archives retain their containing archive's read session. Copy/extract jobs retain source
and ancestor read sessions before admission, release them on failed submission or queued cancellation, and
hold them through worker completion. F3 retains ownership during opening and gives the viewer its own lease
for later reads. The existing protected-entry indicator remains visible after unlocking.

Restored tabs retain their archive address without startup password questions. Explicit Refresh can unlock
them. Refresh of an already unlocked archive preserves marks, focus and history. Locked tabs prepare in the
new destination tab, avoiding duplicate password questions. Neither this ownership nor a restored location
grants archive mutation authority.

The initial candidate is `c5ae7e91b9c51cd9716886951437a3d37589e54c`; the connected nested/viewer/refresh
candidate is `02be74a55344d9175b9444b3fbbe296a4383866f`. The recovery-container correction is
`ff8cc93e323709cdd3994aef5801db6aa6e8f545`. Guest-only batches retain source deltas, DLL/PDB bytes,
compiler source snapshots, exact case outcomes and final process censuses.

The first 18 Core and 11 headless UI cases pass on all three operating systems. Its Windows/Ubuntu complete
Core suites preserve all preceding cases and passes. Four Mac failures identified an omitted case-sensitive
fixture; the subsequent batch uses the existing case-sensitive image and detaches it afterward. The expanded
20 Core and 27 UI cases pass on Mac/Ubuntu. Its full suites exposed 19 recovery/backing-source regressions:
the lifetime helper incorrectly required navigable providers for identity-only ancestors. The correction
keeps source-provider admission and retains only registered ancestor providers. All 52 affected preflight
cases pass on both Unix platforms. Original failed runs remain sealed and inspectable.

The final cohort also covers the existing navigation, clipboard, member, creation/update, viewer admission,
mutation preparation, dialog focus, exit, operation scope, sort/focus and cleanup UI families. Its complete
Core gate compares names and outcomes with the sealed member-command baseline and refuses any lost prior
case or pass. These automated UI cases use headless Avalonia on the guests; they do not complete the plan's
native desktop, scaling, accessibility or final independent-review gates.

| Gate | Windows | Mac | Ubuntu |
|---|---:|---:|---:|
| Password/read ownership Core | 20 passed | 20 passed | 20 passed |
| Connected password/navigation/viewer UI | 27 passed | 27 passed | 27 passed |
| Recovery/backing-container preflight | 44 passed, 8 platform skips | 52 passed | 52 passed |
| Application regression selection | 236 passed, 1 platform skip | 235 passed, 2 platform skips | 236 passed, 1 platform skip |
| Complete Core inventory | 8,017 passed, 461 platform/opt-in skips | 8,266 passed, 207 skips | 8,288 passed, 185 skips |

Every preceding Core case and pass is preserved. Each final bundle binds 21 changed C# inputs and 12 DLL/PDB
pairs to the frozen source candidate. All three final owned FileCat/test censuses are zero; Mac's wake helper
36765 remains active. Eight final/historical bundles totaling 192,126,684 bytes are retained. The final batch
includes the existing application ownership families directly; it requires no separate continuation build.

Windows initially postponed for another tester and then refused an incorrectly versioned wrapper resource
path before applying source changes. The corrected wrapper preserves the fixed Python, source and system
PowerShell paths. Both records are retained. Host activity is limited to builds, packaging and static evidence
verification; application/test execution stays in the guests and Mac.

Versioned non-ZIP external edit creation/commit, final member UX edges, Unix native mutation/confinement,
current-binary writer qualification, remaining fuzz/package/release gates and final independent review remain
open. Password entry for an explicit update remains a separate operation; read sessions are not serialized
into queued mutation plans or edit-session records.
