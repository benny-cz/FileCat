# I656 — Linux ARM64 builds open some files and folders with x64 flag values

**Found:** 2026-11-24 by I646's fix ([PR #32](https://github.com/benny-cz/FileCat/pull/32), which takes `O_DIRECTORY` from `LinuxOpenFlags` and noted the other uses), confirmed in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open; to be fixed for rc.3 (the owner's decision of 2026-11-24: every known issue is fixed for the next candidate). No 1.0.0 package is Linux ARM64: rc.2's Linux assets are x64 only.

## Observation

Two of Linux's open flags have other values on ARM64:

| Flag | x64 | ARM64 |
|---|---|---|
| `O_DIRECTORY` | `0x10000` | `0x4000` |
| `O_NOFOLLOW` | `0x20000` | `0x8000` |

On ARM64, `0x10000` is `O_DIRECT` and `0x20000` is `O_LARGEFILE`. `LinuxOpenFlags` (`src/FileCat.Core/FileSystem/LinuxOpenFlags.cs`) gives the right pair for each architecture, and the readers in `UnixFileRecords*.cs` and `UnixHiddenData.Inventory.cs` choose by architecture. These places use the x64 values on every Linux:

| Place | What it opens | On ARM64 |
|---|---|---|
| `SingleInstance.cs:371` | the single-instance file | no `O_NOFOLLOW`: a link swapped in after the path check is followed. The regular-file check on the descriptor still applies. |
| `HtmlPage.cs:205` | a page's resource | no `O_NOFOLLOW`: a final link swapped in after `ResolveLinks` is followed. |
| `UnixAnalysisBackend.cs:46` | the analysis root, resolved before | no `O_NOFOLLOW` |
| `UnixAnalysisBackend.cs:278` | items below the root, through `openat2` | `O_DIRECT` instead of `O_DIRECTORY` for folders. Links are still refused by `RESOLVE_NO_SYMLINKS`. |
| `UnixAnalysisPrivateDirectory.cs:21-22`, `.Export.cs:25-26`, `.Input.cs:24-25` | the analysis's private folders and their files | Folders are opened with `O_DIRECT` and without `O_DIRECTORY`; files are opened without `O_NOFOLLOW`. |

Linux refuses `O_DIRECT` on a folder (`EINVAL`), so on ARM64 the folder opens in the last two rows are likely to fail, and storage analysis with them.

**Not run.** No Linux ARM64 machine was used for this record. The hosted `ubuntu-24.04-arm` runners run only the ARM64 qualification workflows, which do not test these protections. This rests on the source and the kernel's flag tables.

## Remedy (proposed) and its cost

Take `O_DIRECTORY` and `O_NOFOLLOW` from `LinuxOpenFlags.Current` in each of these places, and add a test that fails when a Linux open flag is spelled as a number outside `LinuxOpenFlags`. Before a Linux ARM64 package ships, run the single-instance, page and storage-analysis tests on a native ARM64 runner.

## Fix

On branch `fix/i656-i658-linux-flags-census`, on `rc3/integration-3` at `928ba1c52`, for rc.3. The status above stays until the owner decides.

**Every Linux `O_DIRECTORY` and `O_NOFOLLOW` comes from `LinuxOpenFlags`.** A scan of `src/` (below) found 16 lines that wrote them as numbers: the record's places and two more kinds.
- **The record's places.** `SingleInstance.cs` (`OpenUnixRegularLock`), `HtmlPage.cs` (`OpenResource`), `UnixAnalysisBackend.cs` (the root, and openat2's folders and entries), and the private folders and files in `UnixAnalysisPrivateDirectory.cs`, `.Export.cs` and `.Input.cs`.
- **`O_TMPFILE`, twice.** Analysis staging and scratch (`UnixAnalysisPrivateDirectory.cs`) and an export (`.Export.cs`) wrote `0x410000`, which is x64's `__O_TMPFILE | O_DIRECTORY`. On ARM64 that lacks `O_DIRECTORY`, and the kernel refuses `__O_TMPFILE` without it (`EINVAL`). It is now `0x400000` with the architecture's `O_DIRECTORY`.
- **Four readers that chose by architecture themselves**, correctly: `UnixFileRecords.cs`, `.Details.cs`, `.Ext4.cs` and `UnixHiddenData.Inventory.cs`. They now take `LinuxOpenFlags.Current.NoFollow`; their values on x64 and ARM64 are unchanged. On 32-bit ARM and ppc64le, for which FileCat builds nothing, they now refuse as `LinuxOpenFlags` does, instead of guessing.
- **Unchanged:** the seccomp filters of the archive and picture workers check only open flags that x64 and ARM64 share; the C helper in `FileCat.Permissions.Linux` takes the macros from the headers it is built with.

**A seam per site.** Each Linux open with a flag that differs takes it from a small function given the architecture: `SingleInstance.LinuxLockOpenFlags`, `HtmlPage.LinuxResourceOpenFlags`, `UnixAnalysisBackend.LinuxRootOpenFlags` and `LinuxEntryOpenFlags`, and `UnixAnalysisPrivateDirectory.LinuxDirectoryFlags`, `LinuxFileFlags` and `LinuxTemporaryFileFlags`. Production passes `RuntimeInformation.ProcessArchitecture`, so x64 gets exactly rc.2's values, which the tests pin.

**The guard.** `LinuxOpenFlagPolicyTests` has 27 cases, which run on every host:
- **Both architectures:** `LinuxOpenFlags` has the kernel's values for x64 and ARM64. On x64 each seam returns the number rc.2 wrote. On ARM64 each returns its own values, and none carries `0x10000` or `0x20000`, which ARM64 reads as `O_DIRECT` and `O_LARGEFILE`.
- **The scan** reads every C# file in `src/`, except `ThirdParty`, the Windows platform and `LinuxOpenFlags.cs`. It reports a number with the x64 or ARM64 value of `O_DIRECTORY`, `O_NOFOLLOW` or `O_TMPFILE` in either of two places. One is a chain of flags joined with `|` that also holds Linux's `O_CLOEXEC` (`0x80000`) or a `LinuxOpenFlags` value. The other is the value given to a name that says no-follow. Comments and strings are skipped. A chain with a value beyond Linux's open flags (Darwin's `O_CLOEXEC`, clone flags) is something else.
- **The scan's own cases:** it finds each spelling rc.2 used (8 cases) and 4 other forms of the same mistake. It leaves Darwin's flags, lsattr's flag table, clone flags, `MaxDirectoryClusters = 65536`, `LinuxOpenFlags` users, comments and strings alone (11 cases).
- **What it misses:** a flag added later with `|=`, or a number passed through a name that says neither no-follow nor `O_CLOEXEC`. The seams' tests cover the places it found.

**Behaviour on the host's architecture.** `LinuxOpenFlagBehaviourTests` (Linux only, 4 cases) opens through every place: an analysis root with a folder and a file below it; private storage's folders, a published staging file, a scratch file and a refused link; an export and a chosen inventory, with a refused link; a page resource behind a link. `SingleInstanceMetadataTests` gains 4 cases: the lock file's open refuses a link put in place after the path check (created and existing), and its flags for x64 and ARM64.

**Hosted runs** (dispatched on the throwaway branch `qual/i656-i658`, since deleted). Each job built the base (`928ba1c52`, with the new tests that build against it) and the fix, then ran the classes on both. Final run [45463438305](https://github.com/benny-cz/FileCat/actions/runs/45463438305); the same I656 results in [45461002016](https://github.com/benny-cz/FileCat/actions/runs/45461002016).

| Classes | ARM64 base | ARM64 fix | x64 base | x64 fix |
|---|---|---|---|---|
| `LinuxOpenFlagBehaviourTests` | 4 of 4 fail | 4 pass | 4 pass | 4 pass |
| The lock-link cases | 2 of 2 fail | 2 pass | 2 pass | 2 pass |
| `AnalysisPrivateStorageTests`, `AnalysisScanningTests`, `AnalysisNativeHandleTests`, `AnalysisPrivateRootExclusionTests` | 30 of 102 fail | 101 pass, 1 skip | 101 pass, 1 skip | 101 pass, 1 skip |
| `HtmlPageTests`, `MarkdownResourceIntegrationTests` | 43 pass | 43 pass | 43 pass | 43 pass |
| Details and hidden-data classes, `LinuxHeldDirectoryTests` | 121 pass, 30 skip | the same | the same | the same |
| `LinuxOpenFlagPolicyTests` | (not on the base) | 27 pass | (not on the base) | 27 pass |

- **How ARM64 failed before.** The ARM64 runner is `ubuntu-24.04-arm`, kernel 7.0.0-1017-azure, aarch64.
  - Private storage and exports stopped at their first folder, `/`, with `EINVAL` (`O_DIRECT` on a folder). Analysis stopped at its root's folder probe through openat2 (`entry-metadata-unavailable`, errno 22). The analysis failures were 14 of `AnalysisPrivateStorageTests`, 13 of `AnalysisScanningTests`, 2 of `AnalysisNativeHandleTests` and 1 of `AnalysisPrivateRootExclusionTests`. Each opened a folder or private storage; the cases that passed did not (file roots, handle accounting, stand-in backends). So on Linux ARM64, analysis could not open a folder, and nothing that needs private storage worked.
  - The lock file and the page resource opened through a link: no exception, and a `FileStream` on the link's target.
  - The fix passes them all on ARM64, `O_TMPFILE` included: staging, scratch and export files were created and published.
- **x64 unchanged.** On x64 the base passes the same tests as the fix.

**Other runs.**
- **The scan on the base:** the fix's test build, run beside `src/` taken from `928ba1c52`, fails and names the 16 lines, and only them. On the fix it finds none.
- **Mutations on this workstation**, each caught:
  - `LinuxOpenFlags` giving ARM64 x64's values: 2 policy cases and the lock's ARM64 case fail;
  - `LinuxFileFlags` writing `0x20000`, and `UnixFileRecords.cs` writing `noFollow = 0x20000`: the scan names both lines, and the ARM64 case reports "private file: 0xA0800 instead of 0x88800".
- **Ubuntu 24.04.5 VM** (x64, kernel 7.0.0-43), the committed source, Release: `LinuxOpenFlagPolicyTests`, `LinuxOpenFlagBehaviourTests` and `LinuxHeldDirectoryTests` 35 pass; page classes 43 pass; the four analysis classes 101 pass, 1 skip; details and hidden-data classes 117 pass, 30 skip; `SingleInstanceMetadataTests` and the census classes pass (I658's record).
- **Windows 11 workstation**, at medium integrity, `TEMP=%LOCALAPPDATA%\Temp`: the same Core classes with the Core recovery classes, 536 cases, 0 fail, 92 skip (Linux and macOS cases); the App classes, 130 cases, 0 fail, 44 skip.

**Not verified.**
- **macOS.** Its flags are unchanged and its branches untouched; no Mac was used. Hosted CI runs the macOS classes.
- **The race itself.** The tests open a link directly; nothing swaps one in between the path check and the open.
- **A Linux ARM64 package.** None is built for 1.0.0. These tests ran on a hosted ARM64 runner from a build, not from a package.
