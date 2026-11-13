# I630 — Per-theme name colors for NTFS-compressed and EFS-encrypted items

**Source:** `ff14b6b2` (parent `42829297`), committed 2026-11-13; no hosted CI has run at it. **Disposition:** implemented preliminarily at source. This is a feature, not the correction of a found defect. No native theme view or installed-candidate check has run.

## User consequence and change

Explorer and Open Salamander show NTFS-compressed names in blue and EFS-encrypted names in green. FileCat drew them like any other item, and only the Attr column's `C` and `E` set them apart. FileCat now colors these names in each theme's own shade, in the file list and in the picture grid. The flags come from the item's Compressed and Encrypted file attributes (`LocalFileSystemProvider.MapFlags`). The Attr column keeps its letters, so color is not the only cue.

A list name takes the first color that applies, in this order:

1. marked;
2. under the active panel's cursor;
3. unavailable;
4. encrypted;
5. compressed;
6. hidden or system;
7. folder;
8. archive;
9. program;
10. ordinary text.

A hidden or system item that is compressed or encrypted keeps the attribute's color, and its icon is dimmed instead. In the picture grid the order is marked, cursor, encrypted, compressed, folder and ordinary text.

| Theme | Compressed (`FcTextCompressed`) | Encrypted (`FcTextEncrypted`) |
|---|---|---|
| Classic | `#0000FF` | `#13850D` |
| Classic Dark | `#6CB2FF` | `#26A65B` |
| High Contrast | `#00FFFF` | `#FF80FF`. Magenta, because this theme's green marks selected items. |
| Cyberpunk | `#4DB2FF` | `#C6FF4D` |
| Psychedelic | `#4CC9F0` | `#B8F25C` |
| Steampunk | `#9CC6E8` | `#A6CF5B` |
| DOS Commander | `#55AAFF` | `#55AA55` |

## Source-level checks

| Test added or extended at `ff14b6b2` | What it requires |
|---|---|
| `AttributeColorTests.Every_theme_draws_compressed_and_encrypted_names_readably_and_apart_from_its_other_name_colors` | In all seven palettes, each new color has at least 4.5:1 contrast on the active and the inactive panel. Each is at least CIE76 ΔE 20 away from the theme's text, folder, archive, program, marked and hidden colors, and the two are at least ΔE 20 apart. |
| `AttributeColorTests.Compressed_and_encrypted_names_take_their_colors_before_hidden_and_type_colors_in_every_theme` | In the headless App, in every theme, the list order above holds for files, folders, archives and programs. Hidden and system items keep the attribute color. Marked, cursor and unavailable rows keep their own colors, and items with neither flag keep their usual colors. |
| `ThemePickerTests.Actionable_text_and_custom_viewer_states_meet_text_contrast_in_every_palette` | Both new resources are now in its panel-contrast table. |

Neither new test skips off Windows. The product commit reports the full App suite passing at medium integrity (2,785 tests), which includes these App tests. Nothing was re-run for this record.

## Limits

- No real compressed or encrypted item has been observed in any theme.
- Contrast is computed only against each theme's panel and inactive-panel colors. It was not measured over the Cyberpunk, Psychedelic and Steampunk effects.
- The picture grid's name colors have no dedicated test.
- ΔE separation is not a color-vision-deficiency check. The Attr column's `C` and `E` remain the cue that does not depend on color.

## Finite candidate check and stop

1. **Hosted CI.** At `ff14b6b2`, or a descendant with the same palettes and name-color code, the producer, Windows x64, Windows ARM64, Ubuntu and macOS jobs pass, including `AttributeColorTests` and the extended `ThemePickerTests` case.
2. **Windows installed candidate.** Use an owned NTFS folder on a Windows edition that supports EFS (Pro, Enterprise or Education). It holds a compressed file and folder, an EFS-encrypted file and folder, a hidden compressed file, and a plain file, folder, archive and program. Check each of the seven themes in the active and the inactive panel, and in the picture grid:
   - Compressed names show the theme's compressed color and encrypted names its encrypted color.
   - Marked, cursor and unavailable rows keep their own colors.
   - The hidden compressed file keeps the color, with its icon dimmed.
   - The Attr column shows `C` and `E`, and High Contrast shows cyan and magenta.
   - Each new color, measured from a capture of the shown name, has at least 4.5:1 against the row background it is drawn on, including where a theme effect is drawn.
3. **Unix candidates.** On one macOS and one Ubuntu candidate, an owned folder of ordinary files shows no attribute colors, as the colors are documented for Windows.

Stop when these pass in the exact candidate and the CI gate is recorded. Reopen only for:

- a changed palette value or name-color order;
- a new theme;
- a demonstrated unreadable or indistinct name;
- an untested mandatory platform.
