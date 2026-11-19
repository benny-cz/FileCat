# File-system details with NVDA on Windows

The Windows 11 x64 VM (build 26340) ran the FileCat desktop app as a standard user with NVDA 2026.3.1. With two files in a local NTFS folder, the keyboard selected `alpha-details.txt` and opened **File-system details** with Ctrl+Alt+Enter. At 96 DPI, the resized report measured 504 × 381 DIP in Windows UI Automation.

NVDA spoke the focused file, the details window and the report controls. It announced **Full · observation** after the first full read, **Summary · observation** after returning to summary, and the cached **Full · observation** after switching back. Escape returned keyboard focus and speech to the same file. Before the fix, a fast read could announce the pending status and drop the completion. The report now keeps the pending notice readable and uses the status live region for the outcome.

**Copy all** produced 2,341 UTF-8 bytes of metadata. It included the full selected path and the omission notice. Both the file payload and its alternate-stream canary were absent, as was the other file's name. The fixture verified that the source and stream kept their original bytes.

The runner is `tests/FileCat.NativeAcceptance/win-nvda-filesystem-details.ps1`. Both runs passed with fresh FileCat and NVDA profiles. The raw speech and UI Automation receipts are `.tmp/nvda-result-r7.json` and `.tmp/nvda-result-r8.json`. The tested `FileCat.dll` SHA-256 is `E1EF6500BCCE8B43BE7D86DB8CF3E22EB0B2D3712886C30B2DCDD61A3AD9C5C2`. The Windows VM also passed all 35 focused report, details and picture-view regression tests in `.tmp/filecat-details-regressions-r7.log`.

The updated runner passed in both the standard profile and the `-NarrowHighContrast` case. The latter ran with FileCat’s High Contrast theme in an isolated settings profile and measured 320 × 521 DIP at 96 DPI. NVDA announced both tiers, Copy all produced metadata for the focused file, and Escape returned focus to that file. See `.tmp/nvda-result-r11-standard.json` and `.tmp/nvda-result-r11-contrast.json`. Native Windows high DPI and picture-view accessibility coverage remain open.

## Narrow report from picture view

The Windows 11 x64 VM passed `-NarrowHighContrast -PictureView` with NVDA 2026.3.1. The fixture switched the populated source tab to its picture grid through keyboard command search, then opened the focused file's details with Ctrl+Alt+Enter. In the 320 × 521 DIP report at 96 DPI, it used the keyboard to read the full record, copy it, return to summary and reopen the cached full observation. NVDA announced the tier changes and spoke the picture grid and the same file after Escape. The clipboard contained the focused file's metadata. The distinct main-stream and named-stream payload sentinels were absent, and both source streams still matched their initial values.

The `FileCat.dll` from `26bfcec30aec2715cc35621a74a79cde59e339b1` had SHA-256 `1B1660119E742BFD0063ACF2DF8E9B9348CCA482BFBDED5C4022BD97FEFE5C65`. The receipt `.tmp/filecat-efs-live-166b6897-r1/filesystem-details-contrast-picture-r1.json` has SHA-256 `55C5F65E76EEFDA0F0B07B57AD59E52A60CB3C4626915A873EE01C5ADFFEF29F`. Native Windows high DPI remains open.
