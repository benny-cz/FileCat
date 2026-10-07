# E-I184 — hex-editor copy completion owns current live demand

2026-10-07. Original canonical product 25432ce5ac8c21cd069ad2e8a518bb2755e2280d. Medium invalidated-result and cancellation-lifetime defect; must fix under I06/V04/V10/V11/V12. Remediated preliminarily at 9230f109f22fcd97b1bea32e3ed6ffea1062087f; native/broader/candidate scope remains.

An old whole/range copy overwrites a newer 32-byte copy's clipboard and status. Closing while its actual source read is held makes CopyAsync throw NullReferenceException when it later asks the closed headless editor for its clipboard. Four original adverse controls fail; two ordinary copies pass. Actual native shutdown or desktop clipboard effects are not measured.

Each copy snapshots its clipboard, reader, selected byte range and active text/hex column while the editor is live. It owns a cancellation source, superseded by another copy and canceled by closure or attaching a replacement reader. Copy uses the actual PagedReader cancellation overload, retaining the active synchronous call until return and stopping before later pages. Conversion stays in background work; existing spaced uppercase hex, printable-ASCII text, 1 MiB cap and single-byte fallback remain. Only current live completion on the same reader submits clipboard content or updates status; cancellation ownership clears/disposes after completion. Already submitted asynchronous platform clipboard operations are not recalled; native ordering remains wider qualification.

Six durable complete-editor headless controls cover replace/close/ordinary completion for whole-file and 4096-byte selected copies. Actual ProtectedHexFile, HexPatchOverlay and PagedReader stay in use, with one decorated provider boundary holding a read. A newer cached 32-byte copy finishes while the old call is held. Observations record clipboard lengths/hashes; independent Python computes exact spaced-hex whole/range/new-copy hashes. Tests compare the actual full strings. The owned 262,144-byte source stays unchanged, no source disposal occurs during reads, and canceled work starts no later source read. All corrected additions have no exception, dispose their cancellation source and clear current ownership.

Original: four adverse failures/two ordinary positives. Working and fresh locked committed builds retain the same 83 selected cases: 81 pass and two existing POSIX-only cases explicitly skip on Windows, exactly 77 prior outcomes plus six passing additions. The independent seal verifies 21 retained files, 423 actual payload file references, all 1,091 original canonical blobs/modes and all 1,093 clean committed blobs/modes plus every archive member. Working/committed differences are CRLF only. Clean FileCat.dll SHA-256 bbf233bb61c0403d15729d43bf55a11ceb340c41e13e7087c2db0c2838b8e1e9. Owned fixtures clean up. No product/fixture preflight failure occurs.

Original push CI [37580524539](https://github.com/benny-cz/FileCat/actions/runs/37580524539), attempt 1 at 9230f10 remains pending at this local seal. Headless controls do not qualify native clipboard/shutdown, Save As attachment renewal, source/overlay revision races, text-column/cap/single-byte boundary matrices, provider errors, reference performance, physical sources, human UX or installed candidate behavior. No persistent machine, physical-source, contract, candidate or publication changes.

Private `FileCatReleaseEvidence/hc184-v1`:

| Retained path | SHA-256 |
|---|---|
| baseline-v1/command.json | 889db47cde11dd4033d5ac23c6067374b57c37c5f4c63dd5af643b6f11c51b34 |
| baseline-v1/results/baseline.trx | 2022b383b25d51f07a96bf3694df6a8e2b79c7860754744aec751233681a3b7d |
| working-v2/command.json | bc8ed6d03db580f59726a7e32b32cfeede2a234d9826e54510c24f0cfcaefb74 |
| working-v2/results/baseline.trx | 9c550f96a8dea754543699b62e85247c3347a031ec4f66859aef963822bef313 |
| independent-working-v3.json | 81552173128465537cbe359f3c90fd19ad89fce549447cf6b31e064d213a7d99 |
| clean-v4/command.json | af252e3d17164423a9a076229640b8b101f70afcd72ea5d8f6cff24e11a95c4d |
| clean-v4/results/clean.trx | d6d53ed8686ebd63c62c2dd3e3d5061128a9307acbf78b7c753ecb2a7ac26cc6 |
| independent-hex-copy-v5.json | dd1adcd5a4ddfaf4cf19b336edbc423385aac95c6fbb4146762fc58cd2268d6a |
