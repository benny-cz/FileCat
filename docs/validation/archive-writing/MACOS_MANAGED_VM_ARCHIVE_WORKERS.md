# Mac managed archive roles inside a Linux guest

## Result and limits

On the authorized physical Mac (macOS 27.0.1, M1 ARM64, ordinary UID/EUID 501), the actual archive assessment,
transform and verification implementations complete a four-profile workflow through headless Linux guests. For
each of TAR, gzip, BZip2 and Zstd, creation, assessment, replacement and fresh verification use four different VMs.
The final reproducible runner succeeds with **16 distinct native helpers, exit zero, all reaped**, natural guest
shutdown and no forced stop. The process census is idle after testing, and the temporary until-reboot keep-awake
helper remains active. No host desktop input or GUI launch is used.

This is healthy archive-workload and stream-broker mechanism evidence. **No production backend/profile is enabled**,
and no complete AW packet or goal gate is closed. Full confinement, resource, lifetime, adversarial, packaging and
guest-system dependency/license qualification remain open. RAR analysis/feasible implementation and the subsequent
independent UX/UI/functional/security review remain later goal work.

## Actual workflow and oracle

The parent uses the existing `ArchiveTransformClient`, `ArchiveAssessmentClient`, `ArchiveVerificationClient`, typed
plans, expected manifests, nonce/sequence framing and numeric source/stage grants. Archive bytes travel only over the
second virtio serial channel. The parent retains original/captured buffers and stage authority; no archive source,
destination, filesystem share, network device, storage device or memory balloon is passed to a guest. Diagnostics use
the separate first console. The native helper uses public Virtualization.framework APIs and the public virtualization
entitlement under an ordinary account. It is ad-hoc signed for this private experiment, not release qualified.

The guest kernel/module identities are unchanged from the [allocation experiment](MACOS_VIRTUAL_MACHINE_BOUNDARY_SPIKE.md):
Alpine's `6.18.60-0-virt` ARM64 kernel, direct Linux boot with its gzip zboot wrapper unwrapped, and matching transport
module. The guest runs stable .NET **10.0.12 linux-musl-arm64**, built by the existing SDK **10.0.101**. CLT clang **21.0.0**
and SDK **MacOSX27.0** build owned native code with warnings as errors, without changing global developer settings.

Guest init installs `memory.max=1073741824`, `memory.swap.max=0`, `pids.max=128`, forks and drops the child to UID/GID 1000
with empty groups and no-new-privileges before runtime execution. The owned entry point checks actual cgroup membership,
limit values, identity/parent and denial of limit writes before reading its bounded startup header. The child has a
fixed 30-second CPU limit and parent-death SIGKILL. Complete CPU/process/lifetime qualification is still required.

The source contains 131,072 bytes of fixed-seed binary content. Replacement uses 196,608 bytes with expected SHA-256
`74bdecf60128622c0bf5a847fb2a3bbaa9f1e233d650dfc525beb19a2ea68c39` under `documents/界.txt`. The independent verifier checks
every role's kernel facts, exact helper PID, zero exit, no OOM/limit events, natural shutdown/state zero, completed
framing prefix, output stage hash and exact member name/payload. Each native BSD tar oracle process has finite CPU,
wall and output-file limits. It extracts only to bounded temporary output, not archive-selected filesystem paths.

| Profile | Output bytes | Independent result |
| --- | ---: | --- |
| TAR | 199,168 | BSD tar member name and exact replacement bytes |
| TAR + gzip | 196,892 | BSD tar decompression, member name and exact bytes |
| TAR + BZip2 | 198,098 | BSD tar decompression, member name and exact bytes |
| TAR + Zstd | 199,184 | Pinned native libzstd decompression, then BSD tar name and exact bytes |

Independent tools: `bsdtar 3.5.3 / libarchive 3.7.4` (binary SHA-256
`ad4e73ac2e00b3d69969b2e8da466f08c7968a51f424380ecb4d9ff6779b28b9`) and the existing reviewed native libzstd **1.5.5** oracle
(`36B1BFDBAACD9B3033DE00459870D6F84DDF3DA7D3FB194FFF3E80935A7EB499`). No oracle is a shipped dependency.

Final helper PIDs: 60135, 60142, 60145, 60148, 60151, 60154, 60157, 60160, 60163, 60167, 60170, 60173, 60176, 60179, 60183,
60186. Every PID is absent after reaping. Between-role census excludes only the current owned parent and postpones if
another FileCat/dotnet/test-host appears; it does not terminate another tester.

## Failures fixed during the spike

The managed include needed instruction alignment after the original init's string literals; compilation initially
refused that branch before any VM ran. The first stream conversation sent data before raw-terminal readiness, and its
failure diagnostics then queried unavailable post-exit Mac process CPU accounting. Readiness now precedes startup
authorization, and the development wrapper retains its last helper-only CPU sample without querying a dead process.

A second protocol failure exposed .NET's interactive `Console.OpenStandardInput` tty path: its Unicode line reader
echoed/transformed arbitrary bytes. A retained 512-byte prefix identifies that corruption. The guest now uses raw file
descriptor streams after native terminal setup. All four binary-content workflows pass after that fix. These are
prototype fixes; they do not change the application's Windows entry point or production backend admission.

The final default allocation-probe builds are also compared against the previously successful control and 1 GiB case:
both generated ELF files and both initramfs files are **byte identical**. The managed branch changes no default probe
payload behavior.

## Memory, distribution and remaining work

The guest cgroup's healthy peaks range from **10,571,776 to 39,964,672 bytes**. They are not whole-process footprints:
initramfs/runtime file pages populated before the child are not all charged to that cgroup. **1536 MiB guest physical
RAM is distinct from the 1 GiB child cgroup ceiling and from host-helper RSS**. The full guest/trusted-host memory and
CPU envelope, process denial, cooperative cancellation, native-host parent death, hostile setup/assets/IPC, large
archives, startup/performance/UX and installed release signing/package behavior still require implementation and proof.
The development wrapper's stop-the-VM cancellation fallback is unqualified for production.

Exact kernel, OS library, CLR, dependency/source/build/license/notice and corresponding-source closure is required
before any guest assets ship. This private fixture selects libraries from pinned Alpine minirootfs/libgcc/libstdc++
archives and references the existing Archives project, including its baseline replaceable Rar15 assembly. No new
encoder licence is admitted or waived. The prototype is outside package composition; all production profiles remain
disabled pending their full gates.

## Reproduction and evidence

[Owned tooling and instructions](../../../eng/validation/archive-writing/macos-vm-worker/README.md) include a pinned
fixture packer, stream-only entry point, native host, shared init's optional managed branch, reproducible runner and
independent verifier. The final runner requires a task-owned native source snapshot and a new evidence directory;
it installs/downloads nothing. No tests are run on the Windows host.

The private Mac output is `/Users/benny/FileCatValidation/archive-writing-42829297/macos-vm-managed-validation-v1`.
The frozen `macos-vm-managed-evidence-v1.tar.gz` has SHA-256
`F6039AB4C98316FFE4B509597FDF5935A56B280DC0623243D5295B6F86D45A62` (104,521,545 bytes). Its 319 payload inventory rows plus
`files.json` include source/base bundles, 17 source snapshots, published host/guest runtime assets, native helper,
ELF/initramfs, tool/build/signing logs, 16 role logs/prefixes, stages, expected payloads, lifetimes, independent results,
original library fixtures and default-probe byte comparisons. All hashes/lengths are verified after SSH transfer into
the private worktree's ignored `.tmp/test-results/archive-writing/macos-vm-managed-v1`; all 17 snapshots match the repo.
