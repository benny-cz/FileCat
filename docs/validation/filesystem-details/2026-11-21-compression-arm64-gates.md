# NTFS compression release gates on Windows on Arm — 21 November 2026

The [x64 compression release](2026-11-21-compression-release-x64.md) passed the C gate set on Windows 11 x64. Windows on Arm kept the `arm64-unqualified` reason, and most C rows said "ARM64 is not enabled". This receipt runs the same native fixtures natively on the hosted `windows-11-arm` runner. Compression stays off on Windows on Arm: N-UI needs a keyboard and screen-reader review, and the hosted desktop gives FileCat's window no keyboard focus ([hosted ARM64 input probes](2026-11-18-ntfs-storage-review-nvda.md#hosted-arm64-foreground-availability)).

## What changed

- **Tests:** eleven native storage tests and the cross-process Cloud Files storage test were gated to the x64 host. They now take the ARM64 qualification opt-in (`FILECAT_TEST_WINDOWS_STORAGE_ARM64_JOBS`) that the other native storage tests already used. Two more executor tests, the capacity and sharing-conflict cases, take it too. x64 behaviour is unchanged.
- **Workflow:** the [ARM64 NTFS workflow](../../../.github/workflows/ntfs-arm64-qualification.yml) gains a `release_gates` input. It prepares the fixtures the x64 runs had on the VM ([fixture log](receipts/2026-11-21-compression-arm64-gates/release-fixtures.txt)), then runs the native storage classes at medium integrity. The fixtures are:
  - a volume with the Cloud Files filter detached ([filter record](receipts/2026-11-21-compression-arm64-gates/release-nofilter-instances.txt));
  - a 256 MB `FCFresh` volume for the capacity case;
  - 512-, 1,024- and 2,048-byte cluster volumes, as VHD images because a VHDX reports 4 KiB physical sectors;
  - a folder-mounted volume;
  - a WOF-backed file ([compact record](receipts/2026-11-21-compression-arm64-gates/release-wof.txt));
  - the EFS opt-in.
- **Teardown fix:** the in-process Cloud Files storage test deleted its evidence folder while the job's journal was still closing. It now retries like other teardown.

## Runs on the hosted `windows-11-arm` runner

| Run | Scope | Result |
|---|---|---|
| [44957815025](https://github.com/benny-cz/FileCat/actions/runs/44957815025) at `d7e2b2a56` | `WindowsStorageTraversalTests`, `WindowsStorageExecutorTests`, `WindowsStorageAdmissionTests`, `WindowsStorageCrashTests`, the in-process and cross-process Cloud Files storage tests | 79 total, 74 passed, 0 failed, 5 skipped ([TRX](receipts/2026-11-21-compression-arm64-gates/native-release-gates.trx), `d4340151…`) |
| Same run, `device_loss` | Detach before, during and after the compression call, and after EFS encrypt and decrypt, on an owned VHDX | 5 of 5 passed ([TRX files and records](receipts/2026-11-21-compression-arm64-gates/)) |
| [44956746693](https://github.com/benny-cz/FileCat/actions/runs/44956746693) at `dce5f61ce`, `commands` | `StorageCommandNativeTests`: the App command and job with real `WindowsStorageTransforms`, both compression and EFS variants, including the refresh and mark fixtures added for the x64 release | 12 of 12 passed ([TRX](receipts/2026-11-21-compression-arm64-gates/native-storage-commands.trx), `1e8c8ea7…`); the recovery-admission suite in the same step passed 15 of 15 |

The release gate run's 23-case native ABI suite and its EFS output, details and recovery suites also passed. So did the earlier ARM64 runs of each.

**Skips:** three device-loss variants (they ran in the `device_loss` step, all passed), the crash child half (it runs only as the parent's child) and the hot-plug disk refusal. The last needs a USB, FireWire or card-reader disk that reports fixed media, which a hosted runner cannot provide.

The portable C fixtures run in CI on Windows on Arm on every push: scheduler claims, call admission, journals, budgets, the review dialog, refresh and the menu wording.

## C gates on Windows on Arm

| Gate | ARM64 evidence |
|---|---|
| T-CAP | Reason matrix in CI. System-volume binding, the no-filter volume and the cross-process Cloud Files refusal ran natively. The hot-plug refusal is not covered. |
| T-TRAV | Deleted cursor children, overlap planning, reparse skips, cancellation and depth limits ran natively. |
| T-LIFE | A held call obeys durable intent and Stop on both sides of admission (native); claim and handoff fixtures in CI. |
| T-REC | Process kills at the three call phases, and evidence owned by a job in another process (native); reconciliation in CI. |
| T-LIMIT | Budgets and history slices in CI; capacity exhaustion ran natively on the `FCFresh` volume. |
| T-REFRESH | Native App command refresh fixtures (12 of 12); coalescing in CI. |
| T-REG | Wording, the Escape audit and menu placement in CI's Windows on Arm App jobs. |
| N-ID | Mount points, small clusters, WOF, sharing conflicts and identity changes ran natively. |
| N-C2 | Capacity, small clusters, mount points and the five device-loss phases here, after the earlier 30 GB dense removal. Hot-plug is not covered. |
| T-UI, N-UI | The headless review fixtures pass in CI. The native keyboard and NVDA review cannot run on the hosted desktop, so compression stays disabled on Windows on Arm. |
