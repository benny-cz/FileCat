# Ubuntu baseline and current-source regression checkpoint

Date: 5 November 2026. This completes the previously postponed Ubuntu regression replay of the implemented foundations.
It does not qualify a Linux archive worker/publication adapter or finish the archive-writing plan.

## Source and environment

The user explicitly authorized stopping the existing picture-testing FileCat PID 65891 and image viewers 66892/67247.
Their exact command lines were checked through VMware before SIGTERM; all three exited. No picture fixture files or
the other tester's checkout were changed. Subsequent runs use the usual availability check and a task-owned test lock.

Owned root: `/home/benny/FileCatValidation/archive-writing-42829297`. Current source is a complete git archive of
`79c90a0b` (production code through `63b39dea`), SHA-256
`1B118EBFE69ECFE51D340BEA5D77CBF9ADFF2CD4C6AB32190BAA45782FF8EAD9`, extracted under `src`.
The separate original baseline `4282929753f062f5bb2cc130e8316b133e5c8e03` is under `baseline-src`; its git archive SHA-256
is `F9D886A5D43736EA90EE61827887C46658962E73DA0A28AE7B862E17480B69C1`. Neither source tree shares build outputs.

Ubuntu 24.04, Linux x64, kernel `7.0.0-38-generic`, ext4, user `benny` UID 1000. The guest had runtime 10.0.14 but no SDK.
SDK 10.0.101 was installed only under this task's root. Its 239,025,495-byte Linux x64 bundle was checked against
[Microsoft's release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), SHA-512:

`0443602455d30af51c819d7805e9b45550101c3718acbb3ec5f991e73d18e79b18fb5bc7cdf77bc308936a96275176b23a56e96133e7de83ae60ca2240bc602f`

The SDK muxer is 10.0.1. The private runtime search path includes the existing serviced 10.0.14 runtime by symlink;
a no-op WorkerProbe `exit0` run captured the actual CoreCLR resolution to
`/home/benny/.dotnet/shared/Microsoft.NETCore.App/10.0.14/libcoreclr.so` in `probe-runtime-trace.log`.

The VM address's changed SSH key was independently read through authenticated VMware guest control, then stored in
the task-local known-hosts file. Strict checking remained enabled; the global known-hosts file was not changed.

Restores used the existing locked-package offline feed, supplemented with the already pinned 10.0.14 runtime/apphost
packages. UI dependencies missing from that feed were restored from the official NuGet feed in locked mode into the
task-private package cache. Dependency versions and lock files were not changed. A staging helper initially compared
the raw package checksum with a lock content hash; it was corrected to use NuGet's content hash, which excludes signing
data. The existing metadata matched the lock, and official locked restore succeeded. See
[NuGet's metadata definition](https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File). The earlier missing-feed attempts
were setup failures, not test passes.

## Observed checks

Core filter is the broader archive/edit/journal/job/undo filter in [the journal checkpoint](ARCHIVE_TRANSACTION_JOURNAL.md).
The UI filter is the interrupted-operation/journal filter in [the recovery UI checkpoint](ARCHIVE_RECOVERY_UI.md).

| Source | Suite | Passed | Skipped | Failed | Total | TRX |
|---|---|---:|---:|---:|---:|---|
| Original baseline | Core | 596 | 5 | 0 | 601 | `baseline-results/archive-baseline-ubuntu.trx` |
| Current source | Core | 721 | 5 | 0 | 726 | `results/archive-regression-ubuntu.trx` |
| Original baseline | Headless recovery UI | 240 | 6 | 0 | 246 | `baseline-results/archive-recovery-baseline-ubuntu.trx` |
| Current source | Headless recovery UI | 249 | 6 | 0 | 255 | `results/archive-recovery-ui-ubuntu.trx` |

The five Core skips are the existing opt-in archive benchmark/index-scale controls, elevated-only drag control,
real-mount control and unrecyclable-item fixture. The six UI skips are Windows-specific refusal controls. All 37 new
journal and nine archive-recovery UI cases execute on Ubuntu. These are actual source builds/native filesystem and
headless UI runs; they do not substitute for native desktop/accessibility or installed-package acceptance.

Raw TRX files and SDK facts are copied to the worktree's `.tmp/test-results/archive-writing`; full restore/build/test
logs remain in the owned guest root. SHA-256 of the source-built current artifacts:

| Artifact | SHA-256 |
|---|---|
| Core test output FileCat.Core.dll | `872F820AC3E29D6AC0BF541836B5C87C95E3E402AB0D2EB85787EAB83332A46C` |
| FileCat.Core.Tests.dll | `9C620A52C5463BE63EB59C4CA78E387A1AAD4D636535580426236C46043F6EEC` |
| FileCat.dll | `13D09E6CB69F06F4916F9D790F4B6C17E7D5092F6DDEA92F19E1DA9E6DFE2BE9` |
| FileCat.App.Tests.dll | `03552D00D3DE276048F353E9295D4815CE5266E065D17BD47449C03A6AA98A6E` |

The runs exited. SDK build-server shutdown was followed by pid-bound SIGTERM of five remaining idle MSBuild nodes
whose executable, SDK path and node arguments matched this task's exact private SDK. A final broad process census
found no FileCat, worker, testhost, VSTest or test-run/build-node instances. Other testers can use the guest.

All new write profiles remain refused in production. Native confinement, resource ceilings, reviewed publication,
sidecar IO, provenance, full write workflows, coverage-guided fuzzing and release qualification remain unfinished.
The newly required independent UX/UI/functional/security review takes place after the full implementation/testing loop,
before goal completion and the deferred RAR licensing analysis. This checkpoint is not that final independent review.
