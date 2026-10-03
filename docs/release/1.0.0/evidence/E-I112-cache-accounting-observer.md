# E-I112 — page-cache stress observer waits for actual load completion

I112/V12/I06. Low severity, validation reliability; no production accounting-policy defect established.
Exact working inputs and full results are retained in [E-I92](E-I92-background-quick-search.md).

The second full Core run fails the concurrent cache test: independently sampled cache pages total 2,162,688
bytes while the shared budget reports 2,097,152. Its observer treats two unchanged budget totals as proof
background loads finished. At a bounded cache limit, new loads and evictions can keep the total constant while
individual readers change; sequential per-reader samples then need not describe the same instant.

A new held-source control explicitly has one pending asynchronous load while budget totals stay equal for
100 ms. The old plateau criterion would infer idle; the completion observer stays pending until the read is
released, then exact cache/ledger equality is verified. `PagedReader.PendingLoads` is an internal locked diagnostic
getter over the existing queued-load set; no cache policy, worker scheduling, limits or accounting operations change.

After all request workers join, the stress test now waits up to thirty seconds for every pending load (including
insertion/trimming) to finish. Zero pages in disposed readers, independent page-sum equality, the unchanged
32-page ceiling and complete release after disposal are still asserted. Seven targeted cache cases pass without
skips; full Core passes 714/760 with 46 declared skips. The original failing full XML/source remain retained in
`cache-observer-before`; the failure does not become a passing historical result.

Targeted cache XML SHA-256 `225f3d2af390a6a947596ca3c2455ac5cd8b6c3ff47cd4a23cf69706d082440f`;
exact source/payload and independent inventory digests are in E-I92. Clean `b7d2e8` passes all four CI lanes and
the elevated Windows 26300 VM's seven cache cases, without skips; exact XML and cleanup independently verify
(E-I92). Test remediation is verified preliminarily; candidate rerun remains open.
I06's decoded-picture/icon/materialized-list budgets remain separate unfinished work.
