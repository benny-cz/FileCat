# I419 — exact pushed-source ZIP warning publication CI

The original push at `26effeb5a305dcda6820b8b67debf68720854c44`, [run 38390574949](https://github.com/benny-cz/FileCat/actions/runs/38390574949), completed successfully. Five required jobs passed and four conditional packaging/preview jobs skipped. This qualifies that source's hosted test matrix; it is not a frozen candidate or stable-release GO.

The official archive at `V:\FileCat\artifacts\release-evidence\ci-i419-green-20261013-v1` retains **27 server-SHA-256-digested ZIP artifacts**, **2,762 individually hashed members** and **14 complete TRX inventories**: **33,112 passed, 1,148 explicit NotExecuted, zero failed**. Its 34-file manifest SHA-256 is `7a4c10a8e1c9d92083f8ec7af60a4bb443fdb56a12bd14e58f98f991883e5646`; audit SHA-256 is `37071dfabfbfe20b2527dc386e1913a8781b912df1b0c0e7a1745e8962b21c0a`.

An independent complete rehash and test-row comparison against the preceding green `dd41c293` run verifies all **34,248 predecessor outcomes, messages and exact skips** unchanged. Each of Windows x64, Windows ARM64, macOS and Ubuntu adds three passing Core cases: two ZIP warning callback changes and one unchanged-source case with unavailable path identity. No other result changed. The separate `ci-i419-comparison-20261013-v1` report SHA-256 is `052e4b06e6d7fb389c285efa4201d3c3add44285e74a2c84e48de1e96ce9fa7c`; its three-file manifest SHA-256 is `fb710ae74375844324bc2a647bf5b30d13358f30507f697c89c9b3733010eedb`.

The paired local/native source-change controls are in [E-I419](E-I419-zip-warning-publication.md). These exact CI results do not lift the I106/I110 physical-source validation HOLD or qualify an installed candidate.
