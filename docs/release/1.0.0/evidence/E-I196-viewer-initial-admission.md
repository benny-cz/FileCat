# E-I196 — initial viewer metadata admission and source ownership

Recorded 2026-10-07. **Preliminary remediation; I06 and broader/native/candidate qualification remain unresolved.**
Original product b0da4ff600d5dee39e8224e477521ac4a4240c34; discovery ecfd767aedfc762cbcbb24db78f304949874343a; corrected producer 7a86b4026dd5abfe83ab8ff43431326edff93cf0.

Ordinary F3 opened content on a worker but constructed PagedReader on the UI thread, synchronously reading length and revision. A held metadata call blocked the dispatcher. An exception after registration left an unfinished viewer in the window list and the source undisposed. Opening after scheduler shutdown still queried metadata and showed a window whose initial header task was cancelled.

Seven original controlled cases use an owned 29-byte file and actual FileContentSource/ViewerLauncher/PagedReader. All six held length/revision cases (success, I/O failure, denied access) block UI work until a three-second fixture release; all four failed opens leave the source undisposed and an unfinished registry entry. The stopped-scheduler case also fails and produces one separately retained headless cleanup error. The original TRX therefore contains eight failed raw rows for seven unique case names; the initial runner's seven-row guard fails after saving the receipt. No raw row is discarded.

Ordinary launches now admit initial metadata on the existing bounded per-device interactive queue. The launcher transfers the source only to a shown window, otherwise disposes it after the active callback returns. It checks scheduler shutdown before publishing the window; unadmitted queued work releases its unopened source. Path-based conflict viewing also opens the file on that queue. F3 and dialog callers await the launch. Registration begins when shown and avoids duplicates after hide/show; partial-content notices use the admitted reader length. The synchronous public constructor remains for existing direct/memory callers; this does not prove all direct constructors or provider-open paths asynchronous.

Nine new controls add shutdown while either metadata call is held and rejection after shutdown. They verify UI availability, independent-device progress, no window/source disposal during the hold, metadata call order and thread, exact exception, successful transfer/failed release, unchanged input SHA-256, no late registry entry and hide/show uniqueness. Working and exact clean producer both pass 125 affected App cases without skips, preserving all 116 preceding names/outcomes. Two existing picture-device controls now await admitted windows and cancel the queued third admission at shutdown; held-reader lifetime, healthy-device pixels and unchanged source hashes remain asserted.

The first working build has a retained CS0103 fixture-edit error: a queue assertion was mistakenly inserted into QuickView. Its one coverage mapping exists, while no test DLL/TRX was produced. Fresh v4 corrects it. Two independent-reader mistakes (missing zipfile import; expecting zero files instead of the actual one mapping after that failed build) remain with source and tool-reported metadata; neither changed or reran source/build/test results. Fresh v9 verifies original 1117 and clean 1119 canonical Git blobs/modes/archive members, seven changed product/test paths from discovery, four exact overlay/export stages, 424 payload references (423 successful/baseline executable-stage entries plus the failed build's one mapping), sixty retained private files and 25 raw admission/input/thread/ownership observations. Its process query finds no owned stage executable.

Original [producer CI 37638630916](https://github.com/benny-cz/FileCat/actions/runs/37638630916), attempt 1 at 7a86b40, is pending. Local evidence does not qualify that run or any native desktop/input/human/reference, remote/removable hardware, aggregate containment or exact candidate. Physical-source/USB hold, contract/freeze, candidate and explicit GO/publication gates remain unchanged. No persistent machine setup was changed.

Private `FileCatReleaseEvidence/va196-v1`:

| Retained path | SHA-256 |
|---|---|
| I196-discovery-v2.json | 38d4e67bf29281b9a95a4ea1b45882d7b0b194e2d827d8dd8c2c46347445b1ba |
| baseline-v1/command.json | a26e5a0e2be68f504f973b7c9ad73666bddb5a8e9dfc6048b3bf4ad32585cb27 |
| baseline-v1/results/display.trx | 7835ceac26c25ef90f061228b4fa775d458950f37048d237aa6f8792e20fa1d3 |
| working-v3/command.json | 1e051f8977ecea52b35ebaa61fccdaafb60388895ae4f8ceac9f3a27fc66337d |
| working-v3/stdout.txt | d6989defac624de531277b915c7d61462aa4175093c80db303e570c60f774c84 |
| working-v4/command.json | b713e39dc5c0eedc2515f18e3b1001dc27ca5f0366eeadb207b6c7a6cc186759 |
| working-v4/results/display.trx | 592222da3f6ddc671b82cf28701dde463f4b8c5a5c2078fce43489c6d97abc46 |
| clean-v5/command.json | 467de2d95cc42989574aeed79cfc1068847a574c1f3911270da62b868e038de9 |
| clean-v5/results/clean.trx | 686dcf31b2e9ec35cf3a3cd5129e6ff21c8524d6f93777296bc37ee9f2811853 |
| seal-viewer-admission-v9.py | e30ab6df664af71fc6e1f4aa4df6e1551a90bacd6c753dee17d58b9b6912e1a2 |
| independent-viewer-admission-v9.json | fdb38d2e7b9224e57021e527ce23bc953ac9dedb3e7389a02baee33973685de9 |
| owned-process-absence-v9.json | 85236289daf7ce5075ec283fff5ce627b05d7f5f06e48d43974a3fd754249d44 |
| seal-reader-import-guard-v8.json | 3012626ab367e65c9beec33d5099bbcf81e2665070f9fae35d05f05992528af7 |
| seal-reader-partial-payload-guard-v9.json | 3e69d29122911ab7551d7895027c94c2a95b2a8b04b9d61ee30882aa4d5db377 |
| global-reader-generation-guard-v11.json | 671ed5c60216c3563494504dadb09cb7233bceb3b3bfdb5df6a8c562cf8798de |
