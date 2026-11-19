# File-system details with VoiceOver on macOS

The signed Apple silicon native build from `92d52654` exercised an ordinary APFS file on the unlocked Darwin 27.2.0 Mac. The framework-dependent `osx-arm64` bundle ran with .NET 10.0.12. The keyboard fixture selected `alpha-details.txt`, pressed Ctrl+Shift+P and opened **File-system details** from the command search. Native accessibility exposed the file row, command field, report controls and the same file row after Escape.

The muted VoiceOver pass captured actual speech for the selected row, the details window, **Full · observation**, **Summary · observation** and the returned row. VoiceOver briefly reported FileCat as unresponsive before speaking the initial row. The subsequent report controls completed the journey. **Copy all** put 2,017 UTF-8 bytes of full-record metadata on the pasteboard. The clipboard included the captured path and the metadata-only omission, and excluded both the real file payload and the xattr canary. The source file and xattr retained their bytes.

The runner is `tests/FileCat.NativeAcceptance/mac-ax-filesystem-details.py`. The Mac receipts are `~/FileCatValidation/mac-details-92d52654-ax-r3.json`, `~/FileCatValidation/mac-details-92d52654-voiceover.json` and `~/FileCatValidation/mac-details-92d52654-voiceover-r2.json`. The second speech pass used the checked-in runner and confirmed that the display remained unlocked. The tested `FileCat.dll` SHA-256 is `016f9912032868ca97d7db61bd4def97c318a03f62dc0ff9d0324fbd75d4ee58`.

The `--narrow --voiceover` pass verified a native report window of 520 × 420 points on the display reported as Retina. The keyboard reached both tiers and **Copy all**. The 2,167-byte clipboard report preserved the full 236-character selected path and excluded the file and xattr payloads. VoiceOver announced both tiers and the original focused row after Escape. The Mac receipt is `~/FileCatValidation/mac-details-92d52654-narrow-voiceover-r2.json`. Other display scaling, text sizing and picture classification cases remain open.

## Properties shortcut

The `--narrow --properties` run used Option+Return on the focused file and reached FileCat's canonical File-system details window. The native AX bridge observed the report controls and the same file after Escape. In the 520 × 420 point window, **Copy all** published 2,361 bytes of metadata with the original path and the omission notice. The original file and xattr retained their bytes.

The Apple silicon bundle `LinkTargetStatus-53f808d5-r1.app` had `FileCat.dll` SHA-256 `727489632327CD4D992D3394206E3166B8753C2538480014AA122E80EB38999E`. The receipt `mac-details-properties-893e4d0f-r1.json` has SHA-256 `9049007297DC039E24BD9C2A8F745DCB3C02E638981A4007D8AA3A4FF52246A4`.
