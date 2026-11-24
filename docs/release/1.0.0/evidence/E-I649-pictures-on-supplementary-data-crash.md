# I649 — Show pictures on a streams or attributes listing ends FileCat

**Found:** 2026-11-24, first by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (scenario `x02_pictures_on_attributes`), then in the Windows 11 25H2 qualification VM on the installed rc.2 (batch 30). The Mac harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/`). **Status:** open. A fix is proposed for the next candidate, and the owner decides.

## Observation

**macOS.** Package `FileCat-1.0.0-osx-arm64.zip`, SHA-256 `8e14aaed…`, on the physical M1 Mac, macOS 27.2.1. Reproduced six times, on a file with and without an extended attribute.
1. Cursor on a file.
2. File → Inspect → Extended attributes and resource fork. The listing opens in a tab.
3. View → Show pictures; wait about 2 s; open the View menu, or press Ctrl+R.

FileCat ends with SIGABRT, and its log reads:

> `Unhandled exception. System.InvalidOperationException: Supplementary row category is unavailable; refresh required.`
> `at FileCat.Core.HiddenData.HiddenDataProvider.GetItemRef(Location listing, EntryData& entry)`

The file's bytes were unchanged.

**Windows.** Installed rc.2 in the qualification VM:
1. `note.txt` with an alternate data stream `fq.note` (27 bytes).
2. File → Inspect → Streams and attributes.
3. Show or hide pictures, then the View menu and Ctrl+R.

FileCat ended with exit code 0xC000041D. The .NET Runtime event 1026 at 16:52:51 records the same exception and stack:

```
System.InvalidOperationException: Supplementary row category is unavailable; refresh required.
   at FileCat.Core.HiddenData.HiddenDataProvider.GetItemRef(Location listing, EntryData& entry) in …\HiddenDataProvider.cs:line 92
   at FileCat.Core.Listing.ListingModel.GetItemRef(Int32 storeIndex) in …\ListingModel.cs:line 264
   at FileCat.App.Controls.FileListControl.RenderPictureGrid(DrawingContext dc, Rect bounds, Boolean active, Boolean inCloudRoot) in …\FileListControl.Pictures.cs:line 87
   at FileCat.App.Controls.FileListControl.RenderCore(DrawingContext dc) in …\FileListControl.cs:line 1220
```

In the same run, F3 on the stream row opened its viewer ("fq.note — FileCat Viewer"), so the listing itself worked.

## Why it matters

Two ordinary commands, in either order, end FileCat on Windows and macOS. Any open work in the session is lost. The exception is thrown while the picture grid draws, where nothing catches it.

## Remedy (proposed) and its cost

- **The grid.** It should not ask a streams or attributes listing for item references it cannot give. Either refuse Show pictures there with a message, or draw those rows as plain tiles.
- **Drawing in general.** Rendering should not let a provider's "refresh required" exception escape: mark the listing for refresh instead.
- **The command.** Check whether other picture-grid paths reach the same call.

This changes product source, so it reaches users only in a candidate after rc.2.
