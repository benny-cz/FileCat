# Native million-file qualification with timestamp capture

All six r1252 cases passed: one million real files in wide and 32-level deep shapes on Windows,
Mac and Ubuntu. This qualifies the frozen r1250 product, including native creation/change
timestamp capture. It does not qualify the later main merge or experimental live preview.
[The exact evidence packet](source-manifest-r1265-native-million-timestamps.json) binds cached
binary hashes, source provenance, raw receipts, driver code and post-run process inventories.

Each case exercised native acquisition, verification of every captured path, hierarchy sorting,
charts, inspector, filtered unique accounting and extension grouping. All source files remained
intact. Every case reported actual owner closure, zero remaining shared scratch quota and no
source-close failures. All final process inventories were clear.

| Platform / shape | Acquisition | Hierarchy/charts | Filtered unique | Groups | Peak increment |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows wide | 464.7 s | 184.7 s | 235.7 s | 531.3 s | 175.42 MiB private |
| Windows deep | 1,519.4 s | 197.0 s | 236.2 s | 517.9 s | 204.65 MiB private |
| Mac wide | 153.4 s | 74.7 s | 64.2 s | 406.1 s | 253.66 MiB physical |
| Mac deep | 248.0 s | 74.9 s | 64.8 s | 408.7 s | 236.30 MiB physical |
| Ubuntu wide | 192.1 s | 122.8 s | 135.9 s | 629.1 s | 256.81 MiB private |
| Ubuntu deep | 229.8 s | 145.2 s | 125.9 s | 639.2 s | 232.87 MiB private |

These are case timings and observed memory increments, not a controlled comparative benchmark.
Windows/Linux private memory was sampled every 50 ms. Mac uses the separately validated
`proc_pid_rusage` current footprint and kernel lifetime peak; it is not committed-private memory.
The tests use a 512 MiB reference ceiling. Ubuntu wide exceeded 256 MiB, so the strict 256 MiB
release gate remains open. Mac committed-private, global multi-session memory, first-visible
latency, ten-million paths and the physical desktop/accessibility matrix remain unqualified.

All cases reused retained source trees and verified the existing r1250 guest inputs; no product
rebuild or overlay occurred. Transfers were approximately 8–9 KiB per platform. Windows ran
deep first, then flattened its explicitly owned fixture only after that case completed and
closed. The helper validated containment, rejected reparse ancestors, recorded each rename,
removed only the verified empty directory scaffold and atomically changed the fixture marker.
Its boundary-negative control also passed. The retained Windows fixture is now **wide**.
Mac and Ubuntu retain their separate wide and deep trees.

Public registration remains off. This evidence does not complete W1, the full product plan or
the requested final independent UX/UI/functional/security review.
