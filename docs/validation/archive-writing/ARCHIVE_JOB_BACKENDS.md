# Archive job dispatch and password ownership

7 November 2026. Production `JobManager` now has a typed, per-manager archive backend route. The public
composition still supplies an empty registry. This component enables no new archive profile and does not
qualify a native recipe, resource envelope or application workflow.

`SubmitArchive` captures an absolute target, immutable profile and bounded change collection. It consumes
the operation password, including on invalid admission. A password can belong to only one job; a second
claim refuses without erasing the first job's secret. Passwords live outside `JobRequest`, journal records,
edit sessions and retained job history. Distinct workers continue to receive independent one-use grants.

The manager retains the operation while queued and through execution and native reconciliation. Queued
cancellation, failed submission, failed worker handoff, journal creation failure and executor failure all
release ownership. Active cancellation retains the password until the backend returns, so reconciliation
can finish. Backends must reap workers and settle borrowed grants and native leases before returning.
The owned buffer is zeroed before a terminal job state is published.

Submission now keeps a job out of scheduling while registration and `JobAdded` observers run. A reentrant
`Schedule`, cancellation or sibling submission cannot start an incompletely admitted job. If admission
fails, the job is removed, canceled and its password released; a sibling queued behind it is rescheduled.
This guard applies to ordinary jobs too, preserving the shared manager's ownership contract.

`ArchiveJobBackends` snapshots trusted composition and rejects duplicate format registrations. Availability
is checked at execution time. An absent/refusing backend reports a controlled refusal; neither an explicit
profile submitted through the ordinary request API nor a missing typed backend enters legacy ZIP writing.
Legacy profile-less ZIP jobs keep their existing dispatch. No environment variable registers a backend.

The 45 focused cases cover all ten shared container/password profiles, zeroing and terminal ordering,
serialization/journal exclusion, queued and active cancellation, failed admission with reentrant scheduling,
failed handoff, journal failure, collection freezing, availability changes, repeated password claims and
per-manager registration isolation. The guest batch also runs the entire core test suite, Windows native
and application-entry selections, independent readers and DLL/PDB/compiler-source binding.

The [verified checkpoint](archive-job-backend-checkpoint.json) records the complete guest batch:

| Platform | Full core passed | Skipped | Focused passed |
| --- | ---: | ---: | ---: |
| macOS ARM64 | 8,032 | 207 | 45 |
| Ubuntu x64 | 8,054 | 185 | 45 |
| Windows x64, standard user | 7,799 | 445 | 45 |

Windows discovers five additional PE-inspector cases (eight versus three). Its six native preflights,
163 selected native cases and ten application-entry cases also pass. Each platform repeats independent
catalog (273 archives), worker (74 archives), native (21 Unix / 33 Windows) and shared workflow (20 archives)
checks. The existing official-reader Zstandard limitation and independent fallback evidence remain explicit.
All six changed C# files match compiler checksums. Ten DLL/PDB pairs bind on Mac/Ubuntu and 17 on Windows;
the source audits cover 1,433 and 2,195 documents respectively. Final censuses find no owned FileCat/test
processes. The Mac wake helper remains running.

Ubuntu's first full run passed 8,053 cases and failed the system-secret-store case because the desktop login
collection was missing. That complete failure is retained. The unchanged binaries then passed the complete
suite with a private DBus session, keyring runtime and data directory. A separate proof checks the actual
Secret Service PID, daemon arguments and private environment; the owned daemon is stopped afterward.
No archive password is stored in that desktop service. The repair changes the test environment, not FileCat.

Future full-suite batches should preflight their system-secret-store fixture before the long regression run.
Continue using checked incremental packets, cached dependency validation and combined machine batches, and
reuse source-bound component evidence when the relevant implementation has not changed. This batch sends
a 52,963-byte delta rather than another full source packet. Do not equate these passed component tests with
production profile qualification.

Next integration work remains concrete: bind assessed raw member keys and recursive selections to the
native executor; derive exclusions from actual captured selections; obtain replacement/exclusion/precision
choices through production decisions; execute journaled creation/update and reconciliation with confined
workers; connect creation, member mutations, external edits, password entry and F6. The frozen legacy-shaped
`ArchivePlan` here is intent, not native identity or publication authority. The executor must acquire and
review the required native leases and complete catalog facts. Unix update/confinement and the remaining
resource/crash/fuzz/RID/package gates still precede enabling profiles and the final independent review.
