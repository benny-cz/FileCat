# Decision: the first archive-writing release routes

12 November 2026. The owner answered the open route questions of the [remaining-work ledger](REMAINING_WORK_LEDGER.md)
(rows 6, 12 and 13). This record states the answers and what follows from them. It lists nothing:
`ArchiveWriteRoutes.Qualified` and `NativeEditStorage.Qualified` stay empty until the conditions below hold, and adding a
route stays a reviewed release change that its pinning test records.

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

win-arm64 and linux-arm64 stay disabled: no test machine runs them.

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
[record](MACOS_SANDBOX_WORKER_SPIKE.md)). Still open: the packaged app's route test, and the denial, lifetime and pressure
runs on the owner-signed candidate.

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
   AppImage, and the macOS app. Each exercises the listed formats with that payload's own workers.
3. On macOS, the sandboxed launcher and the memory monitor pass their denial, lifetime, cancellation and pressure tests
   in the packaged app (they pass in the published application: [record](MACOS_SANDBOX_WORKER.md)).
4. The release manifest records the application commit, profile schema, exact source and component hashes, worker
   protocol, runtimes, filesystem routes and evidence IDs (plan §16.1).
5. The independent review (ledger row 14) closes, and its fixes are re-validated.

The listing is then one reviewed change of both lists, each route with its evidence ID, and their pinning tests. Release
signing (a trusted Windows signing provider, macOS Developer ID and notarization) stays with the owner. The macOS denial
evidence is repeated on the owner-signed build before release.
