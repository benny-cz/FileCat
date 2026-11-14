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
build - each refused with its reason: the AppImage in its own package route lane, from a real mount; the
framework-dependent build in `ArchiveAppContainerRuntimeAccessTests` (`7bc4944a`), on a synthesized layout, since no
package route lane runs from that zip.

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
   unless the committed check's report, carried under that route's own `…/edit/check`, accepts the archive carried
   under its `…/edit` - by digest, by name, and on the system that route's runtime implies, by a line whose whole tail
   is the check's own. What that binds is the report to the archive and the archive to the route. That an archive is a
   machine's own sealed run rests on the runner that sealed it, and that a report is the committed check's own rests
   on whoever carried it - both are read as text and bytes here, so a file built to the shape with a report written
   for it satisfies this. The file system in a route's ID is in no sealed record, so it rests on the composition
   pin inside the run. Where the check reads a system, the run's own dual-root pin has to agree with the machine
   record; Linux and macOS are told apart by that record's `uname -a` line alone. So what this condition keeps out is
   the mistake - a lane that did not run the pins, a gate that was off, a report moved to another archive or route, a
   stale report, an archive renamed or swapped, parts of one archive that disagree. Whoever can write the evidence
   folder is outside it, and always was: that is what the runner sealing each archive on its own machine is for.
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
refusal then checked that a listed route carried an entry and not what was inside it, which is why that report is
carried as evidence in its own right; since the twenty-ninth to thirty-first reviews it reads that report, and binds
it to the archive and the route it speaks for (condition 4). It is carried under each route's edit ID because the
manifest credits an entry to the longest listed ID it is under: `…/edit/check` is the edit route's evidence, where
`…/edit-check` would have been credited to the archive route above it.

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
unless the report under that route's own `…/check` accepts the archive carried under its ID, with cases of its own for
the ways the old refusal could be met. This candidate's manifest keeps the digests it was written with, and the
binding holds for it: run over the three sealed archives, the check names exactly the digests the manifest records for
the three `…/edit` entries ([status](IMPLEMENTATION_STATUS.md)).

The thirtieth review read that binding by trying it. An archive bound by its digest alone was still bound to no route -
one machine's lane, with its own true report, satisfied all three - and only the two ends of the report's verdict line
were read, so a hand-written line could say anything in between. The check now names the system out of the archive's own
machine record as well as the digest, the manifest requires the whole tail in the check's own words with that route's
own system in it, and it refuses outright to bind two listed routes that share a system, because a report naming the
system cannot tell them apart. Read today, each of the three sealed archives names the system and the digest of the
route it is listed under.

The thirty-first review attacked that binding with reports the check itself had written. It read the system from
whichever machine record came first, so an extra record put ahead of the real one spoke for the run; and a record whose
one `Os` field was rewritten said whatever it liked, while the same report listed the Windows platform suite and the
dual-root pin passing in the Windows class. An archive now carries one machine record, and the system that record names
has to agree with the class that passed the run's own dual-root pin, so both of those archives are refused by the check.
Nine cases hold the requirement now, each failing the one it claims; what the binding does not establish is said in
condition 4.

The thirty-second review attacked both halves again and found the rule the round before it had added: read as a set of
labels, any third class that also passed the dual-root pin switched the agreement off, so one added row had a Windows
lane accepted as Linux evidence and listed on the Linux route. Each class is asked for on its own now, and results
claiming both are refused. It also found that a repeated member name answered for the original - appending clean copies
under the names already there turned a failed run into accepted evidence - which is refused by name, and that the twin
of a count rule was measured by nothing. Every rule of both halves is now measured one at a time, each failing the one
case that claims it.

The thirty-third review attacked both of those rules and got through both. One appended folder header emptied any file
in a sealed tar, because a tar resolves a name to its last member, gives nothing for a folder, and the new filter kept
folders out of the list where anything would have noticed - so a log saying a test had stepped aside read as empty and
the lane was accepted. Members are read through now rather than by name. And a run naming neither platform class for
its dual-root pin left the machine record unchallenged, so the Windows lane with one renamed class was accepted as
Linux evidence; such a run is refused, since nothing in it says what produced it. Thirty-seven cases hold the check and
ten the manifest, each rule failing the one case that claims it.

The thirty-fourth review read the gate against that boundary and found no way past it for an untampered archive, and
three rules that gave way to an append or had no case: a zip appended to a sealed zip was read as the appended one
alone, so nothing of the sealed run was looked at; a name was matched as the whole leaf by no case, and loosened it
would find a `status.txt` in `full-r35-win-status.txt`; and the refusal every route in this document rests on - a
listed route whose evidence the manifest does not carry - was held by no case in either suite. All three are closed,
with their cases, and the step-aside scan now reads the results file where a test's own reason is, not only the console
log that never carries one.

The thirty-fifth review found no way to make the gate accept or misbind an untampered archive, and two things to close.
The first form of "an archive begins at its own start" asked only whether some listed member starts at byte 0, which a
directory appended to a sealed zip satisfies while covering none of the rest: the failed Windows full-regression lane
was accepted that way. A sealed run's own listing now has to cover its file exactly, once. And six rules of the two
halves could change a verdict with no case noticing - how a log, a results file, a system word and the gate's own name
are matched, whether every status file is read, and whether a remote package's evidence is carried - each of which has
its case now. Forty-five cases hold the check and twelve the manifest.

The thirty-sixth review took the layout reading apart. Two more ways to add bytes: the spans were measured with the
sizes a zip's own directory states, so a directory appended with one size overstated covered a sealed archive exactly
on paper (the local header states that size too, and the two now have to agree); and the tar half had no rule, so a
sealed `.tar.gz` put after a forged one was passed over in favour of the forgery (one gzip member with nothing after it
is required now). Nine rules of that reading could change a verdict with nothing holding them, each of which has its
case; two guards have no case and none to be had, and say so where they are.

This is where that reading stops. Every way past this gate found since the twenty-ninth review needed bytes added to a
sealed archive, and each was closed by reading the container more carefully - which the next shape of forgery will ask
again, because a parser can always be asked one more question. What ends it is a digest of each archive taken by the
runner on the machine that sealed it and carried beside it, so this host compares bytes instead of parsing them.
Neither runner writes one today; that is a change to both and one more file per lane, for the next candidate. Until
then the gate keeps out the mistake it says it keeps out, and the layout rules keep out the appends that have been
tried.

The thirty-seventh review found the gate sound for every archive as its runner sealed it, and two claims of these
records false. One: the overlap check was said to have no case and none to be had, where two directory entries can
point at one member under two names - a listing that drops a member and pads the gap that way was refused by that check
alone, with no member byte changed. It is closed by a new rule instead, that a member's local header carries its own
name and the directory's name for it has to be that name, which also closes a directory that renames a member to hide
it. Two: "every way past this gate needed bytes added to a sealed archive" counted two ways that needed no sealed
archive and were closed by binding rather than by reading a container - so a runner-sealed digest ends the container
questions and not the binding ones, and, carried by the same hands as the archive, not forgery either. Also closed: a
gzip member holding two tars, a tar that was not gzipped, a hole in the middle of a listing, a stream that cannot be
read, and one ordinary shape refused wrongly. Fifty-eight cases hold the check and thirteen the manifest.

The thirty-eighth review read the guards of that layout reading against what they do. The overlap check, which these
records called unreachable, is reached by a directory alone wherever a member's own bytes hold a local file header - a
member holding a stored zip does - and it was the only rule refusing a listing that drops a member and pads the gap
with the member inside another; it has a case now. The reason given for the run-past-the-directory guard was false (it
is the reach half of the sums that refuses that shape, and the pair is mutually redundant), and the count of guards
without a case was three in the zip reading and one in the tar reading, not two. Two archives an ordinary writer makes
were being refused - a sparse tar member, measured by what it expands to rather than what it holds, which macOS's own
tar writes and the Mac runner seals with; and a libarchive zip written wide, whose descriptors are eight-byte while its
local headers carry no zip64 field - and both are accepted now. Two rules the manifest's binding rests on, that the
verdict line begins and ends as the check writes it, and the system word the check itself prints, had no case; each has
one. Sixty-two cases hold the check and fourteen the manifest.

The thirty-ninth review read that harness against the check it measures, and found the harness's own claim to be the
false one: its list was not every rule of the check, and six rules it did not list were held by nothing - among them
the line that turns the check's findings into a refusal, without which a lane that failed is written an accepting
verdict. Three of its entries measured a message rather than a rule. All of that is closed: each entry says what is
expected of it, the harness has an audit that prints the conditions no entry names, and its answer today is fifty-four
rules listed, forty-seven held by a case, seven noted as redundant or caseless, and none expected to be held and
unheld - which its exit code reports, so a rule added without a case fails the run.
