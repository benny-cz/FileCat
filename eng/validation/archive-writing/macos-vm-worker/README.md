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

The guest cgroup ceiling, 1536 MiB guest physical RAM and host-helper memory are different quantities. Initramfs/runtime
file pages populated before the child are not all charged to that child's cgroup. The small healthy memory peaks
therefore do not establish a whole-process footprint or the full guest/host budget. Aggregate host/VM CPU accounting,
cooperative cancellation, parent death of the native host, hostile assets/IPC, large/hostile workloads, setup races,
signing/distribution, startup/UX costs and the complete resource envelope remain unqualified. The prototype's cancellation
fallback stops the VM; it is not an admitted production cancellation route. Healthy tests complete no full AW packet.

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
