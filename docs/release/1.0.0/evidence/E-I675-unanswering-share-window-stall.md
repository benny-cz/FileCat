# E-I675 — A share whose server did not answer stopped FileCat's window for about 21 s

2026-11-25. Found by the release session while it fixed [I672](E-I672-unc-sign-in-enter.md)'s share case, on the installed `v1.0.0-rc.4` (`1.0.0+16f9379a`) in the Windows 11 qualification VM and on the host.

## Finding

The probes used `\\192.0.2.N\share`. 192.0.2.0/24 is TEST-NET-1 (RFC 5737), where nothing answers. Each measurement used an address no earlier probe had used, so that Windows' memory of an unreachable server could not shorten the wait.

- **On the host:** `Directory.Exists` on such a share returned after 21,027 ms. On `\\server\share`, a name no DNS server knows, it returned after 2,694 ms; on a missing share of `\\localhost`, after 7 ms.
- **VM, rc.4, Go to path (Ctrl+L)** to `\\192.0.2.1\share`: the probe sent WM_NULL through `SendMessageTimeout` every 200 ms for 45 s. Before the Go to, the window answered in 12 ms; after it, the window answered only after **21,019 ms**.
- **VM, rc.4, start:** FileCat started with `--left \\192.0.2.9\share` showed its window after **22,037 ms**; with `\\192.0.2.10\share`, after 22,010 ms. Three starts on a local folder around them took 908 to 1,110 ms.
- **Impact:** a NAS that is off, a bookmark or saved start location on it, or a dropped VPN froze FileCat for the connection's timeout. Windows marks such a window as not responding.

## Cause

Three calls on the window's thread opened the path:
- **Go to path** asked whether the typed path is a file (`File.Exists` in `MainWindow.NavigateToText`), to open the file's folder with the file focused.
- **The start arguments** (`--left`, `--right`, locations, and those that a second launch forwards) asked the same in `MainViewModel.OpenLocations`.
- **Every navigation and tab switch** started the folder's watcher (`TabViewModel.StartWatching` → `ChangeMonitor` → `FileSystemWatcher`). The watcher's constructor checks that the folder exists, then opens it.

The listing itself already ran on the device's I/O thread.

## Fix

`74b02d3d` and `599dfe2c`, [PR #61](https://github.com/benny-cz/FileCat/pull/61). The file check (`MainViewModel.OpenPath`, used by Go to path and the start arguments) and the watcher now run on threads of their own (LongRunning, not the pool). The window waits for them only as long as an answering location takes: 200 ms.

- **A local folder, or a share that answers, behaves as before.** The watcher is on before the navigation returns, and a typed or given file opens its folder with the file focused.
- **A location that does not answer no longer holds the window:**
  - The path opens at once as a folder, and its panel shows "Loading… (Esc stops)".
  - If the late answer says "file" and the tab is still there, the file's folder opens with the file focused.
  - A watcher that comes late is taken if the tab still shows that folder, and the folder's time stamp is then checked for a change in between. If the tab moved on, the late watcher is closed, off the window's thread.

A dump of a build with the fix, taken 4 s into a start on such a share, showed the window's thread idle in its message loop. Only the file check, the listing and the watcher's construction were waiting, each on its own worker thread.

## Tests

`UnansweringLocationTests` (App) holds an answer back to stand in for the server:
- **A folder whose watcher is slow to start:** the navigation returns at once, unwatched. The watcher is taken when it comes and reports a new file.
- **A watcher that comes after the tab moved on:** it is closed, not taken.
- **A typed path that is slow to answer:** it opens at once, then as its file's folder with the file focused. A path that answers is still settled before the window goes on.
- **A late answer:** it does not move a tab that went elsewhere.
- **A start location that is slow to answer:** it does not hold the window back.

**Runs** (Windows 11 host, Debug, medium integrity through explorer.exe):

| Code | Result |
|---|---|
| The first four tests, with both waits set to 60 s (the old unbounded behaviour) | 4 of 4 failed: each window stayed blocked for the hold's 30 s limit |
| With the fix | 5 of 5 passed |
| Around them, with the fix | 72 tests passed in `DeniedParentNavigationTests`, `DirectoryDiffTests`, `FolderChurnTests`, `ManyTabsTests`, `MutationPreparationTests`, `NavigationIntentTests` and `FileListSmokeTests`. In the start-argument classes (`StartupArgumentsTests`, `AdministratorActionsTests`, `JournalPlaceTests`, `RecoverySafetyTests`), 57 ran with 0 failed, and 12 were skipped for the environment (Unix only, an administrator token, or VM fixtures). |

## In the VM with the fix

The build was a local merge of the two rc.5 fixes, `1.0.0-dev+633ab423`. It is not a candidate, and it is not ReadyToRun-compiled, so it starts more slowly than a candidate.

| Case | rc.4 | With the fix |
|---|---|---|
| Go to an unreachable share: the window's longest wait to answer | 21,019 ms | 1 ms (`\\192.0.2.11\share`; also 1 ms on the first build of the fix) |
| Start on an unreachable share: window shown after | 22,037 and 22,010 ms | 2,272 and 2,321 ms |
| Start on a local folder: window shown after | 908 to 1,110 ms | 1,952 to 2,004 ms |
| Longest wait in the 25 s after a start | 113 to 128 ms | 217 to 249 ms |
| Tab switches (Ctrl+T, Ctrl+Tab) after starting on such a share | not measured | longest wait 38 ms |

With the fix, the panel showed "Loading… (Esc stops)" while Windows tried the connection, then `\\192.0.2.1\share: The network path was not found.` with `Ctrl+R retries · Backspace goes up`.

## Not covered

- **The candidate's own bytes:** rc.5's Windows qualification repeats the probes on the installed candidate.
- **A server that stops answering after its folder listed:** a few calls still ask that server on the window's thread. These are a new folder's or a rename's name check, and a paste of paths copied in another program. They wait only if the server stops answering after the folder listed. Path suggestions and command-line completion already ran off the window's thread.
- **macOS and Linux:** a hung network mount (an NFS hard mount, say) belongs to the same class. The watcher's fix covers navigation on every platform, because that code is shared, but no probe with a hung mount was run.
