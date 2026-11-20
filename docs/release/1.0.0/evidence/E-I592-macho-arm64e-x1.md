# I592 — ARM64E X1 Mach-O slice was mislabeled

## User consequence and distinct scope

An expert inspecting an owned copy of macOS `/usr/bin/true` in FileCat's Info viewer saw the third universal slice as `ARM64 armv7k`; the summary repeated `ARM64` for two distinct slices. This can mislead architecture and binary-analysis decisions. The file was never executed. The original 133,184-byte fixture SHA-256 was `511602b8cf083bdff74d43242b71c27484bd63121c07f3b59575d35676e1f6cb`, and the sibling target was empty.

Independent `file`, `lipo -detailed_info` and `codesign -dv --verbose=2` identified x86_64, arm64e and arm64e.x1 slices. The installed Apple SDK's `mach/machine.h` defines `CPU_SUBTYPE_ARM64E_X1 = 12` for ARM64; it also defines `CPU_SUBTYPE_ARM_V7K = 12` for a different CPU family. The old mapper matched subtype 12 without considering the CPU family. `codesign --verify --strict` passed for the Apple binary; FileCat itself continued to say signature **Present, not verified**, which is the correct limit of this viewer. The isolated before-state app was stopped.

## Correction and bounded verification

Product `14868053c60eac53aefd3f256b112855d4fd8168` maps subtype 12 under ARM64 to `arm64e.x1`, retaining `armv7k` under ARM32. Universal headings and slice sections now include meaningful subtype names. Pointer-authentication reporting recognizes the X1 slice. A focused thin/universal regression passed **1/1**; the affected Mach-O test filter passed **4P/1 expected Windows skip**.

A fresh Mac checkout at the exact product commit built local package ZIP SHA-256 `4475ccb67ff6eea89af22c70c7f946fd738a676058741680d1a032008c8fb479`; staged App DLL SHA-256 `c1da62a3c7800d4f9c9a1001a0969011b3ee0828dd0434a3423fa8f0a07f77c5`. The extracted bundle passed strict deep code-signature verification. An isolated native FileCat process opened the same file's Info viewer; the accessible structure report now reads `Mach-O universal binary · x86-64, ARM64 (arm64e), ARM64 (arm64e.x1)` and its table identifies the third row `ARM64 arm64e.x1`, while the second remains `ARM64 arm64e`. Independent rehash still matched the fixture oracle; the target had no entries. The exact test process was stopped. This proves one real Apple universal binary path and the affected mapper controls, not every Mach-O variant or installed-candidate acceptance.

## Disposition and finite candidate rule

Medium expert-facing integrity of inspection, **remediated preliminarily**. On the exact installed 1.0.0 candidate, open this three-slice fixture in Info and require three distinct labels, including `arm64e.x1`; verify the fixture hash, no execution, and truthful signature wording. Stop after this affected route and latest-product five-lane CI with skip reconciliation. Public preview.5 predates I592; stable 1.0.0 remains **NO-GO** until candidate gates and explicit owner GO. The active exploratory clean streak resets to **0/20**.
