# I645 — An elevated FileCat sweeps the ordinary account's temporary folders and follows links there

**Found:** 2026-11-24 by the B12 review of the modes added since B12 (I641, [PR #28](https://github.com/benny-cz/FileCat/pull/28), finding F1), in the source of [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md). `src/` at `main` `f4e42468b` is identical to the tag. **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Observation

At every start, `src/FileCat.App/Services/AppServices.cs:153-158` runs three sweeps of FileCat's temporary folders in the profile:
- `DragStaging.Sweep`;
- `ClipboardVirtualStaging.Sweep`;
- `AppPaths.SweepTemporaryLeftovers`.

None checks the token. So an administrator window, which is the same account with the elevated token and the same profile, runs them as administrator. Elsewhere FileCat already refuses user-profile state at high integrity; for example, it writes no crash log and exports no logs there.

The sweeps follow links:
- `src/FileCat.Core/Operations/DragStaging.cs:109-121` and `:149-157` follow a junction under `temp\drag`, and set every file in its target to `Normal`.
- `src/FileCat.Core/State/AppPaths.cs:264-282` and `src/FileCat.App/Services/ClipboardVirtualStaging.cs:164-179` delete by path through `temp`.

## The review's probe

The review built `FileCat.Core` from the tag. It called these methods from an elevated session, on fixtures it owned in that session's temporary folder, and removed them afterwards. FileCat itself was not run.
- **Junction under `temp\drag`.** Through a planted, backdated junction, a read-only, hidden and system file had its attributes reset to `Normal`.
- **`temp` itself a junction.** The leftover sweep deleted a file in the junction's target.

## Why it matters

A program running as the ordinary account, at medium integrity, can plant such a junction in the account's FileCat temporary folder. The next time the user opens FileCat as administrator, files in folders that only administrators can change have their attributes reset, or are deleted.

**Not run.** The usual opportunistic-lock and object-manager redirection, which would let these deletes reach a file the attacker chooses, was not tried. It is the class of FCSEC-011 and FCSEC-025.

## Remedy (proposed) and its cost

Skip these sweeps in an elevated FileCat, and in a FileCat running as root. Also make them never follow a reparse point or symbolic link, elevated or not. This changes product source, so it reaches users only in a candidate after rc.2.

## Fix

[PR #33](https://github.com/benny-cz/FileCat/pull/33) (branch `fix/i645-temp-sweeps-links`, from `main` `cafc6f65e`); the code is commit `325b22c61`. Not merged, and in no candidate yet.

### What changed

**No sweep when elevated or root.**
- `AppServices.SweepTemporaryFolders` runs the three sweeps only when `IShellServices.IsElevated` is false.
- That is FileCat's one "elevated or root" decision (review finding R-M12), already behind the administrator window's own gates:
  - Windows: a high-integrity token (`WindowsTokenAuthority.IsHighIntegrity`), which also covers SYSTEM, the built-in Administrator and an elevated Backup Operators member;
  - Linux and macOS: an effective user ID of 0 (`Environment.IsPrivilegedProcess`).
- An elevated Windows FileCat leaves nothing there to sweep: it refuses drags out of archives and virtual pastes, and keeps the archive members it opens in memory, or refuses them. The next ordinary FileCat of the profile sweeps.
- Tests can set the decision through `AppServices.CreateForPathsWithStartupAuthority`; nothing else in the composition uses it.

**Never through a link, elevated or not.** The three sweeps now share `src/FileCat.Core/State/TemporarySweep.cs`:
- `temp`, `temp\drag` and `temp\clipboard` must each be a plain folder, not a reparse point or symbolic link. Otherwise that sweep does nothing, and the link stays.
- Each entry is judged by its own attributes and times, as the listing reports them, never by what a link leads to.
- A stale link where a sweep removes things is removed itself. That is a link in place of a staged folder, or one named `member-*` or `nested-*`.
- A stale staged folder is emptied depth first. Each item is checked again just before it is handled, and a link inside is removed itself, never entered.
- No attribute is changed:
  - Windows: each item is opened as itself (`FILE_FLAG_OPEN_REPARSE_POINT`) and deleted through that handle with `FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE`. Where that flag is missing (before Windows 10 1809, or on FAT), the item's own read-only flag is cleared through the same handle first.
  - Linux and macOS: `unlink` and `rmdir` act on the name itself, whatever the file's mode.
- `DragStaging`'s other removals (a refused, failed, cancelled or discarded drag) and the virtual-paste cleanup use the same removal.

### Tests

Run on this development host (Windows 11 build 26320, Debug build), at medium integrity (`whoami /groups`: Medium Mandatory Level), with `TEMP` set to `%LOCALAPPDATA%\Temp`. On Windows the links are junctions, which a program at medium integrity can make.

| Tests | rc.2 source with the new tests | Fixed source |
|---|---|---|
| Core `TemporarySweepLinkTests` (drag and leftover sweeps) | 6 of 6 fail | 6 of 6 pass |
| App `TemporarySweepTests`, clipboard sweep | 4 of 4 fail | 4 of 4 pass |
| App `TemporarySweepTests`, the elevated-or-root gate | does not compile (no seam) | passes; fails with the gate removed (mutation) |
| App `TemporarySweepTests`, the page view without a data folder | — | passes |
| Core `AppPathsOwnFolderTests` | — | skipped here (Linux and macOS only) |

How the rc.2 source failed:
- **Junction in place of a staged drag folder:** the read-only, hidden and system file beyond it became `Normal`, as the review's probe found.
- **`temp\drag` or `temp` a junction:** a day-old folder in the junction's target was deleted.
- **`temp` a junction:** the leftover sweep deleted the target's `member-*` and `nested-*` files and returned 2, as the review's probe found.
- **`temp\clipboard` or `temp` a junction:** the clipboard sweep deleted the target's writable files and stopped at the read-only one.
- **A stale staged folder holding a junction:** .NET's recursive delete called `DeleteVolumeMountPoint` on the junction, which fails at medium integrity, so the folder stayed.
- **A stale link in place of a staged clipboard folder, or named as a leftover:** it stayed; the fix removes it.

With the fix, on the same host and at the same integrity:
- **Core:** the classes of the changed code ran 75 tests, with none failed and 13 skipped. The skips are the EFS and elevated fixtures, and the Linux and macOS cases. The classes are `TemporarySweepLinkTests`, `AppPathsOwnFolderTests`, `DragStagingTests`, `DragStagingByteBudgetTests`, `DragStagingDeclaredBudgetTests`, `PathAndStateTests`, `ArchiveEfsSpoolTests` and `RecoveryJobTests`.
- **App:** `TemporarySweepTests` ran 6 tests, with none failed. The 38 classes that stage drags or pastes or open the viewer ran 376, with none failed and 8 skipped (administrator-VM and other fixture cases).

Two mutations of the fixed source, each built and run alone, were caught:
- **Gate removed,** so that `SweepTemporaryFolders` sweeps whatever it is told: the gate test fails, because the "elevated" composition swept all three leftovers.
- **Link check off,** so that `IsPlainFolder` ignores reparse points: 4 of the 6 Core link tests and 3 of the 6 App tests fail.

**Not run here:** the Linux and macOS cases (symbolic links, `AppPathsOwnFolderTests`, the page view on macOS), which hosted CI runs; FileCat itself, elevated or not; any race.

### Neighbours reviewed

The other writes an elevated or root FileCat makes in the profile at start or exit, and two it makes on use that the review named:

| Write | Elevated Windows FileCat | Root FileCat (Linux, macOS) | Here |
|---|---|---|---|
| Page view data (`cache\webview`): WebView2's user data, WebKit's content rules | Written by an elevated browser process when a page was viewed; nothing kept it from links planted there (inferred, not tried) | Same on macOS; Linux's WebKitGTK keeps nothing | **Fixed:** an elevated or root viewer gives the page view no data folder; Windows and macOS then show the page's source and say why |
| Folder modes at start (`AppPaths.Ensure`, 0700) | — | chmod followed a link among FileCat's folders, so a root FileCat whose HOME is the user's changed whatever the user's programs linked there | **Fixed:** root changes no folder that is a link |
| Icon caches | In memory only (`NativeIconSource`); Shell pictures come from the restricted helper | In memory only | Nothing to fix |
| Settings, workspace, history, working sets | Refused at high integrity (`JsonFileStore`; `WindowsSettingsFile` only below it) | Loaded and saved as root; a link at a file name is not followed (exclusive temporary file, then rename), but a linked folder receives root's files | **Left:** see Residuals |
| Diagnostics and crash logs | Refused at high integrity | Appended as root; the append follows a link at `filecat.log` | **Left:** see Residuals |
| Interrupted-job scan and journal pruning | Skipped at high integrity | Scans the user's journals and prunes finished ones by name | **Left:** see Residuals |
| Edit sessions, hex recovery | Refused at high integrity | Edit sessions are loaded; hex recovery is Windows-only | **Left:** see Residuals |
| Staging (drags, virtual pastes) | Refused at high integrity | Drags are staged in the profile's `temp\drag` on use; virtual pastes are Windows-only | **Partly fixed:** their removal no longer follows links; root's staging is left |
| Folders made at start (`AppPaths.Ensure`, `keys`) | Made, through any link in their parents | Same | **Left:** empty folders only, which FileCat needs to start |
| Instance files (`instances\running-*`, the Unix lock) | Made and removed, with fresh random names | Same | **Left:** in `SingleInstance.cs`, which I646 changes |
| Rolling disk-space history (window open) | Stopped (settings read-only) | Refused (`AnalysisAdmission`) | Nothing to fix |

### Residuals

- **Races in the sweeps.** The checks are made on paths. A process that can change FileCat's temporary folder while a sweep runs could swap a folder it checked for a link before the sweep lists that folder. Deletions on Windows act on a handle to the item itself, so a swap at the last step deletes only that item. With the gate, such a process gains nothing: no elevated or root FileCat sweeps, and an ordinary FileCat sweeps only what its own account could delete anyway. The exception is a data folder that another account can write, such as a portable copy on a shared volume.
- **A root FileCat whose HOME is the user's.** macOS's sudo keeps HOME, as `sudo -E` does on Linux. Such a FileCat still reads and writes the user's settings, history, logs, journals and edit sessions as root, because those guards check only the Windows token. Treating root like an elevated Windows FileCat for profile state would cost root's own settings, logs and journal review where HOME is root's, which is the usual case. That needs the owner's decision, and a separate issue is suggested.
- **Root's staged drags** in the profile's `temp\drag` are not refused; that happens on use, not at start or exit.
- **What a root FileCat leaves in its own temporary folder** (staged drags, and spooled members after a crash) is not swept, since no ordinary FileCat uses root's profile.
- **The root mode check** is made on the path too: a link swapped in after the check is still followed by chmod.

### What the next installed candidate must check

1. **Windows, elevated.** Plant, at medium integrity:
   - `%LOCALAPPDATA%\FileCat\temp\drag\x`: a junction, backdated two days, to a scratch folder holding a read-only, hidden and system file;
   - `temp\clipboard`: a junction to a scratch folder holding a two-day-old subfolder;
   - a two-day-old `temp\member-*` file.

   Open FileCat as administrator. Both scratch folders, their files and their attributes stay as they were, and so do the three planted items.
2. **Windows, ordinary.** Then start FileCat normally:
   - the `drag\x` junction is removed itself, and its target stays as it was;
   - the `clipboard` junction and its target stay;
   - the plain `member-*` file is removed.
3. **Elevated viewer.** In the administrator window, an `.html` or `.md` file shows its source with the reason, and `cache\webview` is not written.
4. **Hosted CI.** Linux and macOS run the symbolic-link cases and `AppPathsOwnFolderTests`, which this host could not run.
5. **Linux or macOS as root** with the user's HOME, if the owner wants it covered: a linked `temp/drag` is not swept, and a linked FileCat folder keeps its target's mode.
