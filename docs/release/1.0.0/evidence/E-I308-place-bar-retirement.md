# E-I308 — retired Places bar payload ownership

2026-10-10 CEST. **Twelve baseline ownership failures and six live/navigation positives become eighteen passing controls.** The affected App scope passes 193 tests with two existing skips; full App passes 1,587 tests with all 25 predecessor skips unchanged. All 1,594 predecessor names/outcomes/messages remain, plus eighteen new passes. Baseline source is **42cde9ac6ae27bfc782f785352a999c1b33871e3**; the tested correction overlays only PanelView and the new test file. This preliminary record pins those actual source/build assemblies. Exact committed, broader native-platform and original hosted follow-up remains separate; no candidate exists.

## Finding and correction

Removing a panel clears its data context, including when it was already hidden. Its directly built Places children were independent of those bindings, so sixteen or sixty-four owned bookmark buttons and 64×64 icon borrowers could remain. A deferred native-icon notification could also repopulate a context-less attached panel. The first correction clears the child list and guards the context, but eight removal cases still retain controls and icons.

An isolated owned-fixture heap and SOS reader identify strong paths through Avalonia composition visual collections to the old button. The final correction releases Image.Source, content, Place tag and tooltip before dropping the child tree, and removes named Place handlers rather than leaving captured Place delegates. It never disposes shared bitmaps. Context changes rebuild the bar; queued updates refuse a null context. Layout-only hiding retains its live context and resources for reuse.

The final oracle distinguishes retired payload ownership from compositor-control lifetime: removed controls may remain reachable, but their icon and Place payloads must be gone. The actual native controls remain observed while every retired bitmap/payload count reaches zero. This does not claim total compositor/native allocation retirement or a whole-process peak bound.

## Native Windows and byte checks

The disposable Windows 11 Insider 26300 guest runs the same unchanged compiled observer against the original and corrected assemblies. Sixteen controls cover visible/hidden removal, pending notifications, cleared-context/rebind and hidden live reuse at sixteen and sixty-four bookmarks. The baseline retains twelve failures/four live positives; the correction passes all sixteen. The two final-oracle runs complete 128 actual compositor batches. Those batches are not physical input or OS presentation measurements.

Each final-oracle run retains 68 complete bitmap-read hashes, covering 44,564,480 bytes; the independent reader reconstructs the entire known byte pattern and verifies every hash. All 136 reads/89,128,960 bytes verify. Live controls remain readable before/after publication and hiding/restoration. All sixteen owned fixture roots per native attempt are removed; recorded product/observer/private-runtime verification passes before/after, with no owned process left. Payload/results roots remain for campaign custody/final cleanup. No workstation UI, VM console, physical source, account, firewall or persistent setting is changed.

## Preserved failures and limits

The initial default-Headless test run records twelve failures/four positives. The first product correction records eight failures/eight passes; native Windows confirms those eight. The next default-Headless run releases payloads but records two pixel-check failures/fourteen passes. Those pixel observations are retained and are not attributed to product corruption: the final default-Headless oracle checks resource identity/extent/ownership and explicitly claims no persistent pixels. Actual bytes are qualified separately by native Windows. A later test draft refers to an unavailable AvaloniaLocator API and fails compilation before tests; both baseline/corrected build failures remain. Two private native observer compiler failures and one SOS command-syntax refusal are also retained. Fresh versions preserve every earlier source and raw receipt.

Parent main CI **38038745460** completes all four required lanes successfully at 42cde9a; this is a saved job/step status observation, not a newly collected artifact inventory or qualification of the correction. The earlier Git deadline/diagnostic and icon-wait corrections remain [independently qualified](E-I307-git-fixture-command-lifetime.md), and the older failed CI records remain. New correction CI will have its own source/run/artifact identity.

I308 is remediated for this preliminary scope. [I06](E-I06-resource-progress.md) remains open for wider consumers, native/compositor allocation, available platforms and required physical-input/reference/human/exact-candidate acceptance. Physical-source HOLD and explicit human GO remain.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence unless absolute. Nested receipts retain all canonical exports, declared overlays, commands, raw failures/skips, actual hashes and restoration.

| File | SHA256 |
|---|---|
| `i308-place-bar-retirement-20261010-v1/independent-final-v1.json` | `14dc59d657f50586a4cefa11b83954f9d3146fc6efaa68f54be3fbd6cebd4831` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/finding-v1.json` | `b3f406c1e89a6fd82b387a7d01817c8f06bf748e6d3520206c60826f82fe2849` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/seal-v1.py` | `88b8e447b6553b3ff514922ea9175db3a009bfdc66b7dd0e06f01ec205dcdde0` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/PlaceBarRetirementTests-v4.cs` | `f33ec1ec26198ce61408693773d443e81696b75fe5a6fd34dd05a40ac89fb282` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/fixed-v4/inputs.json` | `41199acf5a938cc28a9e6369e25725890399ae1914ae5e76fb800d7abc369c69` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/baseline-v4/inputs.json` | `7a2da903bff43d1b3875b24f53adae702d4fcb69b08c7e82deb4f9a6d5f27da9` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/baseline-v4/controls/command.json` | `acc7f71a418fd335687ae92f4480632680f711c9652826e811b23d259da6be74` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/fixed-v4/controls/command.json` | `bea3795a12ec97c1bc6609b3b858a822f075ad8932600f7b2ea7d4a8e757d2b4` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/fixed-v4/affected/command.json` | `cb40c758fc9d495e73e39763d37891acd09338889b64de4ddfea52c9f3e64ae6` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/fixed-v4/full-app/command.json` | `227b2c9ebd2a5b9e3732f6442ce3d166bca40c09a1a6f5c5d5d5df463d7e5891` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/fixed-v4/full-app/results/results.trx` | `5f6e725ff698a0976fc3ce3e19cc10da55fbca0bd45fa93efa237e048605a7e3` |
| `i308-place-bar-retirement-20261010-v1/native-original-v5/transport-final-v1.json` | `70db242b12c5061d3436f635c4191260d2d78267b531a5370e0159c2ca31b65c` |
| `i308-place-bar-retirement-20261010-v1/native-fixed-v5/transport-final-v1.json` | `b18f699a5daac19dafc783cd23b516453f914860f2102630d8e821b631d3c758` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/native-original-v5/retrieved/places.json` | `6bb0610b9384dd7ae978da73d1ee1541ec8114cb074d30ec4ab0a7c65fd05029` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/native-fixed-v5/retrieved/places.json` | `f157560e61175d1e56e30fd0946864964354bef04be48c7104d835294d59f5a0` |
| `i308-place-bar-retirement-20261010-v1/native-fixed-v3/transport-final-v1.json` | `790ed3cc4254efb89fa9099b8fa7093a81ec7b699e424acdf2c3e2af4880229c` |
| `i308-place-bar-retirement-20261010-v1/roots-final-v1.json` | `62df3f3d3f2b2a8354e345f73404a986e35f5525a45105d61c38a59e0e9f2311` |
| `i308-place-bar-retirement-20261010-v1/roots-path-v2-command.json` | `e04bba7c88eed0d32b629ce21f8fe0abb20a4db4b3dc6e226a8c337df2c6056f` |
| `i308-place-bar-retirement-20261010-v1/roots-path-v2-stdout.txt` | `d3a3500216be19741bc07e41f72b73eb5e0526ac98767d829e7da16ad859728e` |
| `i308-place-bar-retirement-20261010-v1/native-observer-build-v1.json` | `f46cb7feb79ea9cee4689f85733dbc05e7f924580a65d0f23805451a25ce8639` |
| `i308-place-bar-retirement-20261010-v1/native-observer-build-v2.json` | `846047cda45b920cc93e8d528420aed101893cd665b1a99107d1da412a81828e` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/fixed-v2/controls/command.json` | `696055708d2b99ebf9a338b16425efc0ce80f79161bc6fdd005815e40c53d7df` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/fixed-v3/controls/command.json` | `727e976cdf404ace1e329600f9243fd14991851645cc62bd22f5108e80d60fd1` |
| `E:/FileCat/artifacts/release-evidence/i308-place-bar-retirement-20261010-v1/baseline-v3/controls/command.json` | `7b83ad1af700f9245aaf03cc959e3063a4a4ecf15aadb54447b63a16701038f9` |
| `i308-place-bar-retirement-20261010-v1/ci-current-v1-raw.json` | `c6af710708a75fa8f97f8155c799acfcddbba43adeb19a106519410fc9483dd5` |
| `i308-place-bar-retirement-20261010-v1/closed-build-compaction-v1.json` | `10e5e3106409086565f67e48c11834fa93e1f34edc91da7865374e2bfb62a057` |
