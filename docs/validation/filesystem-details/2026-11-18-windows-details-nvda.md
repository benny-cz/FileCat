# File-system details with NVDA on Windows

The Windows 11 x64 VM (build 26340) ran the FileCat desktop app as a standard user with NVDA 2026.3.1. With two files in a local NTFS folder, the keyboard selected `alpha-details.txt` and opened **File-system details** with Ctrl+Alt+Enter. At 96 DPI, the resized report measured 504 × 381 DIP in Windows UI Automation.

NVDA spoke the focused file, the details window and the report controls. It announced **Full · observation** after the first full read, **Summary · observation** after returning to summary, and the cached **Full · observation** after switching back. Escape returned keyboard focus and speech to the same file. Before the fix, a fast read could announce the pending status and drop the completion. The report now keeps the pending notice readable and uses the status live region for the outcome.

**Copy all** produced 2,341 UTF-8 bytes of metadata. It included the full selected path and the omission notice. Both the file payload and its alternate-stream canary were absent, as was the other file's name. The fixture verified that the source and stream kept their original bytes.

The runner is `tests/FileCat.NativeAcceptance/win-nvda-filesystem-details.ps1`. Both runs passed with fresh FileCat and NVDA profiles. The raw speech and UI Automation receipts are `.tmp/nvda-result-r7.json` and `.tmp/nvda-result-r8.json`. The tested `FileCat.dll` SHA-256 is `E1EF6500BCCE8B43BE7D86DB8CF3E22EB0B2D3712886C30B2DCDD61A3AD9C5C2`. The Windows VM also passed all 35 focused report, details and picture-view regression tests in `.tmp/filecat-details-regressions-r7.log`. Native Windows high DPI, High Contrast and picture-view accessibility coverage remain open.
