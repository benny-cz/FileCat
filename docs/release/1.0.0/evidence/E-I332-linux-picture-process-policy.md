# E-I332 — Linux x64 picture-worker process creation

2026-10-11 CEST. Baseline **8241462341c46c87ffd62df6f72ca280de092e93 /1519 canonical raw Git blobs**; declared test/product overlays retain their own inputs and payloads. This finding belongs to the broader open I08 boundary, rather than claiming whole Unix containment.

## Finding and correction

Four actual baseline FileCat workers block waiting for input with no seccomp filter and no no-new-privileges flag. An ordinary-user private observer confirms successful normal and detached-session program launches from both the main and a pre-existing runtime thread. The previously corrected attached-live-child stop path cannot contain a decoder descendant after root exit or detachment. No malicious image exploit is alleged or used.

The Linux x64 worker now installs a process-creation filter **before reading encoded input or entering native Skia decoding**. It checks the syscall ABI, refuses fork/vfork/execve/execveat and non-thread clone, and answers clone3 with ENOSYS so libc can use the checked thread-only clone path. TSYNC applies the filter to existing runtime threads. Any nonzero installation result refuses decoding. The parent process keeps its existing state. This restricts direct worker syscalls; inherited filesystem/network/UID authority, indirect influence on other processes, memory ceilings and parent-death behavior remain separate scopes. [Kernel documentation](https://www.kernel.org/doc/html/latest/userspace-api/seccomp_filter.html) explicitly distinguishes syscall filtering from a complete sandbox.

## Native result and regressions

[Exact committed/native follow-up](E-I332-native-qualification.md) qualifies dcd4f8e /1523 raw blobs/no overlays: all six native controls, every 120 predecessor outcome/message/two exact skips, complete bytes and 294 staged/runtime checks remain. Original hosted run 38094053882 collects independently.

All **four new permanent controls pass in Ubuntu 26.04.1 /Linux 7.0.0-38 /glibc 2.43 /UID 1000 /.NET 10.0.12**. The actual worker has a new filter on every observed thread before input is supplied. BMP and PNG return every independently checked 3×2 BGRA pixel; invalid bytes and EOF return bounded refusals. The private observer loads the actual compiled production policy: normal/detached-session starts on the main/pre-existing thread and direct exec calls are refused; new managed threads and a task remain healthy. These are controlled native calls, not an image exploit.

A second observer forces PR_SET_NO_NEW_PRIVS to fail inside its own process, then invokes the actual FileCat entry point. The baseline decodes all 78 input bytes; the fixed entry returns its bounded policy error with **all 78 bytes still unread**, observed through FIONREAD. Both observers identify the same actual FileCat module as the permanent-worker campaign.

All **120 predecessor outcomes/messages** remain: **58 host passes/two exact existing Windows Unix-only skips** and **60 Ubuntu picture passes**, including existing pixel/orientation/scaling/admission/lifetime controls. The four new Linux-only tests are explicitly skipped on Windows. Fresh independent postchecks verify **194 product/observer files, eight refusal-observer files and 193 reused runtime files**, with no owned payload process or test-temp tree. No workstation UI, guest package/system setting or physical source is changed.

## Original adverse observations and alternatives

The original refusal controller misread a Windows manifest path on Linux and stopped **before invoking FileCat**; its exception, native exit and retrieved bytes remain failed at v1. The corrected fresh v2 controller passes without changing any safety oracle.

Earlier feasibility probes remain distinct. The first Mac C source transfer refuses its hash before compilation; the next fails against an incompatible SDK/linker. A fresh ordinary-user C control built with an explicitly selected existing SDK refuses fork/spawn and allows a pthread. Its actual SDK declares sandbox_init deprecated and “No longer supported”; **no Mac implementation is shipped from that probe**. All three owned Mac probe roots are retrieved, rehashed and removed; no persistent Mac setting changes. The first Ubuntu C probe has no native result/exception trace; a later native observation confirms the compiler paths absent, without inventing the original stack. An independent .NET-only Linux prototype then demonstrates all-thread refusal and healthy runtime threads. Prototype results do not qualify FileCat; the actual-product controls above do.

Linux ARM64 and macOS process policy, wider native parser integration, direct/indirect permission boundaries, exited-root/parent-death containment and installed-candidate qualification remain. I08 stays open. No owner risk acceptance, contract freeze, candidate or publication is claimed.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records preserve exact inputs, commands, raw failures/skips, native observations and independent cleanup.

| File | SHA256 |
|---|---|
| `i332-linux-picture-process-policy-20261011-v1/independent-remediation-v1.json` | `aafa466eabd8719371dae19f07bb923642bf2e5e0e03931b50e2ea322f6003fc` |
| `i332-linux-picture-process-policy-20261011-v1/retained-tool-sources-v1.json` | `a16c488f596469ab71a2b06b32e58e465d521daafbd06b8ae5c1f1cfdcc086ed` |
| `i08-process-policy-feasibility-20261011-v1/independent-feasibility-v1.json` | `f52f70d9ab4834454b0c49a67aface08ab8a80e6f76c4a5e3b43c9288a8f1ff9` |
