# Unregistered Mac managed archive guest experiment

This development harness runs the actual production TAR-family assessment, transform and verification code in
separate headless Linux guests on an ordinary-user Mac. It is outside application composition and packaging. It
does not register or qualify a backend. No arbitrary archive is opened by the host: the parent supplies fixed
numeric stream grants through the existing framed protocol and retains the stage in its own memory.

Each of TAR, gzip, BZip2 and Zstd exercises creation, source assessment, replacement and fresh verification in four
different guests. The parent plans expected content and the verifier checks 16 distinct successful/reaped helper
PIDs, each guest's kernel/identity/resource facts, natural shutdown and independent extraction. Binary fixture data
includes all byte values, and the member name includes Unicode. BSD tar is the independent TAR/gzip/BZip2 reader;
the existing hash-pinned native libzstd oracle decodes Zstd before BSD tar reads it. No oracle is shipped.

Guest root init installs a 1 GiB memory cgroup, disables swap, limits tasks to 128, forks and drops UID/GID to 1000
with empty supplementary groups and no-new-privileges. The child verifies the actual controls and refusal to change
its limit, sets parent-death SIGKILL and a fixed 30-second CPU limit before starting .NET 10.0.12. It establishes a
raw second serial channel; only after readiness does the parent send a bounded nonce/role authorization. Raw descriptor
streams avoid .NET's interactive Unicode terminal reader. A separate console carries diagnostics, bounded to 1 MiB;
only the first 512 protocol bytes are retained for diagnosis. No network/storage/share/balloon device is configured.

The guest cgroup ceiling, fixed 1024 MiB guest physical RAM and host-helper memory are different quantities. Initramfs/runtime
file pages populated before the child are not all charged to that child's cgroup. The small healthy memory peaks
therefore do not establish a whole-process footprint or the full guest/host budget. Aggregate host/VM CPU accounting,
startup/release cancellation, hostile assets/IPC, large/hostile workloads, setup races,
signing/distribution, startup/UX costs and the complete resource envelope remain unqualified. Healthy tests complete no full AW packet.

The managed host now accepts only `1024` MiB and configures exactly 1 GiB, with no balloon. Guest startup also checks
kernel-reported physical memory. A controlled 256 MiB changing-entropy native allocation completes, while a 1280 MiB
attempt is killed after 800 MiB has been touched in the measured run. These are deliberate diagnostics, not passing
fuzz-limit kills. The distinct allocator-only experiment in `macos-vm` retains its original settings/evidence.

The runner measures helper/provider/observer RSS and physical footprint through the public SDK's `proc_pid_rusage`,
checking its structure layout with a compiled C ABI probe and retaining stable process-birth identities. Ten-second
draining/blocked changing-entropy output cases also complete and clean up. The provider peaks above 1 GiB in RSS,
so guest physical RAM does not establish the whole host-process envelope. Accounting samples and abort watermarks
are diagnostic evidence; they are not hard OS memory controls or production admission.

The native development host now requires its actual parent PID, registers a synchronous `kqueue` process-exit watch
before VM setup, checks parent identity again and polls both the kernel event and parent relation. Parent death stops
the VM through the public stop API with bounded start/stop waits and an observed stopped state. Startup/stop deadlines
use the monotonic clock. Installed signing/entitlement modes and full resource/process-denial qualification remain open.

The parent now owns a dedicated inherited anonymous-pipe writer. Closing it requests cancellation without writing
archive protocol bytes. The native host verifies an inherited read-only FIFO, polls EOF separately and relays one
byte through a third raw serial port. Guest init supplies only fixed read-only fd 3. The guest reuses Core's actual
bounded transport source with its cancellation token, without adding a production API/friend. A real assessment
session and blocked-input case exit cooperatively; full output can require the bounded hard fallback. A guest that
ignores cancellation is stopped through the VM API after 1.5 seconds, leaving margin before Core's two-second kill.

Four cancellation cases and ten invalid-descriptor refusals are verified independently. Twelve direct SIGTERM/SIGKILL
helper-death cases cover blocked input, a spinning guest and full unread output. The observer holds protocol/control
pipes open, identifies the uniquely created same-UID dedicated virtualization provider and requires its kernel exit
and disappearance after helper death. It never signals that provider or a baseline VM. These measured development
cases support the hard-stop fallback; they do not qualify every release/package/startup state or the full backend.

The runner also performs 8 invalid-parent cases and 20 owned-parent SIGKILL/SIGTERM cases: before native exec, after
guard setup, with a guest blocked waiting for a request, and with a guest spinning without protocol reads. Its observer
keeps both protocol ends open after parent death, so EOF cannot satisfy the stop test. Kernel process-exit status,
explicit VM-stop completion/state zero, direct parent reaping and orphan absence after adopter reaping are checked.
All these are development experiments; their reports retain `production_backend_qualified: false`.

Use a task-owned source snapshot on the authorized Mac, with the SDK and CLI tools already installed. The runner changes
only that snapshot's build/restore outputs and its new evidence directory, including generating private musl restore
locks. Never pass another implementor's/shared checkout. CLT clang 21 and SDK MacOSX27.0 are selected explicitly, without
changing global developer settings. An idle process census before build and each role postpones work if another tester
is active; every owned helper is stopped/reaped. The runner installs/downloads nothing and needs at least 8 GiB machine RAM.

Prepare the pinned kernel/module described in [the memory experiment](../macos-vm/README.md). The library fixture directory
must contain exactly these three archive inputs; the packer checks their SHA-256 and packages selected `.so` files only:

| Input | Official source | SHA-256 |
| --- | --- | --- |
| Alpine minirootfs 3.24.3 aarch64 | [Distro release](https://dl-cdn.alpinelinux.org/alpine/v3.24/releases/aarch64/alpine-minirootfs-3.24.3-aarch64.tar.gz) | `4d3f80afaebd447c5591a63a73bde380a149fe8b2d85488cafabb2ff97551f50` |
| libgcc 15.2.0-r5 | [Distro package](https://dl-cdn.alpinelinux.org/alpine/v3.24/main/aarch64/libgcc-15.2.0-r5.apk) | `369aaa6e9d099a737bad6dd3e6c2fe7bb1547ca26d22b94ee0411228f709b403` |
| libstdc++ 15.2.0-r5 | [Distro package](https://dl-cdn.alpinelinux.org/alpine/v3.24/main/aarch64/libstdc%2B%2B-15.2.0-r5.apk) | `2302e766d4e4926038ec166ecb85837ee884576115236ddb565e3a5fca4a11d7` |

These are private experiment fixtures, not FileCat distribution assets. The broad existing Archives reference also
copies the baseline replaceable Rar15 assembly. No new GPL/LGPL encoder is admitted. Exact source/build/dependency/
license/notice/corresponding-source closure for every guest-system/runtime asset is required before packaging; a VM
does not waive that requirement. The experiment grants no licence conclusion about shipping this guest system.

```sh
export FILECAT_ARCHIVE_ZSTD_ORACLE_LIBRARY=/absolute/reviewed/libzstd.dylib
bash /absolute/private/repo/eng/validation/archive-writing/macos-vm-worker/run-managed-probes.sh \
  /absolute/private/repo /absolute/kernel.Image /absolute/virtio_mmio.module \
  /absolute/library-fixtures /absolute/dotnet /absolute/new-evidence
```

The output contains both published app/runtime bundles, build/tool identities, source snapshots, signed native helper,
owned ELF/initramfs, per-role diagnostics/protocol prefixes, stages, expected payloads, child lifetimes and independent
`verified.json`. Successful reports explicitly retain `production_backend_qualified: false`.
`parent-death/facts.json` records every forced-death case, signal, exact parent/helper identity, kernel exit status,
VM outcome and elapsed time. The observer never signals another tester and the idle census runs between cases.
`cancellation/facts.json`, `cancellation/verified.json` and `helper-death/facts.json` retain cooperative versus forced
outcomes, actual session cancellation/reaping, descriptor refusals, dedicated provider identities/exit events and
unread-output byte counts. The final runner includes each check; every production qualification flag remains false.
`memory-envelope/*.json` preserves all native accounting samples, maxima and allocation/output outcomes. Its independent
verifier checks the samples and eight refused RAM arguments. The ABI facts/tool binary remain alongside the evidence.
