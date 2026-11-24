# I656 — Linux ARM64 builds open some files and folders with x64 flag values

**Found:** 2026-11-24 by I646's fix ([PR #32](https://github.com/benny-cz/FileCat/pull/32), which takes `O_DIRECTORY` from `LinuxOpenFlags` and noted the other uses), confirmed in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md) (`7269ec61`). **Status:** open. No 1.0.0 package is Linux ARM64: rc.2's Linux assets are x64 only.

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
