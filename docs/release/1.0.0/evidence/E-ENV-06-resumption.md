# E-ENV-06 — execution resumption, 2026-10-02

Read-only preflight facts and preliminary development evidence; no candidate or final qualification.
The four requested authority documents were read in full before proceeding. The launcher-required planning prompt
was also read. The historical plan baseline remains `4f6b062fa8548fc8fd417a50262a72c0b804461f`.

## Source and repository

- Resumption input: clean `main`, equal to `origin/main`, at `08c2e2dee4e04c936a34cd867770d05d758af686`;
  the release records already contain work through I95. This is not a new campaign from the historical baseline.
- Seven existing untracked planning/execution documents were retained unchanged and pushed, with the owner's
  authorization, as `3316f1502a59e166c95de1d920ed2fcfcac72123`.
- [CI 36948530515](https://github.com/benny-cz/FileCat/actions/runs/36948530515), source `08c2e2d`, completed
  successfully: Windows x64, Windows ARM64, Ubuntu and macOS lanes succeeded; all three release package jobs skipped.
  This refresh did not derive a new per-test skip inventory from its TRX files.
- The next run, [36985897644](https://github.com/benny-cz/FileCat/actions/runs/36985897644), source `3316f15`, failed
  in the ARM64 Recycle Bin test (I96). The other three lanes passed. The documentation-only delta cannot establish
  the cause. [36987552356](https://github.com/benny-cz/FileCat/actions/runs/36987552356), source
  `a5a3c0cb68dd0868e8642565dcf49cc93ddecee4`, subsequently passed all four lanes, including the unchanged Recycle Bin
  test. Its three package jobs skipped. See [E-I96](E-I96-recycle-bin-ci.md) for the failure and bounded test remedy.
- Remedy source `55032629283473684b0b48864b8524444d6fa941` (CI 36989092570) and UNC test-fix source
  `ca1afe0db40c89eb8e65bcb30310223f1377a164` (CI 36989898493) subsequently passed all four lanes; package jobs skipped.
- GitHub controls read at resumption: private vulnerability reporting disabled; `main` unprotected; zero rulesets;
  zero environments; immutable releases disabled and not owner-enforced. No settings were changed.

## Available environments

- Host: physical Gigabyte B550M AORUS PRO-P, Windows 11 Pro **Insider Preview** 10.0.26220, 68,659,625,984 bytes RAM.
  The VMware backing drive V: had 275,110,813,696 bytes free at the baseline capture. This is preliminary hardware,
  not the GA W64 or reference V16 qualification machine.
- VMware reports **both guests running**: `V:\Virtual Machines\Barebit\Ubuntu 64-bit\Ubuntu 64-bit.vmx` and
  `V:\Virtual Machines\Jamf\Windows 10 x64\Windows 10 x64.vmx`. Neither was started, reset or reverted by this turn.
- Ubuntu guest authentication works. `/usr/lib/os-release` confirms **Ubuntu 22.04.5 LTS**; this does not supply the
  fresh Ubuntu 24.04/26.04 desktop environments in ENV-04. Server availability has not been revalidated in this turn.
- Windows guest encryption is accepted. The first two supplied guest usernames were rejected; the owner supplied
  the correct local administrator, and guest operations then succeeded. Credentials are not retained in these records.
  Probe: `DESKTOP-A60F1NE`, VMware7,1, 8,588,185,600 bytes RAM, Windows 11 Pro **Insider Preview** 10.0.26300,
  elevated guest operation. One original 200 GiB NVMe virtual disk is the boot/system disk; it was not formatted.
  C: had 104,569,344,000 bytes free. The installed .NET 10 runtime is 10.0.5; no campaign private runtime remained.
  Storage and SMB cmdlets are available.
- The historical Windows cold-boot failure remains relevant, but the immediate stopped-VM gate is cleared by the
  owner's restart. Native ReFS/SMB copy work resumed with a new guarded harness (E-V03-CLONE-1).

## Retained raw records

Local root: `artifacts/release-evidence/resume-20261002-3316f15/`. This is not the owner-controlled immutable REP store.
The initial baseline JSON precedes the owner's corrected Windows login; the later Windows probe supersedes that
access gate. SHA-256:

| File | SHA-256 |
|---|---|
| `baseline.json` | `4f2c3acf4be33cdd0e2afa14945e6989a6c2cb948294755de50ee75329b2d7c8` |
| `ci-36948530515.json` | `c52c75c265d311f6d57af02fa7f3e1ec93f07f3e2acdfb53bce5b8677917870f` |
| `win-probe.json` | `0a65df7d7b9371acb736b168fc916b18b8d4c3cb88c850d835b9062766da2c8c` |
| `win-probe.ps1` | `08c8bf61957f15712e58b9c58331eed55d605fc67baaa7fa381ea19a07722863` |
| `ubuntu-os-release.txt` | `594d5ddd35aedb47f00d9c34d140017907a5b9f93c975aba125fc924daac5c07` |
| `ci-36989092570.json` | `62bceb107535d35c345267fdbef69d2cc36ba3234b98b9484632a674b0489c3c` |
| `ci-36989898493.json` | `46ff6bf455f78d4329f8b0231ee447453aa3b676229e4ebc85d38803437daea1` |

The baseline JSON also retains the SHA-256 of each of the four authority documents. No stable release, tag,
candidate build, signing operation, human GO, package qualification or publication occurred.
