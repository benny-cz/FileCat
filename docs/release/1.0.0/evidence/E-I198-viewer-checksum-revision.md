# E-I198 — viewer checksum source-revision ownership

Recorded 2026-10-07. **Preliminary remediation; I06 and broader native/human/reference/candidate scope remain unresolved.**
Original product 94d5af7530a0c871b0fc37b69c887adfdc4d92d3; discovery 91fbf60ee3f3b543a0e986ac21df89dcf53f976d; corrected producer ba05bf2195ecfe3018c6b1cadaadeceff67c3fc6.

A confirmed reader refresh did not retire an in-progress viewer checksum. After a held page call returned, the old calculation replaced the newer status and clipboard. Its whole-file SHA-256 covered an old first half and a changed second half, matching neither on-disk revision. The range case read changed bytes but still published work belonging to the earlier demand.

Four controls use an owned 262144-byte patterned file, actual FileContentSource/PagedReader/ViewerWindow checksum and revision-poll paths, and the explicitly headless clipboard. A source wrapper holds page two while the ordinary reader poll detects a known same-length replacement with a changed timestamp. Both original changed-source cases fail; both unchanged controls pass. An independent Python SHA-256/CRC-32 oracle reconstructs original, replacement, selected-range and mixed bytes. The original whole-file mixed result is SHA-256 9d8bc2ab2d90772cc2f66159725fe66bcb613a9bdd424fd304525384233623df /CRC-32 8a86f78d; all original observations are retained before the product change.

The viewer now cancels its current checksum demand when refresh confirms a source change. An active source call returns safely; PagedReader observes cancellation before another page is read, and the old completion leaves the newer status/clipboard intact. Unchanged work still publishes the exact whole-file or selected-range SHA-256 and CRC-32. This finite correction does not claim immutable snapshots, detect changes not observed by the poll, qualify an already in-flight native clipboard publication, or complete all provider/native/candidate cases.

The first correction retains 140 passes and one read-count failure: the original recorder counted three source reads in one changed whole-file case where its two-read assertion expected two. It had no offsets/thread identities and cannot attribute the extra read. Publication was already retired in both changed cases. The fresh fixture keeps the clipboard/status/lifetime assertions and adds every post-arm read's offset, thread and UI flag; it separately verifies the held checksum thread starts no subsequent page and allows independently classified viewer demand. Working and exact clean producer pass 141 affected App cases without skips, preserving all 137 preceding names/outcomes. All four new cases pass. No original result is replaced.

Independent v7 verifies original 1121 and clean 1123 canonical Git blobs/modes/archive members, exactly two changed paths, all baseline/intermediate/final overlays, 564 actual payload references, thirty retained private files and sixteen raw input/hash/revision/cancellation/read/ownership observations. Its process query finds no owned stage executable. A preparation assumption that the intermediate status still said Computing checksums failed before any mutation; inspection disproved it, and the failed preparation/missing-source tool outputs are retained in the refinement metadata. Automatic approval review rejected the first clean-runner preparation as a possible retained-evidence overwrite before execution; a fresh checksum-specific filename with exclusive creation was approved and used. Existing evidence remains intact.

Original [producer CI 37648735698](https://github.com/benny-cz/FileCat/actions/runs/37648735698), attempt 1 at ba05bf2, is pending. No native desktop/OS clipboard, human, physical/reference or exact-candidate qualification is claimed. No physical source or persistent machine setup changed. Physical-source/USB hold, contract/freeze, candidate and explicit human GO/publication gates remain.

The first global checker selected a prefix inside a quoted guard string and failed compilation on line 9. Its exact truncated fragment and reader hashes are retained; the fresh checker matches a complete statement boundary. No original source/build/test/CI result changed.

Private `FileCatReleaseEvidence/cv198-v1`:

| Retained path | SHA-256 |
|---|---|
| I198-discovery-v2.json | 4848235d31545b673ad10af1846e14e9a48a47dde02f3148e82eb055f93ef710 |
| I198-refinement-v4.json | 7d7c2b3ae3d1a88b544af96f84a676ed473d1824133218dffea92f34399e86be |
| baseline-v1/command.json | ecb57135b39da55b8ba28be7673f3ed4079bfcd5628088f8d862ab1f3a93cc42 |
| baseline-v1/results/app.trx | 35df4309199f7b79f4da787c494ed7abfcb1bade673058ae708a57992d353eb3 |
| working-v3/command.json | 03d694057639b2857abb9c936017217e15d59537c043d381b4086fe4905c87e3 |
| working-v3/results/app.trx | 38a43d8141e2c7e46251a16c7f65cb6ca081ef145ec601ce709213f20eaf9f4e |
| working-v4/command.json | 37fff0e18cf3fc1de8c995344ae1505c14f69ef2ab3d1d4c77200de808ab91dc |
| working-v4/results/app.trx | 339bb4ec2a12d117817055a839fe154185b713251c6c0be78a584ebd2c91f8f2 |
| clean-v6/command.json | c6aaf7f1620db0db6218e087b6387521c57017694c6b85698e3842d0d9a22dad |
| clean-v6/results/app.trx | 412b614d9736d4fe9d50469907c06fcb83da02944ba2a4ffc2370ca7f628cdb0 |
| independent-checksum-observation-reader-v7.py | 0feedd0fe4300bd2dcfbaa7f469a5f7895eac07a2b853ce6c5fd5cdb089257a8 |
| seal-checksum-revision-v7.py | a004c2361e28f6802c5d62033506d1bc03422077eed4dc05c8182632d8f0de88 |
| independent-checksum-revision-v7.json | b28e7eb5e03567f4a0b270c523f54dade0cfa82c381392f05a49a8b396e227dd |
| owned-process-absence-v7.json | 620a6870aa44223464e826cae009c67cc0338e7f4b7fad7216f9ef485459acb1 |
| run-baseline-v1.py | 271f49791605d21277685731ea63c2e7b265618a4bfb3b5c9715a6df644ad10e |
| run-working-v3.py | a91a2b52472c134b5466668287c00b8c897e2c23389c32e71816ce5a8729a525 |
| run-working-v4.py | 96f47665ab05dfa15813539a14849d37159da73d3919004e05299f1a41c5de14 |
| run-checksum-clean-v6.py | 0ab81d10ee101f4f224f105d06b3bb9201532a4328491a49728951b2013aa603 |
| safe-fresh-runner-review-v9.json | d3d603580a0be804e69d4bf548b88722d2e9ce77c844a6715429138476b03d63 |
| global-prefix-fragment-guard-v10.json | 2bd945ac870ca963801c5455d4b66f854cf9083c540c80e83c4ebfdc260e1e7b |
