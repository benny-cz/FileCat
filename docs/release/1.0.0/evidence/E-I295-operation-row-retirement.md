# E-I295 — retire removed operation rows

2026-10-10 CEST. Correction base **20f1bdc5ce168c414fb1c9457c0f76b61c9e2059**, with declared App fixture/two product overlays in complete 1371-blob exports. I06/V03/V12 preliminary scope.

The operations panel’s Remove handler removed the visible row and manager history entry directly, but did not remove the OperationCenterViewModel map entry. A live center therefore kept the removed JobViewModel, job and frozen source list. Both single-row and bulk removal now use a common retirement method, clearing the map/row/obsolete selection and preserving live rows and existing summary/manager notifications. Active jobs are refused by the single-row method. No queue limit or aggregate budget changes.

Nine controls drive the real headless OperationsView’s public routed Remove/Clear finished buttons, or retain a live row, with 1/128/4096 real frozen entries and complete owned 32 KiB known bytes/hashes. No executor starts. The owned visual DataContexts are explicitly retired before bounded collection to isolate the center’s ownership; no production private fields are altered to manufacture collection. Three original Remove failures/six clear/live positives become **nine passes**: all removed row/job/source-list graphs collect while their center remains held, and live rows stay reachable. All **41** affected operation/job/exit results pass. Unchanged compiled payloads pass **full App 1407/25 exact skips**, retaining all **1423** preceding outcome/message records and every exact skip. Core production is unchanged by this App correction; no new full Core run is claimed.

The initial fixture compile refusal (ambiguous Location) and the subsequent six-failure visual-retention attempt are preserved. That second attempt did not isolate the center from owned visual contexts and its wrapper expected the wrong count; its actual native six failures/three passes are not relabelled. The corrected original fixture’s three failures/six positives provide the bounded product reproduction. No historical visual-retention cause, actual native input/incidence, process peak, aggregate/native/frame/reference/human/candidate result is inferred. Exact committed/original hosted follow-ups remain. I06 stays open; physical-source HOLD and human GO remain.


## Selected immutable follow-up receipts

Private FileCatReleaseEvidence paths unless absolute. Nested receipts retain complete commands, raw outputs and source identities.

| File | SHA256 |
|---|---|
| `i06-operation-row-retirement-20261010-v3/OperationRowRetirementTests.cs` | `2e59055c989a871f6a77b87101b5b352cbb99ed7cc27fbef572509f5254a3836` |
| `i06-operation-row-retirement-20261010-v3/OperationCenterViewModel.cs` | `24c4b9f2562762b5a8008b3359ff766a53313f21425a21ce9b30a25716bc0082` |
| `i06-operation-row-retirement-20261010-v3/OperationsView.axaml.cs` | `baaf7955570ee4be9252bfaa606e6671ef6c7b048f6bd3a48ffb68afc9a7b6d9` |
| `E:/FileCat/artifacts/release-evidence/i06-operation-row-retirement-20261010-v3/original/app-controls/command.json` | `ec02d718086794d8d055f8e0368f1afce008d1ee63c316e98a62c66382a5d491` |
| `E:/FileCat/artifacts/release-evidence/i06-operation-row-retirement-20261010-v3/fixed/app-controls/command.json` | `6fdd746c1dbf63d240b5ee3cfaf8da391b000e892c0ce4901d45facc8999f4f7` |
| `E:/FileCat/artifacts/release-evidence/i06-operation-row-retirement-20261010-v3/fixed/operations-app-controls/command.json` | `ad7c396ce572eac5063baba1fdd2e20161a2dd8b51b5921127ecd5ed3b1a3efb` |
| `E:/FileCat/artifacts/release-evidence/i06-operation-row-retirement-20261010-v3/fixed/full-app/command.json` | `571f43b788ac857fde86b519b110ac415a8bd8da3178de63be56946ba0c75924` |
| `i06-operation-row-retirement-20261010-v3/seal-operation-row-v1.py` | `33fcb17bfdcf420d55d4bafb6a5e70a1376a9d2eccf0c45c847c5169512fceba` |
| `i06-operation-row-retirement-20261010-v3/independent-operation-row-final-v1.json` | `b00f95858facea94c2bd6d41223461453d99badde06deb625b4189bb440b55ac` |
| `E:/FileCat/artifacts/release-evidence/i06-operation-row-retirement-20261010-v3/owned-temporary-files-v1.zip` | `35053369f5031f4034b48f1f29ba6e8914d56a906aab6a305ad084d7cecc5de6` |
| `E:/FileCat/artifacts/release-evidence/i06-operation-row-retirement-20261010-v1/original/app-controls/command.json` | `a155769bc2fe9bdb4cad53c8d30fd72b0315fdbda64e9b1b9698e638e055ef1f` |
| `E:/FileCat/artifacts/release-evidence/i06-operation-row-retirement-20261010-v2/original/app-controls/command.json` | `2d1682cac2c5b15eaf23c62d2505ac56d5857edef2b685fe9798ef514d9b2822` |
