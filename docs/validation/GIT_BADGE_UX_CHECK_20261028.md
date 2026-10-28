# Git badges and panel overlays: 28 October 2026 check

## Result and scope

The one actionable defect was stale Git state after a normal folder reread. A listed folder's watcher and manual Refresh use `ListingChange.RefreshComplete`, while `FileListControl` previously restarted Git only for `ListingChange.Reset`. A tracked file could remain labelled **Git clean** after it changed until navigation or window activation. `FileListControl.OnListingChanged` now starts a cancellable replacement read on refresh completion. The existing badge stays visible until the replacement is ready; the tooltip is cleared when the new state arrives.

The Git glyphs previously had a row accessibility description but no pointer explanation. Hovering a file or folder icon with a known Git state now says **Git clean**, **Git changed**, **Git added**, **Git untracked**, or **Git conflict**. If a verification tip is also present, both labels appear. `DescribeItem` and the icon tip use the same state-to-text mapping. This adds an explanation for the small symbol without adding another column or changing keyboard focus.

## Source and test evidence

| Area | Evidence | Conclusion |
| --- | --- | --- |
| Status collection | `src/FileCat.App/Services/GitStatusReader.cs`: `ReadAsync`, `ReadRepositoriesAsync`, `GitStatusSnapshot.Parse` | Local repository files, immediate child folders and repository folders listed by their parent receive bounded, cancellable snapshots. Ignored items are plain. Rename paths and conflicts are handled. |
| Panel refresh | `src/FileCat.Core/Listing/ListingModel.cs`: `Refresh`, `ApplyResult`; `src/FileCat.App/Controls/FileListControl.cs`: `OnListingChanged`, `RefreshGitStatuses` | A normal reread emits `RefreshComplete`; the prior reset-only trigger was insufficient. The new trigger refreshes a completed reread and generation/cancellation checks prevent an older result replacing a newer location. |
| Painting and explanation | `src/FileCat.App/Controls/FileListControl.cs`: `RenderRow`, `UpdateTip`, `DescribeItem`; `src/FileCat.App/Services/IconProvider.cs`: `GitOverlay`, `ShellOverlayIcon`, `VectorIcons.GitOverlay` | FileCat draws the same ringed, shape-distinct vector badge over native or vector file icons on Windows, Linux and macOS. The Git state is also in the row's accessible description and now in the icon tooltip. |
| Windows Shell overlays | `src/FileCat.App/Services/IconProvider.cs`: `ShellOverlayIcon`; `src/FileCat.App/Services/NativeIconSource.cs`: `GetOverlayIcon` | Windows Shell overlays are available only with native icons, a local file-system row and the restricted Shell helper. A known FileCat Git state wins; a row without one may use the Shell overlay. Linux and macOS do not call this path. |
| Regression | `tests/FileCat.App.Tests/GitBadgeUiTests.cs`; `GitStatusTests.cs` | A real temporary repository verifies clean to changed after a completed panel reread, plus icon tooltip and accessible description. Windows host: new UI case 1/1; status tests 13 passed, 1 environment-specific share capture skipped. |

## Platform interpretation

The badge drawing and UI regression are portable Avalonia code. Automatic Git discovery is deliberately limited to conventional installation paths: Windows Program Files Git, or `/usr/bin/git`/`/bin/git` on Linux and macOS. A machine without usable Git, an unsafe repository, a budget overrun or an ignored item can correctly show no FileCat Git badge. Windows can independently show a third-party Shell overlay for a row with no FileCat Git state. Native icon availability does not gate the FileCat badge because `IconProvider.GetIcon` has a vector fallback.

The Windows host test proves the corrected panel behavior. Cross-platform CI and any isolated native desktop checks are recorded in [implementation status](UX_UI_IMPLEMENTATION_STATUS.md) when complete. A headless run establishes composition and accessibility properties in the Avalonia control; it is not a native pixel or screen-reader acceptance claim for every desktop environment.

## Adequate designs retained and unsupported concerns

- Keep cancellable background reads, generation checks, bounded repository scans, and plain-icon fallback. These avoid blocking browsing and avoid treating an unknown status as clean.
- Keep the Windows Shell helper boundary and the portable glyphs for known Git states. The glyphs use distinct shapes as well as colors.
- Keep no badge for ignored items and repositories refused by safety checks. This is an explicit trust/performance policy, not evidence of a rendering failure.
- A changed file deep inside an unshown subdirectory may not cause the displayed folder's non-recursive filesystem watcher to fire. Window activation or manual Refresh requests a new status snapshot. This check did not establish a high-ROI, safe recursive-watching design, so no recursive watcher was added.
- No claim is made that third-party Shell overlays have identical visuals or availability across Windows installations. Their assignment is controlled by installed Shell extensions, and Linux/macOS have no Windows Shell overlay path.
