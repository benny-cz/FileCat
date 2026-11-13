# Archive writing implementation status

**13 November, the listing re-validated after the eighteenth and nineteenth reviews (`a4606689`).** Two independent
reviewers read the listing and its records. Both found the qualification and the removal boundary sound; between them
they found three defects in the listing, all fixed in `a4606689`:

- **An edit made before the listing could no longer be discarded.** `EditSessions.Discard` refused whenever a native
  root was composed — a guard written when that method deleted the session folder by pathname, which `38e56944` had
  since made an object-bound removal. From the listing commit every older edit on the three routes was undiscardable,
  behind a confirmation the application offered and then answered with “The working copy could not be deleted (is it
  still open in the editor?)”. Only a native record needs the native route now; a legacy folder goes through the
  legacy store's bound removal, the dual-root case pins it (the old edit discarded beside a native root, nothing else
  in either root touched, a native record still refused), and a refusal is reported as itself.
- **The edit-storage routes shared the archive routes' evidence IDs,** which the release manifest's “a listed route
  stands on its evidence” refusal collapses into the archive routes' own requirement: a candidate that never ran an
  edit suite would have passed it. They carry their own IDs below the route's now (`AWQ-win-x64-NTFS/edit` and so on).
- **The committed edit-storage batch never set `FILECAT_EDIT_NATIVE_REQUIRED`,** the variable that makes a native edit
  test fail rather than skip and arms the application's composition pin. It sets it itself now, beside the fixture
  root it already set.

The nineteenth review checked the records against the evidence and found two statements it contradicts — “every lane”
ran with the required-native variable (the seven package route lanes did not) and a Core rerun's total quoted as its
passes — both corrected above, with the installed-layout staging, the Program Files access list, the provenance of
the F4 fix and the Ubuntu VM's death as an operator observation. The help text now says where its promise holds
(FileCat's own data folder on a listed file system, not the archive's place), the release note the owner publishes
gained what a stopped F4 does, the “Remove unfinished copy” consent text no longer promises what the removal
boundary's accepted residual denies, and five component records that still said ordinary startup composes no native
edit store were corrected.

`a4606689` then ran on all three platforms, each package built from a clean checkout of it, and every lane passed but
one Windows case, described below:

| Platform | Full regression (passed) | Package route tests |
|---|---|---|
| macOS (M1) | App 3,111, Core 9,189, Remote 2,638, Rendering 23; no failure | App 12 of 12; packaged launcher 14 of 14 |
| Windows 11 VM | App 3,202, Core 8,399, Windows platform 1,631, Remote 2,405, Rendering 24; one failure, below | Installed layout 12 of 12; portable 12 of 12, its grant exactly the 247 runtime files |
| Ubuntu 24.04 | App 3,113, Core 9,248, Remote 2,543, Rendering 23; no failure | .deb 12 of 12 and its bundle check; the AppImage refused with its reason |

The two failures of the previous run did not recur: the macOS 7z PPMd case and the Ubuntu secret store both passed in
their full runs (the VM's login keyring was unlocked again first). One new failure appeared, again away from edit
storage: `PlaceBarHierarchyTests.Compact_panel_header_uses_available_width_and_keeps_overflow_places_reachable`
asserted `pinnedButton.Bounds.Right <= row.Bounds.Width` on the Windows VM. The test waits for the pinned place to
leave the overflow, which the measure pass decides, and then read bounds the later arrange pass had not set; the class
passed 3 of 3 alone at the same commit. Fixed on `codex/archive-writing` in `7e810c13`: the test finishes the layout
(its jobs, then `UpdateLayout`) before it reads the bounds.

**The download mark on a private copy: through the folder for an archive member, by its pathname for the rest.** The
archive route writes a member's mark through the folder FileCat holds (`EditDirectoryLease.CopyOrigin`) because it had
to: its old path pin needed deletion rights that a creation's own hold does not share, so it failed outright (the ten
cases above). A server file's edit, a legacy-ZIP member's edit, a legacy commit copy and a Save copy still write the
mark to the copy by its pathname (`WriteOriginMark`), and that is accepted here, not an oversight:

- The path names an object FileCat created moments earlier, in a folder FileCat created under a root it holds and will
  not compose unless that root is private and not reached through a link - 0700 on Unix, FileCat's own access list on
  Windows - so no other user can put an entry there or rename one.
- What a wrong object would receive is the mark's text (the source's zone or URL), not the user's bytes. Nothing is
  removed, truncated or overwritten by this write, and it is checked: the creation fails if the mark could not be kept.
- The copy's mark is read back through the held folder by what depends on it - a commit's capture compares the origin
  digest of the copy it read before and after writing it, and Save copy refuses a destination that cannot keep the mark
  - so a mark that did not land on FileCat's own copy is reported there rather than passing silently.

The residual: only this user's own process could replace that name in the window between the copy and the mark, and
FileCat would then hold a copy without a mark and say so at the next operation that needs one, rather than carry a mark
it never wrote. Making the four symmetric needs a handle-based write of a raw mark in both platform leases (a
handle-relative stream on Windows, `fsetxattr` on Unix) and the write moved into the copy itself, where the output file
is still open; that is queued as its own change, not folded into this listing.

Each of the three full regressions ran with `FILECAT_EDIT_NATIVE_REQUIRED=1`; the package route lanes carry their own
variables. The Windows installed layout was staged into Program Files before the chain, from the host and
elevated, as an installer is: that folder cannot be made by an unelevated process, while the lane that then reads it
must not be elevated. Its staging log, which records both, is evidence.
The release manifest is [release-manifest-a4606689.json](release-manifest-a4606689.json): the same shape as the
listing commit's, with the three edit-storage routes under their own evidence IDs, a Windows package log of its own
(so those hashes rest on sealed evidence too), 14 packages and 21 evidence files.

**13 November, native edit storage listed as the default edit store (rows 4, 8 and 13).** With the listing active
locally, the application's own edit suites showed what it changes: a stopped or failed F4 kept its half-made copy, the
creation marker was visible while the bytes were copied, concurrent copies waited behind one slow source, Save copy did
not work for a native edit at all, and the tests drove hooks only the legacy store calls. Seventeen rounds of
independent review followed; every High and Medium finding is fixed in `ef5c36d1` (whose message names the sixteen
rounds that had closed when it was written), and `fda3f03c` lists the three routes with their evidence IDs and pins the
list in Core and in the application. A creation now tracks every object it
makes and removes exactly those when it is stopped, cancelled or fails, keeping, logging and naming whatever it cannot;
nothing inside private storage is reached by its path; a copy is not an edit until its record is published; the storage
gate is held to admit a copy and to publish its record, never while the bytes are copied; and every process accounts
for the copies still running, so the review budget still judges what is stored.

The complete suites found what the focused rounds could not: on Windows the archive route pinned the private working
folder *by path* to carry a member's download mark, which the creation's own deletion-shared hold refuses, so ten cases
of the standard-user native F4 commit lane failed with “The private edit directory cannot be pinned safely”. The mark
now goes through the creation's own folder lease (`ef5c36d1`, an ancestor of the listing commit; that failing run was on
an earlier commit of this branch and is not in the evidence set below, where all ten cases pass). The listing commit then passed everywhere, each package built from a
clean checkout of that commit:

| Platform | Full regression (passed) | Package route tests |
|---|---|---|
| macOS (M1) | App 3,111, Core 9,188, Remote 2,638, Rendering 23; one failure, below | App 12 of 12; packaged launcher 14 of 14 |
| Windows 11 VM | App 3,203, Core 8,399, Windows platform 1,631, Remote 2,405, Rendering 24; no failure | Installed layout 12 of 12; portable 12 of 12, its grant exactly the 247 runtime files |
| Ubuntu 24.04 | App 3,113, Core 9,247, Remote 2,543, Rendering 23; one failure, below | .deb 12 of 12 and its bundle check; the AppImage refused with its reason |

Each of the three full regressions, and the two macOS reruns, ran with `FILECAT_EDIT_NATIVE_REQUIRED=1`, so a native
edit test fails there rather than skipping, and the application's composition pin asserts that ordinary startup really
composes the native store on each route's file system. The package route lanes carry their own variables (the package
route, that payload's assets and the platform's native flag) and not that one, so a native edit test they reach could
still skip.

Two tests failed once each, both away from edit storage, and both passed again at the same commit:

- **macOS, one 7z case — a test closing another test's file.**
  `SevenZipWritingTests.Codecs_create_independent_folders_…(codecId: 6, compressedHeader: True)` threw
  `ArgumentOutOfRangeException` from `FileStream.Read` under `SourceStream.Read` → `BufferedSubStream.RefillCache` →
  the PPMd range decoder, reading back the archive it had just written. The cause is in neither 7z nor SharpCompress:
  `MacArchiveProcessTests.An_unrelated_inheritable_descriptor_is_closed_before_the_worker_starts` called
  `dup2(original, 200)`, and `dup2` silently closes whatever descriptor 200 already is — in the parallel Core host, this
  7z case's open archive. Its `FileStream` then read a 17-byte canary file with its position past that end, so
  `Math.Min(count, Current.Length - Current.Position)` went negative. This run's own results show it: the 7z case failed
  at 15:54:55.927, inside the descriptor test's 15:54:55.761 to 15:54:55.993. Fixed on `codex/archive-writing` in
  `17790a26`: that test takes the lowest free descriptor from 200 with `fcntl` `F_DUPFD`, a policy test fails any source
  that declares `dup2` or `dup3`, and `SourceStream` clamps its remaining count at zero, so a file truncated under a read
  is reported as damaged instead of throwing. The earlier reading of this failure here — the PPMd decoder reading past
  its packed extent — was wrong: `BufferedSubStream` bounds every read by what is left of its sub-stream and
  `SourceStream.Seek` refuses a position past the end, so no decoder can reach that state.
- **Ubuntu, the system secret store.** `SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret`
  reported “Cannot create an item in a locked collection”. The cause was the VM, as the operator saw it rather than as
  sealed evidence (this route carries no chain log): its VMware process died 17 minutes into the chain, its own log
  ending mid-run with no exit line and nothing in the host's event log, and after the cold boot autologin left the login
  keyring locked. `gnome-keyring-daemon --unlock` could not reach the session daemon, which had never made its control
  directory; with one daemon serving an unlocked keyring again the class passed 4 of 4 at the same commit.

The Windows installed-layout lane was rerun for a reason of its own: the chain's staging step could not make
`C:\Program Files\FileCatRouteTest` without elevation, so the assets folder it named did not exist and its twelve cases
failed before testing anything. The payload was then staged there from the host, elevated as an installer
is, which gives it that folder's own access list — including read and execute for *restricted* application
packages, which is what this lane's workers depend on, since a package route lane grants them nothing itself (no case
asserts the list) — and the lane passed 12 of 12, unelevated. The chain log keeps both attempts; the lane's own evidence
is the rerun's, which replaced the first attempt's, so what that log holds is the cause of those twelve failures
rather than their results.

The release manifest of the listing commit is [release-manifest-fda3f03c.json](release-manifest-fda3f03c.json):
commit, profiles, record schemas, worker protocol, the 30 archive routes and the three edit-storage routes with their
evidence IDs, the Windows payload, 14 packages (the Linux and macOS ones hashed on their machines) and 18 evidence
files, the two reruns included. Still the owner's: release signing, the macOS denial evidence on the signed build, and
the Save-copy download-mark question of review 13 — FileCat's own temporary area must be able to keep the mark, while
a destination that cannot keep it only reports the loss; carrying the mark to the destination instead was tried and
reverted, because it changes a pinned decision.

**12 November, the release candidate `3781e7ae` re-validated; routes listed (rows 11 to 14 closed).** After the verification
review of `04dbc8fa` (one High, a macOS access-list regression the Mac run found too, fixed in `e41e6b2b`; its Low points
in `3781e7ae`), the candidate passed on all three platforms, each package built from a clean checkout of that commit:

| Platform | Full regression | Package route tests |
|---|---|---|
| macOS (M1) | App 2,837, Core 9,147, Remote 2,638, Rendering 23; no failure | App 12 of 12; packaged launcher 14 of 14 |
| Windows 11 VM | App 2,932, Core 8,396, Windows platform 1,592, Remote 2,405, Rendering 24; no failure | Installed layout 12 of 12; portable 12 of 12, its grant exactly the 247 runtime files |
| Ubuntu 24.04 | App 2,839, Core 8,647, Remote 2,543, Rendering 23; no failure (see below) | .deb 12 of 12 and its bundle check; the AppImage refused with its reason |

The probe tests ran where they apply: on the Mac every Unix case including the macOS access lists; on the Windows VM the
ordinary folder, the junction and the hard link, and the OneDrive sync root it has; on this machine the real OneDrive.

On Ubuntu one test hung: `SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret`, which stores a secret in
the system keyring. Every other Core test had passed; the run's guard ended the hung test host after an hour. The cause
was the VM, not FileCat: a second keyring daemon, started there earlier today to create the login keyring, had left the
session daemon listing a `login` collection it no longer served, so the secret store waited for an unlock that could not
come. With one daemon serving an unlocked `login` keyring again, the test passed alone at the same commit (4 of 4). Its
hang dumps are not kept in the evidence.

The release manifest of the candidate is [release-manifest-3781e7ae.json](release-manifest-3781e7ae.json): commit,
profiles, record schemas, worker protocol, the 30 listed routes with their evidence IDs (none for edit storage), the
Windows payload, 14 packages (the Linux and macOS ones hashed on their machines, their package logs as evidence) and 16
evidence files. Still the owner's: release signing (a trusted Windows signing provider, macOS Developer ID and
notarization), the macOS denial evidence repeated on the signed build together with updating an archive the installed,
quarantined app made itself, and the decision on the proposed ADR amendments for released builds. Native edit storage
is not listed (rows 4 and 8).

**12 November, the ninth review and its fixes (rows 13 and 14).** A ninth independent reviewer read `90806f12`. It found
no safety break and no over-refusal by the probe beyond the intended places. Fixed in the next commit:

- **Medium: a Linux or macOS archive whose group the user cannot keep** (for example downloaded with `sudo` and then given
  to the user, group root) was refused only when the replacement's metadata was applied, after the rebuild; a ZIP had
  left its old route for it. The update admission, and so the probe, now checks it first: the group a new file there
  gets (macOS: the folder's; Linux: the process's, since the private stage folder clears a set-group-ID bit) or one the
  process may give. Memberships beyond the process's own group list (nested macOS groups) are not assumed: such an
  archive is refused, not changed late.
- **Medium: a macOS test regression.** A test composed routes with the temporary folder as given; the judge resolves it
  through macOS's `/var` link. The Mac run of `90806f12` failed it, as the reviewer predicted; fixed.
- **Low:** the Pack dialog judges every format for the folder alone (an existing file at some default name hid the
  format); a Windows original that is read-only or not writable by its permissions is refused before anything is rebuilt
  (it ended in Interrupted operations); the job's own decisions (the old-ZIP-route guard and the executor) ask afresh,
  outside the two-second answers; a file changing while the probe reads it is left to the job; the docs list every
  admission rule; a Windows folder FileCat cannot hold is no longer called a link.

**OneDrive, checked on this machine with the owner's permission (read-only: attributes and sync-root queries; nothing in
OneDrive was opened, written or synced).** The owner's OneDrive holds the moved Desktop and Documents. Windows hides a
cloud folder's reparse point from ordinary processes (PowerShell opts out, which is why it showed one), so FileCat's own
attribute check saw OneDrive as an ordinary folder. The probe now asks the cloud files API whether a folder lies in a sync
root (`CfGetSyncRootInfoByPath`), which also covers iCloud and Dropbox. A test runs against the real OneDrive folder: the
root, Desktop and Documents are refused as cloud-synced, and a junction inside OneDrive (to `D:\Documents`) as a link.
This machine also has its Downloads, Documents, Music, Pictures and Videos moved to `D:` by junctions: archives there are
writable when opened at `D:`, and the message now says so. Its `C:` drive supports no NTFS transactions, so existing
archives there are read-only, as the probe says; `D:` supports them.

The next commit also took a note of that review too far: it added Darwin's generic access rights to the rights a macOS
deny entry may not hold, but `acl_get_perm_np` does not take them and fails, so the Mac run of `04dbc8fa` refused macOS's
own "deny delete" on Desktop, Documents and Downloads (its access-list test failed). The list is back to the rights
`acl_get_perm_np` takes.

The Mac run of `90806f12` also failed the macOS memory-monitor test once: under the parallel suite the sampler ran late
and the worker reached 543 MiB against its 256 MiB budget before it was stopped (usual peaks 266 to 341 MiB). That is the
documented nature of the owner's re-scoped gate (a sampled bound, never a ceiling); the test's allowance was a guess and
now bounds the overshoot at 768 MiB, well short of the 2,048 MiB the worker tries to fill.

**12 November, the eighth review and its fixes (rows 13 and 14).** An eighth independent reviewer read `2c453079`, the
target probe. It found no exception escaping a probe, no handle leaked, no access or metadata changed, and no way for
the deny-only access lists to let another user at the stage, the exchange or an edit folder. It found the probe still
let through two places the job then refuses, both fixed in the next commit:

- **High: Windows drives without NTFS transactions.** Every update is published in an NTFS transaction; this host's
  own system drive (a recent Windows build) supports none (`fsutil behavior query disabletxf C:`: "the volume does not
  support TxF"), while its other drives and the Windows VM do. An update there was rebuilt, verified and then refused,
  and ended in Interrupted operations. The probe now refuses an existing archive on such a drive (a ZIP keeps the old
  route), and the job refuses it before anything is rebuilt.
- **High: Linux and macOS archives the job's update admission refuses.** The job refuses an archive owned by another
  user, with special permission bits or file flags, or carrying extended attributes it cannot reproduce. Browsers tag
  every macOS download with one (`com.apple.metadata:kMDItemWhereFroms`), so a ZIP in Downloads passed the probe and was
  then refused with no old route. The probe now applies that same admission and returns its reason.

Also fixed: on Linux and macOS the probe judged a folder reached through a symbolic link by the link, although the job
resolves it (`ArchiveLocalPath`), so it now judges the resolved folder too; Windows folders OneDrive syncs (reparse
points the folder pins accept) are refused, as the text said; the macOS rule accepts only entries that deny deleting the
folder itself (a deny of adding or removing names would stop the exchange part way); the reasons say what the cause is
(root, an archive over 80 GiB, a drive that is read-only or not local); every format in the Pack dialog is judged at its
own name; and one shared judge per composition keeps its answers for two seconds (the key bar, menus and dialogs asked
the same thing many times a second; a job's execution always asks afresh). New tests: extended attributes on Linux and
macOS, a deny-adding entry on macOS, a symbolic link to a shared folder, the transaction support of the temporary
folder's drive on Windows.

Not covered by tests: a OneDrive folder (no test machine has OneDrive), and a macOS archive the installed, quarantined
application made itself (if macOS gives such files an attribute FileCat cannot keep, updating them would be refused
with that reason; to check on the owner-signed build).

**12 November, the listing's review and its fixes (rows 13 and 14).** A seventh independent reviewer read the listing
(`a1cd79be`) and the grace fix (`f4aa66a7`). It found no way to write somewhere unsafe: every misdetection it built
(junction and mount-point folders, `subst`, symbolic links, macOS firmlinks, ext2/3) still fails closed at the writer's own
checks. It found that listing would have broken writing in common places instead, all fixed in the next commit:

- **High: macOS Desktop, Documents and Downloads.** The writer refused any folder with an access list (ACL), and macOS
  puts `group:everyone deny delete` on the home folder and on Desktop, Documents, Downloads, Music, Pictures and Public
  (checked on the test Mac). An entry that only denies, and passes nothing on to new files, grants no one anything: such
  folders are now writable. An allow entry, an inheritable one, or an access list that cannot be read in full is still
  refused.
- **High: places the writer cannot write, found only when a command ran.** Routes were judged by the drive's file
  system alone, but the writer refuses a folder reached through a link, a junction or a mount point, a folder synced by
  OneDrive (its folders and files are reparse points, checked on this host), a folder another user can change (Linux
  and macOS), and an archive with another hard link. A ZIP in such a place lost the old route, which works there. Each
  writer now answers, before anything starts, whether it can write a place, using its own checks (a few handle opens
  that hold nothing; the archive itself is opened for its attributes only, sharing everything). A place it cannot write
  is not covered: a ZIP there keeps the old route, and the other formats say why. The job checks everything again.
- **Medium: an elevated FileCat** lost ZIP writing and learned it only when a command ran. Elevation is now one of those
  answers: a ZIP keeps the old route, the other formats say that FileCat must run without administrator rights (or not
  as root).
- **Medium: the release text promised that another hard link keeps the old content;** the writer refuses such an
  archive. The text now says so (a ZIP keeps the old route, where it still holds), as does the proposed ADR-07
  amendment.
- **Medium: an existing native edit root that is not this user's private one** (made by another account or computer,
  as on an external drive or a shared `--data` folder) would have refused every edit and the startup review. Startup now
  checks it too and keeps the legacy store then.
- **Native edit storage is not listed after all.** With it as the default, the Ubuntu App suite of `a1cd79be` failed 187
  edit cases. Some only inject faults into the legacy store's file operations, which the native store does not use; but
  others show that F4 behaves differently: a stopped F4 keeps its partly made edit for review instead of removing it,
  and the creation marker that makes a crash recoverable is visible while the copy runs. The application's edit flows
  were never qualified against the native store as the default, and its own checkpoint lists cancellation and
  interrupted creation as unfinished, so `NativeEditStorage.Qualified` stays empty (ledger rows 4 and 8). Editing
  archive members keeps working through the legacy store, as in every run so far. The startup checks above stay, for
  when it is listed.
- **Low:** three App tests assumed the legacy edit folder and now count both roots (or make their pre-switch edit in the
  legacy folder); two test predicates were tied to elevation or to `/var` and now ask what they depend on; a Windows
  network drive is excluded before anything is asked of it; an x64 build running emulated on Arm64 composes no route;
  where native edit storage is listed, startup logs which edit store it uses; the decision record, the ZIP design and
  the limits (64 GiB in all, an 80 GiB archive) are current.

Tests: the probes on real folders (Linux and macOS: a symbolic-link folder, a link and a hard-linked archive, a folder
others can change, and on macOS a deny-only, an allow and an inheritable access list; Windows: a junction, a hard link,
an elevated process, and another program holding the archive open), the route composition with a probe, and the native
root that is not private. The listing-revision tests now wait up to 60 s for a read: on the Windows VM during the
parallel Core suite, the background read's first very-low-priority I/O waited past their old 5 s (two failures at
`c1e93b58`).

**12 November, the routes listed (rows 11, 13 and 14).** After six independent reviews (the last found no High or Medium
defect, and its three Low findings are fixed in `f4aa66a7`), the routes of the [owner's decision](RELEASE_ROUTE_DECISION.md)
are listed in one change: `ArchiveWriteRoutes.Qualified` names all ten formats on win-x64 (NTFS), linux-x64 (ext4) and
osx-arm64 (APFS) (native edit storage was listed too, then withdrawn by the next commit: see the entry above). Each route
carries its evidence ID
(`ArchiveRouteEvidence`: `AWQ-win-x64-NTFS`, `AWQ-linux-x64-ext4`, `AWQ-osx-arm64-apfs`); the release manifest refuses a
listed route whose evidence it does not carry, under that ID or IDs below it. The pinning tests name every route, check
that other runtimes (win-arm64, linux-arm64) compose nothing and ask nothing, and that other file systems (FAT32, ReFS,
btrfs, the AppImage's FUSE mount, HFS+) are refused with their reason. The file system names are those .NET reports on
the three machines (`NTFS`, `ext4`, `apfs`, checked on each).

Listing the native edit store made it the default where it is qualified, so one more case was handled first: the store
pins its root's folder without following links, so a state folder reached through a link (an AppData moved with a
junction, a symbolic link to a home folder, macOS's `/tmp`) would have refused every new edit. Startup now opens that
folder once and keeps the legacy store, which works there, when it cannot be held without following links or by this
process (an elevated FileCat on Windows); it logs which store it uses, without the path. A test on Linux and macOS
composes through a real symbolic link and through the real path.

A second case: .NET reports a mapped network drive's file system as its server's (`NTFS` for a Windows share). The
Windows writer refuses remote volumes only when it runs, so a ZIP on a mapped drive would have left the old route, which
works there, for a worker route that fails late. A network drive now has no local file system for routes and the edit
store: a ZIP there keeps the old route and the other formats say why they are read-only, as on a UNC path.

The README and [CAPABILITIES.md](../../CAPABILITIES.md) now carry the [release text](RELEASE_TEXT_DRAFT.md). The final
re-validation of this change on all three platforms, its package lanes and the release manifest follow below.

**12 November, independent review and its fixes (row 14).** A reviewer read today's changes (`3c239903` to `9ceb9027`)
against the code. It found no way past the macOS sandbox boundary, and seven defects, all fixed:

1. Where a ZIP route was composed but its worker could not run (the AppImage, a framework-dependent build, a portable copy
   in a folder the user does not own), ZIP changes silently took the old in-process route. The route now decides by
   composition, `IArchiveJobBackend.Covers`: such a change is refused with the worker's reason, the job manager refuses
   any profile-less ZIP change a route covers (except a legacy edit commit, `ArchivePlan.LegacyEditCommit`), and Windows
   "Send to → Compressed folder" follows Pack.
2. The Windows portable grant gave the worker inherited read access to the whole folder: the portable `Data` state and,
   unpacked into Downloads, the user's files. It now grants exactly the runtime files `FileCat.deps.json` names (with the
   host, deps and runtimeconfig files), each by its own entry, and traverse only on their folders; an earlier whole-folder
   grant is withdrawn. Availability checks every runtime file, deny entries with generic rights and inherit-only entries.
3. A ZIP listing on the worker route hashed the whole archive under the provider's global lock, uncancellable. Listings
   now carry a token and show their rows at once (`ArchiveListingRevision`). The ZIP index no longer reads its
   FileStream's handle from other threads (that flushed a buffer another read used). The native formats do the same.
4. F4 on the worker route skipped an edit session started on the old route. It now shows or reopens that session.
5. macOS: the package checks the launcher's entitlements exactly; the worker also checks an outbound connect and the user
   folders; `RLIMIT_FSIZE` 0 limits the container to empty files; an app another user installed is accepted; two comments
   no longer overstate the boundary.
6. Linux: the AppImage refusal applies only to a bundle inside `APPDIR`, not to a .deb started from an AppImage's process.
7. The release manifest refuses a tool or payload built from another commit than the one it names, and reads git's two
   streams at once.

A second reviewer then read the fix commit (`9db8a01b`). It found no remaining legacy route and no lock or lease defect,
and four problems, all fixed in the next commit:

- **The earlier whole-folder Windows grant stayed.** A copy that build prepared looked readable, so the worker start
  never withdrew the grant. A portable copy whose folder still carries an inheritable grant for the worker is now
  prepared again at each worker start, which withdraws it. The test now takes that path, through `EnsureRuntimeReadAccess`.
- **Binding a command to its listing rested on the change token alone, for as long as the listing stayed open.** On an
  NTFS volume without a change journal, that token is only the change time, which a writer can restore. As soon as a
  listing is shown, the archive is now read in the background: outside the lock, at low I/O priority (Windows background
  mode, macOS throttled, Linux idle class), and stopped when the index closes. A command waits for that read. The binding
  window is again only the time of the read, as when the listing read the whole archive. Archives over 1 GiB are read
  when the first change needs them, and the limit is documented. A revision once read outlives its index, so a metadata
  change or a cache eviction no longer makes rows on screen ask for a refresh.
- **An archive over the 80 GiB write limit asked for a refresh at every command.** Its members are now read-only, with
  that reason. A short read counts as a change.
- **A test comment claimed journal behaviour that does not exist.** It now checks that the edit store's commit plans
  carry the marker.

A third reviewer read `1fd1f5b6`, the background reads. It found seven defects, all fixed in the next commit:

- Every enumeration started a read, not only a panel's listing; a Find over archives started one per archive. Only a
  shown listing (`IEnumerationSink.Shown`, set by the panel's listing) now starts one.
- Closing an index cancelled a read a command waited for, and a cancelled command left its read running at normal
  priority. A read now stops only when no command waits for it and it is not the shown listing's own read (which drops
  back to low priority). A resolved index is touched, so it is the last to leave the cache.
- A change a read saw could be read past on retry. It is now permanent for that listing.
- On Windows a low-priority read on the listing's own synchronous file object could hold up member reads through it. It
  now reads through a file object of its own (`ReOpenFile`).
- A lease leaked if the read's thread could not start. It is now returned.
- The Windows withdrawal also removed deny entries. It now removes only allow entries.
- Linux restored the default I/O priority, not the thread's own. It now restores the previous value (macOS likewise).

New tests cover the read's lifetime on a real file: the size limits, closing while nothing or a command waits, a command
giving up, a change seen once, and the deny entry kept on Windows. A closing index now releases the revision outside its
own lock, which avoids a lock-order inversion.

A fourth reviewer read `12f924d5`. It found no lock, lease or handle defect. It did find that the shared-read state machine
still had edge cases:
- A change seen once stuck to a reused index, so a refresh did not help.
- On Linux before 5.15, restoring the priority could fail.
- A command could inherit a read that had just been stopped.
- A background read outlived the panel that showed it.

The next commit replaces the shared read with two kinds of read that never share. While a panel shows the listing, one
background read runs at low priority. It stops when no panel shows the listing (the panel's listing pipeline ends it),
when the index closes, or when a command starts reading; its thread ends with it, so no priority is restored. A command
that finds no revision read yet reads the archive itself, at normal priority, stops with the command, and is kept open
by its caller's lease. A change any read sees marks the listing's index stale, so a refresh lists the archive anew with
a new token. A change token that cannot be read at the moment is an error to try again, not a change.

A fifth reviewer read `c1e93b58`. It found no way to bind a command to a listing other than the one shown, and five
problems, all fixed in the next commit:

- Moving to another folder of the same archive ended the shown listing's read, and the next listing started it again
  from the beginning. A read now stops 10 seconds after the last panel stopped showing the listing; a listing of the same
  archive shown within that time keeps it going.
- A panel's showing held the revision, and through it the index's catalog, after the cache had dropped that index. It
  now holds the revision weakly; only a running read keeps it.
- Starting a read took the index's lease under the revision's lock, and that lease waits for the index's own lock; a
  panel's tab closing on the UI thread could wait behind a member being decompressed. The read's own thread now takes
  the lease.
- A listing shown while a command read the archive started a second read. None starts while a command reads.
- On Windows a change token that lost its USN part for a moment (a journal query that failed) counted as a change, final
  for the listing. Where the parts both tokens carry agree, a token of another shape now proves neither, and the
  revision read reports an error to try again (`SourceChangeToken.Holds`); parts that differ are still a change. Member
  reads and cache reuse still treat any other token as a change, which refuses on the safe side. A password-protected
  archive that a revision read finds changed while its read view opens says so, rather than that it cannot keep a
  password session (rare: a stale cached index is otherwise rebuilt, and its password asked again).

Esc while a listing loads or refreshes ends its read as well, after the same grace; a command then reads the archive
itself. The tests now prove each stop on its own (closing, a command, hiding, the grace's end, being shown again), that
a panel's showing does not keep the revision in memory, and that the listing pipeline ends a shown listing's work on a
refresh, a navigation and a closed tab.

A sixth reviewer read `717c2cee`. It found no High or Medium defect: the safety property holds, no token source is used
after disposal, the counters balance and the lock order holds. It found three Low problems, all fixed in the next
commit:

- A grace timer that had already fired as the listing was shown again could still stop its read (`CancelAfter` cannot
  recall a timer the timer queue has picked). The grace is now a timer whose callback decides under the revision's lock,
  and only for its own generation, so being shown again, or a later hiding, makes an earlier timer stop nothing.
- The grace test slept past a one-second timer, which a stall on a loaded machine could defeat. The grace's clock is now
  injectable, and the tests fire its timers themselves: after the listing was shown again, and from an earlier hiding.
- The listing-model test could see a refresh's rows before its provider kept the work it checks. The provider keeps the
  work before its rows, as the archive providers do.

From its notes: a read that starts now first checks whether the revision was read or seen changed meanwhile; a test
holds the index's lock while a panel hides and shows the listing, which no longer waits for it; and the sentences above
on token shapes and the password message now say exactly what the code does.

Two timing-fragile tests outside archive writing failed once each, under parallel load. One was the keyboard reference
test, which waited a fixed 20 ms for the dialog; it now waits for the parts it uses. The other was a Windows icon test,
which is under observation. The Ubuntu VM's ssh session had no login keyring collection, so the system secret store test
could not pass there; the collection was created on the VM (password `a`), so that test can now run.

Re-validation of `1fd1f5b6`, before the third review: on macOS every suite passed except the keyboard reference test
above, and the package and packaged-launcher lanes passed (14 of 14). On Ubuntu every suite passed except the keyring
test, and all package lanes passed.

Re-validation of `9db8a01b`, before the second review:
- **macOS:** full regression passed: App 2,833, Core 9,102, Remote 2,638, Rendering 23, with 10 sandboxed creation flows
  and the ZIP lanes. The only failure was a probe bug: .NET reports the file-size limit's EFBIG as
  `ArgumentOutOfRangeException`, which the container-write check did not catch. With that fixed (`d3077ded`), the 14
  boundary tests passed with the published launcher, and again with the packaged app's launcher. That launcher was
  byte-identical, carried exactly the App Sandbox entitlement, and every denial held, including writes in its container.
  The package route lane passed 12 of 12.
- **Ubuntu:** full regression passed: App 2,835, Core 9,162, Remote 2,543, Rendering 23. The only failure was the
  system keyring test, which failed identically before this work: the VM's ssh session has no login keyring collection.
  The .deb route lane passed 12 of 12, along with its bundle check and the AppImage refusal under `APPDIR`.

**12 November, macOS archive workers in the App Sandbox (route decision, rows 3 and 13).** A C launcher with its own
Info.plist and only the App Sandbox entitlement removes process creation, inherited descriptors and the bootstrap port,
then executes the application in worker mode. The worker checks those facts and the sandbox's denials before input and
exits when its parent does. The parent samples the footprint every 5 ms and stops the worker above 768 MiB, the owner's
re-scoped gate. On the Mac, run `mac-sbx-v5` at `92a2a2c3` passed 14 boundary tests and 10 real-dialog create-and-add
flows with real workers. The runs found and fixed a disposal deadlock and codesign's refusal of commented or CRLF
entitlements. Details and limits: [record](MACOS_SANDBOX_WORKER.md). No route is listed.

**12 November, route decision recorded (rows 6, 12, 13).** The owner chose the first release routes: win-x64 on NTFS,
linux-x64 on ext4 and osx-arm64 on APFS, every native format and ZIP through the worker profile, native edit storage on all
three, and listing only after the independent review (row 14). For macOS the owner re-scoped the memory gate to footprint
monitoring with termination; FileCat takes the App Sandbox helper of the plan's two macOS options. The
[decision record](RELEASE_ROUTE_DECISION.md) lists what must hold before the listing. Nothing is listed by it.

**12 November, native edit store composed behind its qualification gate (rows 4, 8, 13).** `NativeEditStorage` lists the
runtimes and filesystems on which ordinary startup composes the native, private edit store (its own `edit-sessions-native`
root beside the legacy folder, the platform's private leases); none is listed, so startup keeps the legacy store and probes
nothing, as `ArchiveWriteRoutes` keeps the archive backends unregistered (`8c07260a`). Through the real application wiring a
listed runtime creates new edits in native storage while older ones stay and both load. Passed on macOS, Ubuntu and the
Windows VM (App suites 2,812 / 2,824 / 2,916, edit and path Core tests 580 / 582 / 253, Windows edit platform tests 332), no
failure. The plan's remaining items are the owner's: which routes and runtimes to qualify (with the ZIP switch to the
worker profile, row 6), the final candidate's packages and signing, and the independent review.

**12 November, fuzz gates complete; fixed commit `489177af` re-qualified (row 7, §13.6).** Details in
[FUZZ_CAMPAIGN_SETUP.md](FUZZ_CAMPAIGN_SETUP.md):

- **Campaign on `62f0a457`.** All 47 transports pass host verification: 1.42 billion executions, no crash, leak, dump or
  oracle skip. Every family reaches 24 CPU-hours on actual CPU (TAR 25.6, compressed streams 28.0, 7z 27.5, RAR5 29.3,
  shared 29.3) except ZIP (15.9): with 11 units on 12 vCPUs its multithreaded runs earned 0.74 CPU-seconds per second,
  and ZIP's code changed afterwards anyway (`1f51bd15`), so the ZIP family is repeated at the fixed commit. Fourteen
  clean runs that ended below their planned per-run floor were sealed at the CPU they earned; new plans count on at most
  one CPU-second per second.
- **Fixed commit `489177af`** (product source as `1f51bd15`). Full regressions: macOS passes; the Windows VM's one
  failure was a test that ignored its VHD-backed S: (fixed in `5b6a5836`, passes on the VM); Ubuntu fails only
  `SecretStoreTests`, whose VM keyring has no login collection. Platform floors: macOS 6.762 and Windows 4.801
  CPU-hours (plan 4) from the image's exact seeds, plus a Windows-rooted record pass; corpus-seeded passes add 25.4
  (macOS) and 11.7 (Windows) CPU-hours. The writer scale theory passes under the Windows standard token at 100,000
  members for every engine. The final corpus (465,380 inputs) replays with no failure on macOS, Linux and Windows.
  The ZIP family on image `fuzz-candidate-v5` at `489177af`: six transports verified, 33.71 CPU-hours (floor 24),
  14.06 million executions, no crash, leak, dump or oracle skip. Every §13.6 fuzz floor now holds for the fixed
  commit.
- **Test-tooling defects fixed** (no product change): structured lanes ended on a Windows scanner holding their report
  file and published 23 times a second on fast targets (`b1253274`); repeated transient unit names blocked the second
  image's canary (`96b63086`); Unix evidence carried 220 MiB of published apps (`d72445df`); runners now refuse on a
  nearly full disk (`c6f1df11`).

**11 November, native fuzz campaign on candidate `62f0a457` (row 7, §13.3, §13.6).** Details in
[FUZZ_CAMPAIGN_SETUP.md](FUZZ_CAMPAIGN_SETUP.md):

- **Two product defects found and fixed.** The edit-record parser accepted a record whose working path is its own
  session folder as a usable edit (`37ffe408`). The 7z browse reader let a filter after a PPMd coder read the decoder's
  overrun bytes, so PPMd + Swap 7z archives, including FileCat's own, read back with a changed tail and failed FileCat's
  CRC check, while real 7-Zip reads them (`d7287a35`, vendored SharpCompress, recorded in its changes file). Each has a
  regression test that fails without the fix; the Mac passed 216/216 in the edit-session and 7z writing classes.
- **A third defect, found beside the campaign and fixed after the candidate.** The ZIP rewrite allocated each new
  payload's data descriptor on the stack inside its member loop, so the frame grew per added or replaced file until the
  rebuild returned; tens of thousands of files (requests admit 100,000) overflow a 1–1.5 MiB worker stack and end the
  process (`1f51bd15`; the regression test's 20,000 files kill the old engine on a 256 KiB thread). Only `zip-archives`
  reaches the changed method, and its collected corpus replays with identical results at the fixed commit. A new opt-in
  AT18 theory writes and strictly reads 100,000 members with every writer engine on a 1 MiB thread (`48a321ad`); all
  ten profiles pass in linear time on the host.
- **Windows path checks of session records and saved-copy intents** were never reached by the earlier Windows
  structured lanes, whose Linux seeds name Unix roots (none of 70 million inputs accepted; the Windows floor without the
  two targets is still 4.753 CPU-hours). Seeds rewritten for Windows (`86f03a28`) reach them: 155,246 and 27,825
  accepted inputs in 30 minutes each, no failure. An 85-input probe of Windows spellings found every non-canonical form
  refused, and Windows-only theories pin them (`8197bad8`).
- **Interim corpus replay** (38,230 inputs from every short run and the first four long runs): no failure on macOS arm64
  (27 targets) or Windows x64 (26 targets).
- **Fuzz coverage widened.** All of Core's archive, edit-session and journal code, the whole writing namespace and the
  vendored codecs are now instrumented; new `sevenzip-writer` and `rar5-writer` targets cover the 7z and RAR5 writers
  against independent readers. All 27 targets passed canary, replay and short runs on image `fuzz-candidate-v4`.
- **Test-environment faults fixed.** The fuzz units' file-size cap also capped .NET's JIT-code memory, PrivateDevices
  left capabilities that FileCat's identity check refuses, and in-process roles kept LZMA tables in thread-pool caches;
  none was a product defect.
- **Other RIDs.** Structured mutation from the image's exact seeds passed on macOS arm64 (27 targets, 6.471
  CPU-hours) and Windows x64 (26 targets, 5.179 CPU-hours in two passes), with no failure; the per-RID floor is 4.
- **Windows publication races (FZ10).** A new Windows lane runs the publication-race oracle against the confined
  worker and NTFS recipes: 179/179 on the VM's standard token, and 121 of 200 seeded random rounds published.
- **Regression at `9f716c75`** (both fixes, Windows FZ10 test). Mac: Core 344 + 8,731, Remote 2,638, Rendering 23,
  App 2,810 passed, no failure; one new skip is a Linux-only lease scenario added in C3. Windows VM (standard token,
  one lane at a time): Core 8,313, Platform 422 + 1,163, Remote 2,405, Rendering 24, App 2,914 passed; the one failure
  is `DeviceReadTests.A_folder_reached_through_a_link_is_placed_where_the_link_leads`, which finds this VM's
  VHD-backed `S:` on `C:`'s disk and fails the same way on the base commit (see the v21d entry). Evidence
  `full-v22a-mac` `72f0bab5…`, `full-v22a-win` `70cefeb4…`. The Ubuntu suite follows the campaign.
- **Running.** 47 sealed-and-verified runs, 25 CPU-hours per format family and 25.3 shared (plan: 24), on the 12-vCPU
  Ubuntu VM; 11 runs are sealed and verified on the host so far. Completion, host verification of every transport and,
  at the fixed commit, the final corpus replay on each RID and the ZIP family's floor remain open.

**11 November, packaging dry run (row 12, §16.1, AT20).** Commit `d44c3d86`, packaged from a clean checkout of exactly
that commit on each system:

- **It found a blocker.** Archive writing extended FileCat's SharpCompress fork and its inventory
  (`licenses/sharpcompress/COMPONENTS.md`, `SHA256SUMS`), but `eng/publish.ps1`, `eng/package-linux.sh` and
  `eng/package-macos.sh` still pinned the earlier notice set, so none of them could build a package from this branch
  ("licenses/sharpcompress/SHA256SUMS differs from the reviewed notice set"). The new inventory entries match the
  files' own headers (public-domain LZMA SDK 26.05 ports, FileCat's own PPMd7 encoding adapter and swap filter); no
  license text and no package dependency changed. The three scripts now pin `b036603d…`; release evidence E-I14
  records the amendment, which needs the same hosted-CI acceptance before release.
- **Windows (host).** Setup payload, portable and framework-dependent builds: 125 dependency notices verified in each,
  the SharpCompress notice set accepted, exact-payload SBOMs of 399, 395 and 207 files (34 reviewed packages).
  Portable zip `e8954397…`, framework-dependent zip `40f44ffe…`.
- **Ubuntu 24.04.5.** Tarball `9a5e8889…`, `.deb` `d3a919d5…`, AppImage `aa296ff2…` with its runtime source, three
  SBOMs (33 reviewed packages; the AppImage's lists 411 files). `appimagetool` 1.9.1 was the VM's existing copy with
  the pinned checksum; nothing was downloaded. The release register validator passes (628 IDs, 981 evidence entries,
  82 acceptance cases).
- **macOS 27 (arm64).** App zip `1c133721…`, SBOM of 389 files; `codesign --verify --deep --strict` passes and the
  unpacked app starts (`FileCat 1.0.0-dev`). Ad-hoc signed only, not notarized.
- **Payload inspection.** No `deps.json` and no folder holds the upstream SharpCompress package, fuzz instrumentation,
  a test framework, a compiler or a CLI oracle; the only executables are FileCat's three and the runtime's
  `createdump`. The archive worker roles ship inside `FileCat.Archives.dll`, run by `FileCat.exe` with a worker
  argument. The dedicated worker package (`FileCat.ArchiveWorker` over `FileCat.Archives.Writing`, the same sources
  without the browsing readers) is built but not packaged; the route decisions must state which one ships.
- **Still open for row 12.** The final candidate's packages, signing (a trusted Windows signing provider, macOS
  notarization), installed and portable route tests, and the release manifest.

**11 November, last: commit captures cleaned up on Unix; retained copies and old edit folders removed on request;
dialog text aligned.** Commit `92073c0e` (plan §12.1, R21, AT16):

- **Commit captures on Unix.** Each native commit takes a private capture (`commit-<id>`: records and the reviewed
  payload). On Unix its cleanup kept everything ("kept for native cleanup review"), so every commit left a copy. The
  folders a capture creates may now remove their own reviewed entries under the cleanup rule, with a complete content
  review right before each name goes (as Windows does). The first run found that a removed folder's lease stayed open
  on Unix, so its parent counted a live child and kept itself; the lease now ends with the folder, as on Windows. The
  shared snapshot matrix expects removal on every system.
- **Restart recovery action.** A retained copy that a stopped commit or cleanup left can be removed from its review
  ("Remove retained copy…", danger, confirmed): a fresh review of the edit's record chain and of that copy, then removal
  of only that copy; the edit, its working copy and other copies stay, and a copy a saved copy refers to is kept. Twelve
  shared cases (complete, interrupted with and without a payload, two copies, extra child or file, changed or replaced
  payload, an out-of-date record, a wrong folder, a live commit, cancellation) and the UI path.
- **Old-folder retirement (§12.1).** An adopted copy retires its old folder ("Retire original folder…"): the adoption
  review and an exact-removal probe first (Linux lease, Windows sharing), then a durable retiring revision that makes
  the copy the edit, then the old objects one by one, each bound to the identity and bytes recorded at adoption, then a
  revision that drops the relationship. A stop resumes; a changed old copy or an extra file is kept and named; "Cancel
  retirement…" returns to the old edit while it is intact. Thirteen shared cases (three need Unix, one of them Linux)
  and the UI path; discard now accepts the history of an adopted edit. The
  application still composes only the legacy store.
- **Independent review of the deletion code, and its fixes.** A separate review of this candidate's removal code found:
  on Unix the content was hashed before the Linux lease was taken (an in-place save in that gap would be removed); an
  atomic save renamed onto the name between the name check and `unlinkat` would be the file removed; a lease break
  signals SIGIO, which ends the process by default, so another program opening a file during a lease could end FileCat
  (this also held for the committed discard); any lookup error on the old folder counted as "already removed"; a
  retirement that could not finish had no way back; and native identity errors escaped as exceptions. Every Unix file
  removal now takes the lease first (SIGIO ignored), reviews the content under it, sets the name aside by an exclusive
  rename and removes it only if that name binds the held object with the lease unbroken, restoring it otherwise; only
  ENOENT counts as removed; a retirement can be cancelled while the old edit is intact; the application holds the old
  edit's commit off during a retirement; a failed final record after a complete removal says so; the Windows remover
  also requires a single link. New cases stage an atomic save at the moment of removal and (Linux) another program
  opening the file during the lease: both keep the file, FileCat survives the break, and the opener reads intact bytes.
- **Theme captures (§11.8).** The shipped Create dialog in all eight themes is legible; the captures showed its text
  lines 50 px right of a widened path box (a stretched block narrower than its row is centered). They are left-aligned
  now, with a wide-window layout case that fails at 51 px without the fix.
- **A test race on Windows.** The adoption kill test read the child's marker while another program (the VM's scanner or
  indexer) briefly held the newly renamed file; it now retries a sharing violation.
- **What the reruns found.** The first Linux run of the reworked removal failed everywhere it removed a file: the
  working-file review checked the name's binding by reopening the file, and under FileCat's own lease Linux refuses that
  non-blocking open (errno 11) and starts a break of the lease. The binding is now read with `fstatat`, without opening
  the file. Mac had passed (no leases). On Windows a test helper read the old folder without sharing while the staged
  editor held a file open for writing; it now reads with sharing. Two older Unix cases still expected a removable
  folder and a capture to be kept (`remove-empty`, `cleanup-admission`); they expect removal now, as on Windows.
- **Validation (v21l, then v21m; Windows one lane at a time).** Focused first: Mac and Ubuntu passed 341 edit, discard,
  capture, adoption and retirement cases and 6 UI cases (v21k). Full v21l: Mac Core 344 + 8,716, Remote 2,638,
  Rendering 23, App 2,810 passed, no failure; Ubuntu Core 355 + 8,778, Remote 2,543, Rendering 23, App 2,822 passed
  (`SecretStoreTests` only for the VM keyring); Windows Core 8,297, Platform 243 + 1,165, Remote 2,405, Rendering 24,
  App 2,914 passed. Besides the known link case (`DeviceReadTests`), two Windows cases failed: the elevation
  download-mark case, because another program briefly held a new download's `Zone.Identifier` stream, and the 64-source
  TAR.BZ2 LPAC verification, whose worker ran out of its time allowance while the Ubuntu VM's full run loaded the same
  host; both passed alone (5/5). v21m turns the retirement cases that need Unix into skips elsewhere (they had returned
  early as passes): Mac 27 passed and 1 skipped, Ubuntu 28, Windows 25 passed and 3 skipped.

**11 November, later still: a failing drive is named; refusals that provably published nothing keep their text; Unix
edits are discarded exactly.** Commit `aa9d8a13` (AT08, plan §11.7, §12.1, R21):

- **A real failing drive (AT08).** `fault_volume.sh` (root, test machines only) puts ext4 on a device-mapper target over a
  loop image and, on request, swaps in dm-flakey `error_writes` (reads keep working), then back with `e2fsck -fy` and a
  fresh mount. Measured on Ubuntu 24.04.5: the first new folder's `mkdir` returns EIO, ext4 aborts its journal, and
  every later change returns EROFS. FileCat's stage-folder creation turned that EIO into a refusal without its errno, so
  the job showed its raw wrapper ("The created private archive artifact requires review: …"). Native opens, the stage
  folder's `mkdirat` and `fsync` now keep the errno, and a drive error says "The drive reported an error before the
  archive was saved, so no archive was changed. Check the drive, then try again." (once saving may have begun: "…while
  the archive was being saved. Check the drive, and review the archive before continuing."). A file system that turned
  read-only, or a write-protected medium on Windows, is worded as a read-only drive, not as a permission problem.
  `A_drive_that_fails_writes_refuses_with_the_drive_text_and_keeps_the_original` fails the drive during an update and
  checks the text, the original byte for byte (also after the check and remount), the reviewed recovery discard and a
  clean folder: 14/14 with the message tests (v21c), and inside the full v21d storage shard.
- **Saving-phase wording.** The overloaded Windows run below showed a small NTFS volume refusing at the space review
  before the content snapshot, at the start of Saving, with "FileCat could not confirm which version is now at the
  archive path. Both available versions were kept." The journal proved nothing was published and the stage was
  discarded. A refusal that durable facts prove unpublished (that review, a refused Unix exchange) now keeps the text
  before saving; the small-volume case accepts either full-drive refusal and never the unconfirmed text.
- **Reviewed discard of native edits on Unix (§12.1, R21).** The archive transactions' private cleanup rules
  ([decision](UNIX_PRIVATE_CLEANUP_DECISION.md)) now apply to edit sessions: a file's name is removed only while it
  binds the held, reviewed object, which must then have no link left (no truncation: an editor may have it mapped); on
  Linux a write lease first proves that no other program has it open, as Windows' sharing refuses an open editor;
  folders only once empty and still bound. All 40 discard scenarios run on Unix with platform expectations (Mac v21a
  760 passed in the edit classes, Ubuntu v21a 40/40). A discard is refused while a commit of the same edit is reading
  its capture in this process (macOS shows no sharing for it).
- **Windows: lanes one at a time.** The v20g Windows run started every lane at once on the 8 GB, 8-vCPU VM: Core took
  42 minutes instead of 9, and 41 Core, 56 Platform, 1 Remote, 1 Rendering and many App cases failed on worker time
  allowances and fixed test deadlines. It is not evidence; the run was stopped and repeated sequentially on v21d.
- **Validation (v21d, all lanes; Windows one lane at a time).** Mac: Core 344 + 8,691, Remote 2,638, Rendering 23, App
  2,807 passed, no failure. Ubuntu: Core 355 (with the failing-drive case) + 8,753, Remote 2,543, Rendering 23, App
  2,819 passed; `SecretStoreTests` fails only for the VM keyring, as before. Windows (standard user, one lane at a
  time): Core 8,297, Platform 244 + 1,140, Remote 2,405, Rendering 24, App 2,911 passed. Two Platform failures:
  `DeviceReadTests.A_folder_reached_through_a_link_is_placed_where_the_link_leads` (this VM's VHD-backed `S:`, failing
  the same way on the base commit) and the adoption kill test at its first boundary, which read the child's marker
  while another program briefly held the renamed file (a sharing violation; the test retries it from the next commit).

**11 November, later: archive dialogs speak their values, counts and reasons; keyboard users reach a waiting decision;
the 50 ms UI budget measured live on Windows and Ubuntu.** Commit `e89f9814` (UX audit C, plan §11.8, AT16):

- **Values of closed combo boxes on Windows.** NVDA said nothing when Down chose "Protect contents" in the closed
  password-protection box: Avalonia 12.1's UI Automation bridge raises no value change (its property map has no entry
  for the value), as its AT-SPI bridge raised none for Orca (answered earlier with an announcement). The box's own node
  now raises UI Automation's value change through the bridge's raise function, which disables itself if that member
  goes away. NVDA now says "Protect contents", then the polite problem "Enter a password or choose No password."
- **Format details by name.** The 7z options and "What is included" expanders are named. Both journeys hear, by Tab:
  Compression, "7z options" (both readers call the expanded header "pressed"), 7z compression method, preprocessing,
  dictionary and header compression, Password protection, Protect contents, Archive password, Confirm archive password.
- **Unavailable reasons.** A disabled menu-bar command shows its reason while disabled and carries it as help text; the
  keyboard reference marks an unavailable command "Unavailable: <reason>".
- **Held Enter.** "Replace compressed contents" (the old contents are not kept) opened on Replace in both routes (Pack
  onto an existing compressed file, F5 onto one): it now opens on Cancel. The format-scan modal's only button, Cancel,
  was the default, so the Enter that asked for a check (still repeating while held) cancelled it: no button is the
  default now, focus starts on the named progress ("Checking <archive>"), and Escape cancels. The actual-worker
  standalone lane presses Enter twice on both replacement questions and finds the contents unchanged.
- **In-job questions.** A screen reader reads the focused button of an in-job question, not its message, so counts and
  examples went unheard: the choice focused first now speaks them (the first example included). Archive reviews cover
  the whole operation and no longer offer "Do the same for similar items". The collision message counts in the
  singular for one name, and "Add 1 items" reads "Add 1 item".
- **Keyboard path to a waiting decision.** Background work never takes the keyboard; its notice says "Choose Review
  decision in Operations (Ctrl+J)". Both journeys found that Ctrl+J only showed the drawer and Tab switched panels, so
  no keyboard user could reach "Review decision…"; with an operation row focused, panel keys (Enter, Delete, F8) also
  acted on the panel's file. From the keyboard Ctrl+J now takes the keyboard into Operations, on the waiting decision
  first; there Tab cycles, only global shortcuts act, Esc returns to the panel and Ctrl+J closes Operations.
- **Journeys (v20f).** Stage 0 (format details) and stage 4 (copying a.txt into the TAR + gzip archive that holds it:
  the notice, Ctrl+J, Space on "Review decision…", the question with Skip focused saying "1 incoming name can replace an
  existing file. Choose Skip to keep all existing members. Example: "a.txt"", Escape cancels, the archive unchanged)
  join the earlier stages. Orca's row navigation now ignores a row spoken before Home took effect (a panel's late focus
  announcement had stopped it on ".."). NVDA 2026.3.1 passes every stage (report `E6F28F6A…`, host `71A1D450…`, worker
  `5706233E…`); Orca 46.1 passes every stage (report `5d583a91…`, host `25d3cea3…`).
- **50 ms UI budget, live.** The qualification host's `--ui-budget` probe (a 10 ms UI-thread timer) notes every gap
  over 50 ms with its time; drivers on Windows and Ubuntu attribute gaps to idle, navigation, packing 20,000 small files
  as TAR + gzip with keys going, opening that archive and adding a file to it. Ubuntu (13,287 ticks): no gap in any
  window; the five gaps were four within 1.6 s of launch and one of 62 ms when the Create dialog first opened. Windows
  (13,581 ticks): none in 160 s of packing (1,007 key presses) or while opening; in the add window two gaps of 65 and
  62 ms, of which 59 and 51 ms were garbage-collection pauses; launch (five, up to 2 s) and the Create dialog's first
  opening (84 ms, and 56 ms mostly collection) account for the rest (reports `A73B7733…`, `f4b4ab17…`). In the VM, Windows
  maintenance (scheduled defrag and disk cleanup, plus servicing) kept the virtual disk at 60–90 MB/s and NVDA needed
  over two minutes to start; the runner now ends the running defrag and cleanup instances first (nothing is
  reconfigured) and waits up to 150 s.
- **Ubuntu queued checks.** The never-executed exact-commit queues are superseded by the current-code full regression
  v19m9 on Ubuntu 24.04.5: marked creation 87 passed, exact-origin update stage 10, restoration preparation 8.
- **Worker-monitor refusals.** No failed Ubuntu result since v15c involved a worker refusal (v15c, v16 and its parallel
  run, v17, v18, v19, v19m7, v19m9, unix-create-v3); `soak_worker_monitor.sh` now replays the TAR pipelines, worker
  lifecycle and native storage classes of a built run repeatedly, three shards at once: six iterations on the v20g
  build (23 minutes) gave 3,060 passes, no failure and no worker refusal. The cause of the two early refusals stays
  unknown; nothing here claims a fix for it.
- **Validation (v20g; the corrected standalone steps rerun).** Mac: Core 344 + 8,652, Remote 2,638, Rendering 23, App
  2,804 passed. Ubuntu: Core 354 + 8,714 (`SecretStoreTests`, the VM keyring), Remote 2,543, Rendering 23, App 2,816.
  On both, the three actual-worker standalone cases failed first: the new F5 step clicked Copy before the dialog
  enabled it; with the step waiting for an enabled button the class passes 7/7 (three Windows-only skips) on both.
  FZ10 `publication-races` replay passes on both (61 published, 120 refused). Windows VM: the v20g run
  started every lane at once and is not evidence (see the entry above); the one-lane-at-a-time v21d run of the next
  commit's tree, which contains this one, is.
- **Not caused by this branch.** This development host runs elevated, and FileCat refuses user-profile edit sessions as
  administrator, so 194 App tests in the rerun classes fail here identically on the committed parent `48f503c6` and on
  this candidate; the Windows VM's standard-user lane, the reference, runs them.

**11 November: full suites on all three platforms, two regressions of this branch fixed; NVDA hears archive phases;
Ubuntu extraction within its budget (R22); FZ10 publication-race target; AT18 memory measured as live bytes.** Commit
`a49f2b54`:

- **Save-copy stage cleanup (regression of `38e56944`, R21).** The bound cleanup removed FileCat's save-copy stage only
  when the stage's identity had been recorded, and the identity was read only after a successful copy, so a copy that
  failed after writing part of the stage left `.filecat-edit-copy-….tmp` beside the destination (Ubuntu's full App suite;
  the branch's base `42829297` passes the same 47 cases there). The identity is now read by name as soon as the copy
  ends, after a failure as after success (the same observation the success path always made), so a partial stage is
  removed under the same rules as a complete one: on Unix only in a folder no other user can change while the name binds
  that identity, on Windows through a handle bound to it; anything else is kept and logged. New tests remove a partial
  stage in a private Unix folder, keep it in a shared one, and remove it through a bound handle with the Windows adapter.
  The App tests' fault-injecting adapter now takes file identities and bound deletion from the adapter production uses
  (on Windows `WindowsFileOperations`); without them the Windows destination-failure and shutdown cases kept their
  stage (seven failures in the intermediate v19m7 Windows App suite).
- **User-private-group folders.** `A_group_writable_destination_keeps_the_copy_unpublished` predates the rule from
  `2acacfe3` that a folder writable only by the user's own private group is private (Debian, Ubuntu, Fedora), so it
  published on Ubuntu. It now makes the folder writable by a group the user shares with others, or by everyone when the
  user is in no such group, and asserts the folder no longer counts as private; the copy stays unpublished.
- **NVDA and archive phases.** A UI Automation trace of a 256 MiB pack in the Windows VM saw no event at all from the
  operation strip's phase text, while the final notification raised its name-change and live-region events. Avalonia
  raises both for every phase, but UI Automation places an event by walking up the element's parents, and the strip is
  hidden until the first operation: a parent lookup while it was hidden left the strip's own peer without a parent
  (Avalonia caches that as known), and FileCat's repair only covered a missing parent directly above the live element.
  The repair now walks the whole chain to the window and relinks it from the window down when it does not arrive. A
  headless test reproduces the gap (no phase reaches the window, with the old check) and passes with the fix. In the VM,
  the trace on the fixed build sees Preparing, Rebuilding, Checking and Saving, each placed in the window, and the NVDA
  2026.3.1 journey now passes completely: dialog and path, every format, the one-file refusal, "Rebuilding", "Checking"
  and "Saving" while packing (NVDA's speech for the returning focus took the place of "Preparing"), the gzip file, and F7,
  F2 and F8 refused inside it (report SHA-256 `CAEF035D…`, host `71A1D450…`, worker `5706233E…`).
- **FZ10 `publication-races`** (fuzz setup record): the real Unix job route with up to six actions of another program
  of the same user around every durable transaction, cleanup and discard record; no version is lost, no concurrent
  version is displaced silently, nothing torn reaches the name. Its 181 seeds (each action alone at eight points for
  creation and update, plus pairs around publication) replay without an oracle violation on the Mac and Ubuntu: 61
  published and 120 refused on each. Writing it found two things: the claim message below, and a fixture defect (macOS
  `/var` is a link, which `JobManager.SubmitArchive` resolves at submission and the harness had not).
- **New-archive name taken at the claim.** A file appearing at a new archive's name between the review and the claim was
  refused correctly (no-replace rename) but reported as `errno 17` (Unix) or a generic "already exists" (Windows). Both
  now say "Another file named “…” appeared while the archive was being prepared. That file was kept and the new archive was
  not saved. Choose another name, then try again." on both Unix claim paths (from the private stage folder and within the
  archive's own folder) and on Windows; the shared `existing-target` case asserts the text on all three platforms.
- **AT18 memory ([review](AT18_SCALE_REVIEW.md)).** The tests' "retained" figure, `GC.GetTotalMemory(true)` after an
  ordinary full collection, can also count heap space that holds no live object (free space the collector has not
  compacted), and on macOS it did: the Mac's 492 MiB was not live state. An opt-in breakdown of 20,101 members read
  3,303 bytes per member after capture on the Mac and 1,682 on Ubuntu, but the bytes surviving a compacting collection
  were 1,682 on both, a census of
  everything reachable from the captured selection sums to the same figure, and releasing the job returns the heap to its
  baseline. Live bytes per member, Mac and Ubuntu alike: capture 1,682, planning +213, the worker request +214, against
  charges of about 2,450 for capture and 1,100 for planning. The AT18 tests now sample live bytes (two compacting
  collections every 15 seconds); at the member limit the Mac (create 518.9 s, assess 19.2 s, update 150.9 s) and Ubuntu
  (535.8 s, 128.8 s, 540.7 s) both peak at 243 MiB of live state while checking, under the charged 512 MiB.
- **Windows AT18 on the v17 capture.** Creation 1,108.7 s (capture 351.7 s, 1,056 s before), assessment 275.9 s,
  update 1,073.9 s, peak 243 MiB while checking, handles 360→370; Defender's real-time protection was off (by the VM's
  owner), so the comparison with v15b is not one to one.
- **R22 on Ubuntu: archive extraction 5.4× → 3.21×.** The opt-in archive benchmark's ZIP extraction (20,000 notes of
  about 1 KiB and four large files, F5 into a folder) took 5.4 times the runtime's own extractor against the 4× budget,
  on this branch (7.2 s against 1.35 s) and on its base `42829297` (8.3 s against 1.5 s); only Windows (2.52×) and the
  Mac (3.71×) had been graded. With 1 MiB instead of 64 MiB large files it was no better (8.5 s against 1.0 s), so the
  cost was per file, not per byte. Measured in the VM: about 58 system calls per extracted file (12 `statx` from the six
  source-container checks, 6 `statx`, 7 `lseek` and 6 `fstat` from the ZIP reader's source checks, the destination
  lookups), but the extracting thread was on the CPU only a third of the time: scheduler traces showed about 1,000 waits
  scattered through managed code, the runtime suspending it for collections, and 550 disk waits in the job journal. The
  collections came from the copy loop allocating a fresh 1 MiB buffer for every item, a large-object allocation zeroed
  each time, 20 GB for 20,000 notes. The transfer executor now keeps one copy buffer per job (items are copied one at a
  time and every write uses only the bytes its read returned). Six runs on the Ubuntu VM: 2.97–3.56×, every one within the
  budget; the candidate's benchmark: Ubuntu ZIP 3.9 s against 1.2 s (3.21×) and TAR.GZ 1.82×, Mac ZIP 6.8 s against
  2.4 s (2.88×, 3.71× before) and TAR.GZ 1.95× (evidence `830b52f0…`, `dfa1efa2…`). The benchmark also now composes the
  adapter the application ships (`UnixFileOperations` on Linux and macOS).
  The remaining per-file system calls and the journal's per-folder durable records are recorded for a later reduction;
  ordinary local copies allocate the same per-file buffer in `PortableFileOperations.CopyFile`.
- **Not caused by this branch.** `SecretStoreTests` fails in the Ubuntu VM's test service because the keyring's default
  alias names a `login` collection that does not exist there; this branch does not touch that code. On Windows,
  `DeviceReadTests.A_folder_reached_through_a_link_is_placed_where_the_link_leads` picks another fixed volume and in this
  VM finds the VHD-backed `S:`, whose file lies on `C:`'s disk; the base commit `42829297` fails it the same way in the
  same VM (evidence `c4f6e0fd81cc48d83422214b2c22f38133987bb5433e01f1e9199cd2f4629baa`).

The exact tree (packet `ca022d2d9e4c7366f9972748ae9f738f3cace4a750b6d44d38e91839d24d82c5`, tree `4428c4c9`) ran the complete
test projects on all three platforms, each as concurrent shards. The Mac passed everything: Core 8,652 and 344, App 2,803,
Remote 2,638, Rendering 23, with platform skips only (evidence `f33fb4c2a246b59473f590fbdb207ef67926470d24b31b454ab5579e3d15a488`).
Ubuntu, with the ext4 small and quota volumes and the confined application workers, passed everything but
`SecretStoreTests` (above): Core 8,714 and 354, App 2,815, Remote 2,543, Rendering 23 (evidence
`4437d25fd8cb592f421869ce70d067182193e28ab23a9f75acd312fb10723d8a`). Windows 11, as `Admin` with a UAC-filtered token, with the AppContainer/TxF lanes and the `S:` small and `Q:` quota volumes, passed everything but one device-topology test outside archive writing: Core 8,296, Platform 244 and 1,141, App 2,906, Remote 2,405, Rendering 24 (evidence `b83150ff73c7efa0e87b97f57772bad032215f8b6c9457318dc80db67b257870`). The FZ10 seeds replayed on the same
packet on the Mac and Ubuntu, and the archive benchmark passed on both (evidence above).

Intermediate candidates, retained: v19m7 found the remaining failures fixed here (the Unix and Linux `existing-target`
expectations, the Windows save-copy adapter) and, on Windows under the full parallel load, four timing-sensitive tests
outside archive writing (a RAR update whose worker exceeded its time allowance, the registry import budget, a page view
and the picture grid), which all passed on this candidate; v19m8 then failed only the claim test that still expected a raw
`IOException` (evidence on the machines: `full-v19m7-*`, `full-v19m8-mac`).

**10 November: worker ZIP profile with eight review defects fixed; destination denial and disk quota on real volumes (AT08);
update-preparation crash recovery on Unix; Windows ZIP workers, profile race and capture speed fixed; Orca and NVDA runs
(NVDA does not yet hear phases).** Commit `c1c7787c`:

- **ZIP worker profile (AW2 task 5, [design](ZIP_WORKER_PROFILE_DESIGN.md)).** A strict catalog reads an existing ZIP once
  from its end region: one end-of-central-directory candidate, an exact ZIP64 record and locator when used, central and
  local headers that agree, data descriptors disambiguated by the next offset, only the ZIP64, extended-timestamp, NTFS and
  Unix-owner extras, ASCII or flagged UTF-8 names under the archive naming rules, Stored/Deflate only, every payload decoded
  under the expansion bound with SHA-256 and CRC-32, and the whole catalog compared with the runtime's own ZIP reader. The
  revision is a digest of every byte the catalog relies on. The rebuild copies unchanged members raw, writes new payloads
  with data descriptors (stored at level "store"), canonical extras and ZIP64 only where needed; the fresh verifier reads
  the stage with the same strict catalog. Each captured input carries the local clock offset for its DOS time, so the
  worker never reads time-zone data. ZIP keeps its legacy production route (R0) until a worker route is qualified, and
  edit sessions keep it legacy; the native job matrix, the legacy comparison (identical files; the worker also records the
  captured folder) and a new `zip-archives` fuzz target cover the profile. An independent adversarial review of the new
  code found eight defects, each fixed with a regression case: a
  decoy end record in the archive comment that .NET's reader (which takes the last end signature) would follow to other
  members, now refused together with any record signature after the chosen end record; links, devices, FIFOs, sockets,
  reparse points and attributes that contradict the name admitted as plain files, now refused (Unix tools would recreate
  them as such); every central directory over 1 MiB refused by the per-entry metadata limit (about 10,000–20,000 members),
  now an aggregate charge (a 12,000-member directory is admitted); a malformed Unix-owner field that crashed the worker
  with an index error, now a refusal; the writer dropping the UTF-8 flag for an ASCII name whose comment holds a control
  character, so fresh verification refused its own output; a saturated disk number that .NET cannot open, now refused;
  names over ZIP's 65,535-byte header field crashing the writer, now refused by the plan with a plain reason; and folder
  entries carrying padded compressed bytes beyond the output bound, now limited to Java's empty Deflate stream. Edit
  matching in the writer is now linear.
- **AT08 destination denial.** A folder that refuses FileCat's first object (EACCES/EPERM/EROFS on Unix,
  STATUS_ACCESS_DENIED/STATUS_MEDIA_WRITE_PROTECTED on Windows) now says "FileCat is not allowed to create files in
  “folder”. Nothing was changed." instead of a generic worker refusal, leaves the folder empty and no journal for review.
- **AT08 disk quota.** On an ext4 loop volume with a 40 MiB user quota (Ubuntu) the space review passes, because free space
  does not count quotas, and the stage write meets EDQUOT: creation and update fail with the full-drive text, the original
  stays byte-identical and the discard returns the quota. On an NTFS volume with a 100 MiB enforced quota (Windows) the
  caller's available space includes the quota, so the review refuses before any object exists. macOS has no per-user
  volume quota to test.
- **Preparation-phase crashes of an update (Unix).** A real update process is killed after `Rebuilding` and around every
  discard record (catalog, each intent and removal of the stage and three sidecars, completion); recovery reopens the
  private folder by identity and removes exactly those objects, with the original untouched. Creation already had this.
- **Accessibility.** Orca said nothing when a closed combo box changed value; on Linux the new value of a focused, closed
  combo box is now announced politely. The Orca journey through the qualification host passes on Ubuntu 24.04 (Orca
  46.1): the Create archive dialog and its path are spoken, every format is heard while choosing, the one-file
  refusal and the disabled Pack are heard, TAR + gzip packs with "Preparing"/"Rebuilding" announced, `a.txt.gz` keeps the
  whole name, and F7, F2 and F8 inside it are refused aloud with the reason. The Create dialog fits 640×480 at 100–200 %
  text (`ArchiveDialogViewportTests`). NVDA 2026.3.1 (portable, from the signed NV Access download) on the Windows 11
  VM through the same host with AppContainer workers speaks the dialog and its path, every format while choosing, the
  one-file refusal ("A compressed file holds exactly one file. To pack several items or a folder, choose TAR + gzip.")
  and the result ("Create TAR + gzip archive “left.tar.gz”: Archive created: 2 entries verified and saved."). **Open:**
  NVDA does not speak the archive phases, even when packing a 256 MiB file lasts seconds; Orca does. NVDA speaks every
  UIA live-region event it receives and Avalonia raises one on each name change of a live element, so the phase region
  (a 1×1 transparent text in the operation strip) is most likely not placed in its window when the event arrives; a UIA
  event trace is the next step. The journey's later stages are not yet confirmed with NVDA. The runner now marks with
  Ctrl+A (NVDA keeps Insert as its own key) and packs a 256 MiB file.
- **Progress in long phases.** Preparing (reading the selection) and Rebuilding (the worker reading each input) name the
  file being read in the operation center, at most four times a second; Checking and Saving name the archive.
- **Windows sandbox.** .NET's ZIP reader converts every DOS time through the local time zone, which the AppContainer's
  registry boundary denies (`SecurityException`), so every ZIP job's worker stopped on Windows only (v15b). Archive
  workers now run with .NET's invariant time zone (`DOTNET_SYSTEM_TIMEZONE_INVARIANT=1`, UTC without a registry lookup,
  as Linux workers already run with `TZ=UTC`); a boundary probe test asserts both the zone and the reader work inside.
  Running test processes in parallel showed that two processes starting workers at once race on the shared per-user
  profile (Windows answered E_UNEXPECTED, or one found no profile folder yet); its setup is now serialized by a
  per-session named mutex, held only while the profile is opened and protected, and eight concurrent starts are tested.
- **Windows capture speed (AT18).** Each access to a captured input re-pinned the selection root's full path from the
  drive root three times (about thirty opens per access in a temporary folder). Folders below the held root are now
  pinned relative to it, each verified against its captured identity as it opens, and the root's own path is re-pinned
  at most once a second: its lease holds every name of that path without delete sharing, so the path cannot change
  while it is held, and the held handle is still checked on every access.
- **Validation tooling.** The Unix and Windows runners can split a test project into concurrent processes
  (`PATH.csproj#Class`, `#!Class`, `parallel:on`; FileCat's worker gate is per process), which cut Ubuntu runs from
  12.6 to 7.6 minutes and Mac runs to under 6; the Windows runner records Defender's state read-only. The AT18 tests
  warm up with a small job before taking their baselines and attribute each retained peak to a phase.

The exact tree (packet `697fc128dd18d6a7e220f1aca2d8cd31eabd53270d9fed9d1a940a096a726114`, tree `3f3ea32b`) passed the
`Archive`/`Journal`/`EditSession`/`EditCommit`/`BackingArchiveTarget`/`JobProgressView`/`JobEngine`/`UndoLifecycle`/
`DialogViewport` subsets with zero failures and only platform skips, every machine running its test projects as
concurrent shards: Mac Core 3,060/3,156 and App 514/564 with the APFS small volume in 5.6 minutes (evidence
`5c9a87abbd70a015e9c3a88143a3d7fbf3055006c05814ec7167f2e2e7a25940`); Ubuntu Core 3,123/3,156 and App 525/564 with the
ext4 small and quota volumes and the confined application workers in 6.6 minutes (evidence
`3aec920bd5acfc1c8bacb69748dfaaff01eb259414302a462c4a408f3218b324`); Windows 11 Core 2,653/3,156, Platform 580/616 and
App 543/564 with the AppContainer/TxF lanes and the `S:` small and `Q:` quota volumes, as `Admin` with a UAC-filtered
token and Defender's real-time protection off, in 10.5 minutes (evidence
`899b621cba90dd7fb6fe10bcb0854b0713fc2cae862493f8568420b8363be7b1`).

Earlier candidates of this change, retained: v15c (the ZIP profile before the review) passed on the Mac (Core 3,036/3,129,
App 514/564, evidence `d3682487c88b5f9da5235a025d62562e383ccff5a8fabbee320ee4d6ee86f606`) and Ubuntu (Core 3,098/3,129,
App 525/564, evidence `ab010add6574efe314cbb24aba0e2f7377959446a32ebe2007a5aae2251dfcca`), both with the AT18 lane; v15b on
Windows passed Core, App and AT18 but its three AppContainer ZIP cases stopped the worker (the time zone above; evidence
`9c0f958820f35c7cc669e63cbcaddd596518864e3ba650872f161e5377a3f418`); v16 to v18 passed on Ubuntu (v16 sequential in 12.6
minutes and in parallel shards in 7.6, evidence `c7509c3ed79749b3ab821dffc2a639a15d8cbaf3381ec2feb0b5765c93be53b1`), and v18
passed on the Mac (evidence `09b9a15b5cb65a6aa2b0cde5860bacb09066b184299e6eb9173d1dac9e5d8c4b`) and Ubuntu (evidence
`bf08151b81d3af4fdd5b9a6d0fd97ccbc3329aa31583a1d4170c72e21a896074`), while Windows v18 found the profile race in one App
and one Platform TAR.ZST case (evidence `f429733949948c0595904fcd865d2133d30210cca837a1a259076cd7cc666895`).

**AT18 measurements** (details in [AT18_SCALE_REVIEW.md](AT18_SCALE_REVIEW.md)): Windows 11 VM (v15b, AppContainer workers,
TxF publication; creation and assessment with Defender's real-time scanning on) create 2,176 s, assess 311 s, update
1,118 s, retained peak 257 MiB, handles 432→449; the 100,001st member refused with the plain limit text. Mac (v15c, measured
alone) create 479 s, assess 19 s, update 146 s, retained peak 492 MiB; Ubuntu (v18, alone, retained heap read at every
phase change) create 520 s, assess 126 s, update 575 s, 212–223 MiB retained at creation's phase changes and a peak of
243 MiB while checking. On Ubuntu the charges (about 3 KiB per member) stay above what is retained; the Mac's peak is
above them and is being attributed to its phase before the charges or the code change. A Mac run alone also exceeded the
descriptor bound by one, because the runtime opened its own descriptors during the first measured job; the tests now
warm up first.

**10 November: AW7 standalone .gz/.bz2/.zst writing (release R3); AT18 passes at the 100,000-member limit on Mac and
Ubuntu; one profiled archive job prepares at a time; routes compose only qualified profiles (none yet).** Commit `93cf0572`:

- **AW7 (plan §8.5, §11.6, capability table).** A standalone stream holds one payload under the synthetic member
  `content`; the name shown inside it comes from the outer file name (`report.txt.gz` shows `report.txt`), so renaming the
  outer file never changes a plan, and a name stored inside a gzip header is never used. Planning admits exactly one regular
  input for a new file or exactly one replacement of that member, and refuses every other shape (a second file, a folder,
  rename, delete, an addition beside the payload, a stale revision) before any object exists; the transform wire, worker
  roles, fresh verification, journaled preparation, publication, discard, recovery and edit sessions are the shared ones.
  gzip keeps the same metadata-neutral admission as TAR.GZ (a stored name or timestamp, as `gzip` writes by default, stays
  read-only), and a replacement keeps the operating-system byte and compression flag; bzip2 keeps its block size; Zstandard
  requires one checksummed frame. Empty payloads produce valid streams in every format. In the app: the Pack dialog offers
  "gzip/bzip2/Zstandard (compress one file)" and names the result after the whole file (`a.txt.gz`); several items or a
  folder get "A compressed file holds exactly one file. To pack several items or a folder, choose TAR + gzip."; a name such
  as `notes.tar.gz` is refused for a compressed single file; an existing target becomes an explicit "Replace compressed
  contents"; F5 of exactly one file onto an opened compressed file asks the same question, while several items, a folder or
  F6 are refused with the TAR alternative; F7, F2 and F8 inside it are unavailable with the reason; F4 edits and commits the
  sole payload, and the session shows the member by the name it started with.
- **AT18 (plan §9.2 review in [AT18_SCALE_REVIEW.md](AT18_SCALE_REVIEW.md)).** The limit run (99,999 members created through
  real workers, assessed, the 100,000th added, one more refused before anything is written) now passes: Mac create 518 s,
  assess 19 s, update 151 s; Ubuntu VM create 541 s, assess 129 s, update 534 s. What made it fail before: the heap charges
  made the limit unreachable, every input recheck re-hashed the input (and re-read `/proc/self/mountinfo` on Linux), the
  worker's CPU allowance had no per-member term, and the transport spent about 1.5 ms of CPU per tiny member. A held folder's
  mount is now rechecked by `statfs` type/flags and the `statx` mount ID instead of mountinfo, and the transport buffers a
  frame into one write and one flush and reuses cleared input buffers. Payload extremes on the Mac: a 512 MiB incompressible
  file into a new TAR.GZ in 30.7 s and into a new `.zst` in 12.7 s; 256 MiB of zeros (beyond 1000:1) refused by fresh
  verification in 1.5 s with nothing published.
- **Cross-job admission (AW2 task 5, §9.2).** Profiled archive jobs prepare one at a time ("Waits for "…": archive changes
  are made one at a time"), so the global retained-heap bound is one job's charge; ordinary jobs keep running beside them.
  With the existing limits (one archive worker, eight waiting, 16 input leases) the cross-job budgets are complete.
- **Availability composition (§16.1, §16.3).** `ArchiveWriteRoutes` registers only listed format/runtime/file-system routes,
  each with a typed refusal (format, runtime, file system, unknown destination), rechecked at execution; the list is empty,
  so nothing new is enabled. A test-only qualification host composes every backend for interface runs (Orca, NVDA).
- **Input rechecks.** Capture observes every input completely (identity, metadata and content SHA-256), the transform's
  receipt and the fresh verifier bind the archive's bytes to those digests, and F6 removal re-reviews content before it
  removes anything. The later prepublication passes (plan §7.1 ReadyToPublish: provenance and outer metadata) now check each
  input's identity, length and complete outer-metadata digest without hashing its bytes again; before, each of about six
  passes re-read and re-hashed every input, which made a 100,000-file creation spend most of its time re-reading inputs.
- **CPU allowance.** A worker's allowance was 30 s plus 2 s per MiB of real input, so 100,000 tiny inputs received about
  31 s and the transform worker was stopped. Transform and fresh verification now add 5 ms per member of the parent's own
  expectation (capped at 2 hours as before); assessment of an existing archive keeps the byte-based allowance.
- **Retained-heap ceiling.** 512 MiB charged instead of 192 MiB. Every per-object charge is unchanged, so the model stays
  an upper bound; at 100,000 members the charges came to about 3 KiB per member plus names while the sampled managed heap
  (which includes uncollected garbage) peaked near 700 MiB on both Unix machines and fell to about 100 MiB at the next phase.
- **Fuzzing.** A new `standalone-streams` target (FZ02 and FZ05–FZ07 for AW7) in the harness, the structured-mutation lane
  and the guest runner: creation, replacement, forbidden change shapes, mutated stages and hostile streams, with the
  runtime's zlib and FileCat's own `.bz2`/`.zst` reader as oracles. Its sixteen seeds replay as expected; a two-minute
  development smoke ran 60,531 calls without an oracle failure (not evidence; the campaign belongs to the final candidate).

The exact tree (packet `3e54a84d9d252720df58c6f01b44d5009b27889f22e344842a64f752e9a881e3`, tree `e8d63efa`) passed the
`Archive`/`Journal`/`EditSession`/`EditCommit`/`BackingArchiveTarget`/`JobProgressView`/`JobEngine`/`UndoLifecycle`
subsets on the Mac with the AT18 lane and the APFS small volume, zero failures: Core 2,982/3,076 and App 473/523
(evidence `b9af90c1b0dc7eaa2a45cf80ab56fff422997a8f07e3306e21e334e157ebe5c0`). Ubuntu ran the same product source as
packet `2dfa32d1e79574c1b1f0512eacfd454b9e0276219c8f7e829fab751c5e49c517` (tree `73c3fef3`, which differs only in one App
test): Core 3,045/3,076 and App 483/523 with the confined application workers, one failure, the bzip2 case of the real-worker
standalone UI test, which pressed F4 on a listing not yet refreshed after the job (the test now activates the panel,
refreshes and waits for the row; evidence `39a144c00636c375fa811d756644cb98183bfcfec5b2e7729e5002542710682f`). Windows ran
packet `c7d3623a84d4cb8162e86201818f06b335a0739abe96c436b15db27fa7325c39` (tree `25e56c0b`), before the AW7 review fix:
Core 2,540/3,023, App 489/512 and Platform 566/599 with four failures, the three real-worker standalone UI cases and the
payload-extremes `.zst` replacement, all the review defect fixed here (evidence
`21b2b558dc0e3726d9ad1cda831ff41b9fb9d8dd7a25a75bc48b7064599d3465`). The next commit's candidate contains this code
unchanged and carries its Windows and Ubuntu evidence.

**10 November: an unpublished save is discarded exactly; F6 moves into Unix archives; legacy edit cleanup is bound (R21);
the operation center stays usable during a format scan; AT08 on a small NTFS volume. All three platforms pass; AT18 open.**
Commit `38e56944`:

- **Discard of unpublished transactions (plan §7.1 "safe owned cleanup").** A failure or cancellation before publication
  no longer leaves the stage, its manifests and an unresolvable "needs review" entry behind. A durable `archive-discard`
  catalog freezes the preparation facts and lists every recorded object and every reserved name that never received an
  identity; each destruction is preceded by `DeleteIntent` and followed by `Deleted` or `Absent`, the empty private folder
  is removed by `rmdir`, and `Completed` releases a new, discardable preparation hold (other journal holds stay hard).
  Only facts that prove publication never started qualify (a preparation phase, ReadyToPublish, or a new archive's review
  record written before its claim). Unix destroys sealed sidecars by recorded version and partial objects as scratch in
  the creator-private folder (identity, owner, single link; content through the held descriptor first); Windows reopens
  each artifact by its recorded NTFS identity and deletes it through that handle. The job runs the discard live; a crash
  or a refused discard leaves a reviewed "Remove unfinished files" action in Interrupted operations (opening the review
  writes nothing). A completely full APFS volume can refuse even the removal; the job then says the files were kept,
  with the reason, and recovery finishes once space returns. See the revision in UNIX_PRIVATE_CLEANUP_DECISION.md.
- **F6 move into native Unix archives (plan §11.6.1).** After verified publication each source is re-reviewed through its
  held descriptor, renamed aside to an exclusive name in its own folder (`RENAME_NOREPLACE` on Linux, `RENAME_EXCL` on
  macOS) and unlinked only when that name binds the held object; another object that took the original name is put back,
  a folder goes only by `rmdir` once empty, a skipped or changed input is never removed, and a hard-linked original is
  refused at capture with a plain message naming it.
- **R21 (legacy edit cleanup).** Commit and save-copy snapshots, failed session creation, record-publication temporaries,
  the save-copy stage and legacy Discard no longer delete bare pathnames: Windows deletes through handles bound to the
  recorded identity or verified directory handles; Unix removes only in a folder no other user can change (group write
  only for the user's own private group) while the name binds the recorded identity; anything else is kept and logged
  with its path.
- **UX:** A4 (the operation center stays above a format scan's backdrop and usable, Ctrl+J moves between them, panel
  commands stay blocked, the progress bar is named), A16 ("Archive edit kept" offers Save copy or "Show working copy"),
  B8 (post-commit read-back "Check saved archive"), the full-drive and hard-link messages, pluralized move summaries.
- **AT08 on Windows:** a 200 MB NTFS VHDX: the space review refuses before any object exists; a drive filled during the
  rebuild fails with the full-drive text, and the live discard leaves nothing beside the original.

The exact tree (packet `8cef6248a4e426b9408abc007f9e00c713cc4842cac37b7b85daf4036499c2fa`, tree `936dff54`) passed the
`Archive`/`Journal`/`EditSession`/`EditCommit`/`BackingArchiveTarget`/`JobProgressView` subsets with only platform skips
and one failure, the opt-in AT18 scale test described below: Mac Core 2,886/2,977 and App 452/497 with the 200 MB APFS
image (evidence `077140858c267276e5ec0befca781ba1fcd17fba3ffd222001fd8ed7f0155249`); Ubuntu Core 2,947/2,977 and App
462/497 with the loop-mounted ext4 image and confined application workers (evidence
`f2ef8cffb7f22e155291c6871ddeef5adf58aaf403ffe8dc0f78d03fb518af4e`); Windows 11 Core 2,495/2,976, Platform 569/601 and
App 480/497 with the native AppContainer/TxF lanes and the `S:` VHDX, as `Admin` with a UAC-filtered token, zero failures
(evidence `757645add5ef26a0a12d0d749c1fe9b099b5695ce42d3a0a44b322f8c8819c40`). The previous Windows attempt (packet v10,
evidence `d3d7afa47ee9f0953a16e34fc1c7eeb06e4eb65537c514fa37e5ea77e5565e12`) failed only in the unchanged
`An_unrelated_inheritable_file_handle_is_not_granted`: its final `File.Delete` met a sharing violation because children
that other tests start in parallel with ordinary inheritance also received the test's deliberately inheritable handle.
The test now withdraws inheritance once the worker exists and retries its own deletion (next candidate).

**AT18 is open.** The new scale test (opt-in, `FILECAT_ARCHIVE_SCALE=1`) first failed on its own arithmetic (100,300
members instead of 99,999). Corrected (packet `1d2766fc211bfc202b81fe8479b4d5adcfb094a90bfc0df218cd4f65f8a98b8a`), it
found a real limit: creating a TAR at 99,999 members is refused as over budget on both Unix machines (Mac evidence
`15e693dba024b0ea2ccf9714b651340f4427a78ff69f7fa28a814efbeff6223b`, Ubuntu
`d187848752f35ecd628cbff1e9e7aac2c42e25c01b205f75c0f0fd9bba620986`). The conservative per-object heap charges of capture,
planning, review and the TAR plan (about 3 KiB per member plus names) exceed the 192 MiB retained-heap ceiling well before
the 100,000-member policy limit, so that limit was unreachable. A measurement build with a 512 MiB ceiling (packet
`46babe1a9bb0da3766746a73635279fd3e5e2b1243200f758e7fc4f4a73a90bf`) creates and assesses the archive on both machines,
and the update adding the 100,000th member then exceeded the test helper's 180-second job deadline (Mac
`a406a4f97b40d2a3df401eecf7fb75a29bd109c8fb0a9085e1bdbb357121e3aa`, Ubuntu
`607bf10d4f4c5a45c7137eb6c488160decee371142a892511615dd0531a79257`). Both are being resolved with measured heap peaks and
phase timings before AT18 is recorded; the shipped ceiling stays 192 MiB until then.

**10 November: archive phases, plan microcopy and full-drive handling; AT08 on real small volumes; all three platforms pass.**
Commit `2acacfe3` shows the plan's phases in Operations (Preparing, Reading archive, Rebuilding, Checking, Saving,
Finishing safely) with Cancel unavailable at the short publication boundary, and gives every controlled archive refusal
the §11.7 text, phase-aware so that nothing says "not saved" once saving may have begun; the technical message stays as
issue details. A full drive or quota (ENOSPC/EDQUOT, `ERROR_DISK_FULL`, `ERROR_HANDLE_DISK_FULL`,
`ERROR_DISK_QUOTA_EXCEEDED`) that surfaces through any writing step now says so ("The drive ran out of space before the
archive was saved, so no archive was changed. Free up space or choose another destination, then try again.") instead of
the generic worker refusal it produced before. Also: one combined review of replacements and exclusions, an explicit "Add
to this archive" before an existing ZIP is changed, a bounded pack estimate, "Open archive" after creation, the context
menu entry, duplicate-name editing guidance, confined Linux application workers, user-private-group folders, and removal
of an empty private stage folder left by a refusal before any object existed.

AT08 now runs on real small volumes, enabled by the Unix runner's new `env:NAME=VALUE` entries: a 200 MB APFS disk image
attached with ownership honoured on macOS 26 and a 200 MB ext4 image loop-mounted on Ubuntu 24.04 (Linux writing admits
only ext4, so tmpfs cannot stand in). On both, the space review refuses creation and update before any object exists,
with both amounts in the message, the original byte-identical and the folder unchanged; and a drive filled by another
writer after the review (between the review and the worker's first stage write) fails the job with the full-drive text,
publishes nothing and leaves the original byte-identical.

The exact tree (packet `08d2b401617c7de54fd3f9cfa80417c638d3bcec43f1bb90ad2f80b6b373f9ad`, tree `45754c31`) passed the
`Archive`/`Journal`/`EditSession`/`BackingArchiveTarget`/`JobProgressView` subsets with zero failures and only platform
skips: Mac Core 2,843/2,933 and App 407/451 (evidence `fc62629735c9d945672e046e8b9029a74d115b5673eb4d962f609ba235811060`);
Ubuntu Core 2,904/2,933 and App 417/451, including all ten confined application-worker jobs under `systemd-run --user`
(evidence `0b2499f31aa9dfed3104d6436752fcc5db56bb8dcc11263316ecad67920d0697`); Windows 11 Core 2,475/2,933, Platform
565/597 and App 437/451 with the native AppContainer/NTFS lanes, as `Admin` with a UAC-filtered token (evidence
`e1f3e394a0ae8eb506f5445fd473e773ab94ecc0f161bdd510a09a8fbdaa4bd8`).

The two attempts before it are retained. Packet `1ab44e8f16cf3b6d79b0933281e9fecbb583937a4ca655c64cf5e4f09598322d`
(tree `bee0c678`) passed on the Mac (Core 2,778/2,868, App 255/299, evidence
`f860b4fdb2c9f8590c18c2d246b92a7d86d278654e86c3781910a4407a837def`); on Ubuntu three confined 7z application cases timed
out (evidence `9c769b1215e453a2bd58734de680423e529a22f254fc0cf6708e36295f028548`; fixed with whole-second fixture times and
job diagnostics in the timeout text), and the Windows runner aborted on xUnit's standard error under Windows PowerShell
5.1 (evidence `885cd08a76ae550d5f7625473625140c68fcf240a7901a28cc67a0fc61d58865`; the native loop now runs with
`ErrorActionPreference=Continue`). Packet `de516bec07caf9039af93a89c88f82f1293611dee2aa37728d6f75bc5cb2dc61` (tree
`7a6078b8`) found the same three failures everywhere (Mac `1ae0d3660545f31be2c6b1d29dea391d3ef33b42e5088b3176df67e1c5690212`,
Ubuntu `e0aec88b839672ec433f56b10dfdbac6de328c936aa87aafee256856a8a0c19e`, Windows
`a6d9276edef13c1bf829b61b122265fdd46d9e0a821dcf895c7c058c0220412b`): a product regression in the same change set (editing
a member that had disappeared indexed an empty match instead of reporting the change), a new test that expected `Completed`
where an accepted exclusion correctly ends `CompletedWithIssues` (its summary now also says "1 unsupported source item"),
and an older pack test unaware of the new "Add to this archive" question.

**10 November: Unix creation moves into the private stage folder; all three platforms pass the archive regression.**
Commit `8872044c` prepares a new Unix archive's stage and sidecars in the same `0700` private stage folder as an update,
then moves the verified stage into the reviewed archive folder with one no-replace rename between the two held folder
descriptors (`renameat2(RENAME_NOREPLACE)` / `renameatx_np(RENAME_EXCL)`). A binding of the same open file description
serves the published review. The shared creation transaction and cleanup catalog are reused: the review names its
destination folder, the catalog accepts sidecars in the private folder, sidecars are destroyed exactly, and the empty
folder is removed before `Completed`, so a new archive leaves no FileCat file beside it. `UnixArchiveCreationCleanupRecovery`
reopens an interrupted creation cleanup by recorded identities (wired into the application review). Real creation
processes are killed around every cleanup record (catalog, each intent and unlink, folder removal, final commit) and
recover to exact cleanup; crashes before the catalog (ready, publishing, claimed but unrecorded) keep every object and
refuse automatic cleanup.

The exact tree (packet `65369f8556a606eba929550272a8f58e850618cd9604e0180758dd32cfcb6706`, tree `af61c112`) passed the
complete `Archive`/`Journal` subsets with zero failures and only platform skips: Mac Core 2,765/2,855 and App 232/266
(evidence `18254704d1bb195d6e2ce2f1dcf5ac728025e1f787c10646cbf2deb247b807b4`); Ubuntu Core 2,826/2,855 and App 232/266,
including the confined lane under `systemd-run --user` with freshly published application and probe bundles (evidence
`4ca140ffe426e0aa1449fc94f380c7924d9f777a3a22bb3b0a0ad4cee35471f3`); Windows 11 Core 2,461/2,915, Platform 565/597 and
App 411/415 with the native AppContainer/NTFS lanes enabled through the extended Windows runner (published bundles,
`FILECAT_ARCHIVE_CONTAINMENT=1`, `FILECAT_ARCHIVE_TXF_NATIVE=1`), as `Admin` with a UAC-filtered token (evidence
`81035eed9a8e7b0a80213b1606cebcd031452aac51e7479240b599cae0e7bd14`). Earlier attempts in this series are retained: the
first found that `fcntl(F_DUPFD_CLOEXEC)` passed its variadic argument in a register on Darwin ARM64 (fixed with the
stack-slot convention already used for `openat`) and that the creation catalog required sidecars directly in the archive
folder (fixed); it also exposed a move test that assumed a case-sensitive file system, false on default macOS volumes
(now skipped there). The second failed only one test assumption: a completed cleanup catalog already resolves the
creation even before the final transaction record.

**10 November: Unix exchange recovery reaches the application; confined Linux application workers pass; linked folders fixed.**
Commit `48f5ed22` wires `ArchiveExchangeCleanupReview` into the interrupted-recovery UI: inspection observes both names
and the remaining private objects without writing, and approval runs the production `UnixArchiveExchangeRecovery`
(observed outcome, exact private cleanup, folder removal, journal closed). The application binary now dispatches the
confined Linux launcher and worker roles. Validation of that exact tree (source packet SHA-256
`2ce83c840dc605741010ce84ad59875309fa9b025ee8ec0f6d3cd06f3515dca6`, tree `9efcca0b`) passed the Unix storage subset and
the new App lane: Mac Core 300/313 and App 4/4, Ubuntu Core 298/313 and App 4/4, only other platforms' cases skipped,
zero failures (evidence SHA-256 Mac `a7d596845db0e397af2afe1dc10ab4bc9efec3836d726d29fda7634e8fe7f44f`, Ubuntu
`670c8daa21c0cfad614b250c2fcb45476f252d38f6111d42b418c587e141ff4a`). The App lane kills a real update process at four
exchange points and drives the real approval dialog: approve after the intent (not published), approve and cancel after
the exchange (backup warning shown, cancel leaves journal and files byte-identical), and approve after an interrupted
unlink (one already-absent file reported and recorded).

The preceding packet (`85d4be1638abc80f676d08dee52fa940c54058312e4dc7a6d859fe8f2e3e8c9d`, tree `03d837e0`) published the
self-contained application (133 MB, 257 files) and worker probe on Ubuntu 24.04.5 and ran the tests inside a transient
`systemd --user` service: all 67 executed `LinuxArchive*` confinement cases and all ten confined application-worker
create-then-update jobs (TAR, TAR.GZ, TAR.BZ2, TAR.ZST, RAR5 and 7z with every password mode) passed (evidence
`ccd0fd69300799347097cae4bb49c735d0a0f6e51ef27b7e264a726d013275f7`). A first attempt from a plain SSH session failed
every confined worker with `EACCES`, because an SSH session's processes are not below the user manager that owns the
delegated cgroups; the runner now uses `systemd-run --user`. That packet's other failures were test defects, retained
and fixed in `48f5ed22`: two assertions read the journal while recovery held its exclusive `flock`, and on the Mac the
review could not open because the journal folder path contained the `/var` link.

That last failure exposed a product bug: Unix native routes pin link-free folders, so an archive or source reached through
a linked folder (macOS `/tmp` and `/var`, `/home` on Fedora Atomic desktops, linked project folders) was refused.
`ArchiveLocalPath` now resolves the parent folder once at submission, review, source capture and journal reopening; the
archive name itself is never followed. A new job test creates and updates an archive through a linked folder.

The Windows 11 VM ran the archive/journal/edit regression of packet
`5b4c42a8bd5a4bb69e93adadb6ddcbfe1fcf519d3b6e499680cfdba8cbd56532` (tree `0ac49cb4`, the exchange commit plus the then
uncommitted application wiring) as `Admin` with a UAC-filtered token (elevation type 3, no enabled Administrators):
2,900 tests, 2,461 passed, 439 platform skips, zero failures (evidence
`a40c1e7a744782dce02727f35d01902553f2db1190265817bac45494ee932dec`). It predates the linked-folder change, which is a
no-op on Windows (`Path.GetFullPath` as before).

**10 November: journaled Unix existing-archive updates pass with exact cleanup and real crash recovery.**
The new unregistered Unix route ([design](UNIX_EXCHANGE_UPDATE.md), [cleanup decision](UNIX_PRIVATE_CLEANUP_DECISION.md))
admits only originals whose promised outer metadata it can reproduce, prepares the stage and sidecars in a `0700`
private folder below the archive folder, applies the original's group, permissions, ACL, preservable flags and macOS
birth time there, and publishes with one recorded `renameat2(RENAME_EXCHANGE)` / `renameatx_np(RENAME_SWAP)`
(`archive-exchange-update` facts). A refused native call is proven atomic (`NotPublished`); other anomalies are
`NeedsReview`. Reconciled private objects are then destroyed exactly: content through the held descriptor, name with
pre/post identity checks, then the empty folder. `UnixArchiveExchangeRecovery` reopens an unfinished journal under
`flock`, re-observes both names by recorded identity, records only what it sees and resumes exact cleanup.
Source packet SHA-256 `521bed92f316aca294bfc1debf9735cb912d1bc508657beba459c37d1a096631` (tree `356d5dbd`) passed the
Unix storage subset on Ubuntu 285/289 and Mac 287/289, with only other platforms' cases skipped and zero failures. Each
platform executed all ten create-then-update jobs (TAR, TAR.GZ, TAR.BZ2, TAR.ZST, RAR5 and 7z with every password mode,
mode `0640` preserved, no private folder left), the metadata/refusal cases (including a macOS ACL deny entry and a
read-only folder that refuses the swap) and all 16 real externally killed process points: after `Reserved`,
`MetadataApplied`, `ExchangeIntent`, between the swap and its record, after `Exchanged`, before and after each of five
exact cleanup steps, and between folder removal and `Completed`. After each kill the archive was exactly the old or the
new version and recovery reached `Completed` with no leftover files. Evidence bundle SHA-256: Ubuntu
`8136596a99e46e8f7e6b1d11ce337ea393dc75500dcdb0cf24559d0576c852af`, Mac
`32b4286d244df99d0ce0958e5bcbfc69b2a5563f23f7e335a3061cec09f98721`. Earlier packets in this series retained their
failures: the first update packets were refused by the preparation's same-folder stage rule (fixed by reservations that
carry the reviewed public parent identity), and one assertion wrongly required the creation job's journal, which still
retains Unix creation sidecars, to be resolved. Not yet covered: preparation-phase crashes before `Reserved`, Unix
creation in the private folder, the application recovery action and confined Linux application workers (wired, not yet
validated), and every release gate.

**10 November: unregistered two-parent Unix exchange primitive passes on Mac and Ubuntu.**
`UnixArchiveFile.ExchangeRetainedAcrossParents` swaps one sealed private stage, held in a pinned
`0700` stage directory created below the reviewed target parent, with the held public original through
`renameat2(RENAME_EXCHANGE)` / `renameatx_np(RENAME_SWAP)` on two directory descriptors. It locks the
pair in a fixed order, rechecks both parents, names, identities, versions, outer metadata and link
counts, syncs both parents, and returns a receipt. After any native attempt both old child bindings
become terminal (`NeedsReview` on reuse), so no live object keeps an outdated parent lock. A source
packet of this exact worktree (SHA-256 `b3ee1516bbe3bbdd257a8e5dd1a5d3d7843ecda03560cdd4326c22cd88d24cb5`,
tree `cc4b6450`) passed the Unix storage subset: Mac 255/257 and Ubuntu 254/257, with only other
platforms' native cases skipped, zero failures, and both new exchange cases executed and passed.
Evidence bundle SHA-256: Mac `38f43ad77f20749a7ea0d910b89bcd45f5ad48331feb3436eea388c880639c71`,
Ubuntu `f920ff10c4dc8f6a6eeca15f6f25658bf75faa08d6f281117383a8d0d7265b45`. They were produced by the
new reusable [`make_source_packet.sh`](../../../eng/validation/archive-writing/make_source_packet.sh) and
[`run_unix_test_packet.sh`](../../../eng/validation/archive-writing/run_unix_test_packet.sh). The same packet built
on the Windows 11 VM through the new [`run_windows_test_packet.ps1`](../../../eng/validation/archive-writing/run_windows_test_packet.ps1)
in `Admin`'s interactive session with a UAC-filtered token (elevation type 3): 20/257 passed, 237 Unix-only
skips, zero failures (evidence SHA-256 `5c0cdd1db38db2ef5058d3a416641ccbbd816e6f6024f515273eeece20d20ebd`).
At the owner's request the VM now signs in as `Admin`; earlier evidence used the standard account
`fcarchive42829297`. A first Windows attempt as `filecatdsa_r2` failed before building because the copied SDK
had an empty DACL (runner setup error, retained). This is a native
primitive only: the two-parent journal, outer-metadata application, recovery, cleanup and production
Unix update route remain open, and no profile is enabled.

**10 November: structured-mutation platform floors pass on Windows, macOS and Ubuntu.**
Five concurrent one-hour lanes (7z/RAR5 strict and password readers plus journal state) of the
portable structured-mutation harness completed on all three desktop RIDs with no failure:
17,818, 18,019 and 18,072 CPU seconds respectively, each above the 14,400-second additional-RID
floor. Windows ran as the real standard account. The independent verifier bound each bundle to the
frozen harness/corpus packets and the committed harness sources; bundle hashes and per-lane counts are
in [the fuzz record](FUZZ_CAMPAIGN_SETUP.md). This is structured replay without coverage feedback; it
does not earn per-target coverage-guided floors or the per-family 24-CPU-hour totals.

**10 November: all three wrapper long fuzz targets have verified floors.**
Ubuntu gzip-strict, bzip2-strict and zstd-strict each completed a bounded
12,000-second driver run with zero crash artifacts. Independent host
verification accepted 10,839.053064, 12,734.511204 and 7,581.796526 kernel
CPU seconds respectively, all above the 7,200-second floor. Their transport
SHA-256 values are `844855e5a0f0e46406d5ab551718f7debaf4ec267e56e5a6fbbcb935aaae0e00`,
`155a43a93369ce5c9e1baf483d5901b457cdf3189ee12e0862a35c30271a55b2`
and `521d5fb9fe82f0529596a7a07775341370ff691f8564fee69c7f7a083d4879fa`.
Zstd's original supervisor failed after the driver exited because an atomic
temporary report file disappeared during the output-size walk. Its failed
queue remains recorded. A narrow evidence recovery from the retained cgroup,
driver, exact image and managed report was marked in the result, then passed
the original strict sealer and independent verifier. Per-second in-memory
samples from that run were lost; final hard-cgroup accounting remains. The
test-only supervisor race is fixed. Other fuzz targets and final qualification
remain open; [details](FUZZ_CAMPAIGN_SETUP.md).

**10 November: pinned private Unix stage-directory increment passes on all three test machines.**
The unregistered `UnixArchiveDirectory.CreatePrivateStageDirectory` creates a
fresh `0700` directory through the held target parent, checks the ordinary
identity and same volume, clears inherited Mac ACL entries through the existing
native helper, and syncs both parent directories. The exact final source passed
Mac storage 253/255 and Ubuntu storage 252/255 cases, with expected platform
skips and zero failures. The independently verified evidence transport SHA-256
values are `3dcf322db09a50d0f52ac5606451bc94243933281accc41ed5f98c83bead0cb0`
and `0ac60095e6aeaebe0619435636305a0d6694f695d4c2263d74d4ecd58928533d`.
Windows rebuilt that same frozen source and passed 20/216 storage cases under
the real standard account, with 196 expected Unix-only skips and no failures;
its evidence transport SHA-256 is
`f7797110e1821db85b995012c968143d0fa39ba96909d522ab44ac9c9cdfca04`.
All raw rows, source hashes and account facts were checked. The first Mac
runner stopped before executing tests because Bash 3.2 rejects its empty
command array under `set -u`; the corrected runner and original failure log
are retained. The first successful Mac/Ubuntu source was then rerun after the
final pre-creation identity check. This is a storage prerequisite, not a
two-parent exchange transaction, journal, cleanup or enabled update route.

**10 November: full Windows platform and rendering suites pass.**
The frozen `6222dc55` source passed 1,336/1,423 Windows platform cases and
24/24 rendering cases under a real standard account, with 87 expected platform
skips and zero failures. Its evidence transport SHA-256 is
`adaa397e8fa1ec4335723b8700fe7f30093bfd1dbe0c9eb4e357290f0fa68384`.
The raw TRX rows include actual RAR5 and 7z AppContainer create/update and
restoration cases. The first full platform run failed 257 cases because its
fresh worker assets lived under the administrator profile, where the standard
account could not grant AppContainer read access. The second runner stopped
before tests because its corrected asset variable was assigned before its
output path. Both failed bundles are retained. The third run placed freshly
published assets under the standard account's profile and passed both complete
suites. These failures were test setup errors; the successful raw cases do not
close the remaining package/RID/security/fuzz gates.

**10 November: the corrected Ubuntu native exchange and codec batch passes.**
The frozen `6222dc55` fixture source passed 271/274 selected Core cases and
199/233 archive App cases, with 37 expected skips and zero failures. The raw
Core rows include all six Unix exchange cases and all 39 RAR5/7z update-stage
method/password cases. The independently inspected evidence transport SHA-256
is `10817d187830549a5d385ac28706ee15180ff0fa023e68caffd2f42af402179b`;
its source-file hashes match the frozen guard and fixture overlays. The prior
`34d98b51` Ubuntu run failed four exchange tests because the test's temporary
parent permitted group/other namespace writes; the production primitive
correctly refused that parent. The failed transport is retained. Commit
`6222dc55` makes each exchange fixture parent private, and the fresh rerun
passes. This is focused native evidence, not full Ubuntu regression or
qualification of a production update route. The wrapper fuzz campaigns and
Windows platform/rendering follow-up subsequently completed as recorded above.

**10 November: strengthened-source full regressions pass on Mac and Windows.**
Mac's frozen `eaf621f1` source passed complete Core 8,767/8,974, Remote
2,638/2,669 and App 2,770/2,963 suites, with 431 expected skips and zero
failures. The independently verified evidence transport SHA-256 is
`7538442b64bf0d422f3c14980a34ac38b0237b826a5e23f96fdff4be1521b2e9`;
the verifier checked the source packet and overlay, all raw test rows, four
exchange cases, image detach and process cleanup. A first Mac runner attempt
stopped at setup because the Remote restore assets were absent; the successful
fresh runner performed restore. The verifier's first Windows-host invocation
also needed a test-only POSIX-path correction before accepting the same sealed
transport. Windows passed complete Core 8,162/8,940, Remote 2,405/2,669 and
App 2,879/2,963 suites under the real standard account, with 1,126 expected
skips and zero failures. Its independently checked evidence transport SHA-256
is `d70e5e46006aaeb4caabbc2ad9592ba99d402f2056dae93000de82050b20d801`.
The raw rows, source hashes, account/profile and native F6 cases were checked.
The subsequent frozen `34d98b51` Windows Unix storage run passed 20/214
cases, with 194 expected Unix-only skips and zero failures; both new metadata
guard cases were present and skipped by their platform guards. Its verified
transport SHA-256 is
`e58fd1ce4731e69c90b28da719e56fb06b4be327b56050f28e2ee1c1bbf6039e`.
The matching Mac guard batch subsequently passed 251/253 storage cases, with
two expected platform skips and zero failures. Both new metadata rejection
cases passed. Its independently checked transport SHA-256 is
`f81b4884b416baf2cd7a6748d8de38d0ab8aa16f7840da9149fa3334659c705a`.
These results do not qualify a Unix update profile.

**10 November: current Mac and Windows RAR5/7z update matrices and Windows full regressions pass.**
The frozen `320f43c6` Mac source passed complete Core 8,724/8,931, Remote
2,638/2,669 and App 2,770/2,963 suites, with 431 expected skips in total and
zero failures. The 7,576,222-byte evidence transport SHA-256 is
`c3ff59b259f8335a5c6f05a4aa38bd887515121d85c25163f7233c4210030439`.
The independent verifier counted each raw TRX row, checked frozen-source hashes,
confirmed the disk image detached, and observed no remaining FileCat/test process.
The frozen `b98a4a43` Unix marked update cohort passed its 12/12 selected
RAR5/7z cases (transport SHA-256
`a803ba9a0f1cc999d68c8c2c3ab5938bc07cd18b7f02e59557f7b1b1f7262879`).
The expanded `45bce961` matrix then passed all 39 method/password cases after
a forced rebuild (transport SHA-256
`73a1fa4db24009240f4461d24ba18fc69e56fdac3fb3ac7ec442bad2aade6155`).
Its raw TRX has 39 passed rows, no failure or skip; the source-file hashes match
the frozen packet. Two earlier Mac matrix attempts are retained as harness
failures: one compared checkout line endings to packet bytes, and the other used
an incremental build that ran the older 12-case DLL. Neither is counted as a
39-case pass. The Mac remains test-idle with the requested until-reboot wake
assertion.

The Windows `320f43c6` full Core attempt stopped progressing after its
five-minute log marker. A managed heap dump and thread stacks show the xUnit
controller waiting with no active managed test frame. After over 40 minutes, the
exact owned Core test PID was verified by executable path and stopped. The failed
run and dump remain retained; no full Windows pass is claimed from it. The
released real-standard-user native archive batch from frozen `b98a4a43` source
passed 551/558 cases with seven expected skips and zero failures. All three
RAR5 and three 7z actual application update/restoration cases passed. Its
evidence transport SHA-256 is
`d3e78665d388c77a384f90e7ddc238cac745c3121dd5a3ee8187594dacb3d51f`.
The raw TRX rows, non-admin account and loaded profile, packet/source hashes,
and successful standard-user exit were independently checked. The subsequent
frozen `92c47e6b` Windows matrix passed all 39/39 actual application cases:
18 RAR5 and 21 7z, no skips or failures. Its evidence transport SHA-256 is
`fe17859e01c8bde949a40926020e4a230becf938ace6bb01920c79ee7f7a1127`;
the raw rows and updated source-file hashes match the frozen packet. The
bounded full `320f43c6` Core/Remote/App retry with VSTest hang tracing passed
8,162/8,936, 2,405/2,669 and 2,879/2,963 respectively, with 1,122 expected
skips and zero failures. Its evidence transport SHA-256 is
`386172899176f8e151bc7605c6700c3bd495f3cec3fa39f2d9e99248155b539c`.
The independent verifier counted every raw TRX row, matched source files and
packet hash, checked the non-admin account and loaded profile, and found the
RAR5/7z native F6 cases passing. The first stalled attempt remains a failed
run, not a pass. Windows then rebuilt the strengthened `eaf621f1` Unix exchange
source and passed 20/212 native storage cases under the standard account, with
192 expected Unix-only skips and zero failures. All four exchange cases were
present and skipped by their platform guards. Its independently checked
transport SHA-256 is
`6e52fa18ca5e92b5de3063120c1661d34b5383921de3d2bc396a2316915c44a9`.
An earlier runner incorrectly required 50 Windows passes and rejected its clean
20/212 result; that harness failure is retained. The later complete
Core/Remote/App regression on the strengthened source passed as recorded above. Ubuntu's
strict 7z and RAR5 native fuzz campaigns have now earned independently checked
8,323.744031 and 9,618.853456 CPU-second floors with zero crash artifacts.
Their evidence transport SHA-256 values are
`58a087e4cf774c6df6b923ba6a5a6a3dcf8d89bf6573c9c77880322e5dca34ad`
and `eb07cd5b0cd09ee3bd0915eae51f25e57b15ef3a3c622e1c9398e68c7ad8e2f3`.
The old post-fuzz queue falsely failed its global-idle check while the three
independent wrapper fuzz workers were still running, cascading FAIL through
dependent native queues. The fresh guarded Ubuntu native batch and the three
wrapper long campaigns later passed as recorded above.

**9 November: exact marked Unix private creation has a cross-format Mac native checkpoint.**
Commit `1b5e6129` carries one byte-identical Linux or macOS origin value from complete
captured evidence to a creator-private new archive, including empty/binary marks and
mixed marked/unmarked inputs; conflicting marks refuse. Mac guest tests passed 52/53
focused and 190/192 broad native cases with expected platform skips, including both
marked publications. Windows guest policy tests passed 25/27 with Unix-only skips, and
120/259 native storage cases passed under a measured medium-integrity child; both Windows
zone-only publication cases passed. The separate real standard-user/AppContainer batch
passed 475/476 cases with one expected skip and no failure. Commit `05a86d83` extends the
Mac native matrix to compressed RAR5 and 7z, both password modes and empty/binary marks;
all 14 marked publication cases passed in a 241/244 selected run. The Windows standard-user
full Core/Remote/App batch now passes as recorded below. Ubuntu's native check is queued and has no result yet. The detailed
[checkpoint](UNIX_MARKED_CREATION_ORIGIN.md) records source and evidence hashes. No profile
is enabled, and Unix marked updates remain unfinished.

**10 November: Windows complete regressions and current-source origin policy pass under a standard account.**
The frozen `05a86d83` guest source passed full Core 8,151/8,921, Remote 2,405/2,669 and
App 2,879/2,963, with 770, 264 and 84 expected skips respectively and zero failures.
The real `fcarchive42829297` account had no Administrators membership and its profile
was loaded. The original Core attempt's two test-output ACL failures remain in the bundle;
the scoped test-bin permission correction and all three accepted reruns are preserved.
The 10,498,140-byte evidence transport has SHA-256
`d3f1f0aabd5cbb0815dd74866cd252bf571ae93778f9f0d4c8900dd25e29994d`.
After that suite exited, the guest built commit `47cff5f4` with zero warnings/errors and
passed all 48 focused origin policy cases under the same standard account. Its 14,585-byte
transport has SHA-256 `36a454fb188f77d4b4e15c06fffab97aa14faf1ba8987dc20f7ca624f9742316`.
Both transports' raw TRX counts, account facts and frozen source-packet hashes were checked
independently; the later source files match the committed checkout after CRLF conversion.
The VM ended these batches without FileCat/test processes. The subsequent `47cff5f4`
native storage/AppContainer matrix passed 475/476 cases under the same standard account,
with one expected platform skip and both actual zone-only publication cases passing.
Its 121,017-byte transport has SHA-256
`70b5fb1ed493cac0e84823bb9ec004e89e5a0b755d451003205a0d1691cfa2df`;
raw rows, source packet, App/probe binary hashes, account and clean process exit were checked.
The first runner's transcribed source hash refused before build or test; that preflight is
retained, and the corrected runner's hash was mechanically compared with the packet before
launch. The same current source then passed all 34 native creation/member/edit/cleanup UI
cases under the standard account. The first UI copy omitted the solution-layout crash
fixture: 30 passed, four cleanup cases failed `Assert.NotNull`. A fresh full rerun with
the exact solution and native harness layout passed 34/34; both raw inventories are in the
25,300-byte transport (SHA-256
`27db5f2f6d2d802b940aa9dffb6cbfd3f4b5b55859747535b89fb53796abe044`).
Its packet, binary hashes, account, loaded profile and clean process exit were checked.

**10 November: Mac full regressions and latest Unix origin native checks pass.**
The frozen `9c0b2a46` source passed full Core 8,723/8,930, Remote 2,638/2,669 and
App 2,770/2,963, with 207, 31 and 193 expected skips and zero failures. The sealed
Mac transport has SHA-256 `3a04445fe2d3deca89cd7afc7d9fe96d513c8e8dd62c7a879adf9451c57e236c`.
The queued latest `47cff5f4` source then passed 266/269 native/policy cases with three
expected skips, including all 14 marked TAR/RAR5/7z creation and eight private snapshot
cases; its 70 KiB transport SHA-256 is
`2ba48d35682a800b35382988d4265fa384d3ed2a5a5d9df59202c49654bf8a3d`.
The host checked raw TRX rows, case counts, source-packet/file hashes and guest idle facts.
No FileCat/test process remains on Mac; the requested keep-awake helper remains active.

**10 November: Ubuntu adoption and strict-format fuzz resume.**
The final adoption candidate `0ee71ae1` passed focused Core 15/15, focused UI 7/10
(three platform skips), full Core 8,703/8,888, Remote 2,543/2,669 and App 2,772/2,963,
all with zero failures. Its 24,626,377-byte transport SHA-256 is
`749ca46869e36583915a63d49cb962064aa1ffbeadc34890fbd15ca0c8fcebb2`;
the independent verifier checked its source map, compiler/binary inventory and raw TRX.
The separate legacy discard recheck passed 58/58. The original post-fuzz queue then
stopped in a test-only seed preflight: the pinned Mac transport contains two verified
root metadata records in addition to `seeds/`. The repaired preflight admits exactly
those records, installs only manifest-listed seeds, and has launched fresh sevenzip/RAR5
strict campaigns. Benchmark, native marked creation and latest Unix-origin checks are
guarded behind that repaired fuzz chain. No long-campaign pass is claimed yet.

The independent wrapper chain was overlapped with the two strict campaigns to use more
of Ubuntu's eight CPUs. Gzip's short run passed, while the BZip2 short run produced a
229-byte crash input (SHA-256
`0b8498e331cf04004c2fbbe1df1aa281d2b61ec82299b7179fe29aa4a37da01f`).
A one-input Ubuntu replay reproduced an uncaught `IndexOutOfRangeException` in the
vendored BZip2 move-to-front decoder. The failed run and original runner are retained.
Commit `d0114807` maps that decoder error to `InvalidArchive` at the strict-reader
boundary and adds the exact crash input as a regression. The committed source passed
the focused TAR/BZip2 pipeline on Mac, Windows (real standard account) and Ubuntu:
36 passed, two expected skips, zero failures on each platform. The Mac, Windows and
Ubuntu evidence transports have SHA-256 respectively
`0e14b826be4ce47280cbf97c94213ac1fdd30d8df5d4ea9910bece86a692f9de`,
`a824aa24090d822e80f10ccd87df3e13b271d8e35523cad9765d6d0fb80a76e6`, and
`f6e033bda5967ea659c4412e848357d38f2378db38b68f36d4c7bee88252ac54`.
On Ubuntu, a one-input replay against a fresh instrumented image of the fixed source
returned one controlled refusal, no crash artifact and a successful process exit.
The new three-target wrapper campaign is active against that image. No FZ02 long-campaign
pass is claimed yet.

The same frozen `d0114807` source then passed complete Windows Core 8,162/8,936, Remote
2,405/2,669 and App 2,879/2,963 runs under the real standard account; Mac passed Core
8,724/8,931, Remote 2,638/2,669 and App 2,770/2,963. All six suites had zero failures;
the remaining cases were expected skips. The 6,355,978-byte Windows transport SHA-256 is
`4f4c53838c3adaafd82081a5f03c2bc671b7ddee89c99817f86853bbfd1f58fb` and the
7,542,105-byte Mac transport SHA-256 is
`951ff1799b7812a1b2d275b42daf5d0dfb5cc1887a9fbfd92ddcb6d357142521`.
Independent verification counted every raw TRX row, including the BZip2 crash regression,
matched source files to the frozen packet and normalized committed blobs, checked the
Windows standard account and Mac disk-image detachment, and confirmed clean guest exit.

**10 November: exact Unix origin reaches the verified native update stage.**
Commit `320f43c6` now rereads the complete sealed origin sidecar against the retained
original, captured files and directories before applying one exact opaque Unix mark to
the private existing-archive rebuild stage. The native stage is then freshly verified;
conflicting marks refuse. The real Mac focused run passed 51/51 cases, including all eight
marked TAR/TGZ/TBZ2/TZST update cases, with no test process left behind. Its evidence
transport SHA-256 is `341f895b269b8a9a857fd18f76da2b8366d7d51f218dcab457fcd7e16ebe34c4`;
raw TRX rows and source-packet hashes were checked. Windows passed 84/86 focused origin
and BZip2 cases under the standard account, with two expected Unix-only skips and zero
failures; its transport SHA-256 is
`336b44c7920393b958d9b658a3b7772b456a45020bf42a9e31f4b7da80994d31`.
Ubuntu's matching native replay is queued after the active fuzz chain. Complete
current-commit Windows and Mac regressions are running. Existing Unix target replacement,
retired-object recovery and production registration remain unfinished.

**10 November: marked Unix restoration preparation has an exact-origin checkpoint.**
`aee8a384` derives one exact opaque mark from the retained original and snapshot evidence,
and applies it only to a creator-owned private Unix stage. `9c0b2a46` corrects an invalid
test fixture for the unreadable-record case. The exact-source Mac APFS run passed 266/269
selected cases with three expected skips, including all eight snapshot-preparation and
14 marked TAR/RAR5/7z creation cases (SHA-256
`6bd266d982b77d6f1aa61ffb3ae0c146a621fdf9a386375406c6cd8675168eca`).
Ubuntu's matching replay is queued after its existing gates. This does not publish an
existing Unix archive update or enable a profile. The [checkpoint](UNIX_MARKED_UPDATE_PREPARATION.md)
records the scope and retained test-only failed run.

**9 November: Ubuntu fuzz qualification and dependent queues are being repaired.** The
RAR5 password long run finished cleanly with 12,003 CPU seconds and no crash artifact,
but its 60-second short run timed out. A separate bounded clean longer short run passed,
and the sealed long campaign independently verified its canary/replay, binaries, 16,834
executions, zero crashes and 7,200-second floor (SHA-256
`7e5710f535c4b479685b94f6ca0a5e71110e49dea1612d6b87c60a7c7068cb54`).
The first downstream adoption replay hit test-harness stale evidence, a too-long private
keyring socket path, and then an incremental-build PDB/source mismatch. Those failed
outputs remain retained. A short isolated keyring probe and a forced source rebuild now
pass; the guarded adoption resume continues. After adoption, 7z/RAR strict campaigns can run concurrently in separate
1 GiB units, followed by concurrent gzip/BZip2/Zstd campaigns; each retains its own
canary, short check, 12,000-second wall run and 7,200 CPU-second floor. Ubuntu AW0 and
marked creation follow those gates. Queued work is not passing evidence.

**9 November: the Mac ZIP extraction performance gate now passes on the focused candidate.**
The built-in Unix file operations observe the resolved file's device/inode with one native
`stat` at each existing backing-source checkpoint. Custom operations retain their previous
final-path and identity observations; the checkpoint schedule is unchanged. The final Mac
guest batch passed 63/63 focused backing-container, alias, replacement, visibility and
retry cases. Its canonical 20,000-note/four-64-MiB-file ZIP extraction took 8.2 seconds
versus 2.2 seconds in the in-box extractor, or 3.71× under the strict 4× limit. The
previous corrected-test run measured 8.0/2.8 seconds, or 2.88×. The final raw Mac bundle
SHA-256 is `1a6f8f7beaf9c679ab5994d0e013381d53085d07714ef4b9fb75b447c5f3675d`.
Windows' separate guest-built focused batch passed 61/61 executed Core cases (two explicit
Unix-only skips) and 66/66 native backing-source cases; raw bundle SHA-256 is
`e0b3a6d1d86055c2cd6f06acca89c9c1bff1d38e0cca4806d1c2d3c71d060fe6`.
The host verifier checked both transport hashes, source-file hashes, raw TRX inventories,
the exact Windows skips and Mac timing. Both guests ended without FileCat/test processes.
Ubuntu's queued fuzz/recovery campaigns still own that VM, so its representative product
and budget check on this candidate remains open. Exact commit `4a0cb2bc` was staged there
as a 66,616,264-byte source packet (SHA-256
`d9737d303aa078d9a8598d7ea171d40cf5e8c58339d5dac828852bed22d5a153`).
The original `filecat-archive-aw0-observation-v1-queue.service` failed on the upstream
fuzz dependency before running its tests; the guarded v2 queue now waits for the repaired
chain and an idle VM, then runs fresh focused cases and canonical ZIP/TAR.GZ benchmarks.
Queue activity is not a passing result. This focused
result does not qualify the
whole AW0 stage or replace final full-platform regression.

**9 November: FZ02 strict-wrapper fuzz targets are replay-ready, with no duration floor yet.**
The test-only harness now drives the production gzip, BZip2 and Zstd strict readers to
physical EOF with a 1 MiB encoded-input and 16 MiB decoded-output harness budget. On Mac,
24 deterministic valid, truncated, corrupt, concatenated, trailing and window/header seeds
produced nine accepted reads and 15 controlled refusals. The four required production reader/
frame types were present in the 45-type instrumented archive assembly. The 3,410,893-
byte replay transport is retained under `.tmp/storage-evidence/mac/fuzz-wrapper-v17` with
guest SHA-256 `5bfb3420d976733fcb6530256161b6e6989c9a49e611c48a148e6dbe685829e6`;
an independent verifier checked source, binaries, regenerated seed bytes, per-case outcomes
and zero-warning Mac build. No FZ02 native campaign or cross-platform qualification is claimed.
The v17 source-bound packet and three-format seed bundle are staged on Ubuntu; the repaired
guarded queue will start its canary, short runs and independent long campaigns only after
RAR5 password qualification, adoption and strict-format batches complete successfully.
The Windows 11 VM then replayed the same 24 seeds against its own guest-built Core/Archives:
nine accepted and 15 controlled refusals, with 45 instrumented Archive types and a clean
test-only build. Its 3,444,092-byte raw transport SHA-256 is
`f63cd83e1c7566b1f7d3c1059cc612c773cd401cb9eba0b076142329d10e2da3`;
the separate host verifier checked the complete case, seed, source and binary inventory.
No guest test processes remained. Ubuntu's native FZ02 campaign is still queued.

**9 November baseline: Mac AW0 extraction benchmark exposed a performance failure.** The canonical
20,000-note/four-64-MiB-file fixture (273 MiB payload) took 11.931 seconds for FileCat's
ZIP F5 extraction versus 2.268 seconds for the in-box extractor, or 5.26× against the
benchmark's strict 4× limit. The raw TRX SHA-256 is
`a734283995979641f7399820e99deb84d9ce5b2bff369b0b1f447e706dd25c53`.
Two preceding 8 MiB diagnostic fixtures likewise took 11.633/2.255 and 10.914/2.112
seconds. A separate temporary test-only instrumented run counted 240,049 final-path
queries taking 3.988 seconds; its raw TRX SHA-256 is
`7458a16a1c929c800cf28edb68b9cd843b5325566d364d50540947bde1bf3019`.
An attempted Unix-path cache still took 10.730/2.113 seconds and failed the gate, so it
was reverted. The Mac source and test build were restored and all test processes exited.
The later resolved-object observation result above supersedes this failed baseline on
the focused candidate.

The same canonical 20,000-note/four-64-MiB-file fixture also completed the Mac TAR.GZ
benchmark: FileCat extracted in 11.4 seconds versus 3.0 seconds for the in-box
`TarFile.ExtractToDirectory`, or 3.77×, within that individual 4× limit. Its raw TRX
SHA-256 is `49b2b89e8844aa9163c390765da0bd53e184512a26e17f5b7389d08ac4c307eb`;
the raw test log SHA-256 is `691b2df446fb3c3c287c046544de37c9ef15b4be14557d93b045854e908aaa83`.
This TAR.GZ measurement is independent of the later ZIP improvement and does not close
the overall AW0 gate.

**9 November: legacy reviewed discard now rechecks its record and complete working bytes.**
Commit `1f88274b` closes a stale-confirmation gap before the existing legacy deletion route.
The Mac guest passed 58/58 focused edit cases, including all four new unchanged/stale-save/
stale-record/wrong-review cases. The Windows standard-user VM passed 48/58 with ten expected
Windows platform skips; the same four new cases passed. Guest builds and the host compile
had zero warnings or errors, and both guests ended without FileCat/test processes. An initial
Windows result-check script incorrectly counted TRX skip rows as `notExecuted`; the raw test
run was passing and a corrected verifier confirmed its 48/10 inventory. Ubuntu's matching
source packet is staged for its post-fuzz replay. This recheck does **not** resolve R21's
pathname deletion race or qualify legacy exact cleanup.

**9 November: current-source Windows native UI addendum passed.** Fresh guest-built application
and probe assets drove real confined workers, NTFS publication and actual UI command paths.
Creation passed 10/10, F5/F6 member operations 10/10, F4 edit commit 10/10 and retained
archive cleanup approval 4/4, with zero skips or failures. The 17,947,999-byte transport
(SHA-256 `b80af4fea33b9972a8851fc41b51662d1001bf4311c33d8b7d1cd351995ba3f3`)
passed independent source/compiler/DLL/PDB/raw-case verification against product commit
`0ee71ae1`. The first native UI launch could not read its admin-created driver; after a
scoped standard-user read grant, creation cases initially failed to add AppContainer read
access to admin-owned copied assets. Those infrastructure failures are retained; granting
the standard user control only over this task's copied app/probe assets produced the passing
batch. No FileCat/test instance remained on the VM. This addendum does not qualify a
production write profile or replace the Mac/Ubuntu native UI gates.

**9 November: Mac's combined retained-adoption candidate is verified.** The final-target
comparison and original-folder review UI passed 15/15 focused Core cases and seven focused UI
cases (three platform skips), then full Core 8,681 passed/207 skipped, Remote 2,638/31, and
App 2,770/193, with zero failures. Its 24,611,390-byte source/compiler/binary/raw-result
transport (SHA-256 `e39b9cabc68a2e04bd159bd316f1511b52448bf05858dc0c3cfe60b87ee115ac`)
passed independent verification. Raw case inventories retained every prior pass after the
explicit UI parameterization mapping. Windows' same candidate passed 15/15 focused Core,
10/10 focused UI, full Core 8,139 passed/754 skipped and Remote 2,405/264 with zero failures.
Its accepted full App rerun passed 2,845/118 skipped with zero failures; the 26,003,925-byte
transport (SHA-256 `034949b428f676b1a07429670774bb4214e5f64c3b1ebb046436db942fee0aef`)
passed independent source/compiler/binary/PDB/raw-result verification. Raw inventories found
one added Core skip and no lost prior case or pass. The first full App attempt had one
folder-count quick-view timeout (2,844 passed, one failed, 118 skipped); its raw TRX is
retained (SHA-256 `99e59030378a2405c492ac7f65349bf99a9cf1457575fc9cfc16400ca3e10673`).
That exact seven-case folder-count class then passed in isolation, and the complete App rerun
passed. The intermittent UI timing has not been attributed to a product defect.
Ubuntu's 7z password long fuzz target
reached 11,687.566561 CPU seconds and 5,855,094 executions with zero crash artifacts; its
2,898,774-byte transport (SHA-256
`4d8ef4f653df31023dc234ab65dd52d6e0f8cb2a264c25630f0350f1aa1e7791`) passed
independent verification. RAR5 password fuzz remains active. These are individual candidate
and target floors, not production qualification.

**Open cleanup defect to resolve before native routing.** The still-active legacy
`EditCommitCopy.Dispose`, failed capture/session creation, record-publication temp cleanup,
`PublishSaveCopy` stage cleanup and legacy `Discard` use pathname deletion after ownership
was observed. A same-user process can replace the name between review and deletion. The
native Unix route correctly refuses uncertain cleanup; the legacy route needs a bound
replacement, regression cases and honest retained-artifact UI. The broader source audit
extends plan risk R21; it does not claim a fix.

**9 November: retained adoption integrity and full-target review.** Mac's full integrity
candidate passed Core 8,679/207 skipped, Remote 2,638/31 skipped and App 2,769/193 skipped;
its 24,593,503-byte source/compiler/binary/raw-result transport independently verified
(SHA-256 `5e81fff13ef9c9d0b4e18a196fc52a273f8ce05e58c19e6d350227b58be58d37`).
Windows' next candidate passed full Core 8,139/753 skipped, Remote 2,405/264 skipped and App
2,845/118 skipped, plus 14/14 focused Core and 10/10 focused UI, all with zero failures.
Its 25,989,953-byte transport independently verified (SHA-256
`27e7c61241e18f3670daa9ec002a5e0f87b1718e25cd58bc0a14aceb78363a51`).
Raw case inventories retained every prior pass, with an explicit one-to-two parameterized
UI case replacement. The next test-only candidate compares every retained native target and
baseline field to its original and shows the original folder and specific review reason in
the UI. The subsequently verified Mac and Windows combined candidate is reported above.
Ubuntu post-fuzz replay, old-folder retirement, production composition and the larger
release/final-review gates remain open.

**9 November: dual-root legacy/native edit composition begins.** A test-only `EditSessionStore`
can now keep v1/v2 sessions in their old root while creating and loading v3 sessions from a
separate native root. Misplaced records are reported as unsupported, and old records retain
their own save path. The shared Mac guest gate passes including two wrong-root records. Host
Core and Windows test builds have zero warnings; the Ubuntu guest gate and the
copy/relationship/retirement implementation remain open. The product factory
is still disabled. See [adoption boundary](LEGACY_EDIT_ADOPTION_BOUNDARY.md).
The v47 Mac connected focused batch passed all 27 gates, including 20 native storage cases.
Its 17,569,160-byte transport (SHA-256
`ff22aad28c6cef8477fe3d61f2cb18aee23a69e8a4c08cfcfae8de9b5fe31754`) passed independent
source/compiler/DLL/PDB/raw-test verification. Windows v47 also passed all 28 gates, including
15 dual-root storage cases; its 18,937,618-byte transport (SHA-256
`a7709eaba42c304be46261242e36421aae3166dfb41d5e6db9842c1352d14bc8`) passed the same
independent checks. Both guests exited their test processes. Ubuntu remains on FZ09.
A Unix-only read-only old-folder admission component now pins v1/v2 objects and refuses
linked, exposed or changed record/working paths while preserving their bytes. Mac's direct
10-case guest gate passed, including a real ZIP v1 record, origin marks, hard links and unknown
xattrs; the host Core test build has
zero warnings. It remains unregistered and needs a connected sealed gate,
copy/relationship/retirement and Ubuntu qualification. The separate Windows read-only old-folder
admission passed 10/10 focused standard-user VM tests, including a real ZIP v1 record, and an
adjacent run passed 168 with 139 guarded skips and zero failures. Eight guest source files and
three DLLs matched the independently verified evidence manifest SHA-256
`dfe38d56ac23dc84ed4e5afeb8466e64429c2e9920647109aa72072ecda9355c`.
See [Windows source checkpoint](WINDOWS_LEGACY_SOURCE.md).

**9 November: FZ03 strict 7z corpus and instrumentation are staged.** The test-only v14 packet
adds a direct production strict-catalog target, and a Mac guest replay accepted seven generated
codec seeds while controlled-refusing eight older variants. All 27 selected Archive types were
instrumented. The packet and replay transport are independently byte/binary verified and staged
inert on Ubuntu. The active FZ09 run must finish before an Ubuntu FZ03 image or campaign starts;
no 7z fuzz CPU floor is claimed. See [fuzz setup](FUZZ_CAMPAIGN_SETUP.md).
The follow-on v15 packet adds the RAR5 logical catalog and physical framing target. Mac guest
replay accepted all 12 generated source/rewrite seeds for methods 0–5 and controlled-refused two
incompatible/malformed inputs; the separate verifier bound its packet, binary, 14 seed bytes
and 33 instrumented Archive types. The v15 packet and corpus are staged inert on Ubuntu. It can
serve the next FZ03/FZ13 image after FZ09; neither format has a campaign floor yet.
The v16 password-aware test packet extends both strict targets through their production read
password sessions, bounded KDFs and encrypted payload/header graphs. Mac's full synthetic 7z
matrix passed 273/273 cases; independent guest replay accepted 21 generated 7z vectors across
seven codecs and all three protection modes, and 36 generated RAR5 source/rewrite vectors
across six methods and all three modes. Eight older 7z fixtures and two RAR4/malformed inputs
were controlled refusals. The 7z transport SHA-256 is
`53ee853544fcb5a36865b213fd6839e481956643ce22f6bc45cb943e0ff9d10b`; the RAR5
transport SHA-256 is `3c7b0f96918e42abff8c7adede2fd33f93b3fa14351e338b4527e88fef51299b`.
Both passed independent packet, sealed Mac binary, exact seed and 38-type instrumentation
verification. This is corpus/replay evidence, not a long fuzz CPU floor.

**9 November: first long FZ01 floor verified; stateful FZ09 campaign started.**
Ubuntu's clean strict-TAR campaign reached 9,708.590486 CPU seconds and 45,447,001
executions with no crash artifacts. The 2,223,951-byte sealed transport has SHA-256
`325385e6040e65c17f1e63a43ffca1b7f9f2a1f1d08ff1d951f39813b00a9625` and passed
independent image, method-coverage, seed and raw-member verification. Ubuntu v40 then passed
all 28 focused recovery/storage gates and its 54/54 v12 recovery-record replay. The v12
instrumented image found and replayed the intentional canary; its clean 60-second stateful
run passed 363,670 executions with both journal and transaction-collector methods exercised.
The bounded 12,000-second FZ09 `journal-state-nightly-v1` run completed with 10,134.451051 CPU
seconds, 38,006,260 executions and zero crash artifacts. Its 2,477,285-byte sealed transport
(SHA-256 `6472b580af3da8bdafa29346719db1d9c20483e0b1ccb2a31a744c2c15b0a7cb`)
passed independent source, canary/replay, production coverage and raw-file verification.
This establishes one FZ09 long target floor; other targets and final release gates remain open.
Mac v44's full Remote and App suites passed; its full Core suite preserved one recorder-test
race failure in a sealed 25,104,400-byte bundle (SHA-256
`9654ec8a4ee9038fff5165e2206336d51c7c3e568b33d3d130a0ff507bbf176c`). The race
is fixed in candidate `bf6690d4cc6775218b7b3777d7618af023b0a1e5`. Mac and Windows v45
both passed complete Core/Remote/App inventories and independent source/compiler/DLL/PDB/raw-test
verification, with zero failures, no missing prior Core cases or lost prior passes. Mac's 30-gate
bundle is 29,716,209 bytes / SHA-256
`effa3bccc8d5e0f62059c0c4ec04782dac58714545ae744d966c5c8e94ee3974`;
Windows' 31-gate bundle is 31,229,247 bytes /
`8dfbee6e2f9a3b05d068123b4930858633251fe19c3d9ce844ce7fddbc85c5a1`.
Windows v46 then passed all 28 connected focused gates for the new read-only retained-copy
startup/session-list hint, with its 18,935,275-byte bundle independently verified (SHA-256
`cc6c465b5d5055c13b410ed6918453423f27d8b6251ed027533a5b44870eba22`).
Mac v46 also passed its 27 connected focused gates and exact source/compiler/DLL/PDB/raw-test
verification. Its 17,573,297-byte sealed bundle has SHA-256
`14c6d36b28958491c3e14af4e512ac6c35e333b5ab91389e638b60b3d9a4e787`.
The Mac test processes exited and its keep-awake helper remains active. Ubuntu v46 waits for the
FZ09 campaign to finish.
The v46 candidate's full suites remain unrun. No native store or new writer is enabled.

**Earlier 9 November checkpoint: late retained-copy fix and fuzz preparation.**
Candidate `8d0946ff49c16b1950017f31ec52dff66407ac0e` keeps the result of a running native
saved-copy publication through late IO shutdown, disposes its receipts and reports a confirmed
copy while the edit UI is active. The host App/test build had zero warnings; Mac's complete v44
Core/Remote/App cohort and the Windows retry were subsequently run as described above.
Ubuntu was then occupied by FZ01. The test-only v12 FZ09 packet adds the production archive
transaction collector to instrumentation, requires it in image preparation, and is staged for
Ubuntu replay/canary/short-run checks after v40. Its two-hour-per-target and shared campaign
floors were still pending at that checkpoint. The
[Unix cleanup boundary](UNIX_EDIT_EXACT_REMOVAL_BOUNDARY.md) records why same-user replacements
still prohibit pathname-based native Discard.

**9 November: Mac full recovery-byte regressions pass; Windows full retry is running.**
The runner-only v41 cohort uses the v40 product sources. Mac passed all 30 connected and complete
Core/Remote/App gates with zero failures, including the exact 335-new-case Core inventory and no
missing prior case or lost prior pass. Its independently verified sealed bundle is 29,697,811 bytes /
SHA-256 `f685aca5fc7b4876819ed30e05a017d88d6774569367a35accb891533420553f`.
Windows v41 passed complete Core and the same inventory, then preserved a sealed failure when the
unchanged 8 GiB preflight reserve refused Remote. Twenty-six old extracted guest cohorts were
checked against retained host indexes before removal; the cleanup receipt records 26 verified and
26 removed folders, zero errors, and 10,190,790,656 bytes free afterward. Two v42 retry attempts
stopped before tests because the packet's expected predecessor hash and runner predecessor folder
did not match the actual sealed v41 source ledger. The v43 runner uses that exact v41 ledger and is
now executing the full Windows cohort. No failed attempt is counted as a pass. Ubuntu follows its
active FZ01 campaign, focused v40 batch and first native FZ09 launch. Full release qualification
remains open.

**9 November: retained-copy Save verified copy UI passes Mac and Windows focused gates.**
The recovery dialog offers the action only for a complete unused capture when a native destination
adapter is present. It uses a local save picker, exclusive native publication, and reports a
published copy separately from later receipt uncertainty. A two-case headless dialog check
reviews without publishing and follows the picker through a real native copy. Candidate
`a521bf45285110a2c836d9caec824e207fb99a5e` passes Mac's 27 and Windows' 28 connected
focused gates, including the two-case dialog gate, with zero failures. The sealed Mac evidence
is 17,565,568 bytes / SHA-256 `dc4d8abaa11f12d002ec2e07a7755a2076eb7d6c9304e07a48e1b9c80156efb1`;
Windows is 18,930,371 bytes / `a91ad57620a7a84fdc2934ec371e1f1bc460057beafbbf56814a5c282dfa4956`.
The [independent verifier](../../eng/validation/archive-writing/verify_edit_recovered_save_copy_ui_v40.py)
passes source/compiler/DLL/PDB/raw-test binding. The default factory remains disabled. Ubuntu's
direct v36→v40 packet is staged, but execution waits for its FZ01 long campaign to finish.

**9 November: restarted retained-capture Save copy passes Mac and Windows focused gates.**
A new internal action reopens one complete, unlinked capture through native handles, binds it to
the fully validated historical state, checks exact payload/record bytes and metadata, admits the
new source intent under the root storage gate, and uses the existing exclusive new-file native
publication protocol. The original working file and captured bytes are retained. Nine shared
cases cover restart, later state, destination collision, corrupt/foreign source evidence,
repeat attempt, stale state and cancellation. Candidate `248100915688bb5e76e073776755703e5531147d`
passes 27 Mac and 28 Windows connected gates, including recovered-save-copy9, capture33,
saved-copy-session26 and admission45 on each, with zero failures. Mac's sealed transport is
17,539,497 bytes / SHA-256 `3be7514445ae3729125319e64228d43b3598a1c38066e7262031514715779071`;
Windows is 18,901,969 bytes / `61ceb7374f527ddae1720f691962e2c45f61b1c86a248987accb665ab92dfaf8`.
The [independent verifier](../../eng/validation/archive-writing/verify_edit_recovered_save_copy_v39.py)
passes source/compiler/DLL/PDB/raw-test binding. Windows' first 8 GiB reserve refusal remains
in its sealed evidence; after exact hash/size checks against host copies, 528,229,959 bytes of
duplicate older guest tar files were removed and the same candidate resumed without lowering
the reserve. No default UI/factory route is enabled. Ubuntu waits for FZ01. Recovery UI,
partial-outcome handling, crash/resource matrix and complete inventories remain open.

**9 November: historical retained-capture recovery passes Mac and Windows guest gates.**
The native review now binds each capture to its exact record in the fully validated bounded
session-state chain, so a later legitimate state revision does not hide an older complete capture.
Three new shared cases cover one and two later revisions and a saved-copy intent linked to the
earlier state. Candidate `a59c7d2aa5515cb81f6c59a2af8a0e138a2437b8` passes all 26 Mac and
27 Windows connected focused gates, including capture recovery33 and saved-copy sessions26 on
each, with zero failures. The sealed Mac evidence is 17,546,759 bytes / SHA-256
`5189bbd807e501aad149678df79f1b8074b19308a1106aa3032d4d93d46bb0be`;
Windows is 18,907,206 bytes / `7983b336985294c24dad4c99db7b7cf44a99df9d4864385343f6690654a92911`.
Independent source/compiler/DLL/PDB/raw-result verification passes; owned processes are zero
and Mac's wake helper remains. Ubuntu's same-candidate packet is sealed but waits for FZ01.
Full inventories for these bytes, recovery actions and all other plan gates remain open.

**9 November: FZ09 recovery-record packet is built; legacy adoption boundary is recorded.**
The test-only v7 harness builds cleanly, adds instrumented production session, capture, saved-copy
intent and receipt parser targets with synthetic schema/corruption seeds; its sealed native-fuzz packet is queued behind
the active Ubuntu FZ01 campaign. Separately, Mac/Windows each pass all28 uninstrumented guest
v7 replays against exact v38 product DLLs (eight accepted, twenty controlled refusals), source/seed
hashes independently checked. No FZ09 campaign floor is claimed. A legacy-adoption draft exposed that old
inherited permissions cannot be opened under version-3's exact private lease; the draft was removed.
The [adoption boundary](LEGACY_EDIT_ADOPTION_BOUNDARY.md) now requires a pinned read-only legacy
source and separate private destination before migration, routing or retirement can proceed.

**9 November: retained-copy recovery UI passes Mac and Windows focused guest gates.**
`v37` passes the new read-only retained-copy review case, all 26 connected Mac and 27 Windows focused
gates, 45 native storage-admission cases on each, and the applicable Discard cases with zero failures.
The sealed Mac transport is 17,540,399 bytes / SHA-256
`6009c4c7ea6a4176a54914530a102eb65655100dfa642a95f304eb6119189f13`; Windows is
18,909,575 bytes / `55bdb00cae83c1de66814fd922e6608685ecfdbb14ea0d18d59fa4f887a0aaf6`.
Independent verification binds the exact source, compiler documents, DLL/PDBs and test counts to candidate
`6cd1b63e92f0b498ea3883d8cdf39c4b7affc93d`. Owned processes are zero on both guests; Mac's
wake helper remains. Ubuntu's `v37` UI batch waits for its active long FZ01 campaign. The dialog only
reviews preserved data; fresh recovery actions, adoption, default composition, full inventories and all
remaining qualification remain open.

**9 November: coverage-guided fuzz canary passes; structured FZ01 campaign is running.**
The [test-only setup](FUZZ_CAMPAIGN_SETUP.md) pins the .NET 10/SharpFuzz/native driver artifacts and
uses separately instrumented guest copies of the verified product DLLs. Ubuntu's canary-only input
adaptation found a post-validation defect in the real Core guard; the artifact crashes only the
patched image. A clean strict TAR/raw-decoder 60-second gate completed 391,877 inputs with no crash.
Four individually accepted synthetic member/PAX seeds now feed a bounded 12,000-second Ubuntu run.
Its nightly floor is **not yet earned**. Other fuzz targets/floors, linked-native tooling provenance,
three-platform and release qualification remain open. The next unregistered native-edit candidate
adds a read-only retained-capture recovery dialog; its Mac/Windows guest UI gates pass above.

**9 November: reviewed Discard allocation and mapped-editor follow-up passes everywhere applicable.**
`v36` passes Windows40 discard cases (including writable mapped pages after the editor's original file handle
closes),6 native-store UI cases and45 admission cases. Mac/Ubuntu each pass45 admission,3 Unix refusal-preservation
and3 applicable UI cases; their3 destructive UI cases remain explicitly skipped because Unix deletion is unavailable.
Every connected focused gate passes on all three systems. Exact raw/compiler/DLL/PDB evidence is preserved and
verified, owned instances are zero and Mac's wake helper remains. The128-inspection allocation ceiling passes;
empty-gate hashing no longer allocates an unused1 MiB buffer per inspection. This candidate's new full inventories
remain unrun. Native registration, Unix exact removal, recovery/adoption/overwrite, full native desktop acceptance,
canary-validated fuzz/RID/package/release gates and final independent review remain open.

**9 November: reviewed Windows Discard and its confirmation flow pass connected guest gates.**
`v35` passes Windows39 real-file discard cases and6 native-store headless UI cases. Mac/Ubuntu each pass3
unavailable-operation preservation cases and3 applicable UI cases; exact Unix deletion remains unavailable.
All three also pass admission44 and their complete connected focused selections. Sealed raw/source/DLL/PDB
bundles are verified, owned instances are zero and Mac's wake helper remains. New full inventories are unrun.
A follow-up now avoids an unused1 MiB buffer in every empty gate inspection, adds a128-review allocation ceiling
and tests an editor retaining writable mapped pages after closing its file handle. The `v36` candidate queues
admission45 and Windows discard40; its host Windows build passes. No production factory/profile is enabled.

**9 November: mutation gates pass on all three systems; reviewed native Discard/UI candidate queued.**
`v34` passes all44 storage/mutation cases and every connected focused gate on Windows/Mac/Ubuntu.
Each sealed raw/source/DLL/PDB bundle is verified with zero owned instances; Mac's wake helper remains.
Windows' initial 8 GiB startup refusal is preserved separately inside its resumed same-candidate bundle.
Two matching preserved-host duplicate archives reclaimed772,614,362 bytes; sources/extracted evidence remain.
The startup collector omitted two lengths after moving files; raw data remains unchanged and its sealed
transport independently proves the actual lengths/hashes. This metadata defect is explicit in the checkpoint.
The preceding full milestone also passes: Mac/Ubuntu `v31`, Windows `v33`, each bound to its own candidate.

The [next connected candidate](NATIVE_EDIT_STORAGE_ACCOUNTING.md) implements fresh native Windows Discard
ownership, full preflight and exact-handle removal for reviewed working/retained files, earlier capture states,
interrupted copies and saved-copy intents. The confirmation action uses its typed complete/partial outcome.
It adds39 native Windows,3 Unix refusal-preservation and6 headless UI cases; Core/Windows/App builds pass.
Fresh guest affected gates are queued. Unix deletion, recovery/adoption, overwrite/composition, native desktop,
fuzz/RID/package/release and final independent review remain required. No production factory/profile is enabled.

**9 November: native retained-storage admission passes Ubuntu's full milestone; connected mutation corrections queued.**
Ubuntu `v31` passes native admission34 and every affected gate plus complete Core8,649/185 skipped,
Remote2,543/126 and App2,765/188. Its sealed raw/source/DLL/PDB evidence is verified with zero owned instances.
Mac's same-candidate full suites also pass: Core8,627/207 skipped, Remote2,638/31 and App2,763/190. Its sealed evidence is verified, owned instances are zero and the wake helper remains. Windows `v33`
passes admission34, connected affected gates and complete Core/Remote; App remains confirmed live and sealing is pending.
Its `v32` failed fixture evidence remains preserved and verified, independently from this correction.

The next connected `v34` candidate serializes saved-copy intent and exact live capture cleanup with storage
admission, uses root-before-snapshot lock ordering, retires all owners/permits after cleanup refusal and checks
new-root parent/child paths. Native Discard's unsafe recursive-path fallback is disabled until exact native
ownership is implemented. Ten new failure/race/retention cases expand the native matrix to44. All44 and every connected focused gate pass on Mac/Ubuntu; sealed source/binary/raw evidence is verified with zero owned instances. Windows follows its active v33 full retry. New complete inventories remain explicitly unrun for these newer runtime bytes. Core and Windows-platform builds pass cleanly. A subsequent UI correction catches unsupported Discard refusals and builds App cleanly; its runtime validation belongs to the next UI candidate.
Safe discard, recovery actions/startup UI, overwrite/adoption/composition and the full original plan remain open.
No native store/profile is enabled. The [cadence](VALIDATION_CADENCE.md) reserves the next complete inventory
for a connected recovery workflow milestone or a finding requiring broader coverage.

**9 November: connected native saved-copy gates pass everywhere; Windows full retry also passes.**
`v29` passes native session25, strict intent14, cancellation fixture2 and all connected gates on Windows/Mac/Ubuntu.
The Windows complete inventories pass: Core8,139/666 skipped (275 required additions, no lost baseline cases/passes),
Remote2,405/264 skipped and App2,835/118 skipped. Sealed raw/source/DLL/PDB evidence is verified, with zero owned
instances on each system and the Mac wake helper retained. Mac/Ubuntu `v29` full suites remain explicitly unrun;
their preceding `v28` full milestone is attached to its own runtime bytes.

The [native storage-accounting draft](NATIVE_EDIT_STORAGE_ACCOUNTING.md) now compiles and measures bounded logical
data plus native metadata without hashing payloads. Windows uses metadata-only handles and a complete bounded
stream-size catalogue. Root-wide write admission, persistent contingency charges, space checks and native
resource/concurrency/crash qualification remain required for the next connected batch. No store/profile is enabled.

**9 November: preceding native edit full milestone passes on Mac/Ubuntu; Windows capacity failure is preserved.**
`v28` completes Core/Remote/App on Mac and Ubuntu with zero failures, 235 required new Core cases and no missing
baseline cases/lost passes. Sealed raw/source/DLL/PDB evidence is preserved and verified, with zero owned
instances and the Mac wake helper retained. Windows Core encountered a confirmed disk-full error and an
unbounded cancellation-fixture wait. Its exact process/dump/exception evidence is retained; 76 checksum-matched
duplicate archives were reclaimed and the independent Remote/App suites completed successfully. That failed
cohort remains preserved separately from the successful `v29` retry above.

The connected [native Save copy session/recovery implementation](NATIVE_EDIT_SAVE_COPY_SESSIONS.md) passes `v29`
on Mac/Ubuntu: native session25, strict intent14, cancellation fixture2 and all connected focused gates. Sealed
raw/source/DLL/PDB evidence is verified, with zero owned instances and the Mac wake helper preserved. Windows
runs affected gates and full inventories on the same candidate, with successful results recorded above.
Mac/Ubuntu full suites for these newer runtime bytes remain unrun until the next complete workflow milestone. The [cadence reassessment](VALIDATION_CADENCE.md)
adds compact read-only observation and keeps all final QA requirements. Overwrite, cleanup, retained resources,
UI composition and the wider plan remain open; no native store/profile is enabled.

**9 November: saved-copy publication/encoding gates pass on all three systems; startup-reader follow-up queued.**
`v26` passes record25 (including 10,000 seeded mutations), native publication25 Windows / 26 Mac/Ubuntu and
all connected focused gates. Exact source/DLL/PDB evidence is preserved and verified with zero owned instances;
Mac's wake helper remains. The original 10,193-input probe now returns 100 accepted / 10,093 controlled refusals /
zero escaping exceptions. Review identified the same delayed Unicode failure in legacy session/capture readers.
Their correction builds cleanly and `v27` expands session recovery to 25 and native capture recovery to 31 cases.
Fresh guest qualification and then complete runtime milestone inventories remain required. No store/profile is enabled.

**8 November: malformed receipt UTF-8 refusal corrected; expanded regression candidate queued.**
The [encoding probe](SAVE_COPY_RECEIPT_ENCODING.md) found `InvalidOperationException` escaping through delayed
JSON Unicode materialization on malformed receipt bytes. Exact failed probe/source/binary evidence is preserved.
Complete bounded UTF-8 validation and controlled Unicode refusal are implemented with five new cases, including
10,000 deterministic mutations. `v26` queues record25 and native publication/connected regressions on all three
systems. The preceding `v23` complete Core/Remote/App inventories now pass everywhere and remain attached to
their own runtime bytes; no store/profile is enabled.

**8 November: native saved-copy exclusive publication and receipts pass Windows/Ubuntu focused gates.**
[Saved-copy publication](NATIVE_EDIT_SAVE_COPY_PUBLICATION.md) adds single-owner native name reviews, complete
receipt readback and confirmed/uncertain outcomes that survive late cancellation or receipt failure. Core and
Windows-platform builds pass without warnings/errors. The first `v24` native gates preserve Windows sharing-reader
and Ubuntu umask fixture failures, with record20 passing. The corrected `v25` candidate passes publication25 Windows /
26 Ubuntu and 20 strict receipt cases on both; all connected focused regressions also pass. Exact sealed bundles
and source/binary bindings are preserved, with zero owned instances and unchanged product binaries across the
fixture correction. Mac follows its active `v23` full
inventory. The changed candidate's complete inventories are explicitly not run yet under
the [measured cadence](VALIDATION_CADENCE.md). Replacement, restart discovery/actions, cleanup/crashes and UI/job
integration remain open; native store/profile registration and the existing guard remain unchanged.

**8 November: a Windows full-run close failure is diagnosed and a correction is under test.**
[Comparison cleanup](COMPARISON_CLOSE_FAILURE_DIAGNOSTIC.md) records the owned-source close exception
that escaped through an async-finally continuation and left the xUnit unhandled-exception handler waiting.
Its dump and failed cohort are preserved. Candidate `fa3975d5` returns the selected failure through normal
task completion after both owners retire; the original 104-case matrix and four deferred regression
variants pass all 108 `v23` cases on Windows/Mac/Ubuntu. The corrected complete Core inventories pass on Windows
(8,093 passed / 611 skipped), Ubuntu (8,514 / 185) and Mac (8,492 / 207). Windows/Ubuntu complete Remote/App also
pass and their exact raw bundles are preserved and verified; Mac's complete App inventory remains active.

**8 November: retained Save copy destination preparation implemented; first native Unix gates pass.**
[Saved-copy preparation](NATIVE_EDIT_SAVE_COPY_PREPARATION.md) copies retained views into a fresh native
private stage, checks complete input/output bytes and preserves exact origin/time and private security.
Mac and Ubuntu each pass all 17 native cases in `awe-storage-v22`; Windows passes them in `v23`.
Mac/Ubuntu `v22` complete Core/Remote/App inventories also pass and their raw evidence is preserved and verified.
Publication/overwrite/recovery and application integration remain required, and the native
Save copy guard/factory qualification remain unchanged.

**8 November: native archive snapshot requests bind the originating target/profile; live gates queued.**
[Request binding](NATIVE_EDIT_REQUEST_BINDING.md) closes a gap between the generic replacement shape
and the archive/member/profile actually reviewed for that session. The snapshot fixture now uses native
archive records and tests redirection, remote-to-archive refusal and explicit rebases. Windows-platform
and Core builds pass. Candidate `39e0f0c6`, `awe-storage-v21`, will combine this with the checksum test
expansion after the active full batches finish. Its first snapshot35 Windows / 34 Mac / 34 Ubuntu and
checksum UI12 gates pass, as does Ubuntu's SFTP23 gate. Complete inventories remain pending. The prior
Mac/Ubuntu `v18` and Windows `v20` complete Core/Remote/App inventories pass and their exact evidence is
preserved; all three now run the same frozen `v21` implementation. No factory/profile is enabled.

**8 November: retained SFTP edit uploads implemented; three-platform qualification is running.**
[Retained SFTP edits](RETAINED_SFTP_EDITS.md) bind a captured edit to its originating server file,
verify consumed bytes and require complete stage readback before guarded replacement. The first
Mac/Ubuntu iteration exposed a connection-pool cleanup fixture error; its failures are preserved and
the corrected 23-case matrix passes on all three platforms as `awe-storage-v18`. Complete Core/Remote/App
inventories continue on Mac/Ubuntu. The Windows `v19` diagnosis proves Defender blocks the deceptive
executable-name fixture; FileCat reports that refusal correctly. The `v20` test expansion requires
that native refusal explicitly and adds a readable variant; all 12 checksum UI checks and 23 retained
SFTP cases pass on Windows before its active full rerun (one platform-specific checksum UI case skips).
The previous complete Core suites pass on all three platforms. Complete Mac/Ubuntu App suites pass;
Windows has one checksum fixture expectation under correction. No factory/profile is enabled.

**8 November: retained native edit snapshots drive legacy ZIP jobs; first integration gates pass.**
[Retained ZIP edits](RETAINED_LEGACY_ZIP_EDITS.md) remove the source-path byte reopen, bind a single originating
session replacement and verify source/output hashes. All three platforms pass 18 real-file cases.
The full application inventory found a canceled-admission retirement defect; its fix passes 29 Core
and 18 history UI checks on each platform. Ubuntu's four picture-fixture failures are corrected by
matching the production runtime environment; its 12 boundary checks pass. The `feb9105e` full Core and
application rerun is complete, with the Windows checksum-dialog finding above. Failures remain preserved.

**8 November: Windows capture-tree cleanup and fresh read-only recovery implemented; full regression rerun active.**
[Capture recovery](ARCHIVE_EDIT_CAPTURE_RECOVERY.md) retains native creation authority through Windows
payload/record/directory removal, preserves exact opaque origin marks and reports retained copies after restart.
Candidate `f62d81f9` passes 27 recovery and 35 Windows / 34 Unix snapshot cases plus its connected gates on
all three platforms. Record-byte hardening and the corrected Windows boundary fixture pass the later
focused gates. The `08097526` complete Mac/Ubuntu inventories exposed missing fixture setup; `b9929987`
restores the earlier case-sensitive image/private-keyring environment for the full Core/application retry.
The progress checkpoint preserves those failures. Unix destructive cleanup, restart actions/UI, durable retained-disk
admission, Save copy, ZIP/SFTP/discard/adoption/composition and the remaining qualification gates stay open.
The application still uses the legacy store; no profile or native-store factory is enabled.

**8 November: native snapshot capture and queued retained-input views pass focused platform gates.**
[Native snapshots](ARCHIVE_EDIT_SNAPSHOTS.md) retain actual file ownership through job reconciliation,
preserve exact bytes/origin/time, bind one replacement to a session and avoid queued source pathname reopens.
All platforms pass 26 snapshot and ten commit-copy cases, their native storage/recovery gates and the
connected Core/application edit selections. The [checkpoint](edit-snapshots-checkpoint.json) binds exact
compiler/binary bytes, preserves fixture failures and proves unchanged product binaries across the retry.
All owned instances are closed; Mac's wake helper remains active. Capture-record/directory cleanup,
Unix payload cleanup, native Save copy, ZIP/SFTP integration, discard/migration, resource bounds,
application wiring and the complete qualification/final-review gates remain open. The factory stays disabled.

**8 November: native edit storage and working reads implemented behind an explicit test factory.**
[Retained edit storage](ARCHIVE_EDIT_STORAGE.md) adds private native creation, interrupted-copy recovery,
exclusive immutable state revisions, bounded complete history review and atomic-editor-save admission.
The Mac/Ubuntu correction matrices pass 19 record, 16 directory, 19 native storage, 110 Core and 190
application cases each. Windows passes 19/16/14/100/190 with ten Unix-only Core cases skipped after an
initial sharing refusal and a later idle-machine retry. [Thirteen preserved bundles](edit-storage-checkpoint.json)
bind exact sources/binaries and all early failures, including Ubuntu's corrected umask admission defect.
The application factory remains unchanged; native snapshot/discard ownership, migration/composition,
complete platform regressions and every release/final-review gate remain open.

**8 November: connected native archive edit/readback matrix passes, ownership gates open, unregistered.**
[Native edit sessions](ARCHIVE_EDIT_SESSIONS.md) connect F4, immutable commit copies, explicit conflict/rebase,
positive publication facts and actual published-member readback. The connected packet also preserves old/future
session records, avoids metadata filename collisions and makes new Unix edit files private. The retained `14cb2678`
cohort and its Windows fixture timeout are preserved. Candidate `f758c487` passes the focused edit gate,
259 application cases on every platform and complete Core inventories (Windows 8,065 / 465 skipped;
Mac 8,318 / 207; Ubuntu 8,340 / 185). Windows also passes ten real F4/Commit cases, 217 native checks,
18 crash cases, 14 application-entry cases and, after a separate navigation-fixture correction, ten member/F5/F6
cases. The [checkpoint](edit-sessions-checkpoint.json) binds exact compiler bytes and retained failures;
all owned instances are closed and Mac's wake helper remains active. Native namespace/cleanup and migration
hardening, remaining UX cases, Unix mutation/confinement and all release/final-review gates remain open.

**8 November: RAR5/7z password browsing and read ownership implemented, unregistered.**
[Password browsing](ARCHIVE_PASSWORD_BROWSING.md) connects masked password retry, source-bound readers,
restored-tab Refresh, nested archive/viewer lifetime and queued extraction after tab closure. All platforms pass
20 Core and 27 connected UI cases, the affected recovery/application selections and complete Core inventories.
The [checkpoint](password-browsing-checkpoint.json) binds 21 changed C# inputs and 12 DLL/PDB pairs per platform,
preserves the Mac fixture and recovery-container failures, and records zero owned instances with the Mac wake
helper retained. New writer registration remains disabled. Versioned native archive edit sessions are next;
member UX edges, Unix native mutation/confinement and the remaining release/final-review gates stay open.

**8 November: connected member/F5/F6 component matrix passes, unregistered.**
[Member commands](ARCHIVE_MEMBER_COMMANDS.md) connect exact occurrences and recursive folders, listing-revision
checks, password retry, typed destination review and skip-aware F6 source removal. All platforms pass 116
focused Core, 53 connected UI and their complete Core inventories. Windows also passes ten real member UI
cases, 217 native regressions, crash/application checks and independent readback of all ten member vectors.
The [checkpoint](member-commands-checkpoint.json) binds frozen source and DLL/PDB bytes, identifies Mac's
test-only startup repair and Windows' reader-only continuation, and records zero remaining owned instances.
Mac's wake helper remains active. Further UX/session, Unix native and release/final-review gates remain open;
no new profile is registered or qualified.

**8 November: retained target review and existing-archive Pack updates implemented, unregistered.**
[Target review](ARCHIVE_TARGET_UPDATE_UI.md) binds queued updates to a complete manifest and native source
observation, preserves existing RAR5/7z password modes and retains preparation ownership through cancellation.
All three platforms pass 28 Core preflight, 16 update UI, 20 creation UI, eight mutation preparation and 21 password
cases, the affected application selection and complete Core inventories. Windows additionally passes ten real
creation/update UI cases, ten native review/update preflights, 217 native regressions, 18 crash cases, 14
application-entry/cleanup cases and independent readback of the new archives. The [checkpoint](target-update-ui-checkpoint.json)
binds exact compiler bytes to the frozen Git candidate and records original failures, explicit Windows Core
reuse after a test-only correction, and zero remaining owned instances. Mac's wake helper remains active.
The next connected chunk covers open-archive F5/F6 and member commands. Browsing/edit password sessions, Unix
native mutation/confinement, full production qualification and the final independent review remain unfinished.

**8 November: shared creation options and encoded password admission implemented, unregistered.**
[Creation UI](ARCHIVE_CREATION_UI.md) connects explicit format/compression/7z options and masked password
confirmation to captured job inputs. All platforms pass 20 creation UI cases, 21 password cases, the affected
application regression selection and complete Core suites. Windows also passes ten real confined-worker UI
creations, 217 native regressions, 18 cleanup crash cases and independent readback of all ten new archives.
The [checkpoint](creation-ui-checkpoint.json) records source/PDB bindings, failed runs and instance cleanup.
Delayed suffix validation and refused-draft control ownership defects are fixed. No new writer is registered;
shared member/update/edit/password journeys and the remaining platform/release/final-review gates stay open.

**8 November: automatic native cleanup and creation recovery implemented, unregistered.**
[Automatic cleanup and creation recovery](ARCHIVE_AUTOMATIC_CLEANUP.md) pass the focused platform checks,
complete Core regressions, Windows native/crash/application matrix and independent readers. The
[checkpoint](native-cleanup-checkpoint.json) binds current source/PDB inputs and archive bytes and records zero
remaining owned test instances on all three platforms.
The Windows candidate cleans creation/update artifacts by default, preserves committed outcomes on cleanup failure,
and exposes creation/RAR5/7z retained-file review. No new writer is registered and the overall goal remains active.

**8 November: Windows native F6 and shared source accounting implemented, unregistered.**
[Native moves](NATIVE_ARCHIVE_MOVES.md) add captured-source removal after reviewed publication, directory
change tracking, exclusions and partial/canceled move accounting. The legacy ZIP case-distinct source accounting
fix passes on the actual Windows adapter. The [checkpoint](native-archive-move-checkpoint.json) records the complete
Core evidence, Mac's four source-preserving APFS fixture repairs, 23 native source guards, twenty actual Windows
create/update moves, 197 inherited native cases, ten application-entry cases, independent readers, source/PDB
binding and instance cleanup. Original failures and the publication-reader lifetime fix are retained. Unix native
moves, cleanup/recovery, shared UI and remaining qualification/final review stay open. No new profile is enabled.

**8 November: case-distinct native source selection fixed.**
[Source-case follow-up](NATIVE_ARCHIVE_SOURCE_CASE.md) preserves distinct file and parent-directory spellings
before native capture. The real NTFS reproduction showed two distinct files collapsed into one source by the
previous Windows string maps. Two new core cases and twenty actual application-worker cases cover the fix across
all six container formats and every RAR/7z password mode. The [checkpoint](native-source-case-checkpoint.json)
records the complete 3,866-case affected archive closure on each platform, 116 focused cases, 197 Windows native
cases, independent readers/source bindings and process cleanup. Unchanged non-archive code retains the preceding
full-core baseline. Native F6/cleanup, shared UI and the remaining qualification/final-review gates remain open.

**8 November: native manager execution and review decisions implemented, unregistered.**
[Native jobs](NATIVE_ARCHIVE_JOBS.md) connect exact catalog/member/folder scopes, multi-parent source capture,
bounded safe previews, replacement/exclusion/7z precision decisions, role-bound workers and native publication.
Existing native targets are rechecked before parsing. A complete duplicate group can relocate together;
partial/new collisions still refuse. The [checkpoint](native-archive-job-checkpoint.json) records 114 focused
cases on each platform, 12 Unix/14 Windows native job preflights, complete 8,320-case Unix/8,325-case Windows core
suites, 177 Windows native cases, ten application-entry cases, independent readers and source/DLL/PDB binding.
Original failures and validation-helper repairs are retained. Checked deltas, verified binary reuse and reader-only
continuations speed the batches while preserving coverage. Default application composition remains empty.
Safe creation cleanup, native F6, recovery UI coverage, creation/member/edit/password UI, Unix update/confinement
and the remaining qualification/final-review gates are open.

**7 November: typed production job dispatch and password ownership implemented, unregistered.**
[Job component](ARCHIVE_JOB_BACKENDS.md) adds per-manager backend composition, frozen queued changes and secret
cleanup on cancellation, admission/handoff/journal failure and completion. All 45 new cases pass on all three
platforms. The [checkpoint](archive-job-backend-checkpoint.json) binds the complete core suites (8,239 cases on
Mac/Ubuntu; 8,244 on Windows), independent readers, Windows native/application-entry selections and cleanup.
Ubuntu's first environment failure and private-keyring rerun are retained. Native backend execution, shared
decisions and UI/edit/F6 integration remain open. Default production composition is empty.

**7 November: shared parent workflow review implemented, unregistered.**
[Shared workflow adapter](SHARED_ARCHIVE_WORKFLOW_REVIEW.md) connects exact member/recursive scopes, directory
merges with provenance, duplicate replacement, exclusion review and declared 7z input precision to all six
container planners. It adds 62 focused cases; the final 3,738-case core matrix passes on Windows/Mac/Ubuntu.
The [checkpoint](shared-workflow-review-checkpoint.json) records the passed independent readers, source/DLL/PDB
bindings, 163 Windows native cases and owned-process cleanup. The subsequent typed job component is recorded
above; native executor/availability qualification and UI/password/edit/F6 integration remain open.

**7 November: 7z shared pipeline implemented, unregistered.**
[Semantic preservation and worker/native checkpoint](SEVENZIP_WORKER_PIPELINE.md) connects parent planning,
packed reuse, exact optional metadata, role-bound passwords and captured/native creation. Windows application
updates and snapshot restoration now pass all password modes. The common 3,666-case platform matrix and the
163-case Windows native selection pass. Shared production workflows/UI, Unix confinement/update and complete
resource/crash/fuzz/package qualification remain open; no RAR/7z profile is enabled.

This is an **incomplete implementation**, not format or release qualification. The controlling requirements remain
[the archive writing plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md). No new writer is enabled in the
application. An explicit new-profile job refuses instead of entering the legacy ZIP executor.

**Next coherent implementation chunk:** native ownership hardening for archive edit directories, working files,
commit snapshots, record replacement and cleanup, including malformed/future-profile recovery and inherited ACLs.
Preserve legitimate atomic editor saves, legacy ZIP/remote sessions and unknown-version working copies.
Then finish member UX edges and qualify Unix confinement/update, moves and cleanup/recovery.
Present the declared 7z precision policy and resolve remaining header compatibility gaps before enabling a profile. Reuse
passed codec/native evidence when its compiler inputs are unchanged; batch the shared UI and native acceptance
work, then complete resource/crash/fuzz/RID/package gates and the final independent review.

The 7z catalog now requires complete graph/codec endings, packed and plaintext checksums/SHA, exact sizes and a final
whole-source revision. A failed empty Deflate BCJ2 branch is fixed, with all password modes covered. Aligned nonzero
ARM64/RISC-V offsets and decoder cleanup are included. The graph's conservative memory reservation is separate from
the persistent metadata limit and does not qualify the native process envelope. See the checkpoint for official
7-Zip's Zstandard gap, the independent fallback coverage, frozen compiler inputs and retained failed phases.

Current user-requested order (7 November): the recursive-selection checkpoint is recorded in `685fe564`;
[independent compressed RAR5 feasibility](RAR5_FEASIBILITY.md), committed in `4672ac14`, supports technical go.
The equivalent [7z source/coverage analysis](SEVENZIP_FEASIBILITY.md) now selects bounded FileCat container code,
existing permitted encoders and reviewed missing public-domain codec/filter paths. It verifies 29 pinned writer/header
candidates and 21 SDK candidates; the analysis itself imports no code and establishes no runtime qualification.
Subsequent bounded implementation checkpoints are described below.

The latest working priority in plan §1.2 places RAR/7z integration next: complete strict catalogs, then connect
both formats to the shared transaction/password/create/update/edit/F6 workflows before optional XZ/ISO work and
the final independent review. The [strict RAR5 logical catalog](RAR5_LOGICAL_CATALOG.md) now implements complete
content/end/filter observations, while preservation admission and the corresponding strict 7z catalog remain open.
Its validation record distinguishes the initial fixture-helper failures, focused correction and decoder-lifetime
follow-up; component evidence does not enable either format in the application.

The subsequent [strict 7z framing/metadata checkpoint](SEVENZIP_PHYSICAL_FRAMING.md) observes exact raw metadata,
coder graphs and physical ownership, including bounded compressed/encrypted headers. It also closes partially
constructed decoder graphs on failure. That checkpoint left logical member/header codec-ending checks and
preservation admission open, with encoded-header ending verification explicitly pending.

The next [LZMA/LZMA2 verification component](SEVENZIP_LZMA_VERIFICATION.md) adds exact marked/markerless finish,
compressed-byte consumption, chunk/history/property checks and owned-buffer cleanup. Its independent official-SDK
comparison covers 224 accept/refuse vectors. It supplies a prerequisite for the complete 7z catalog; subsequent
graph integration is recorded below, and the component does not enable a production writer.

The [complete 7z header verifier](SEVENZIP_HEADER_VERIFICATION.md) now connects that strict LZMA/LZMA2 component to
the encoded-header graph, with exact Copy and AES consumption, bounded padding and aggregate decoder reservations.
Successful framing observations require complete header endings. Logical member graphs and the complete strict
catalog still remain open; no production profile is enabled.

The [PPMd7/BCJ2 strict verification components](SEVENZIP_RANGE_VERIFICATION.md) add exact known-size range endings,
all four BCJ2 branches, shared consumed-byte accounting and owned PPMd model cleanup. The final 102 new cases and native
SDK comparisons cover positive and malformed streams on all three platforms, with LZMA regression repeated after
the shared input change. Complete member graph/catalog integration remains open.

The [raw-Deflate verification component](SEVENZIP_DEFLATE_VERIFICATION.md) now checks complete final blocks,
exact sizes/consumption and real history. It fixes backward copies before produced history, pooled-window cleanup
and the separate vendored encoder's sync/async leave-open finalization. All 75 new cases pass within the 2,849-case
platform matrices; independent native zlib agrees on 265 vectors on every platform. Initial fixture/assertion and
Unix comparison-launch failures are retained separately. Complete graphs/catalog and shared RAR/7z adapters remain
the immediate integration work; no new profile is enabled.

The [RAR5 preservation assembly component](RAR5_PRESERVATION.md) now represents exact optional semantic metadata
and archive/header features, adds per-member packed SHA-256 witnesses to the complete logical catalog, and assembles
retained compressed/encrypted content together with newly encoded inputs. The 115-case component matrix and
independent metadata/native-reader checks exercise creation, rename/reassembly and mixed records. Parent preservation
planning, worker/runtime secret consumers, native publication and application workflows remain open; this component
does not register a new format.

The subsequent [RAR5 parent/stream rewrite checkpoint](RAR5_TRANSFORM_PLANNING.md) now derives trusted expected
manifests from exact occurrence edits and reviewed numeric inputs, verifies the entire source before output,
assembles retained/new records, and checks the whole source again afterward. It covers rename/replace/delete/add
together across all methods/password modes, including wrong-password/remove-all and bounded final-EOF behavior.
The final 49 new cases pass in 2,898-case platform matrices with independent metadata/native-reader checks. Shared
worker/secret consumers, captured-owner/native publication and application/UI integration remain open; no new
profile is registered. This RAR work advances independently of the remaining 7z member graph/catalog.

The [RAR shared worker/password pipeline](RAR5_WORKER_PIPELINE.md) now carries those plans through real parent
clients and distinct transform/assessment/fresh-verification subprocesses. Fixed protocol-2 endpoints consume
nonce/role/request-bound one-use grants with job-local crypto budgets; protocol-1 TAR bytes remain unchanged.
Parent name admission matches the existing 64 KiB UTF-8 RAR policy, and IO allowances cover both encoding passes.
The corrected 60-case component passes within the 2,958-case platform matrices, with independent metadata/native
reader checks. Initial test/admission failures are preserved separately. Native protocol-2 bootstrap/resource/package,
captured-owner/publication and application/UI qualification remain open; no new format is registered.

The [RAR captured/native pipeline](RAR5_NATIVE_PIPELINE.md) now retains capture-owner costs and directory evidence,
borrows job-owned passwords for fresh role-bound grants, and performs actual native capture/sealing/new-file
publication on Mac/Ubuntu/Windows. Windows application protocol-2 workers also perform identity-preserving NTFS
updates and fresh retained-snapshot restoration in all password modes. The 2,994-case broad platform matrices pass;
affected-case follow-ups pass 18 native RAR cases on each Unix system and 24 on Windows, plus ten application-entry
cases. Test-budget/read-sharing corrections and a static compiler-audit coverage correction are documented and frozen
separately. Unix production confinement/update, full UI/password/edit/F6 workflows, all release qualification gates
and complete 7z member graph/catalog/shared integration remain open. No new profile is enabled.

The [RAR/7z public-options checkpoint](RAR_7Z_PUBLIC_OPTIONS.md) advances shared integration: explicit version-2
codec/filter/header/method and password modes now participate in every canonical request, digest, sidecar, creation
witness and journal binding, with version-1 bytes preserved. Public options map to existing stream encoders and
one-use password sessions, and the encrypted output tests use that adapter. Complete container preservation/
transaction/worker/UI workflows and runtime role-bound secret consumption still remain open.

The priority decision is in plan §4.3.2: bounded RAR C# implementation first, then needed 7z codecs/filters/container;
strict catalogs and shared worker/transaction/capture/resource integration; complete workflows/UI; large guarded
three-platform qualification batches plus actual six-RID/package/fuzz gates; final independent UX/UI/function/security
review and fix/retest. Prepare coherent source/dependency/runner packets and collect evidence/cleanup in each batch.
Do not create one machine session per small edit or omit needed coverage because upstream lacks an encoder.

Both encoders require explicit method/version/filter/mode inventories and all needed feasible encoding paths. Gaps
must distinguish missing work, profile policy and demonstrated blockers. RAR methods 0–5 and 7z's seven current
codecs, one-stream filters/swaps, compressed headers and four-stream BCJ2 now have implementation checkpoints. Remaining
version/filter/metadata inventories and broader qualification remain work, not impossible features. 7z FILETIME's 100 ns limit
and over-budget dictionaries/models need explicit policies. Neither analysis enables a production profile.

The user chose option 3 with fallback to option 1: a documented feasibility no-go authorizes stored RAR5 and optional
installed `rar` compression where lawful and qualified. The primary route has not received a no-go; fallback is not
activated. The goal remains active and implementation/release qualification and independent final review remain open.

The subsequent 7 November instruction makes **password support required for both RAR and 7z**, superseding initial
unencrypted-only policy. Plan §4.3.1 requires payload/header encryption, password-aware read/browse/extract/update/edit
and fresh verification, bounded KDF/cipher handling, session-only secret grants and complete UI/negative/native tests.
Public password flows and reader refusal text remain integration gaps even with the internal cipher foundation below. Passwords/keys
must not enter persistent settings, journals/logs/evidence or process arguments.

The [bounded C# RAR5 codec/creation checkpoint](RAR5_CSHARP_ENCODER.md) now implements store and genuinely distinct
bounded search presets for methods 1–5. All 23 new cases pass on Windows/Mac/Ubuntu within 444-case regression
batches, and independent native APIs verify all 84 generated records per platform. The six archive hashes match
across all three systems. It remains stream-only and unregistered: required password/data/header encryption,
strict update/source/verification catalogs, worker/secret grants, preservation, workflows and full gates remain open.
See that record for exact evidence and corrected harness failures; this is not complete RAR feature support.

The [7z container/codecs and password-derivation checkpoint](SEVENZIP_CSHARP_CODECS_AND_KDF.md) now writes Copy,
LZMA/LZMA2/BZip2/Deflate/Zstandard using streamed raw headers and bounded presets. All 21 new 7z and 19 KDF cases
pass within the 522-case batches on all three platforms; independent APIs verify 42 records per platform and archive
hashes match. Mac name-presentation normalization is checked against exact raw UTF-16 names. These are not full 7z
or password features: PPMd/filters/compressed headers, actual cipher/header/read/secret-grant integration and remaining
workflow/qualification gates stay open. Profiles remain unregistered. The later checkpoints also test simultaneous
composed/decomposed names; all owned test processes are closed and other testers are preserved.

The [PPMd7/filter checkpoint](SEVENZIP_PPMD_AND_FILTERS.md), committed in `1466b3c7`, implements bounded PPMd7,
delta, branch and swap encoding with 19 exact SDK comparisons and independent three-platform container checks.
The [streamed-header checkpoint](SEVENZIP_STREAMED_HEADERS.md), committed in `47a9c86c`, adds raw/compressed headers,
including a 1,304,142-byte raw header, exact independent metadata readback and 574-case passing platform matrices.
These are foundations with recorded method/reader gaps, not completed AW6 or public feature availability.

The [password foundation](ARCHIVE_PASSWORD_FOUNDATION.md) now creates and reopens encrypted RAR5 and 7z, including
optional hidden names, all current codecs and raw/compressed 7z headers. Each platform's 653-case matrix has zero
failures; independent readers verify 52 encrypted archives/194 payloads through official 7-Zip and 29 encrypted
7z archives/196 metadata records/140 payloads through py7zr, including Zstandard. Every transferred evidence file
is manifest-verified. Current internal sessions bound and dispose password/KDF/cipher state; versioned worker secret
grants, view/edit lifetime, raw catalogs, shared transactions, complete UI, BCJ2, legacy cipher inventory and the full
resource/security/crash/fuzz/six-RID/package gates remain required. No new profile is registered. The next shared
integration chunk includes password-aware catalog/verification contracts rather than freezing interfaces around TAR.

The [worker secret-grant foundation](WORKER_SECRET_GRANTS.md) adds explicit protocol 2, one-use role/request-bound
password frames, owned-buffer clearing and explicit JSON nonserialization. The full affected matrix passes 770 cases
with platform skips and zero failures on each machine. Nine real child delivery/refusal/cancellation journeys pass
for all three roles. A focused replay qualifies a create-only recorder fix after Windows PID reuse overwrote one
exit record; all nine separate records now survive per machine. Both source/evidence sets and all current C# inputs
are verified. This is shared transport/API work: actual password-aware request consumers, native secret-role admission,
parent job/view/edit lifetime and complete UI remain open.

The [BCJ2 checkpoint](SEVENZIP_BCJ2.md) adds fixed-buffer raw encoding and four sequential packing passes with one
active compressor, frozen-source checks and four distinct encryption IVs. The full matrix passes 846 cases with
platform skips; the follow-up 81-case suite covers constructor/disposal failures as well as all codec/header/password
combinations. SDK checks match 72 streams; official 7-Zip verifies 36 archives/72 payloads, and composed independent
py7zr/AES/codec plus SDK BCJ2 checks verify all 42 graph payloads including Zstandard. Libarchive qualifies zero of
these graphs; its failures are retained. Direct tests fix Deflate small-read data loss and encrypted BCJ2 stream
cleanup. All 30 earlier unencrypted archives remain unchanged; SBOM controls and actual compiler-source audits pass.
Source/evidence identities and the exact standard-user setup corrections are recorded in that checkpoint.

The next chunk is strict password-aware RAR/7z source/update/verification catalogs and typed shared worker/profile
integration, including four-pass BCJ2 traffic/CPU/decode accounting and aggregate memory ownership. Continue through
shared transactions, complete UI/workflows, native/resource/crash/fuzz/six-RID/package gates and final independent
review. No new profile is registered or complete; the explicit goal remains active.

The [RAR5 physical framing checkpoint](RAR5_PHYSICAL_FRAMING.md) now observes complete admitted framing with exact
metadata presence, occurrences, timestamps, raw native owner bytes, encryption descriptors and physical revision
hashes. All 62 new cases pass within the same 913-case matrix on Windows/Mac/Ubuntu, including truncated prefixes,
header reservation/size limits, cumulative metadata/member limits and mid-payload cancellation. Current C# inputs,
all three frozen bundles and the writing-only dependency closure are verified. This physical catalog still needs
strict decompression/end/filter/checksum/plaintext-SHA verification before it can produce a complete assessment;
preservation-compatible writing and the remaining shared/native/workflow gates stay open.
The same record includes a 334-case boundary follow-up: RAR creation now respects the reader's 64 KiB UTF-8 name
limit before IO and chunks large headers to the proxy ceiling. All six new cases pass on every machine, official
7-Zip verifies the three accepted boundary archives/payloads, and the pinned old assembly reproduces five expected
failures. Existing six archive hashes remain unchanged; all six full/follow-up bundles are locally manifest-verified.

## Source and workspace

- Baseline: `4282929753f062f5bb2cc130e8316b133e5c8e03`.
- Branch: `codex/archive-writing`.
- Isolated worktree: `E:\FileCat\.tmp\worktrees\archive-writing`. The shared `E:\FileCat` checkout remains on `main`.
- The baseline plan was an untracked user-provided file; this branch retains it with the user's authorized additions for local move-into and feasible licence-compatible RAR writing before final review.
- Implementation checkpoints: `a8313365` (contracts/proxy IO) and `7e63b39e` (strict TAR/gzip transforms).
- The transferred TAR/gzip foundation delta (before the later native-worker changes) has SHA-256
  `70E85DE4D5DA731DF0C38DD814E1253B87F2D822CE1172CFA02D65DC2BFE2CDA`.
  It contains those foundation production and test sources, applied over the baseline source archive.

## Implemented foundations

The [bounded recursive-selection checkpoint](RECURSIVE_SELECTION_CAPTURE.md) freezes exact roots, uses native
directory enumeration and lazy ancestor-bound file leases, preserves empty folders and records explicit exclusions.
Native destination-ancestor checks refuse staging inside excluded source subtrees. Mac/Ubuntu suites, 220 native
Windows regressions, ten final recursive Windows cases and eight actual Mac-hosted ARM64 VM helpers pass; independent
readers verify all four TAR-family outputs. Global preparation/snapshot/resource ownership, full review UI and all
remaining format/RID/package/fuzz gates remain open; unsupported-name samples need finer leaf detail before UI use.

The [shared Linux native-open follow-up](UNIX_ARM64_OPEN_FLAGS.md) fixes remaining x64-only flags in Unix archive
storage, mount-info reads and POSIX ACL directory opens. Actual ARM64 guest directory/file/link probes pass as part
of 29 exact kernel checks; all 16 archive roles and lifecycle suites pass. The affected 211-case suite passes with
explicit platform skips on Windows, Mac and Ubuntu. Host/provider memory and the remaining production gates stay open.

The [dedicated VM archive-worker checkpoint](DEDICATED_VM_ARCHIVE_WORKER.md) adds a non-UI worker and writing-only
dependency graph, strict shared guest bootstrap/transport and canonical locked musl publication. Live ARM64 guests
exposed and fixed architecture-specific open flags and musl clone/signal bugs. Both tested bundles complete 16 actual
roles with independent extraction; 23 kernel-policy checks and extra-process startup refusals pass. Focused policy/TAR
regressions pass 51 tests on each desktop platform. Compiler hashes verify 810 source documents per bundle. Host/provider
memory, production packaging/admission and remaining plan gates stay open; this enables no writer profile.

The later [captured-member defaults checkpoint](CAPTURED_MEMBER_DEFAULTS.md) corrects ordinary Unix permission bits
and Windows 0644 defaults, with passing native regression checks on all three platforms. It does not complete capture.
The subsequent [exact TAR timestamp checkpoint](EXACT_TAR_TIMESTAMPS.md) adds native nanosecond capture, versioned
input/member metadata and a bounded PAX encoder adapter, with three-platform regression/native and independent-reader
checks. Complete capture/publication/production integration and release qualification remain unfinished.
The subsequent [preparation-space checkpoint](PREPARATION_SPACE_ADMISSION.md) checks the complete stage/evidence,
known snapshots and safety margin against capacity from the retained native destination, before intent or artifact
creation. Unknown capacity remains explicit; full capture/space UI, allocation guarantees and qualification remain open.
The later [Unix visible-attribute checkpoint](UNIX_EXTENDED_ATTRIBUTE_INVENTORY.md) adds bounded descriptor-based
name/value inventories to native rereview and origin/rename reconciliation. Hidden policy coverage, complete outer
admission/application, publication and production integration remain unfinished.
The later [private new-file publication checkpoint](PRIVATE_NEW_FILE_PUBLICATION.md) adds schema-5 creation evidence,
native exclusive claim and conservative reconciliation for creator-private, unmarked new archives. It retains all
artifacts and leaves profiles disabled; complete metadata admission, marked creation, replacement, cleanup/recovery,
the full crash matrix and production qualification remain unfinished.
The later [creation-provenance integration](MARKED_CREATION_PROVENANCE.md) binds sealed origin records to the exact
captured sources, applies a canonical Windows aggregate before fresh verification and supports private marked Windows
creation through the unregistered publication bridge. Unix marked creation, full metadata preservation, replacement,
cleanup/recovery and production qualification remain unfinished; this does not enable a writer profile.
The [Windows resource-policy follow-up](WINDOWS_RESOURCE_POLICY_OBSERVATIONS.md) captures resource attributes and CAP
identifiers with the retained security snapshot, detects value changes without payload/time/DACL changes, and refuses
a late changed stage before publication. Audit/other unqueried security state, metadata application and existing-file
replacement remain unfinished.
The [NTFS transactional-content investigation](WINDOWS_TRANSACTIONAL_CONTENT_SPIKE.md) passes 12 owned native probes,
including a read-only pin across commit and independent preservation checks for seeded audit/resource/CAP descriptors.
It changed no production route or source-lease contract. At that investigation checkpoint, the typed recipe, old-data
snapshot, journal/recovery, hard-link, durability and application integration were still required for qualification.
The subsequent [journaled NTFS integration](WINDOWS_TRANSACTIONAL_CONTENT_INTEGRATION.md) adds the separate typed
content-update recipe, verified private original-byte retention with provenance, exact preparation/input binding and
the transaction-to-normal-reader pin transfer. All 88 Windows confined-worker/native cases pass, including 20 new
actual update cases across the four TAR profiles; common recovery/UI regressions pass on all three platforms. One
native-discovered identity/digest contract mismatch is fixed and the failed evidence retained. Complete crash/recovery/
cleanup, broader metadata/resources/aliases and production admission remain unfinished; the bridge stays unregistered.
The [NTFS process-crash follow-up](NTFS_CONTENT_PROCESS_CRASHES.md) moves that coordinator behind typed native
contracts and validates sixteen actual externally killed snapshot/prepare/commit/rollback boundaries. All 104 Windows
native cases pass; common/UI and prior creation/crash regressions pass on all three platforms. Recovery retains every
surviving artifact, including a committed file whose outcome append was interrupted. Active restoration/cleanup,
deletion crash points, broader filesystem/resource/alias qualification and the remaining plan are still unfinished.
The [live NTFS cleanup follow-up](LIVE_NTFS_ARTIFACT_CLEANUP.md) persists a complete sealed-artifact catalog before target
mutation and implements creator-owned, object-bound unlink with durable per-object intents/outcomes and the original
snapshot last. All 131 Windows native cases pass, including twenty deletion crashes; common/UI and prior regressions
pass on all three platforms. Completed cleanup clears only its own recovery hold. Interrupted-cleanup resumption,
native recovery/restoration, broader qualification and production workflows remain unfinished.
The [NTFS cleanup-recovery follow-up](NTFS_CLEANUP_RECOVERY.md) adds an exclusive retained journal reopener, fresh
native target/catalog inspection and a separate bounded reconciliation history distinguishing confirmed unlinks from
observed absence. Eight actual crash-to-recovery paths and five changed/stale/busy/incomplete refusals pass within all
144 Windows native cases; common/UI regressions pass on all three platforms. Recovery preserves archive bytes and can
finish exact private cleanup.
The [recovery-process crash follow-up](NTFS_RECOVERY_PROCESS_CRASHES.md) adds shared reconciliation sequencing and
31 actual recovery-parent kill cases, covering all six intent/unlink/outcome boundaries and persisted failure holds.
All 175 Windows native cases and the three-platform common/UI regressions pass. Restoration, production actions and
broader gates remain unfinished; profiles stay disabled.
The [ordinary Windows standard-user follow-up](WINDOWS_STANDARD_USER_QUALIFICATION.md) passes 170 native cases with
five expected administrator-only skips, with a loaded real user profile and raw token-group proof of absent Admin SID.
It also fixes the validation transfer procedure: complete app/probe packages and all 469 actual hashes are checked,
including all 261 app files against SPDX. Earlier host inventories do not establish exact incremental runtime transfer.
This closes that explicit identity/artifact validation gap; the broader implementation and qualification gates remain open.
The [fresh snapshot-restoration follow-up](SNAPSHOT_RESTORATION.md) uses new confined assessment/verification, a fresh
job and native transaction, and a separate current-version rollback copy before restoring exact retained original
bytes. It adds durable linked restoration facts, restored-target cleanup reconciliation and read-only reporting.
All 187 administrator native cases and 182 standard-user cases pass (five administrator-only skips); common/UI
regressions pass on Windows, Mac and Ubuntu. Restoration process-crash/failure qualification, production actions
and the broader gates remain unfinished; writer profiles stay disabled.
The [restoration-process crash follow-up](SNAPSHOT_RESTORATION_PROCESS_CRASHES.md) passes twelve actual external
parent-termination boundaries and a fresh retry after uncertain intent. It reproduces and fixes a retry refused
because a fresh write has a new modification-time metadata digest, while enforcing distinct job/journal/transaction
identities and immutable same-attempt outcomes. All 200 administrator native cases and 195 standard-user cases pass
(five administrator-only skips), with passing common/UI/independent-fixture checks on Windows, Mac and Ubuntu.
Cancellation/failure matrices, restoration cleanup-deletion crashes, production actions and wider gates remain open.
The [restoration cancellation/failure follow-up](SNAPSHOT_RESTORATION_FAILURES.md) passes nine additional actual
native cases before/after commit and during backup handling, with separate exact catalog reconciliation and retained
uncataloged evidence. All 209 administrator native cases and 204 standard-user cases pass (five administrator-only
skips), with passing Mac/Ubuntu builds and Windows-only guards. No production fix is needed for these cases. Broader
fault/resource coverage, restoration cleanup-deletion crashes and the remaining production/release gates stay open.
The [restoration cleanup-crash follow-up](SNAPSHOT_RESTORATION_CLEANUP_CRASHES.md) passes all eighteen per-artifact
intent/unlink/outcome kill points. Fresh scopes distinguish observed absence from confirmed deletion, reconcile each
catalog separately and preserve the old source snapshot through new-job cleanup. All 227 administrator native cases
and 222 standard-user cases pass (five administrator-only skips), plus passing Mac/Ubuntu builds and Windows guards.
No production fix is needed. Wider fault/resources, metadata/security/aliases/durability, production recovery actions
and remaining format/platform/workflow gates are unfinished.
The [retained archive cleanup action](RETAINED_ARCHIVE_CLEANUP_UI.md) connects fresh native NTFS review to an explicit
keep-archive/cleanup approval dialog, exact reconciliation and retained closure reports. It fixes a clipped action
found on the actual Windows guest; 273 common UI cases pass on each platform, two actual Windows native UI cases pass,
and all 227 administrator/222 standard-user native cases pass (five administrator-only skips). The guest desktop
confirms safe Enter cancellation, exact six-file cleanup and unchanged archive/origin bytes. Further drawer guidance,
report accessibility/clipping, restoration UI and the broader implementation/qualification gates remain open.
The [drawer follow-up](ARCHIVE_CLEANUP_DRAWER_FOLLOWUP.md) resolves those reproduced guidance, report-row accessibility,
completed-button clipping and closure-heading defects. All three platforms pass 276 full UI cases; Windows also passes
two actual native UI cases, 33 standard-user focused cases and the repeated 227/222 native matrices (five privileged
skips). The final guest desktop confirms Cancel preservation, exact approved cleanup, the concise accessible row and
visible report action/consistent viewer, then exits normally. Restoration UI and the wider gates remain unfinished.
The [private creation process-crash follow-up](PRIVATE_CREATION_PROCESS_CRASHES.md) passes ten new publication/metadata
boundaries and thirteen neighboring crash cases on each platform. It leaves production and recovery authority unchanged.
The [TAR.GZ worker pipeline](TAR_GZIP_WORKER_PIPELINE.md) adds strict wrapper-aware assessment, streamed create/update
transformation and fresh verification, with fixed-pass replay and native preparation/publication cases. Application
registration, update publication and the remaining release gates stay unfinished.
That checkpoint also retains two intermittent Ubuntu controlled-worker monitor refusals as an open reliability
investigation, despite the subsequent passing broad/native replays. Their cause has not been established.
The subsequent [catalog/lifecycle follow-up](TAR_GZIP_CATALOG_REUSE.md) removes a duplicate retained gzip-update
catalog and tightens compressed replay to four physical passes. It preserves monitor/terminal/conversation causes,
and reproduces and fixes an exited-process CPU observation bug in the controlled test child. The two older refusals
did not capture their precise cause; complete production lifecycle/release qualification remains unfinished.
The [TAR.BZ2 worker checkpoint](TAR_BZIP2_WORKER_PIPELINE.md) adds strict single-stream assessment, streamed rebuild,
fresh verification, preserved block size, bounded buffered proxy IO, independent bidirectional fixtures and native
preparation/private-creation checks. Format enablement and complete common/platform release gates remain unfinished.
The [TAR.ZST worker checkpoint](TAR_ZSTD_WORKER_PIPELINE.md) adds pre-codec frame/window/block admission, bounded
streamed rebuild, checksummed output, separate fresh verification, pinned independent C interoperability and native
huge-window refusal. A resource-scoped test collection fixes accidental oversubscription of the unchanged bounded
shared queue. Full workflows, common transactions, worker/platform and release qualification remain unfinished.
The [cooperative cancellation checkpoint](WORKER_COOPERATIVE_CANCELLATION.md) adds a separate EOF-only lifetime pipe,
bounded interruptible worker protocol IO and a two-second grace before native termination, with queue ownership through
actual reap. It fixes a reproduced Unix pipe-disposal deadlock. Fresh Windows/Mac/Ubuntu regressions, independent
archive readback, actual Windows LPAC/application and Linux containment cases pass. Complete workflow, cancellation/
publication races and platform/release qualification remain unfinished.

| Area | Current behavior | Remaining gate |
|---|---|---|
| Typed contracts | Explicit versioned profiles/enum IDs, raw occurrence keys, portable names, cumulative member/metadata/heap/logical budgets and TAR request/manifest/receipt wire | Other-format schemas, production service registry and session contracts |
| Framed protocol | Nonce, version, sequence, reserved-field/opcode, frame-size, cumulative payload/metadata and finite frame-count checks before payload allocation; bounded fragmented records; poisoned channels cannot resume; distinct TAR assessment/transform/verification roles; separate EOF-only cancellation pipe and bounded interruptible worker IO | Other formats, full cancellation/publication races and final platform/package qualification |
| Canonical manifests | Frozen ordered physical members, contiguous exact duplicate occurrences, profile/features and separate full/content/metadata digests; aggregate name/heap/metadata/logical budgets; shared incremental hashing | Complete format feature inventories, rebuild/verification wire schemas and native sidecar integration |
| Manifest sidecar codec | Scoped versioned stream encoding, incremental bounded review, complete EOF/semantic/file checksums, poisoned failure state; no pathname authority | Native private-file leases/permissions/durability, transaction integration and exact-object recovery/cleanup |
| Windows archive storage | Unregistered held NTFS directory/file leases, exclusive private ACL-at-create stages/sidecars, bounded sealed streams, complete object versions, no-replace handle rename and exact reviewed deletion; [native evidence](WINDOWS_ARCHIVE_STORAGE.md) | Typed provenance/outer metadata, durable journal/publication/recovery integration, directory-entry durability, global lease accounting, other filesystems/platforms and release qualification |
| Typed origin evidence | Absent/present/unavailable raw snapshots; held Windows ADS and Mac/Linux xattr reads; private sealed-stage create-only copying with post-rename capability closure; [Windows checkpoint](ARCHIVE_ORIGIN_CHECKPOINT.md), [Unix observations](UNIX_ORIGIN_OBSERVATIONS.md), [Unix application](UNIX_ORIGIN_APPLICATION.md), [bound Windows creation application/publication](MARKED_CREATION_PROVENANCE.md) | Unix semantic classification/strongest-input aggregation, full promised metadata application, directory capture, global budgets, update reconciliation and release qualification |
| Windows outer observations/guards | Frozen held-file owner/group/DACL/label/resource-attribute/CAP observations, exact creation/mtime, attributes/link count/origin; mandatory same-object metadata checks before/after full-hash namespace review; [initial evidence](WINDOWS_ARCHIVE_METADATA.md), [resource-policy evidence](WINDOWS_RESOURCE_POLICY_OBSERVATIONS.md) | Audit/other unqueried security-policy admission, promised metadata application/prepared-publication state, aggregate budgets, durable publication/recovery and release qualification |
| Unix outer observations | Held regular-file owner/group/mode, nanosecond mtime/ctime/optional birth, link count, native flags, bounded raw ACL and typed origin; [checkpoint](UNIX_OUTER_METADATA.md) | Complete security/xattr admission, promised metadata application, global budgets, publication/reconciliation and release qualification |
| Unix archive storage | Held directory/regular-file leases, private-at-create ext4/APFS artifacts, bounded sealed streams, native sidecar roundtrips, reviewed no-replace rename and checked sync calls; exact unlink deliberately refused; [checkpoint](UNIX_ARCHIVE_STORAGE.md) | Full metadata/origin application, durable journal/reconciliation, reviewed replacement/rollback, exact cleanup or retained-artifact workflow, global budgets and release qualification |
| Declared Windows origin zone | Closed bounded UTF-8/UTF-16LE declaration parsing, exact-byte non-decreasing updates, URL-free zone-only output; independent Windows reader corrected encoding admission; [declaration evidence](WINDOWS_DECLARED_ORIGIN_ZONE.md), [complete captured-input binding and native creation integration](MARKED_CREATION_PROVENANCE.md) | Existing-file native application/reconciliation, Unix representations, directory capture and release qualification; a declaration is not an effective trust/policy decision |
| Streamed provenance aggregation | Scoped bounded source-version/raw-mark sidecar, complete EOF/checksum review, all-input Windows declaration aggregation with retained raw URLs, native private-file reopen/seal/copy test; [evidence](ORIGIN_EVIDENCE_AGGREGATION.md) | Durable transaction references/retention, reviewed source-plan binding, global budgets, native Unix representations, final metadata application and actual publication/recovery qualification |
| Proxy IO | Child uses fixed input IDs and bounded stage requests; independent views preserve retained native owners; parent checks offsets/lengths/IO amplification before streams; no worker namespace authority | Scalable native capture/snapshot repositories, global budgets and production launch integration |
| Queue/lifetime | One active in-process reservation and at most eight pending; all three roles share exit, diagnostics/CPU, deadline, cancellation/termination and reaping checks | Cooperative cancellation, cross-parent scheduling, global budgets and operation-center phases |
| Legacy ZIP publication extraction | Separate compatibility-only publication/rollback/cleanup boundary; existing codec and journal ordering retained; explicit profiles refused before intent | Object-bound publication, typed provenance, durable archive facts/recovery and separately reviewed ZIP worker migration |
| Archive journal facts | Immutable bounded versioned snapshots, durable transitions, schema-2 provenance reservations/finalized versions/counts, conservative legacy/damaged/unknown/out-of-order retention and read-only reporting; [provenance journal checkpoint](ARCHIVE_ORIGIN_JOURNAL.md) | Native sidecar storage/integration, object-bound recovery/actions and actual publication/crash integration |
| Archive recovery UI guard | Fresh journal reads at every entry point; generic archive mutations refused; read-only reports plus explicit native NTFS retained-file review/approval and exact cleanup for complete supported catalogs; reproduced drawer/report findings fixed | Restoration action, Unix/native adapters, complete archive workflow/accessibility and package qualification |
| Windows boundary spike | Suspended restricted LPAC, actual-token/access checks, exact inherited pipe list, private-profile write protection, finite job commit/CPU, bounded stderr and idempotent kill/reaping; actual guarded application entry tested | Full engine workloads, other namespace/asset/setup-race probes, ordinary user and installed-package/RID qualification |
| Linux boundary development lane | Unregistered stream-only launcher/worker, native delegated cgroup memory/pids, finite CPU, pidfd/parent-death lifetime, all-thread seccomp and Landlock, dedicated cancellation pipe, bounded stderr and exact empty-controller cleanup; [native checkpoint](LINUX_ARCHIVE_WORKER_CHECKPOINT.md) | Production entry/roles, complete package/update/descriptor/resource/cancellation races, ordinary desktop routes, ARM64/other filesystems/kernels and release qualification |
| TAR assessment process | Actual early entry, numeric read-only source proxy, bounded physical SHA/raw TAR/runtime-decoder passes, checked catalog phases and terminal digest; shared parent watchdog, cooperative cancellation and final drain/reaping checks | Qualified AppServices registration, reviewed input leases, full cancellation/publication integration |
| Fresh TAR verification process | Separate role/nonce/process derives the complete manifest without the parent's expectation; complete physical reads, cooperative cancellation, terminal/drain/reaping checks and exact manifest comparison, now bridged to sealed native stages | Other formats, full native/package qualification, scalable capture, metadata/journal/publication integration and full cancellation races |
| Parent TAR expectation | Frozen positive numeric inputs, exact occurrence edits and flattened additions; complete source admission, combined source/input budgets, independent expected order/content/metadata/features before encoding; [evidence](TAR_PARENT_EXPECTED_MANIFEST.md) | Reviewed native source/folder capture and lease binding, complete output-bound audit, production integration and complete release qualification |
| TAR transform process | Typed bounded request/receipt, per-reference input aliases, finite parent stage/IO ceiling, isolated engine execution, cooperative cancellation, terminal/reaping and parent manifest comparison; [evidence](TAR_TRANSFORM_WORKER.md) | Complete profile-bound audit, scalable capture, metadata/journal/cancellation integration, production contracts/registration and release qualification |
| Retained native TAR stage | Native object/content/metadata bindings, disposable independent read views, exact-bound fresh artifact claim, actual sealing, fresh verification and final stage/all-input rereview; [evidence](RETAINED_NATIVE_STAGE_PIPELINE.md) | Full metadata/origin admission/application, scalable capture/global budgets, durable journal/publication/recovery and production/qualification integration |
| Durable stage creation | Exact single-attempt native reservation; versioned flushed intent before creation and immutable identity/bound binding after creation; conservative recovery/reporting and real external process-termination checkpoints; [evidence](STAGE_CREATION_RESERVATION.md) | Remaining publication/recovery integration and full crash/durability/production qualification |
| Journaled native TAR preparation | Frozen schema-4 sidecar reservations, private native assessed/expected/provenance/observed manifests, complete readback, source/destination binding and durable Rebuilding/Finalizing/Verifying with distinct finalized/verified stage versions; [checkpoint](JOURNALED_TAR_PREPARATION.md) | Full capture/defaults/global budgets, metadata/origin application, publication/recovery, production workflows and full qualification |
| TAR assessment | Full raw USTAR/PAX framing, checksums, exact UTF-8, numeric/PAX grammar and allowlist, pre-allocation metadata limits, complete payload hashes, end markers and padding; independent runtime-decoder agreement | Coverage-guided campaigns, complete metadata/preservation corpus, scale measurements and native independent tools |
| TAR transform | Stream-only create and exact per-occurrence replace/delete/rename; whole-source namespace/logical budgets; order/duplicate/metadata preservation; native stage, bounded worker and fresh verifier roundtrips | Durable safe save/metadata/provenance, scalable folder preparation/review and production dispatch |
| Gzip/TAR.GZ | Neutral single-member framing, explicit DEFLATE terminal status/consumption, CRC/ISIZE, bounded expansion; streamed create/update and fresh verification, fixed-pass replay and independent fixtures | Production registration, complete bound/scale/fuzz/wrapper corpus and inherited publication/release gates |
| BZip2/TAR.BZ2 | Complete single-stream CRC/footer/padding/EOF admission, bounded expansion, streamed create/update/fresh verification and preserved block size; native preparation/private-creation and independent fixtures | Production registration, complete bound/scale/fuzz corpus and inherited metadata/publication/release gates |
| Zstandard/TAR.ZST | Pre-codec single-frame/window/block admission, complete decoder/checksum/EOF validation, bounded streaming and checked encoder options; create/update/fresh verification, native private creation and independent C fixtures | Production registration, complete bound/scale/fuzz corpus and inherited metadata/publication/release gates |

The raw gzip inflater is the already-vendored SharpCompress/DotNetZip component, whose existing MS-PL/BSD attribution
is retained. It is used because the runtime DeflateStream API does not expose a terminal-state/consumed-byte result.
The encoder remains the runtime DeflateStream. No upstream source/package was added or upgraded. The BZip2 follow-up
updates the fork's reachable-code SBOM description and passes the Windows development-payload notice/SBOM validators.
Complete reachability review and exact release-artifact SBOM/notices for every enabled RID remain required.

## Observed checks, 4–5 November 2026

The test command is `dotnet test tests/FileCat.Core.Tests/FileCat.Core.Tests.csproj -c Release --no-restore` with filter
`FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget` and a TRX logger.
SDK: `10.0.101`. Raw logs/TRX files are retained under the worktree's `.tmp/test-results/archive-writing` and the owned guest
validation directories; they are not evidence of installed-package or GUI qualification.

| Environment | Baseline result | Current-source result | Scope/limitations |
|---|---|---|---|
| Windows x64 host | 506 passed, 28 skipped, 0 failed | 592 passed, 28 skipped, 0 failed (620 total) | Includes real-child TAR assessment and protocol/lifecycle adversaries; no GUI/package qualification |
| macOS ARM64, macOS 27.0.1 / APFS | 515 passed, 19 skipped, 0 failed | 601 passed, 19 skipped, 0 failed (620 total) | Owned source checkout; SDK 10.0.101; controlled child tests do not qualify a signed confined worker |
| Windows 11 x64 VM, build 26340 | Not replayed natively | 592 passed, 28 skipped, 0 failed (620 total) | Host-built binaries; actual .NET 10.0.14 and medium integrity verified; no GUI/package qualification |
| Ubuntu VM | See later replay below | See later replay below | Initially postponed for another tester; subsequently available with explicit user authorization |

The new tests include 1,000 checksum-repaired TAR mutations and 1,000 gzip mutations. These are deterministic regression
tests, **not** coverage-guided fuzz campaigns or satisfaction of the plan's CPU-hour release floors. The foundation subset
passed 63/63. The manifest/wire/assessment/lifecycle work adds 23 cases, included in the 620-case native replays above.

Two defects found during this work have regressions: the runtime TAR writer emits no end markers if no entry is written,
and the runtime raw DEFLATE stream emits no payload for an entirely empty input. The adapters explicitly emit valid empty
containers. A seekable captured payload view also avoids TarWriter's whole-payload buffering when its output is compressed.

The later narrow ZIP publication extraction passed 52 tests with eight existing Unix-only skips on both Windows host
and Windows VM, and all 60 on Mac. It adds two explicit-profile guard cases. The later Ubuntu replay includes these cases. See
[the extraction checkpoint](ZIP_PUBLICATION_EXTRACTION.md). This is a compatibility refactor, not qualification of
the legacy pathname operations for new formats; no new profile is enabled.

The later versioned archive-journal checkpoint adds 37 cases and broadens the affected suite to include journal, job
and undo behavior: Windows host and medium-integrity Windows VM each pass 696 with 30 skips (726 total); Mac passes
705 with 21 skips (726 total). The existing headless recovery UI suite passes 244 with two skips on the host. See
[the journal contract checkpoint](ARCHIVE_TRANSACTION_JOURNAL.md). These results do not implement sidecar IO, native
publication/recovery, or the required real archive process-crash matrix. The subsequent
[read-only recovery UI checkpoint](ARCHIVE_RECOVERY_UI.md) guards the direct staged-intent loop and exposes recorded
archive facts without opening the artifacts. Native archive recovery actions still need implementation before production
registration.

The UI guard adds nine headless cases. Its final affected UI replay passes 253 with two skips on both host and Windows
VM, and 249 with six skips on Mac (255 total in each environment). Stale rows cannot enter generic archive cleanup,
transfer retry or unresolved tracking closure, and reports do not open archive artifact content.

After the user's explicit authorization to stop the existing Ubuntu picture-test instances, the broader native Ubuntu
replay passed: original Core baseline 596 passed / five skipped (601 total), current Core 721 passed / five skipped
(726 total); baseline headless recovery UI 240 passed / six skipped (246 total), current UI 249 passed / six skipped
(255 total). No failures. These are separate owned source builds on Ubuntu/ext4, SDK 10.0.101 with serviced CoreCLR
10.0.14. See [the Ubuntu checkpoint](UBUNTU_REGRESSION_CHECKPOINT.md) for source/artifact hashes, setup details and limits.
All test and private SDK build-node processes were closed; no new profile or native publication capability was enabled.

The streamed sidecar checkpoint adds 61 cases and enforces the plan's explicit aggregate member-name ceiling. The
affected Core replay now passes 757 with 30 skips on both Windows host and VM, 766 with 21 skips on Mac, and 782 with
five skips on Ubuntu (787 total in each). Canonical digests remain schema-compatible. See
[the sidecar checkpoint](MANIFEST_SIDECAR_CHECKPOINT.md). The stream codec does not create private files or implement
native publication/recovery; those remain necessary before enabling a profile.

The Windows native worker lane now separately passes **25/25**: twelve archive authority/resource/lifetime/assessment
cases and thirteen existing restricted-worker regressions. The tracked runner was exercised successfully on the guest. A healthy
64 MiB framed stream-proxy copy/readback, native 64 MiB allocation, denial of an additional 1 GiB commit, finite CPU,
bounded stderr, unrelated inherited-handle denial, repeat disposal and real parent death are included. This is a
development boundary/assessment checkpoint, not complete write-pipeline or installed-package qualification. The actual
self-contained application assessed a TAR inside LPAC; forged startup facts refused before source IO. Seven early-entry
cases pass on Windows and Mac. See the [assessment report](ASSESSMENT_VALIDATION.md) and
[native spike report](NATIVE_WORKER_SPIKES.md) for artifact hashes, failed iterations, limitations and remaining gates.

The Mac ad-hoc signed App Sandbox probe admitted its own executable read and refused unrelated file read/write,
network access and `fork`. Its attempted absolute 1 GiB `RLIMIT_AS` returned EINVAL and a 2 GiB mapping still succeeded.
The hard whole-process memory gate remains **unqualified**; no Mac writer is enabled from these partial denials.
The [Darwin footprint-limit follow-up](MACOS_FOOTPRINT_LIMIT_PROBE.md) tests the SDK-declared Mach setter as ordinary
UID 501. Its 64 MiB request returns `KERN_NO_ACCESS`; a control touches 96 MiB with a 102,105,568-byte physical footprint.
Both children exit/reap. That direct per-user call does not provide a boundary in this environment. Another enforceable
Mac worker-memory route and full qualification remain required; no profile is enabled.

The [supported Linux VM candidate](MACOS_VIRTUAL_MACHINE_BOUNDARY_SPIKE.md) starts and stops under ordinary Mac UID 501
with the public virtualization entitlement. Its owned Linux allocation child runs as UID 1000 and cannot raise its
cgroup limit: a 32 MiB control completes, while a larger allocation is OOM-killed at an exact 1 GiB peak. All owned
children are reaped. This is mechanism evidence, not archive-worker or production qualification. Actual worker/broker
integration, the complete guest/host memory envelope, all remaining confinement/resource/lifetime gates and guest-system
source/build/license closure remain open. Fixed guest RAM does not establish a whole-host/helper RSS bound.

The [actual managed guest workflow](MACOS_MANAGED_VM_ARCHIVE_WORKERS.md) now passes create, assess, replace and fresh
verify for TAR/gzip/BZip2/Zstd via the existing production clients/roles and a second raw serial stream. Its final
ordinary-user Mac runner records 16 distinct zero-exit/reaped helpers, natural shutdown, exact independent BSD tar/
native-Zstd payload checks and byte-identical original allocation probes. Terminal readiness, interactive Console
byte conversion and post-exit diagnostic failures were fixed during the spike. Production registration, all remaining
resource/confinement/lifetime/package gates and guest-source/license closure remain open. Small cgroup peaks exclude
some prepopulated initramfs/runtime file pages and must not be reported as full process/VM/host memory qualification.

The native development host's [parent-death follow-up](MACOS_VM_PARENT_DEATH.md) adds an actual-parent check, installed
kernel exit watch, monotonic deadlines and bounded explicit VM stop. The 16 healthy archive roles still pass. Twenty
owned-parent SIGKILL/SIGTERM cases pass with protocol pipes deliberately kept open, including blocked/spinning guests;
eight invalid-parent cases refuse before VM setup. Active guests reach state zero through successful stop completion,
all direct parents are reaped and all helpers disappear after adopter reaping. This qualifies that private experiment's
observed lifetime mechanism, not the production backend or complete package/resource/cancellation gates.

The [dedicated cancellation/helper-death follow-up](MACOS_VM_CANCELLATION.md) adds a verified inherited FIFO and third
raw serial channel, reusing Core's bounded transport with the guest token. Four live cancellation cases pass: actual
assessment/session cancellation and blocked input exit cooperatively in 29–59 ms, while full output and a spinning
guest complete explicit VM stops in about 1.52 seconds. Ten bad descriptors refuse before setup. Twelve direct helper
SIGTERM/SIGKILL cases also observe the newly created dedicated virtualization provider exiting/disappearing with all
protocol/control pipes held open, including full unread output. The 16 healthy roles and 20 parent-death/8 invalid-parent
cases regress successfully. Startup/release cancellation, the complete resource/confinement/package and guest-license
gates remain open; no production backend is registered.

The [fixed guest RAM/resource follow-up](MACOS_VM_MEMORY_ENVELOPE.md) changes the managed experiment from 1536 MiB to
exactly 1 GiB and refuses all other RAM settings. All healthy/lifetime cases regress successfully. A 256 MiB unmanaged
entropy allocation completes; a 1280 MiB attempt is killed after 800 MiB touched. Draining/blocked entropy output runs
for ten seconds each with per-process accounting and complete cleanup. The provider reaches 1,233,207,296-byte RSS /
1,086,506,424-byte footprint, exceeding 1 GiB despite the fixed guest RAM. This explicitly leaves the whole host/helper/
provider memory envelope unqualified. No limit is waived and no production profile is enabled from these measurements.

## Outstanding work

The later [Linux worker checkpoint](LINUX_ARCHIVE_WORKER_CHECKPOINT.md) adds 41 cases. Its final native Ubuntu replay
passes 40 with one platform-guard skip; Windows VM and Mac each pass 26 with 15 Linux-native skips. The existing native
Windows containment suite separately passes 25/25 with the updated shared Core/probe binaries. No failures. The Linux
lane remains internal and unregistered; no profile or complete AW packet is enabled or qualified by this checkpoint.

The [held Unix outer-metadata checkpoint](UNIX_OUTER_METADATA.md) adds 46 cases. Its affected origin/metadata suite
passes 192 with 36 skips on Windows, 221 with seven skips on Mac and 218 with ten skips on Ubuntu (228 total per
platform), with no failures. Native C observers independently agree on every stat/flag field on Mac and Ubuntu.
Private Unix storage, metadata application and journaled publication/recovery remain unfinished.

The later [Unix storage checkpoint](UNIX_ARCHIVE_STORAGE.md) adds 32 cases: the 96-case affected storage/observation
replay passes 56 with 40 skips on Windows, 88 with eight skips on Mac, and 86 with ten skips on Ubuntu. The stricter
mount-bound Linux worker admission separately passes 40 with one platform-guard skip. No failures. Native private
files and reviewed no-replace primitives now exist, but complete metadata application, journaled publication/recovery
and exact cleanup remain unfinished; the Unix unlink API explicitly retains evidence rather than using path deletion.

The [Unix origin-application checkpoint](UNIX_ORIGIN_APPLICATION.md) adds 13 Core native cases and one Windows case.
The affected Unix storage/observation/origin replay passes 100 with nine skips on Mac, 98 with eleven skips on Ubuntu,
and 56 with 53 skips on Windows (109 total). The affected Windows native archive/publication lane passes 121 with two
skips (123 total). No failures. Origin bytes can be copied create-only to sealed new private artifacts; renamed/recovery
leases cannot write them. Strongest-input aggregation and complete transaction integration remain unfinished.

The later [schema-2 provenance journal checkpoint](ARCHIVE_ORIGIN_JOURNAL.md) adds 43 Core and three headless UI cases.
Its affected Core replay passes 955 with 30 skips on the Windows VM, 964 with 21 skips on Mac, and 980 with five skips
on Ubuntu (985 total). Recovery UI passes 256 with two skips on Windows and 252 with six skips on Mac/Ubuntu (258 total).
No failures. Legacy completed journals stay visible for archive-specific provenance review; recorded artifacts are not
opened or cleaned by the report. Native publication, real crash recovery and complete UI/package qualification remain
unfinished; these results complete no AW packet or full-goal gate.

AW0 is partial: baseline replay exists, but instrumented canary validation, tool inventories and scale/resource measurement
are unfinished. AW1 is partial: protocol/proxy/admission exist, but no production platform confinement/publication adapter
has qualified. AW2 is partial: typed dispatch refuses safely, but shared publication/recovery and worker-backed ZIP remain
unfinished. AW3 is partial as described above. AW4–AW7 (complete UI/provider/edit-session workflows, BZ2/Zstd, audited 7z and
standalone formats) remain unfinished. AW8–AW9 remain the optional XZ/ISO work defined by the plan.

Do not mark any packet complete, enable a format, claim all-platform live acceptance, or claim absence of bugs from these
results. Continue with production worker confinement/lifecycle and native publication/transaction facts, then connect and
qualify the transforms and UI. Preserve existing ZIP behavior throughout the separate publication and worker refactors.

Guest use checks for other test instances before starting. Close only instances launched by this task and confirm exit
after each test. A temporary `caffeinate -diu` assertion keeps the Mac awake until reboot at the user's request; it is not
a test instance and is intentionally retained.

The Windows guest setup is owned at `C:\Users\admin\FileCatValidation\ArchiveWriting-42829297`.
The Mac's current run includes the full assessment source and final accounting corrections described in the assessment
report. Windows native probes use the host-built binaries
identified in the spike report. The Windows SDK copy is hash-verified
(`8DC778E823BBA1BF6C6D6C608727304663C162790D783187AFCF1B5B6EB8CF17`) and reports 10.0.101. Download from the guest stalled;
only this task's matching downloader was stopped. SDK/reference packs and a locked-package offline feed were copied from
the host. The repository deliberately pins serviced runtime/apphost packs 10.0.14 separately from SDK 10.0.101's bundled
10.0.1 packs. Copying the missing serviced apphost packs resolved offline restore. Test execution uses the installed
10.0.14 host with the private SDK's VSTest console. Earlier elevated/setup runs failed or timed out and are not passes;
only the medium-integrity functional run and the separately recorded successful native lanes are counted above.
VMware paths are `V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx` (actually Windows 11) and
`V:\Virtual Machines\Barebit\Ubuntu 64-bit\Ubuntu 64-bit.vmx`. The user supplied `a` for VM encryption and guest passwords.
Control commands require `vmrun -vp a`; guest users are `admin` and `benny` respectively. On 5 November the user explicitly
authorized stopping the pre-existing Ubuntu picture-test FileCat PID 65891 and image viewers 66892/67247 so this work
could continue. They were checked against their exact command lines before being signaled. This exception does not
change the rule to postpone on other occupied machines or authorize stopping unrelated future test instances.

## Required independent review after the implementation loop

On 5 November the user required a separate independent-review pass after the full implementation/testing loop. Treat
this as a completion gate within the active goal, after the RAR analysis and any feasible implementation. Re-read the controlling plan and
final diff from the reviewer's perspective; assess actual behavior and evidence without relying on implementation
assumptions or test success as proof of correctness.

Cover UX, UI, functionality and security: complete workflows and truthful unavailable states; defaults, focus, keyboard
and accessibility; naming, occurrence selection, fidelity, cancellation and recovery; confinement, IPC/resource bounds,
identities/aliases, publication/rollback/cleanup ownership, provenance and metadata; licensing/package boundaries and
all-platform qualification. Record reproducible findings and severity, fix findings, and repeat appropriate tests/live
validation and review until no known actionable findings remain. Preserve explicit limitations; do not claim absence
of all possible bugs. A final review report must identify the reviewed commit/artifacts, coverage, fixes and remaining
qualification limits. Do not mark the goal complete before this gate is satisfied.

## RAR analysis and feasible implementation before independent review

After the main archive-writing implementation/validation loop, research RAR writing before the final independent
review. The user's later clarification supersedes the earlier review-first order and analysis-only scope: the analysis
should lead to implementation and validation where a license-compatible, technically viable path exists.

At that point, research current primary licensing/specification sources and the repository's existing RAR provenance.
Assess a FileCat-owned implementation developed without violating upstream restrictions, including any necessary clean
separation from restricted decompressor sources. If other viable options fail, assess invoking an external `rar` tool,
including user-supplied installation versus redistribution, license obligations and Windows/macOS/Ubuntu feasibility.
Produce an evidence-backed comparison and recommendation, then implement/validate a viable option if possible. Do not
waive license, security, platform, packaging or preservation requirements to obtain a RAR writer. Record specific
evidence if no conforming implementation is feasible; the final independent review follows this RAR phase.
## User-requested local move into a writable archive

The existing local ZIP route now replaces the F5/manual-delete refusal with F6 move intent, verified publication,
unchanged-source removal, skipped-input retention, queue/name propagation and honest partial results. Real-file/native
adapter and headless UI checks passed on Windows, Mac and Ubuntu; counts, immutable evidence and limitations are in
`LOCAL_MOVE_INTO_ZIP.md`. The follow-up `ZIP_MOVE_NATIVE_GUI.md` adds Mac keyboard/AX and Ubuntu desktop journeys and fixes
edited target mapping, safe conflict defaults and singular summaries, with 85 passing adjacent UI checks on each
platform. Native Windows desktop, complete Mac visual acceptance and full crash/namespace/metadata/scale qualification
are still open. These checkpoints do not enable new profiles or complete the common transaction boundary.

The amended plan §11.6.1 also applies to future qualified formats. RAR feasibility should lead to an implementation
where lawful and feasible, before the final independent review. The main implementation loop and those final phases
remain unfinished; keep the goal active.
