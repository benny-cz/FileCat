# E-I676 — The picture grid's rendering test gave the first thumbnails less time than the decoder allows

2026-11-25. Found by PR #60's full-matrix run 45662381953 on `windows-11-arm (Remote and Rendering tests)`.

## Finding

- **Failed test:** `PictureGridRenderingTests.Grid_renders_mixed_items_and_decoded_thumbnails_in_a_bounded_viewport` reported "Assert.Equal() Failure: Expected: 2, Actual: 0" after 9.47 s. No thumbnail had been decoded when its 300 rounds ran out.
- **Same runner, next test:** `Large_picture_grid_keeps_visible_thumbnails_across_redraw_and_unchanged_refresh` decoded 65 thumbnails and passed in 11.2 s.
- **No other test failed** in that job: Remote 2,598 passed with 0 failed; Rendering 167 passed with 1 failed.
- **The same test on windows-11-arm, earlier green runs:**

  | Run | Duration |
  |---|---|
  | PR #57's run 45615496168 | 2.24 s |
  | rc.4's tagged run 45641843032 | 2.95 s |
  | PR #59's run 45634525115 | 8.68 s, just inside its budget |

## Cause

The test's budget, not the product. The first thumbnails wait for the decoding workers to start: one restricted worker process per picture, cold on the runner. The product allows one decode `PictureDecoder.Timeout`, which is 30 s. The test allowed the first two about 9.5 s, and the runner's cold starts varied between about 2 s and close to 9 s.

A failed decode would keep its file icon for the control's lifetime, so a faster failure would look the same in this test. The green runs above, which needed up to 8.7 s, point to time rather than failure. The new failure message tells the two apart if it happens again.

## Fix

`fix/i676-picture-grid-wait`, [PR #62](https://github.com/benny-cz/FileCat/pull/62). It changes the test, plus one internal counter in the product.
- The test waits up to the decoder's bound plus 10 s. It still ends as soon as both thumbnails are cached.
- A failure now states:
  - how many thumbnails were kept as icons, that is, failed decodes;
  - how many were pending;
  - how many loads started.
- `FileListControl` gains the internal counter `IconPictureThumbnailCount` for that message.
- No product behaviour changes.

## Tests

`PictureGridRenderingTests` (3 tests) passed three times in a row at medium integrity on Windows x64. PR #62's full matrix includes the ARM64 lane.

## Not covered

- A deliberate rerun loop on windows-11-arm: ARM64 is available only on hosted runners.
