# Decision: the first archive-writing release routes

12 November 2026. The owner answered the open route questions of the [remaining-work ledger](REMAINING_WORK_LEDGER.md)
(rows 6, 12 and 13). This record states the answers and what follows from them. The routes were listed once the
conditions below held (see "Listed" at the end); adding or removing a route stays a reviewed release change that its
pinning test records.

## Answers

| Question | Owner's answer |
|---|---|
| Runtimes | Windows x64, Linux x64 and macOS arm64. For macOS the owner accepts a re-scoped memory gate: monitoring and termination instead of a hard ceiling |
| Formats | All: TAR, TAR.GZ, TAR.BZ2, TAR.ZST, standalone GZ, BZ2 and ZST, 7z, RAR5, and ZIP through the worker profile |
| Native edit storage | On the routed runtimes and on macOS APFS |
| Timing | Routes are listed only after the final independent review (ledger row 14) closes. Until then the candidate is prepared, its package route tests run and the release manifest is written |

## Routes to prepare

| Runtime | Filesystem | Formats | Native edit storage |
|---|---|---|---|
| win-x64 | NTFS | All ten | Yes |
| linux-x64 | ext4 | All ten | Yes |
| osx-arm64 | APFS | All ten | Yes |

win-arm64 and linux-arm64 stay disabled: no test machine runs them. Writing is unavailable from two payloads that do
ship - a mounted AppImage (install the .deb, or extract it onto an ext4 drive) and the framework-dependent Windows
build - each refused with its reason, which is what that payload's own route lane proves.

## The macOS memory gate

Plan §10.2 and AW1 require an enforced whole-process memory boundary before a worker consumes untrusted bytes. An
ordinary macOS user has none: `RLIMIT_AS` returns `EINVAL` ([native spike](NATIVE_WORKER_SPIKES.md)), the footprint
setter returns `KERN_NO_ACCESS` ([probe](MACOS_FOOTPRINT_LIMIT_PROBE.md)), and the virtual-machine provider's own
footprint exceeds its 1 GiB guest ([envelope](MACOS_VM_MEMORY_ENVELOPE.md)).

For osx-arm64 only, the owner accepts this instead: the parent samples the worker's physical footprint at a short fixed
interval and terminates the worker when it exceeds the profile's budget. The job then ends like any other worker failure:
nothing is published and the original stays. This is a sampled bound. Between two samples a worker can exceed its budget,
so it is described as monitoring and termination everywhere (status, release notes, release manifest), never as a
ceiling. Every other worker gate stays as written: confinement before the first untrusted byte, CPU, process-count,
lifetime and cancellation controls, and denial evidence on the signed release candidate.

## The macOS worker boundary

The plan offers two macOS boundaries: a separately signed helper using App Sandbox, or a virtual machine. FileCat takes
the App Sandbox helper. The worker roles run in a separately signed executable that carries the App Sandbox entitlement
and nothing else. The ordinary-user probe found that such a helper's unrelated reads, writes and network access fail with
`EPERM`, and that a fork fails with `EAGAIN` under a zero process limit. The virtual-machine route is not pursued for this
release: it would ship a guest kernel and image, and its provider's footprint exceeds the 1 GiB it was designed to hold.
The [launcher, the footprint monitor and the parent watch](MACOS_SANDBOX_WORKER.md) exist and pass on the Mac (spike:
[record](MACOS_SANDBOX_WORKER_SPIKE.md)). The packaged app's own route test and the packaged launcher's fourteen
boundary tests pass as well (`AWQ-osx-arm64-apfs/app` and `/launcher` in each candidate's manifest). Still open: the
denial, lifetime and pressure runs on the owner-signed candidate.

## ZIP

Once a ZIP route is listed, ZIP uses the worker profile ([design](ZIP_WORKER_PROFILE_DESIGN.md)): creation, F5 into a
ZIP, member commands and edit commits. ZIPs that its strict catalog refuses become read-only for writing, and FileCat
says why (encryption, unknown extra fields, self-extractors and the other refused rows). Until a ZIP route is listed, the
legacy in-process writer (release R0) stays, and the switch is inert. Edit sessions made before the switch keep the route
they were recorded with.

## The shipped worker

On Windows and Linux the worker roles run inside the application executable, as implemented and qualified (`FileCat.exe`
or `FileCat` started with a role argument). The dedicated `FileCat.ArchiveWorker` package is not shipped (ledger row 12).
On macOS the roles run in the App Sandbox helper described above.

## Before any route is listed

1. The release candidate is built from a clean checkout with its packages for every runtime.
2. Package route tests run from the installed and portable payloads: Windows setup and portable, Linux .deb and
   AppImage, and the macOS app. Each exercises the listed formats with that payload's own workers, except the AppImage,
   whose lane proves instead that writing from a mounted AppImage is refused with its reason. These lanes do not cover
   native edit storage: the variable that makes a native edit test fail instead of skip is set by the edit lanes, and
   the composition pin judges the file system of the root its own run gives it, so the three edit-storage routes stand
   on those suites and not on an F4 inside a packaged payload. One packaged F4 per platform with the store required is
   the next piece of work, to be carried as `…/edit/package`.
3. On macOS, the sandboxed launcher and the memory monitor pass their denial, lifetime, cancellation and pressure tests
   in the packaged app: in the published application ([record](MACOS_SANDBOX_WORKER.md)) and again with the packaged
   app's own launcher, whose lane is `AWQ-osx-arm64-apfs/launcher` in each candidate's manifest.
4. The release manifest records the application commit, profile schema, exact source and component hashes, worker
   protocol, runtimes, filesystem routes and evidence IDs (plan §16.1), and it refuses a listed edit-storage route
   unless the committed check's report, carried under that route's own `…/edit/check`, accepts by digest the archive
   carried under its `…/edit`.
5. The independent review (ledger row 14) closes, and its fixes are re-validated.

The listing is then one reviewed change of both lists, each route with its evidence ID, and their pinning tests. Release
signing (a trusted Windows signing provider, macOS Developer ID and notarization) stays with the owner. The macOS denial
evidence is repeated on the owner-signed build before release, together with updating an archive the installed,
quarantined application created itself in Downloads (the eighth review's open check).

## Listed

12 November 2026: every archive-writing route in the tables above is listed, after the sixth independent review (no
High or Medium finding; its Low findings fixed in `f4aa66a7`) and the seventh, of the listing itself, whose findings are
fixed. The archive routes' evidence IDs are `AWQ-win-x64-NTFS`, `AWQ-linux-x64-ext4` and `AWQ-osx-arm64-apfs`
([status](IMPLEMENTATION_STATUS.md)); the edit-storage routes shared them until `a4606689` gave them IDs of their own,
below each archive route's (see below). Native edit storage was not listed then: as the default store it changes what a
stopped F4 leaves behind, and the application's edit flows had not been qualified against it (ledger rows 4 and 8).
The release candidate is `3781e7ae`: it passed on all three platforms with its package route tests, and its
[release manifest](release-manifest-3781e7ae.json) records the routes, packages and evidence.

13 November 2026: native edit storage is listed on the same three routes, after seventeen rounds of independent review,
every High and Medium finding of which is fixed, and the connected qualification on all three platforms. The evidence
IDs are each route's own (`AWQ-win-x64-NTFS/edit`, `AWQ-linux-x64-ext4/edit`, `AWQ-osx-arm64-apfs/edit`, under the
archive route's own ID since `a4606689`): every edit suite, the
application's and the platforms' own, ran with native storage as the default store, and the listing commit `fda3f03c`
passed the full regression and the package route lanes on all three machines. From this commit a new edit on a listed
route is kept in FileCat's own identity-bound folder beside the legacy one: a stopped, cancelled or failed F4 removes
exactly what it made and keeps, logs and names whatever it cannot; a copy stays out of the session list until its record
is published, and after a crash what is left is listed for review and offered for removal only when it can hold nothing
of the user's; copies no longer wait for one another; and Save copy works for a native edit as for a legacy one. Its
[release manifest](release-manifest-fda3f03c.json) records the routes, packages and evidence.

The listing stands on `a4606689`, which fixes what the eighteenth review found in it: an edit made before the listing
could no longer be discarded, and the three edit-storage routes shared the archive routes' evidence IDs, so the manifest
could not refuse a candidate that never ran an edit suite. They carry their own IDs below the archive routes'
(`AWQ-win-x64-NTFS/edit`, `AWQ-linux-x64-ext4/edit`, `AWQ-osx-arm64-apfs/edit`), so a candidate carrying nothing under
them is refused - though that was all the refusal did, and that commit's own manifest still carried the archive lane's
archive under both IDs; what is inside an edit entry is read only since the twenty-ninth review, below. That commit
passed the full regression and the package route lanes on all three machines in turn
([manifest](release-manifest-a4606689.json), [status](IMPLEMENTATION_STATUS.md)).

14 November 2026: the listing re-validated after the twentieth to twenty-eighth reviews, on `048bf334`. Each route's
`/edit` entry is the sealed archive of a lane that ran with the native store required, and beside it `/edit/check` is
the committed checker's own report on that archive: it requires the gate's recorded value to be exactly 1, the runner's
own verdict on the run, every file the other requirements are read from, the three pins by name with a passing row and
no failing one, and no test that stepped aside because native storage could not be held. Until `dcf19885` that checker
refused every real Windows archive - the dual-root pin is one method name in two classes, and a Windows run carries
both, the other platform's recorded as not executed - so no Windows archive had ever been accepted by it. The manifest's
own refusal checks that a listed route carries an entry, not what is inside it, which is why that report is carried as
evidence in its own right. It is carried under each route's edit ID because the manifest credits an entry to the longest
listed ID it is under: `…/edit/check` is the edit route's evidence, where `…/edit-check` would have been credited to the
archive route above it.

The twenty-third to twenty-eighth reviews changed the discard, the startup review, the storage accounting and what the
user is told, and none of them changed either list: the three routes in `NativeEditStorage.Qualified` are those listed
on 13 November, with the same evidence IDs, still pinned in `EditStoreCompositionTests` and in Core's
`NativeEditStorageTests`. `048bf334` is the commit they close on. It passed the full regression and the package route
lanes on all three machines but one Windows cadence case, each package built from a clean checkout of it, with each
machine's edit lane sealed on its own and accepted by the committed check on all three pins; that case failed in its
lane and passed when its class ran alone at the same commit, away from edit storage and from everything these rounds
touched ([status](IMPLEMENTATION_STATUS.md)). Its [release manifest](release-manifest-048bf334.json) records the commit and its
clean checkout, the worker protocol, the profiles and record schemas, the thirty archive-write routes and the three
edit-storage routes with their evidence IDs, fourteen packages with their digests, and twenty-three evidence files - the
three edit archives and the three checker reports among them.

The twenty-ninth review read these records against the evidence and against the conditions above. Every count, digest
and lane verdict held; what did not was the refusal this listing leans on. A listed edit-storage route was satisfied by
a label: the manifest refuses a route whose evidence it does not carry, and nothing read what that evidence was, so a
text file under the right ID - or a report saying in its own words that the archive was not edit-storage evidence -
satisfied it. The check now names the archive it accepted by digest, and the manifest refuses an edit-storage route
unless the report under that route's own `…/check` accepts the archive carried under its ID, with six cases of its own
for the ways the old refusal could be met. This candidate's manifest keeps the digests it was written with, and the
binding holds for it: run over the three sealed archives, the check names exactly the digests the manifest records for
the three `…/edit` entries ([status](IMPLEMENTATION_STATUS.md)).
