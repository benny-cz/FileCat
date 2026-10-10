# E-I303 — unpublished Registry search ownership

2026-10-10 CEST. I06/V12/V13 preliminary scope. Original source **0b200873f0f26680d490e2d98766f605c54a1ab8**, all **1404 canonical raw Git blobs** independently checked in each of five exports. Final validation declares two product overlays and one regression fixture; dependencies and locks are unchanged. This record establishes a provider registration defect, not a process-memory peak or a candidate qualification.

## Finding and correction

Registry search registered every result set before the user chose Show in panel. Closing the dialog, going to a selected item, or repeating a search left unpublished result sets strongly held by the session provider. At 4096 matches, three searches retained 12,288 members even after dismissal. The per-query 100,000-match limit did not bound the sum of these abandoned searches.

Each search now starts with a private result set. Only Show in panel registers that same object through the existing adoption operation, now public. Registration preserves its identity and any streaming producer. Go to selected still returns the selected original item, and Close still cancels the search. Published tabs, their histories and working-set retention policy are unchanged; no new resource cap is invented.

## Reproduction and validation

Fifteen controls use the real Windows Registry APIs and actual headless overlay, with owned `HKCU\Software\FileCat-Tests\RegistrySearchOwnership-<GUID>` keys and 16, 128 or 4096 binary values. They exercise Close, Go to selected, three searches followed by Close, three searches followed by Show in panel, and one directly published search. They verify the first-300 display limit, complete published membership/identity, unchanged fixture bytes and removal of every owned key. Read-only reflection observes provider registration; it does not mutate product state.

The unchanged product produces **12 ownership failures /three healthy publication passes**. The fix produces **15 passes**, with zero private registrations during search and only the requested final result registered after publication. The same payload passes **full App: 1520 passes /25 explicit skips /zero failures**. All **1530 predecessor outcomes/messages and all 25 exact skips** remain, with only the fifteen added controls. Their normalized ownership observations match the focused run. The affected Core working-set/result-removal/note/search controls add **29 passes**.

Earlier fixture attempts remain immutable: the first stops on a test-only ambiguous Location type before execution; the second fails control lookup before search; the third waits for controls because the deliberately portable headless composition has no Registry adapter. The final fixture registers the real Windows Registry provider in its isolated services and waits for overlay readiness, also surfacing early dialog task faults. All executed earlier cases remove their owned keys. These setup failures are not classified as product ownership failures.

## Limits and next work

This is headless interaction with real native Registry data, without workstation UI or a foreground VM console. Searches complete before the original fifteen controls publish or dismiss. [Committed/native follow-up](E-I303-native-registry-search.md) adds all fifteen exact committed passes and 21 native guest passes, including queued publication/cancellation; [original hosted CI](E-CI-registry-search-ownership.md) checks all four required lanes. Physical native-input acceptance remains separate. Wider I06 aggregate/provider/native/reference/human/candidate work remains open. Physical-source I106/I110 HOLD, owner/freeze/candidate gates and explicit human GO are unchanged.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts preserve complete raw source exports, overlays, commands, payload hashes, original failures, actual TRX rows and cleanup observations.

| File | SHA256 |
|---|---|
| `i303-registry-search-20261010-v1/independent-final-v1.json` | `21a80affb22750e3842f6aa56705622e1b3a6749ae0888dab5c7cb2b32aa3ec5` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/seal-v1.py` | `b8bb493e386742abb18e8faf04402d1d86cfa6019f63ec85451fd84bb974652f` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/run-batch-v1.py` | `b2697eeafa45d8afcc29753c786416b63a8f39beace64028de56783e8cbda28a` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/run-batch-v2.py` | `0054f891515ca4f11abd2aa962986a7f5a2c79be205c4e6310c88e6ca30b9c2d` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/run-batch-v3.py` | `700035d8ae4ccb913099eca4f1fd217671608009b0b48dc39ab37f3d9338fd61` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/run-batch-v4.py` | `6bc7916de4546497f27418703e37723169dcc191f90151d4a17591782f6d79c5` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/run-core-v1.py` | `2112019166ad43efaf2506ac5d5938b7b27d426bb1ae57496fa788937d42886c` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v1/inputs.json` | `65b8cec5077d339e415301e4de510089d93ffe59368113cc9958c6c7cb30f61d` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v1/controls/command.json` | `62dc43ce4b170634e5e6bb3e4dcc91903f5b8010b824aa969cd9c0a13458bb2e` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v2/inputs.json` | `1109f345d35fb5b782507cd1c0db9ed177d279ac99018860eb3428b33b0ace9e` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v2/controls/command.json` | `36554753de02c5216c3ff1c4655810c1f9d4bb0c02a71196945cf19ff1295067` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v3/inputs.json` | `5770f2b997c28efd54cdf16aa309e8f284ebe44f33bc0cc16d9b2f6904ec22b7` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v3/controls/command.json` | `88b7de57538c4437fadb6001ce6dbcbf29ff81c6f87bfc67427804ef7200e1cf` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v4/inputs.json` | `709f1b3b2636394452f0bd52223ca0973c0ec19ed0d41eb2a547c7a6b9feb847` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/original-v4/controls/command.json` | `bb52b8bc727f8ddcc1247066a26e1fe94da14586873d1731b546af23fb1e27c4` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/fixed-v4/inputs.json` | `5936054b80870c93a9c7da7c698fbd4f1432b6cfc69046e10904d3993d3f980f` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/fixed-v4/controls/command.json` | `31e8ca9b48cc4cfc7c6ceacc30e673f034c9b26f04ccc081b39f3cf8ce1f8301` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/fixed-v4/full-app/command.json` | `2ea291affd11de5c65df033ece1338cb41425cf79529610133ca2c2a3427ca52` |
| `E:/FileCat/artifacts/release-evidence/i303-registry-search-20261010-v1/fixed-v4/core-affected/command.json` | `4a948980b35bbaf8f1a91be5e113d93c906eb9ed27f21c0d6e40f95602b63266` |
