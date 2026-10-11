# Original I106, I368 and I369 selection CI

2026-10-12 CEST. Three **separate original-push** GitHub Actions CI runs succeeded at their own source identities. This is hosted test/build evidence, not installed-candidate, physical-source or final release qualification.

| Correction | Exact commit / run, attempt 1 | Independently checked outcome |
|---|---|---|
| Windows pre-open GUID comparison [I106](E-I106-preopen-device-identity.md) | `cd19d7cdb794fc5b378ee0d2304aa18d833ed5f4` / `38180903387` | Four required build/test lanes and producer policy succeed; 32,903 Passed, 1,085 explicit NotExecuted, zero failures in 14 actual TRX inventories. |
| Unix selected descriptor binding [I368](E-I368-unix-preopen-device-selection.md) | `c6c7f309a0ed63d94add24da04f199ef9734ac4c` / `38181890980` | Four required build/test lanes and producer policy succeed; 32,909 Passed, 1,091 explicit NotExecuted, zero failures in 14 actual TRX inventories. |
| Selected Windows PnP disk-interface opening [I369](E-I369-cloned-windows-disk-interface.md) | `cc93071eb08d80832ba56eb1e0f09ee8bbcdaf2c` / `38183808053` | Four required build/test lanes and producer policy succeed; 32,909 Passed, 1,091 explicit NotExecuted, zero failures in 14 actual TRX inventories. Every prior I368 case name/outcome/message multiset remains identical. |

For **each** run, the original attempt-1 API metadata, nine job outcomes (five success, four expected publish/package skips), 27 digest-checked artifact archives, 2,762 extracted archive members, 14 raw TRX inventories and all selected case names/outcomes/messages were independently rehashed and reconciled. The I369 audit also compares every case name/outcome/message multiset with the independently sealed I368 run; no predecessor changes are observed. GitHub's raw run-log archive and each retrieval command receipt are retained. The result was not inferred from a later branch state or a new run. The observations and full assets reside under the matching `V:\FileCat\artifacts\release-evidence\ci-i106-preopen-20261012-v1`, `ci-i368-unix-selection-20261012-v1` and `ci-i369-interface-selection-20261012-v1` roots; the audit receipts reside in matching private `FileCatReleaseEvidence` roots.

| Private audit receipt | SHA-256 |
|---|---|
| `ci-i106-preopen-20261012-v1/independent-ci-audit-v1.json` | `e70120a81be3da195cbe2ed7d2502a1585f90a9c4c599236399617267bdf40c3` |
| `ci-i368-unix-selection-20261012-v1/independent-ci-audit-v1.json` | `890459203aae8178f73fe20d5ed81fd7e017bd181aabb2b8b4ab18fcb55d3504` |
| `ci-i369-interface-selection-20261012-v1/independent-ci-audit-v1.json` | `f0981cae569b97c1a9bd36981eb383b7a8ae02e32aa7d8a8b6e959319d3e1f94` |
| `FileCatReleaseWork/i330-20261011/collect-exact-ci-v1.py` | `2e06a74ce46fd35e06388e658b5b2d8ad55864f192cd7c99c4d9ba4ef2866c52` |
| `FileCatReleaseWork/i330-20261011/audit-i369-ci-v1.py` | `b5e5417bf745cf9b711ec586a06465c4c2676f370affb1fccd339a642d8c1494` |

The later [cloned-GUID collision](E-I369-cloned-windows-disk-interface.md) means the successful I106 run did **not** establish safe selection for byte-cloned media. The `cc93071` interface-path correction has separate virtual-device evidence and its own original CI run; neither older run is relabeled as testing that correction. Hosted CI does not replay the virtual clone, actual consent or physical USB. The I106/I110 physical-source/capture HOLD, owner contract, candidate and explicit human GO remain.
