# E-I351–I353 — exact committed archive and fixture follow-up

Exact **7e88ef72ac00a11b9d64f95db61d8d2862d4a27d /1606 raw Git blobs /no overlays** passes the archive/source and corrected native fixture follow-up. Archive isolation retains **80 host/native passes and 48 explicit filesystem skips**. Initial-identity coverage retains sixty host and 120 native cases, including the ninety native Core regressions and thirty Windows-adapter controls. Five host identity-fixture controls and three existing Windows regular-image pipe cases pass.

**6806 selected affected records /131 exact skips** retain every original exact predecessor and add 120 passing initial-identity regressions. The selected **348-case App** inventory has 336 passes/twelve exact skips, preserving all 347 predecessors and adding the corrected hidden-data case. Native postchecks independently verify **966 files**; both host scratch roots/twelve files are archived and restored.

The Windows postcheck's first command timed out; a bounded background command also timed out. After the completed native outputs were retrieved/rehashed and their receipts reported no owned test processes, the authorized disposable guest restarted in the background. A second postcheck attempt observed VMware Tools unavailable during boot; the third succeeded after Tools reported running. The original transport failures remain; their initial cause is unproven. Independent-reader v1 stopped before qualification output after selecting an earlier quoted function boundary; v2 corrected the reader only. No tests were rerun for that reader mistake.

[Original committed CI](E-CI-archive-native-fixtures.md) is independently green at this exact source. The original 69cd563 CI failures and earlier full App timeout remain adverse. This is preliminary source qualification, with no host UI, physical-source resumption, contract freeze or candidate/publication authorization.

## Selected immutable receipts

Paths are relative to private FileCatReleaseEvidence. Nested records retain exact source, payloads, commands, failures/skips and restoration.

| File | SHA256 |
|---|---|
| `i351-i353-exact-validation-20261011-v1/independent-final-v1.json` | `35836a3e7c984221fefc1c3b568a62e5877c5a4d098fda1796c8a5aa9bfc6b64` |
| `i351-i353-exact-validation-20261011-v1/retained-tool-sources-v1.json` | `7dd5e4e9dc78656f254bc699b377f745cc1bec21842d0d063ef24d657e21df06` |
