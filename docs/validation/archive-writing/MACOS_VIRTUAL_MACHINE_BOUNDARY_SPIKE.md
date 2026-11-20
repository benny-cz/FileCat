# Ordinary-user Mac VM memory-boundary spike

This follows `73f66714`. The direct Darwin setter remains unavailable to the ordinary user, but an owned Linux guest
now demonstrates a supported-API candidate with a real kernel-enforced 1 GiB allocation-child limit. This is a mechanism
experiment. No archive worker/backend/profile is registered or qualified, and no production dependency changes.

## Route selection

.NET 10's pinned WASI documentation describes its path as an unsupported production prototype. Wasmtime's resource
limiter covers guest allocations rather than all host/store allocations. Those mechanisms alone do not establish this
plan's process-memory envelope, so no WASI workload/package or Wasmtime dependency was installed or admitted.
[.NET 10 WASI source documentation](https://github.com/dotnet/runtime/blob/v10.0.0/src/mono/wasi/README.md),
[Wasmtime resource-limiter API](https://docs.rs/wasmtime/50.0.1/wasmtime/trait.ResourceLimiter.html).

Apple documents a public virtualization entitlement and a fixed guest physical-memory configuration. Its configured
RAM is separate from host process RSS; allocation is demand-based. These APIs provide a candidate guest boundary, not
an automatic proof of FileCat's total host/broker memory budget.
[Apple entitlement documentation](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.security.virtualization),
[Apple VM memory configuration](https://developer.apple.com/documentation/virtualization/vzvirtualmachineconfiguration/memorysize).

The actual Mac is arm64, macOS 27.0.1/Darwin 27.0.0, with 16 GiB RAM. A plain UID-501 helper fails configuration
validation for the missing entitlement. The same owned code with the public ad-hoc entitlement validates, starts and
stops a 512 MiB firmware VM without elevation. The initial initializer diagnostic and missing-variable-store iteration
are retained. No global signing/developer setting was changed.

## Actual Linux guest mechanism

The private experiment uses official Alpine aarch64 `6.18.60-0-virt` kernel/module inputs and an owned minimal initramfs.
No compiler, libc, shell or archive engine is needed in the allocation guest. FileCat's freestanding AArch64 init creates
the memory/pids cgroup, disables swap for it, moves the child into it, clears groups, drops to UID/GID 1000 and sets
no-new-privileges. A native `getuid` check and denied controller-file open precede allocation. The parent reads actual
kernel counters and the child wait status. The VM has one CPU, no network/storage/balloon devices or host directory share.

| Reusable final case | Host PID/UID | Guest RAM | Child limit | Observed peak | Child/kernel outcome |
| --- | --- | --- | --- | --- | --- |
| Healthy 32 MiB control | 58470 / 501 | 512 MiB | 64 MiB | 33,816,576 bytes | Exit 0; no OOM/kill |
| Attempted 1152 MiB | 58474 / 501 | 1536 MiB | 1 GiB | 1,073,741,824 bytes | SIGKILL; `oom_kill 1` |

Both cases report denied limit-file access after the privilege drop, kernel counters and their exact child wait result.
Both guests power off themselves, reach stopped state 0, and leave no helper process. No force-stop or supervisor kill
is needed. All eight compiler/packer/signing/helper children exit and are reaped in the lifetime ledger. The independent
verifier checks identities, configuration, exact markers, kernel events/peaks and complete shutdown. An earlier 64 MiB
over-limit case also reaches exactly 67,108,864 bytes and records one OOM kill.

These expected diagnostic OOM kills are not archive/fuzz passes. Real workloads must refuse over-budget work before
their emergency ceiling; an unexpected limit kill remains a failing workload/fuzz artifact.

The first Linux start failed before guest execution: the distro image has an EFI gzip zboot wrapper, not the flat
ARM64 image expected by the direct boot route. The bounded owned preparer decodes that documented container and checks
the ARM64 magic. A guest poweroff constant was also corrected before successful runs. Earlier failures and their
inputs remain separate from the successful evidence. [Linux EFI zboot header](https://github.com/torvalds/linux/blob/v6.18/drivers/firmware/efi/libstub/zboot-header.S).

## Provenance, tooling and scope

The eight owned [reusable sources and reproduction instructions](../../../eng/validation/archive-writing/macos-vm/README.md)
are checked in under validation tooling, outside production projects. CLT clang 21.0.0 and the MacOSX27.0 SDK compile
the Objective-C host and relocation-free AArch64 object with no added build dependency. The host compiles with warnings
as errors. The small ELF/initramfs packer rejects unresolved relocations; the preparers bound fixed fixture bytes.

Pinned private inputs:

| Input | SHA-256 |
| --- | --- |
| Distro configuration | `B7643CDDCD3BA109183A2AAAFA3C911D91E7D89485FD7B2CAF5EDE9396BF78CA` |
| Original wrapped kernel | `CD05FED9E617B5DF93DC62ECE9B78C422FD94E9B432A2F7C092714B139D5EEF4` |
| Original distro initramfs | `189402937646965374EF2DF5D37F76467135179AE168C88175DF2FE3E264A0B0` |
| Uncompressed kernel | `A2CECB9B6FDBD83E87BD43C8040DE67300D5B8DDE1FC50C34714FAA272888F17` |
| Matching transport module | `97C9144F7C1C4E8D38A4CEF9B83FEC13214B4EB90BB49B438F546A1711279B26` |

The official distro kernel/module are private experiment inputs, not FileCat application assets. Exact source/build/
dependency/license/notice closure and corresponding-source packaging must be completed before any guest system is
admitted to production. This report grants no encoder/component licence waiver and does not complete that audit.

Ubuntu had another FileCat instance (PID 655440), so its build/testing was deferred. The Mac implementation uses its
existing cross-target assembler without installing tooling on Ubuntu or changing global Mac settings. Live work uses
the Mac only, never the Windows host desktop. Existing temporary keep-awake configuration is retained.

Raw evidence remains in the private worktree's ignored `.tmp` directory. Every final source, binary, kernel, initramfs,
lifetime and result inventory row is hash/length-checked after SSH transfer; the eight source snapshots match the repo.

| Evidence | SHA-256 |
| --- | --- |
| `macos-vm-boundary-evidence-v1.tar.gz` (37 payload rows plus inventory) | `74012DB11BEDD9B074168E6AD99F7525B8AE0053467EC058475BFAEA8CE017E7` |
| `macos-vm-origin-evidence-v1.tar.gz` (18 payload rows plus inventory) | `0C6417CFC839BF6E0FDA63D0239720A0C579F5CD9DC507E4962B244C0D94A840` |

## Required next implementation

Integrate the existing stable Linux/.NET archive roles through a stream-only guest broker and qualified native host
launcher. Prove the full process/VM/trusted-host memory envelope, CPU/IO/time limits, cancellation, parent death,
fresh verification, setup/assets/IPC attacks, full format workloads, startup/UX costs and installed signed packages.
Complete guest-system licensing/source/build closure before packaging. Full archive/platform/fuzz qualification and
remaining implementation work, RAR analysis/feasible implementation and final independent review are still open.
