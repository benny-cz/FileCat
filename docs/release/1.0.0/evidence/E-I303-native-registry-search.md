# E-I303-NATIVE — committed Registry search and active publication

2026-10-10 CEST. Fixed source **555d6dad8065ef892378a18095b436b5beecac79**; unchanged baseline **0b200873f0f26680d490e2d98766f605c54a1ab8**. The exact committed export checks all **1406 canonical raw Git blobs**, with no source overlay, and passes all fifteen real-Registry/headless ownership controls. Their normalized observations match the validated working controls and all fifteen owned keys are removed.

## Native Windows controls

The signed-in Admin session of the Windows 11 VMware guest runs the actual native main window and Registry-search overlay, using .NET 10.0.12, x64 and HWND handles. The API reports Windows 10.0.26300. The same independently pinned observer executes 21 cases per product: 16, 128 and 4096 binary Registry values across Close, Go to selected, repeated search/Close, repeated search/Show, direct publication, queued publication and queued cancellation.

The original product yields **15 ownership failures /six healthy publication passes**. The committed fix yields **21 passes /84 compositor completions**. Only the explicitly published result becomes session-owned, including publication while its real producer is queued with zero members: that same result later receives all matches. Closing the queued search cancels it without session registration.

The queued controls use one observer-process worker gate, save its ThreadPool limits, restore them and release the owned blocker. No VM policy changes. All **42 owned HKCU keys are removed**, fixture bytes stay unchanged, all **193 private runtime files** verify before/after, and no owned process remains. The allowlisted guest transfer server and thread stop without firewall changes.

Finite parent/child process samples are retained separately; they do not establish peaks, a total memory budget, physical input or OS-present timing. This closes the affected committed/native/active-publication follow-up, alongside [original hosted CI](E-CI-registry-search-ownership.md). Wider I06, reference/human/candidate work and the physical-source HOLD remain open.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested records retain raw source, commands, payloads, failures, skips and cleanup.

| File | SHA256 |
|---|---|
| `i303-registry-search-20261010-v1/independent-committed-v1.json` | `9b6014f52612608e09e0526e390b7d21197f9bbc69affcbdfa6fbbeb822e7abf` |
| `i303-registry-search-20261010-v1/independent-native-v1.json` | `e48e24f49fd4419c6fb5d98cbd212063f47518493d30cce13afc666c45660620` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/seal-committed-v1.py` | `4a5f1b04e7250841b413ee74921171894046ed11de41c10ca19a9a4cf16626f1` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/seal-native-v1.py` | `b5e73836b278a41507aa73981eaa0d18a8b848bc4f8f980976e17792cc208937` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/committed-v1/inputs.json` | `25e6f5681f0489936718752032aa8d3ed7d84920fb6d71558169ece38cebddc7` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/committed-v1/controls/command.json` | `787834450b3b9a0dd4942868273b608ff54bb0ff9d5e11c5f28e02fa639751d1` |
| `i303-registry-search-20261010-v1/native-original-v1/transport-final-v1.json` | `394f35d8c96b1dfb47ae0756f39eeb8673f116c1bb5faf63b195efecc71ef0de` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/native-original-v1/manifest.json` | `d4cdfdd041bfe57c910dedd0ad00ba8003e31c0ef31cf786d1d3047beadd2d50` |
| `i303-registry-search-20261010-v1/native-fixed-v1/transport-final-v1.json` | `53b7f82592d3084e8d9b76d4d87002a2d564a5b5ce9e2e9177893b0d28371ef4` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/native-fixed-v1/manifest.json` | `bf7902174c0943bc81e9ca814161edf1306ea7c7d21520dba9f5e33e41b955d6` |
