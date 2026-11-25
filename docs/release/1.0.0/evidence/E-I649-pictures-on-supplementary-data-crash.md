# I649 — Show pictures on a streams or attributes listing ends FileCat

**Found:** 2026-11-24, first by the Mac GUI harness rehearsing [`v1.0.0-rc.2`](E-RC2-TAG-20261124.md)'s b-rows (scenario `x02_pictures_on_attributes`), then in the Windows 11 25H2 qualification VM on the installed rc.2 (batch 30). The Mac harness and its results are kept locally in `.tmp/fq-rc2/mac/` (`results/rc2b/`). **Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I649](../FILECAT_1_0_RELEASE_ISSUES.md#i649), merged into main as `a2f983e9a`); first recorded as open. A fix is proposed for the next candidate, and the owner decides.

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

## Fix

Commit `f4381a072` on branch `fix/i649-i653-i654`.

**Behaviour chosen: such rows are drawn as plain tiles.** Show pictures is not refused. Inspect opens the listing in the same tab, and a tab keeps its picture setting. A refusal would therefore not cover the other order: a tab already showing pictures, then Inspect.
- On a file's streams and attributes, the grid shows every row, `..` included, as a plain tile: icon, type badge, name and size.
- It asks for no thumbnail and reads no value, not even a stream named like a picture. A value is read only when asked (F3, F5), as the rows already say ("Not read — F3 to view").
- Show pictures there says why: "Pictures: thumbnail grid in this tab. Streams and attributes are read only when asked (F3), so they show as plain tiles." On macOS and Linux it says "Attributes are …".

**Cause.** The grid asked the listing for every tile's item, `..` included, though only a picture's thumbnail needs one. A streams and attributes listing answers `..` with "Supplementary row category is unavailable; refresh required." So every frame threw. The crash guard keeps FileCat running through five UI exceptions in three seconds and lets the sixth end it. The View menu or Ctrl+R drew enough frames for that.

**What changed**
- **Provider.** `ResourceProvider.ExplainNoThumbnails` says why a place has no thumbnails; the default is null. The streams and attributes provider returns the sentence above.
- **Grid.** `FileListControl.RenderPictureGrid` asks for an item only for a tile that can show a thumbnail: a file named like a picture, not offline, in a place that has thumbnails. It never asks for `..`.
- **Drawing.**
  - Nothing drawn lets a provider's refusal escape. `TabViewModel.ItemForDrawing` serves the grid and the list's metadata columns (`CaptureMetadataContext`), the only other place where drawing asks a provider for an item.
  - When a provider throws for a row (`InvalidOperationException`, `NotSupportedException`, `ArgumentException`, `IOException` or `UnauthorizedAccessException`), the row is drawn without the item: a plain tile, or an unread metadata cell.
  - The listing is marked for refresh: it is read again after the frame, once per visit, so a provider that keeps refusing cannot start a loop of rereads. Ctrl+R still reads it again.
  - The log records only the exception's type, because a provider's message can name a path (I643).
- **The other paths.** I checked every `GetItemRef` caller.
  - Quick view, history, the selection and the commands already skip `..`. A command that throws is reported, not fatal.
  - `PictureBrowseContext.Step` (Space in the viewer) asks only for picture-named files, which the streams and attributes provider can describe.
  - No other provider throws from `GetItemRef`: the archive, ZIP, recovery, result-set and Recycle Bin providers answer `..` as an ordinary row.

**Tests.** `PicturesOnSupplementaryDataTests` holds 4 headless App tests. They run on every platform: the streams and attributes come from a fixture, so no ADS or extended attribute is needed.
1. `Show_pictures_on_streams_and_attributes_draws_plain_tiles_and_FileCat_keeps_running`: the record's order, then the View menu and Ctrl+R.
2. `Streams_and_attributes_opened_in_a_tab_showing_pictures_are_drawn_as_plain_tiles`: the other order.
3. `A_row_its_provider_can_no_longer_describe_is_drawn_plain_and_the_listing_is_read_again_once`: in the grid, and the thumbnail is asked for once the provider can describe the row again.
4. `A_metadata_cell_of_a_row_its_provider_can_no_longer_describe_is_left_unread_and_the_listing_read_again`.

The checks:
- no UI exception, and every tile drawn: the frame finished;
- no thumbnail asked for, and no value read;
- the "plain tiles" notice;
- for tests 3 and 4, exactly one reread.

**Results**, at medium integrity on Windows 11. The TRX files are kept on the development PC, in `%LOCALAPPDATA%\Temp\fc-a0267\`.
- **Before**, against the unchanged product code: all 4 failed. Test 2 met `InvalidOperationException: Supplementary row category is unavailable; refresh required.`; the frames of tests 1 and 3 never finished; test 4 threw from `CaptureMetadataContext`.
  - `before-app.trx`, SHA-256 `8362f5cddbea5f55c53b7b7755215d22da393c14315453900477f10a465dab9d`.
  - The run also held the I653 and I654 tests then written: 10 tests, 10 failed.
  - It ran the tests' first draft. The final tests also redraw the grid after the View menu and Ctrl+R, because an unchanged window draws nothing again. The mutations below prove them.
- **After**: 4 of 4 passed.
  - `after-app.trx`, SHA-256 `e2148f3a31b8a3c8cc739e5fc33d2be70b1de87af87bdada178fd97c2860463c`.
  - The run held the three issues' 16 tests: 16 passed.
- **Mutations** of the final code:
  - Restoring the unconditional `GetItemRef` failed tests 1 to 3.
  - Letting the refusal escape `ItemForDrawing` failed tests 3 and 4.
  - Allowing thumbnails on streams and attributes failed tests 1 and 2, because `cover.png`'s thumbnail was asked for.
  - TRX files: `mutation-a.trx` `d3c7adb551d5151f567dcf7df657b570c48e94174f29ae6f0a8d0bd7b120615f` and `mutation-b.trx` `4ad05bfeaf06f23dd3351e92a7eb47dfcbe855a7bb0fe7ce0b31aaaba273e63a`. Each set also carried an I653 and an I654 mutation.
- **Regression**: the whole App test suite on the three fixes ran 4,003 tests: 3,709 passed, 0 failed, and 294 were skipped as their environment requires (`full-app.trx`, SHA-256 `faec871c6108e203823cdb9b3121827f9d94f598ca619240e361976d8f0a39bc`, 38 min).
- **The grid's drawing**: the Rendering tests' `PictureGridRenderingTests` and `AnalysisRenderingTests` ran 969 tests. 963 passed, 5 were skipped, and 1 failed only with the fixture root I chose (`render-regression.trx`, `b417ebd2b2af836621154fc96e845f5c622bd232615dc28ae2c2654c9e11880e`; see E-I653's Fix).

**Not verified here.** No installed build was run, and the fix was checked headless only. macOS and Linux run these tests only in CI.

**The next installed candidate must check**, on Windows and macOS, on a file with an alternate data stream (Windows) or an extended attribute (macOS):
- The record's sequence in both orders (Inspect, then Show pictures; Show pictures, then Inspect), then the View menu and Ctrl+R, waiting more than 3 s.
- FileCat keeps running and shows the rows as plain tiles with the notice.
- The log has no "Unhandled UI exception".
- F3 on a stream still opens it.
