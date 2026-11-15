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
   pin inside the run, which the gate arms. Where the check reads a system, the run's own dual-root pin has to agree with the machine
   record; Linux and macOS are told apart by that record's `uname -a` line alone. So what this condition keeps out is
   the mistake - a lane that did not run the pins, a gate its runner's record shows off, a report moved to another
   archive or route, a stale report, an archive renamed or swapped, parts of one archive that disagree. The record says
   what reached the tests only through the runner's own layers - three reviews running found one read wrong - and
   the machine's own environment is in no record at all; what would end both, the tests recording the gate as they
   read it, is queued beside the digest. Whoever can write the evidence
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
then the gate keeps out the mistakes it has been shown, and the layout rules keep out the appends that have been
tried - the fortieth review found one mistake it did not keep out, in its own reading of a runner's record.

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
expected of it, the harness has an audit that points at lines no entry names, and its answer then was fifty-four
rules listed, forty-seven held by a case, seven noted as redundant or caseless, and none expected to be held and
unheld - which its exit code reports for the rules it lists, and for those alone.

The fortieth review was asked whether anything was left that the harness, its audit or this boundary did not already
report, and it found that something was. The gate read a runner's record by a looser parse than the runner itself applies, so a
mistyped command line - a trailing space, a pair joined by the other runner's separator, the gate given twice - was
read as the gate in force while the tests ran without it: the first finding since the thirty-third review about an
archive exactly as its runner sealed it, and the sentences that said the gate held for every such archive now say
otherwise where they stand. Each runner's record is read as that runner applies it now, and a gate given twice is
refused; the three sealed lanes are accepted as before. (The forty-first review found three more layers, below.) Two claims about the harness were false - its exit code speaks
only for the rules it lists, and its audit filtered line prefixes and could not see that finding's lines - so the audit
walks the syntax tree now and calls itself a pointer, not a proof. A descriptor shape with no case, two ordinary
writers still refused, and a run that an unreadable archive could end with a traceback are closed with it. Seventy-five
cases hold the check and fourteen the manifest, and the harness lists seventy-nine rules, seventy-one held by a case,
eight noted and none unheld.

The forty-first review replayed both runners and found three layers between what a runner records and what reached its
tests, each read wrong on an archive exactly as its runner sealed it: Windows names matched without case, so a
lowercase gate after the real one is the value the tests read; the recorded Windows `Env` passing through `cmd.exe`
and the ANSI code page on its way to the child; and Python ending a Unix line where bash does not. Each is closed - a
Windows `Env` is trusted only as plain ASCII and only as its sealed `child.cmd` passed it - and both runners now refuse
those inputs before a run, while every value the r35 lanes sealed passes them. (The forty-second review found
`child.cmd` re-cut through the runner's other arguments, which neither runner checked, and the Windows pattern letting
a final line feed by - below.) Two reviews running have found such a
layer, which is the container question again: the check re-derives what the tests were given from a description of
what the runner was asked to do. What would end it is the tests recording the gate as they read it, and that is queued
for the next candidate beside the digest. Eighty-three cases hold the check and fourteen the manifest; the harness
lists ninety-three rules, eighty-three held, ten noted, none unheld.

The forty-second review found three inputs the runners accepted as they then stood, each sealing an archive the check
read as the gate in force while the tests ran without it: a `"` in the Windows filter re-cut `child.cmd`, so an `-e` of
its own bound the child's Env while the real `-Env` text, which the check matched by pattern, still stood in the file;
`VSTESTCLIRUNSETTINGS` beside the gate, which the test platform turns into the gate set to `0` inside the test host
(reproduced here); and a Unix filter holding a line break, which wrote an `env=` line its runner never exported. The
check now reads `child.cmd` whole, as the runner's own launcher, and refuses a name outside `FILECAT_`; both runners
check every argument that reaches `child.cmd` or `machine.txt`, and every argument the r35 lanes passed goes through.
(The forty-third review found a `!` in `-Dotnet` still deleting the `-Env` under delayed expansion, and the runner's
other patterns ignoring case - below.)
What no record can show is recorded as the boundary: a variable in the machine's own environment can set the gate
the same way, and a Unix record cannot tell a smuggled line from its runner's own. Both end with the tests recording
the gate as they read it, queued beside the digest. Eighty-six cases hold the check and fourteen the manifest; the
harness lists one hundred and five rules, ninety-three held, twelve noted, none unheld.

The forty-third review found delayed expansion's other form: where it is on, cmd.exe deletes `!NAME:` up to the next
colon with no closing `!`, so a `!` in `-Dotnet` and a `:` in the Env took the `-Env` out of `child.cmd` while the
Dotnet path still resolved - replayed here, the child ran with no gate and the check accepted the archive. A
`child.cmd` holding `!` is refused now, and the Windows runner refuses `!` in every argument, with `#~` for a negated
shard. It found the runner's other patterns ignoring case, so U+0130 and U+212A got by; they match with case now. And it
found the harness counting a rule as held where a case noticed only a message. The harness now measures verdicts, and
checks each entry's note against the measurement: thirty-two entries were not held by a verdict, twelve of them
redundant for any archive and noted so (eleven: the forty-fourth review found one argument wrong), sixteen held by cases refused for more than one reason, which are single-fault
now, three about what a verdict names, and one held only by a crash, noted as such. Eighty-eight cases hold the check and fourteen the manifest; the harness
lists one hundred and ten rules, eighty-five held by a verdict, twenty-five noted, none unheld or contradicted.

The forty-fourth review found the launcher line's length: cmd.exe reads a batch line as written only up to 8191
characters, and past that an `&` inside quotes ended the command, so an Env padded to put its `&` there reached the
child without the gate after it while the check read the gate as in force - reproduced here. A launcher line longer
than that is refused now, and the runner bounds each argument so its line cannot reach it (6,143 characters at most).
It found two harness verdicts false - a `redundant` note on the rule that takes a gate's value after its first `=`,
which `GATE=1;GATE=1=0` gets past, and an entry held only by a crash - and a third false note, a real rule with no
entry, and a stale count; each is closed, and a raise is now measured as that call's own refusal with the case going on.
Ninety-three cases hold the check and fourteen the manifest; the harness lists one hundred and twelve rules, eighty-eight held
by a verdict, twenty-four noted, none unheld or contradicted. (The forty-fifth review found the 8191 characters to be
of what follows the program's name, which the bound leaves to spare; one of the eighty-eight held only by a crash; and
the tar floor's note true for a regular member alone - below.)

The forty-fifth review found three harness verdicts or notes made false by archives the suite did not hold: the tar
floor's `redundant` note (tarfile lists a last folder or link whose size field claims data, and only the floor refuses
it), the descriptor-shape `no case` note together with a false refusal (an unsigned descriptor whose CRC spells the
signature fitted no shape read as signed alone), and an entry held only by a crash in main(). Each is closed: the floor
has a case, every descriptor shape is tried and what follows decides, and a main() that raises is measured as every
archive it was given refused, its case putting a passing archive first. A record entry with no `=` is refused, a
skipped case stops the measurement, and the texts on cmd.exe's 8191 characters are exact. Ninety-six cases hold the
check and fourteen the manifest; the harness lists one hundred and thirteen rules, ninety-one held by a verdict, twenty-two
noted, none unheld or contradicted. (One hundred and twelve and ninety - two entries weakened the check alike, the
forty-sixth review found.)

The forty-sixth review found the filter reaching the gate: dotnet hands `--filter` to MSBuild inside quotes, so a `"` in
a Unix filter - which the Unix runner admitted - ended that property and set `VSTestCLIRunSettings`, and the test host
read the gate as `0` while the record named it as `1` (replayed here through SDK 10.0.101). A filter holding `"` or `@`,
or beginning with `-`, is refused now by the check and by both runners. It found the check taking a zip64 member's size
from its directory unchecked, against the claim that each member is as long as its own header says, and two harness
entries that weakened the check alike, leaving one descriptor shape unmeasured; the zip64 size is checked, each shape
has its own entry and case, and the harness refuses twin entries. Ninety-eight cases hold the check and fourteen the
manifest; the harness lists one hundred and twenty rules, ninety-eight held by a verdict, twenty-two noted, none unheld or
contradicted. (The forty-seventh review found a rule outside both the harness and its audit, and MSBuild decoding `%XX`
in a filter - below.)

The forty-seventh review found a rule changing the verdict of every runner-sealed archive outside both the harness and
its audit - the status read past its line end, which the suite's fixtures never ended - with two more rules hidden on
lines other entries named; the fixtures seal what the runners seal now, the audit prints a named line whose checks
outnumber its entries, and each of those rules is listed with a case. It found MSBuild decoding `%XX` in a Unix filter,
against the claim that the filter is passed on as written (false, the forty-eighth found, for its trailing backslashes
too), and the Windows runner's UTF-16 console logs read as UTF-8,
which left the log half of the step-aside scan inert on every Windows archive; `%` is refused in filters and project
entries on both runners, and members are read by their byte-order mark - the r35 verdicts unchanged. One hundred and five cases hold
the check and fourteen the manifest; the harness lists one hundred and thirty-five rules, one hundred and ten held by a verdict, twenty-five noted (one
hundred and eleven and twenty-four as first written - the forty-eighth review), none unheld or contradicted.

The forty-eighth review found the claim that a filter is passed on as written false once more - dotnet halves a Unix
filter's trailing backslashes - and a harness entry held only by its weakening's skipping the work after it, with five
rules the audit printed and the byte-order marks and system words held in constants, none of them held by a case.
Reading those, a Windows caption in another language named no system. A Unix filter or project entry holds no
backslash and neither runner's filter begins with `/` now; the gzip header's entry is noted redundant and the
forty-seventh's counts corrected; each of those rules is listed with a case; and the Windows record names Windows
wherever its caption says the word, and nothing else, the Unix record Linux or macOS and nothing else - the r35 verdicts unchanged. One hundred and seventeen cases hold the
check and fourteen the manifest; the harness lists one hundred and sixty-one rules, one hundred and thirty-six held by a verdict, twenty-five noted, none unheld or
contradicted.

The forty-ninth review found two test processes named alike writing over each other's results, log and exit code, so
that a failed process went unread and the check said the run had no failure; a project path holding `..` reaching
another run's sources under the packet's own hash, where the Unix runner's text said every entry was recorded; a
comment claiming every step-aside became a failure under the gate; double-byte code pages, in which the Windows record
did not read as JSON; and five loops no case held. Both runners refuse names alike and `..` paths before a run, the Unix
runner records each entry, and the check refuses an archive that does not hold as many test logs as processes its
runner listed (the counts, names without case - not each log against its process's name, the fifty-seventh review
found), and a recorded project path holding `..` (but not, until the fiftieth review, where it followed a `#`
in a built or published entry); the Windows runner writes machine.json and summary.json in UTF-8 (its account.json and launch.json from the
fifty-second review); each loop, and every other
printed line found to be a rule, is listed with a case - the r35 verdicts unchanged. One hundred and twenty-six cases hold the check and
fourteen the manifest; the harness lists two hundred and one rules, one hundred and seventy-five held by a verdict, twenty-six noted, none unheld or
contradicted.

The fiftieth review found a `..` after a `#` in a built or published entry let through by both runners and the check,
which cut every entry at its `#` where the runners take a built or published entry whole; two rules held in plain
expressions - the tar's floor over every member, and every byte past it - that no entry, audit line or case reported;
the step-aside comment counting two gate-free tests where the edit lanes run six, two of whose reasons the scan lacked;
and three printed lines no case held. A built or published entry is read to its end now, each rule is listed with a
case, the scan holds every reason the six give, and the texts say what the audit cannot see - the r35 verdicts
unchanged. One hundred and thirty-one cases hold the check and fourteen the manifest; the harness lists two hundred and fifteen rules, one hundred and eighty-eight held by a
verdict, twenty-seven noted, none unheld or contradicted.

The fifty-first review found a zip64 member's compressed size read by a subscript no case could tell from the plain
one, the suite's zip64 members all stored; the `expand:` prefix the packet rule passes over held by no case; the check
and the suite still saying the Windows runner writes machine.json and summary.json in the ANSI code page, with fixtures sealed so;
the dual-root pin's "a case of it passed" loop held by no case, every fixture reading the passing row first where r35's
Windows lane reads the stepped-aside row first; and the round-forty-nine and round-fifty entries of the implementation status counting six of r35's Windows
`-Projects`
values where it has five. Each rule has a case and an entry, the texts and fixtures follow what the runner writes now
(the r35 shape a case of its own), the records are corrected, and both runners refuse a test entry that names no
project file - the r35 verdicts unchanged. One hundred and thirty-three cases hold the check and fourteen the manifest; the harness lists
two hundred and nineteen rules, one hundred and ninety-two held by a verdict, twenty-seven noted, none unheld or contradicted.

The fifty-second review found three rules held by no case because no fixture carried the members a runner seals
besides the ones those rules read - the test-process logs told by their prefix from the build, publish and task logs;
a gzipped tar's stream measured over every block, where the suite's tars fitted in one; and the record scanned for what
dotnet changes only on the lines that hand the tests something, where the Unix record's `df` line holds `%` - and the
Windows runner's account and launch records still in the ANSI code page beneath words that said all its JSON records
were UTF-8. The fixtures seal every member the runners seal, as r35's edit lanes hold them (the members, not each in r35's
shapes - the fifty-third review); each rule has an entry and
an accepted tar longer than a block is a case; the runner writes those two records in UTF-8 and the words name the
records they mean; the audit names lines by where an entry's text lies; and the wrapper takes the packet and SDK by
full paths alone - the r35 verdicts unchanged. One hundred and thirty-four cases hold the check and fourteen the manifest; the harness lists
two hundred and twenty-five rules, one hundred and ninety-eight held by a verdict, twenty-seven noted, none unheld or contradicted.

The fifty-third review found five rules the sealed lanes rest on held by no case, the fixtures holding the right
members but not in r35's shapes - a result's name and outcome read across the logger's attributes, a built or published
entry refused for `..` alone, a launcher argument's `+`, a member's name compared as its directory wrote it, a Unix
test line's `!` - each of which, weakened, refuses sealed r35 lanes; the records saying the fixtures held r35's shapes;
three comparisons held on one side only; and the Unix class's leading dot held by no case. Two cases are built as
r35's own lanes, each rule and each comparison's other side has an entry and a case, the words say what the fixtures
hold, and the Unix runner matches its run folder as text - the r35 verdicts unchanged. One hundred and thirty-eight cases hold the check
and fourteen the manifest; the harness lists two hundred and thirty-four rules, two hundred and seven held by a verdict, twenty-seven noted, none unheld or
contradicted.

The fifty-fourth review found the accepted Unix lanes resting on the gzip reader's full window with no case holding it
(no accepted case's stream reached back further than 512 bytes, the accepted r35 lanes' further than 16 KiB), the skipped count's sum held by no
case, two loops held only by the string-hash seed each run drew, the Windows caption's own test held by no case, two
comparisons and the tar floor's rounding held on one side only, and a record naming three of the five lanes a rule
protects. The case built as r35's full Unix lane carries a thousand result rows, the two-process fixture
skips a test, the family's names are read sorted, each rule and side has an entry and a case, and the record is
corrected where it stands; the Unix runner matches its run folder with its slash, and the harness reports a suite run
that does not end - the r35 verdicts unchanged. One hundred and thirty-nine cases hold the check and fourteen the manifest; the harness lists
two hundred and forty-five rules, two hundred and eighteen held by a verdict, twenty-seven noted, none unheld or contradicted.

The fifty-fifth review found the check's acceptance of what the runners admit held by no case - which characters a
launcher argument, a Windows Env value or a Unix record line may hold, and how many digits the hang minutes have, each
of which, narrowed, refuses a lane built from inputs its runner accepts while no case changed - the Unix side of
reading the gate's name whole held by no case, and three texts saying every r35 Unix lane's stream needs gzip's full
window, true of the four accepted lanes only. A case per runner carries every character its own patterns admit, an
entry holds each rule, a Unix case holds the gate's name, and the words say "accepted" - the r35 verdicts unchanged.
One hundred and forty-one cases hold the check and fourteen the manifest; the harness lists two hundred and fifty-seven rules, two hundred and twenty-six held by a verdict,
thirty-one noted, none unheld or contradicted.

The fifty-sixth review found three notes whose arguments were false: a directory understating a member's size, plain or
zip64, is refused by the size rule alone where a member listed inside the short span covers its bytes, and the
next-header test's directory signature took a wrong width where a wider reading ended inside the directory at bytes
spelling one. A case holds the size rule in both forms, the signature half is gone and the directory's own place is held
by an ordinary writer's archive; a summary's `ok`, a step-aside reason in a run's last file, a Unix record's own lines
past what the machine says before them, and a manifest suite whose tool does not build are each held or closed - the
r35 verdicts unchanged. One hundred and forty-five cases hold the check and fourteen the manifest; the harness lists two hundred and sixty-three rules, two hundred and thirty-six
held by a verdict, twenty-seven noted, none unheld or contradicted.

The fifty-seventh review found two rules read where a line begins - a listed process's `test `, a Unix record's own
`packet=` - and an empty env value held by no case, each of which, narrowed, refused a lane: `test ` and the empty value
lanes built from inputs their runner accepts, `packet=` only a machine configured so - its file system named `test` and
mounted under a path holding `packet=` (corrected by the fifty-eighth review, which found this said every one a runner
input's); the round-fifty-six entry naming a descriptor reading wrongly; and "a log of its own" said where the check
compares counts. Cases carry each reading and value, the words are corrected, what follows a descriptor is read from the
listing rather than from bytes, and every report line is written as one line - the r35 verdicts unchanged. One hundred and forty-six
cases hold the check and fourteen the manifest; the harness lists two hundred and sixty-nine rules, two hundred and forty-one held by a verdict, twenty-eight
noted, none unheld or contradicted.

The fifty-eighth review found the Unix path rule's `entry=` half and literal parts of the launcher's and a result's
patterns held by no case, the round-fifty-seven record overstating the `packet=` reading, and the suite's texts saying
four things r35 does not - where its pins run, which members it holds, how PowerShell 5.1 writes a record and how
Compress-Archive stores an empty file. Cases hold each part found now (the fifty-ninth review corrected "each part"),
the records and texts are corrected, and the measuring
suite writes each note as one line; a child.cmd naming another script - bytes changed in a sealed archive - falls inside
the first boundary. The r35 verdicts are unchanged. One hundred and forty-seven cases hold the check and fourteen the manifest; the harness
lists two hundred and ninety-one rules, two hundred and sixty-two held by a verdict, twenty-nine noted, none unheld or contradicted.

The fifty-ninth review found the launcher's line ends and the characters of its program's path, and parts of the two
result patterns, held by no case, and two records saying "each part" where the parts found were meant. The launcher is
built from its own text escaped now, so a character changed in it refuses the runner's own launcher, and the length rule
measures every line of child.cmd; results are read as XML, not by patterns - an attribute in single quotes or with space
around its `=` included; the records are corrected. The r35 report is the same byte for byte. One hundred and forty-seven cases hold the
check and fourteen the manifest; the harness lists two hundred and ninety-nine rules, two hundred and sixty-nine held by a verdict, thirty noted, none unheld or
contradicted.

The sixtieth review found no case reaching the counts r35's own lanes reach - where their pins stand among thousands of
results, their processes, members, blocks and suites, the fourteen archives the records read in one run - so that a loop
read only as far as the cases reached refused every archive r35 accepted while no case changed; the check's text saying
it read results in the logger's namespace or none, where it reads any; and no case holding a nested row. A full Unix lane
past r35's own in the counts it names (the sixty-first review found "in each count" here, the sizes of r35's members
past every case) is read through main() now, with a run of forty archives, a digest over nine
blocks, a step-aside reason in a full lane's last file and a failure in its last suite; twenty-one entries read each
such loop only as far as r35's lanes reach; the text says what the code reads, and cases hold a failure nested, under another root and in another
namespace. The launcher's hang minutes and run folder are read as its runner writes them. The r35 report is the same
byte for byte. One hundred and fifty-two cases hold the check and fourteen the manifest; the harness lists three hundred and thirty-one rules, two hundred and ninety-eight held by a
verdict, thirty-three noted, none unheld or contradicted.

The sixty-first review found r35's own accepted lanes reading members larger than any case's - a 1.86 MB results file in
the Windows edit lane the manifest binds, 34.9 MB ones from seven-block streams in the full Unix lanes - so that a member
or a stream read only part way refused archives r35 accepted while no case changed, and the round-sixty texts saying
every count was reached; a second gate past a Windows Env's third item, the RunIds the runner admits, and a descriptor's
width decided past the first members, each held by no case. A full Windows lane past r35's own, with a results file past
36 MiB, is read through main() now, as the full Unix lane is with one of its own; the stream's case spans nine blocks; a
second gate stands fifth, two RunIds of sixty characters hold every character the runner admits, bsdtar's empty member
stands thirty-eighth of forty files, a macOS record's own lines are read; entries read each member, the stream, the Env, the
RunId, the starts and a zip's listing only as far as r35's lanes reach; the texts are corrected where they stand. The
check is unchanged, and the r35 report the same byte for byte. One hundred and fifty-three cases hold the check and fourteen the manifest;
the harness lists three hundred and forty-four rules, three hundred and eleven held by a verdict, thirty-three noted, none unheld or contradicted.
