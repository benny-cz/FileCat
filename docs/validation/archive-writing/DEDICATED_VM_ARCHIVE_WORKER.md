# Dedicated archive worker and private guest policy

## Result and limits

`FileCat.ArchiveWorker` now uses the stream-only `FileCat.Archives.Writing` assembly. Its published closure contains
no UI, broad `FileCat.Archives`, Rar15 or DiscUtils dependency. Application registration/packaging remain disabled.
This checkpoint completes no format, platform backend, implementation packet or release gate.

On the authorized Mac (macOS 27.0.1, M1 ARM64, ordinary UID/EUID 501), the actual dedicated executable completed
**16 distinct, successful, reaped helpers**: creation, assessment, replacement and fresh verification for TAR,
TAR.GZ, TAR.BZ2 and TAR.ZST. BSD tar and the native libzstd oracle independently checked the Unicode member name and
exact binary payload. A second **16-role run** booted the standard locked-published bundle and passed the same checks.

The strict run passed the existing 20 parent-death cases/eight startup refusals, four cancellation cases/ten descriptor
refusals, 12 direct helper-death cases, four memory/output pressure cases/eight RAM-argument refusals. **23 guest kernel
policy checks** and five malformed/diagnostic-only production startup refusals passed. Separate images containing an
extra root or UID-1000 userspace process both refused with worker exit 65 **before protocol readiness**, then shut down
naturally with helper reaping. These are component mechanisms and specific workloads, not complete hostile-input qualification.

The focused policy/TAR-reader suite passed **51 tests, zero skips** on Windows, native Mac and Ubuntu. Windows used
the existing medium-integrity harness (12288 to 8192); its administrator policy was not weakened. An earlier elevated
run's two unknown-size reader refusals are not counted as passing reader tests or product defects. Full UI/platform
acceptance and fuzzing remain unfinished.

**Whole host memory remains unqualified.** Provider maximum RSS was **1,237,532,672 B**, maximum footprint
**1,091,651,000 B**, despite exactly 1 GiB guest RAM. Healthy child cgroup peaks of 14,983,168–37,306,368 B do not
establish a provider hard bound. CPU accounting, installed signing, image ownership/admission, packaging, guest-system
licences and complete adversarial qualification stay open. No production profile is enabled.

## Implementation and live-discovered fixes

The writing-only assembly compiles the existing encoder/strict-verifier sources and shared TAR header guard. The
original reader retains the same implementation; the extracted guard body was compared with its previous definition
and is unchanged. SharpCompress grants the new assembly the same internal access used by the existing archive package.
This adds no encoder package or RAR implementation.

The dedicated entry accepts only `--archive-linux-vm-worker`, refuses non-Linux immediately and uses a bounded fixed
startup header: nonempty nonce, one of three production roles, zero reserved bytes. Probe roles are unavailable.
Transport and dedicated serial cancellation now use shared Core implementation code.

Before parser input the ARM64 bootstrap verifies actual UID/GID/groups/capabilities, parent and parent-death state,
no-new-privileges, exact CPU/controller settings, physical RAM, root-owned private RAM filesystem/runtime, serial
descriptor identities and narrow environment. It applies the all-thread syscall policy and main-thread lifetime guard.
Writable path opens, openat2, namespace mutation, sockets, process creation, tracing, privilege changes and limit
increases remain denied. The desktop Linux route retains its separate all-thread Landlock policy.

Live execution found three compatibility bugs:

- Control-file opens used x64 flags on ARM64. The implementation now selects `O_DIRECTORY`/`O_NOFOLLOW` explicitly:
  x64 `0x10000`/`0x20000`, ARM64 `0x4000`/`0x8000`; unknown architectures refuse. The live probe positively opens the
  runtime directory and refuses directory/file symlinks. See [Linux ARM64 UAPI](https://github.com/torvalds/linux/blob/v6.18/arch/arm64/include/uapi/asm/fcntl.h).
- musl pthread creation supplies the ignored legacy `CLONE_DETACHED` flag. Denial caused CLR thread-start failure and
  a misleading out-of-memory message at a small footprint. That ignored flag is allowed while `CLONE_THREAD` remains
  mandatory and process/namespace flags stay denied. See [musl pthread_create](https://git.musl-libc.org/cgit/musl/tree/src/thread/pthread_create.c).
- musl pthread signalling uses `tkill`. Denying it prevented the runtime suspension signal; failed SIGABRT attempts
  then ended in SIGSEGV. A separately traced image recorded the denied calls. Omitting the broad filter completed all
  16 roles; omitting only the writable-open restriction still failed. Those were diagnostic variants, not qualification.
  See [musl pthread_kill](https://git.musl-libc.org/cgit/musl/tree/src/thread/pthread_kill.c).

The signal exception applies only to the private guest. Bootstrap verifies the root userspace supervisor, this worker
as the only other userspace process, and root-owned kernel threads otherwise. Kernel threads are identified by the
actual [Linux Kthread status field](https://github.com/torvalds/linux/blob/v6.18/fs/proc/array.c), not names. With process
creation/credential changes denied, UID 1000 and no capabilities, kernel permissions confine `tkill` to worker threads.
Only exact positive TIDs and signals 0, 6 and 35 are admitted. The desktop policy still denies `tkill` unconditionally.
The live probe allows a harmless own-thread query and denies supervisor signalling and an unapproved self signal.
Injected extra-process images verify the bootstrap refuses when this confinement prerequisite is absent.

An initial inventory falsely rejected kernel threads; the corrected check uses the kernel flag and root credentials,
while rejecting every extra userspace process, including root. All failed runs are retained separately.

## Locked publication and provenance

The dedicated musl build pins the tested .NET runtime **10.0.12**. Its three portable libraries add the musl ARM64
restore target; application runtime targets stay unchanged. Their existing lockfiles gain only the empty musl target,
without package version/hash changes. The two new projects have canonical lockfiles.

Lock generation uses `dotnet restore -p:RuntimeIdentifier=linux-musl-arm64 --force-evaluate` with the explicit
dependency-update switch. `dotnet restore -r` replaces the runtime list and produced incompatible single-RID library
locks; the failed sequence is retained. Standard `dotnet publish -r linux-musl-arm64 --self-contained true` subsequently
passes normal locked mode without a runtime-list override or dependency-update switch. That bundle passed the second
live 16-role workflow. Native Mac unit tests and entry refusals also pass from the canonical snapshot.

The compiler-source auditor verifies each production assembly's portable-PDB document checksums, including embedded
generated sources, and retains exact bytes. Both bundles matched **810 documents / 7,589,067 bytes**. This audit was
added after execution and verifies the compiler's recorded hashes; it does not backdate later verifier/project files.
The updated normal runner captures the native Linux flag source, dedicated project/lock and complete linked writing
sources before building. Existing captured snapshots were not rewritten.

## Evidence and remaining work

Strict output: `/Users/benny/FileCatValidation/archive-writing-42829297/macos-vm-production-validation-v9`.
`macos-vm-dedicated-worker-evidence-v1.tar.gz`: **262,586,402 B**, SHA-256
`A37977B73C90855A73AC3FF653CE7C62C481500D98D24975A401F308BC6C63DE`, **1,413 payload rows plus inventory**.
The local archive digest and every inventory size/hash passed independent verification. Its inventory explicitly
records that compiler-source auditing and extra-process tests followed the main run.

Canonical output: `/Users/benny/FileCatValidation/archive-writing-42829297/guest-worker-canonical-v2`, including
`locked-live-v1` and its independent oracle. `macos-vm-canonical-worker-evidence-v1.tar.gz`: **131,333,617 B**, SHA-256
`FA8F622649102C369C910DB02B292424DDA8F10571CB07145205F238985B2FD6`, **3,198 payload rows plus inventory**.
Its inventory identifies excluded intermediate build directories and duplicate published files.
The locally copied canonical archive also passed digest and all 3,198 size/hash checks.

Windows focused results are under the owned `guest-worker-unit-v5` and final-source `v6` snapshots; Ubuntu's successful
expanded suite is under `guest-worker-locked-restore-v1/results`. Ubuntu's failed `test-v2.log` is a private single-RID
lock mismatch, not a pass. Later checks were deferred when another rendering tester became active; it was not stopped.
Nonqualifying Mac failures remain under production-validation `v1`–`v8` and diagnostic directories, including
`macos-vm-production-diagnostic-strace-v2` with exact denied-signal records.

Final Windows `v6` passed all 51 focused tests, worker compilation and entry refusals. The Windows process census is
empty; Mac's idle census passes and wake-helper PID 36765 remains. Neither machine retains an owned test instance.

Only the authorized guests and Mac executed live tests; the host desktop was not used. Close owned instances after
testing and retain the Mac's until-reboot wake helper. `E:\FileCat` remains on `main`; implementation is isolated on
`codex/archive-writing`.

Next work remains the host/provider resource bound, production package/signing and dependency/licence admission, then
the remaining capture/publication/recovery/cleanup/format/UI/fuzzing gates. After the main loop, perform RAR analysis
and any viable licence-compatible implementation, followed by the final independent UX/UI/functional/security review
and fixes/retests. The goal stays active.
