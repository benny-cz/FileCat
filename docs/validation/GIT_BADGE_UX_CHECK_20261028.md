# Git badges and panel overlays: 28 October 2026 check

## Result and scope

The source search found one Git-specific panel feature: per-row status badges and their refresh/description path in `FileListControl`, plus window-activation refresh in `MainWindow`. There are no separate Git action controls in the panel view models to assess in this check.

The one actionable defect was stale Git state after a normal folder reread. A listed folder's watcher and manual Refresh use `ListingChange.RefreshComplete`, while `FileListControl` previously restarted Git only for `ListingChange.Reset`. A tracked file could remain labelled **Git clean** after it changed until navigation or window activation. `FileListControl.OnListingChanged` now starts a cancellable replacement read on refresh completion. The existing badge stays visible until the replacement is ready; the tooltip is cleared when the new state arrives.

The Git glyphs previously had a row accessibility description but no pointer explanation. Hovering a file or folder icon with a known Git state now says **Git clean**, **Git changed**, **Git added**, **Git untracked**, or **Git conflict**. If a verification tip is also present, both labels appear. `DescribeItem` and the icon tip use the same state-to-text mapping. This adds an explanation for the small symbol without adding another column or changing keyboard focus.

## Source and test evidence

| Area | Evidence | Conclusion |
| --- | --- | --- |
| Status collection | `src/FileCat.App/Services/GitStatusReader.cs`: `ReadAsync`, `ReadRepositoriesAsync`, `GitStatusSnapshot.Parse` | Local repository files, immediate child folders and repository folders listed by their parent receive bounded, cancellable snapshots. Ignored items are plain. Rename paths and conflicts are handled. |
| Panel refresh | `src/FileCat.Core/Listing/ListingModel.cs`: `Refresh`, `ApplyResult`; `src/FileCat.App/Controls/FileListControl.cs`: `OnListingChanged`, `RefreshGitStatuses` | A normal reread emits `RefreshComplete`; the prior reset-only trigger was insufficient. The new trigger refreshes a completed reread and generation/cancellation checks prevent an older result replacing a newer location. |
| Painting and explanation | `src/FileCat.App/Controls/FileListControl.cs`: `RenderRow`, `UpdateTip`, `DescribeItem`; `src/FileCat.App/Services/IconProvider.cs`: `GitOverlay`, `ShellOverlayIcon`, `VectorIcons.GitOverlay` | FileCat draws the same ringed, shape-distinct vector badge over native or vector file icons on Windows, Linux and macOS. The Git state is also in the row's accessible description and now in the icon tooltip. |
| Windows Shell overlays | `src/FileCat.App/Controls/FileListControl.cs`: `RenderRow`; `src/FileCat.App/Services/IconProvider.cs`: `ShellOverlayIcon`; `src/FileCat.App/Services/NativeIconSource.cs`: `GetOverlayIcon` | Windows Shell overlays are queried only for a row with no Git state in a successful snapshot, with native icons enabled, a local file-system row and the restricted Shell helper. A known FileCat Git state wins. No successful snapshot means no Shell overlay request. Linux and macOS do not call this path. |
| Regression | `tests/FileCat.App.Tests/GitBadgeUiTests.cs`; `GitStatusTests.cs` | A real temporary repository verifies clean to changed after a completed panel reread, plus icon tooltip and accessible description. Windows host: new UI case 1/1; status tests 13 passed, 1 environment-specific share capture skipped. |
| Native Windows | [Win11 UIA receipt](evidence/git-badge-win11-native.json), [clean icon](evidence/git-badge-win11-clean.png), [changed icon](evidence/git-badge-win11-changed.png), [exact build](evidence/git-badge-win11-build.json), [runner](../../tests/FileCat.NativeAcceptance/win-ax-git-badges.ps1) | In the isolated VMware guest at 96 DPI, the same visible row changed from **Git clean** to **Git changed** after an on-disk edit and watcher reread. Its captured 16-pixel icon changed in 206 color channels. The exact source commit was `47f09505`, app DLL SHA-256 `D14DED95…546C6E`; no navigation or window activation was used. |

## Platform interpretation

The badge drawing and UI regression are portable Avalonia code. Automatic Git discovery is deliberately limited to conventional installation paths: Windows Program Files Git, or `/usr/bin/git`/`/bin/git` on Linux and macOS. A machine without usable Git, an unsafe repository, a budget overrun or an ignored item can correctly show no FileCat Git badge. Windows may show a third-party Shell overlay for an omitted row only when a successful Git snapshot exists; FileCat does not query one when status is unavailable. Native icon availability does not gate the FileCat badge because `IconProvider.GetIcon` has a vector fallback.

The Windows host test and isolated Win11 run prove the corrected panel behavior and visible Windows glyph change. The [exact implementation CI run](https://github.com/benny-cz/FileCat/actions/runs/40696681900) passed all five lanes on `47f09505`: producer policy, full Windows tests, Windows ARM64 tests and package start, Ubuntu 24.04 portable tests, and macOS 26 portable tests. Those platform App suites include the real-repository panel regression. A headless run establishes composition and accessibility properties in the Avalonia control; it is not a native pixel or screen-reader acceptance claim for every Linux/macOS desktop environment.

## Adequate designs retained and unsupported concerns

- Keep cancellable background reads, generation checks, bounded repository scans, and plain-icon fallback. These avoid blocking browsing and avoid treating an unknown status as clean.
- Keep the Windows Shell helper boundary and the portable glyphs for known Git states. The glyphs use distinct shapes as well as colors.
- Keep no badge for ignored items and repositories refused by safety checks. This is an explicit trust/performance policy, not evidence of a rendering failure.
- A changed file deep inside an unshown subdirectory, or a Git-only index update, may not cause the displayed folder's non-recursive `ChangeMonitor` to fire. Window activation or manual Refresh requests a new status snapshot. This is a source-based limitation, not a demonstrated cross-platform rendering defect. This check did not establish a high-ROI, safe recursive-watching design, so no recursive watcher was added.
- No claim is made that third-party Shell overlays have identical visuals or availability across Windows installations. Their assignment is controlled by installed Shell extensions and FileCat's successful-snapshot gate; Linux/macOS have no Windows Shell overlay path.
- The Shell helper returns a composed bitmap, not an overlay meaning. A third-party Shell mark on an omitted row therefore has no trustworthy state text for FileCat to announce. The check did not invent a Git label for that bitmap.
