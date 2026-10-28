# E-I549 — native Linux ARM64 picture-worker process boundary

## Scope and producer

- Baseline source: `88988ef497865cdbcf2c3b02f9b7087485a0f66c`
- Accepted implementation: `445f20dca754fc49bcd3cb46c2a0176fe6ae6bf9`
- Raw evidence: `V:\FileCat\artifacts\release-evidence\i549-linux-arm64-picture-policy-20261028-v1`
- Raw manifest: 34 files, zero mismatches, SHA-256 `20534ae538ff534723e1ac62907f0ff549b14b5a9f18dfb41ad162db19541cba`
- Complete-history Git bundle SHA-256: `8c90984b1c788ce462505530e6ce9fab8be7a16d3a0a978f9c3dcb24f8b48755`

Stopping condition: on native Linux ARM64, install the picture-worker process-creation boundary before input, preserve healthy BMP/PNG and bounded invalid/empty behavior, refuse actual process creation, and keep the established Linux x64 boundary intact.

## Baseline and correction

The baseline policy returned unless `ProcessArchitecture == X64`. On a native `aarch64` Ubuntu 24.04.5 VM, all four worker cases therefore skipped (**0P/4S**) and an actual pre-input FileCat worker reported:

```text
NoNewPrivs=0
Seccomp=0
Seccomp_filters=0
```

The correction selects an explicit ABI record for x64 or ARM64, checks the audit architecture before syscall interpretation, uses the generic AArch64 syscall numbers and fails closed for unsupported Linux architectures. The x64 x32-ABI rejection remains x64-specific; both architectures deny `execve`, `execveat`, process-style `clone` and `clone3`, while thread-style `clone(CLONE_THREAD)` remains available.

## Results

- Native ARM64 final: **9P/0F/0S**. The four actual worker cases cover BMP, PNG, invalid and empty input. The actual worker reports `NoNewPrivs=1`, `Seccomp=2`, `Seccomp_filters=1` before input.
- An independent disposable probe loads the exact FileCat assembly, invokes the real policy, then tries `/bin/true`; the launch is refused with `EPERM`.
- Native Ubuntu x64 final: **9P/0F/0S**, preserving the established boundary.
- Windows structural run: **5P/4 explicit Linux skips/0F**.
- Complete isolated Release solution build: zero warnings and zero errors.

The native ARM64 environment was Lima 2.2.1/VZ on Apple M1, Ubuntu 24.04.5, kernel 6.8.0-142, SDK 10.0.101/runtime 10.0.1. The exact cloud-image digest and VM configuration are retained. The temporary VM, Lima formula and transfer directory were removed; the Ubuntu x64 `/tmp` source/SDK stage was also removed and independently checked absent.

The first Windows compile exposed an invalid record field name and the first full build omitted solution-wide restore. ARM64 transport also exposed CRLF-vs-LF patch input, and the first x64 run lacked `dotnet` in VMware's noninteractive path. These setup failures remain recorded and are not product results.

## Disposition

`I549-LINUX-ARM64-PICTURE` is **Accepted** as preliminary native remediation evidence. `REL-I08-CONTAINMENT` remains Open for ordinary-user filesystem authority, whole-process memory, remaining parser/Shell boundaries and installed-candidate qualification. `GATE-CI-FINAL` is reopened for exact `445f20dc`; stable publication remains NO-GO.

Primary ABI references are the Linux kernel seccomp documentation, `asm-generic/unistd.h`, `linux/audit.h` and arm64's generic unistd include, linked in the raw summary.
