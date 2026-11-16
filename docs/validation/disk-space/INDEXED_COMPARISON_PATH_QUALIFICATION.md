# Indexed comparison path stage — 2026-11-16

The internal indexed comparison path stage now matches two immutable datasets without expanding every full path into managed strings. It reuses the memory comparison's captured-root mapping rules, then finds each tagged raw child component under its already matched parent. The correspondence vector and reverse index use authenticated private scratch storage, the existing session/profile quotas, and bounded Background I/O. Raw case, Unicode normalization, encoding tags and invalid native bytes remain distinct. Matching a path does not establish object continuity, complete coverage, a zero baseline or live operation authority.

This is an internal stage for the remaining large indexed comparison implementation. Metric/entity and hard-link matching, freshness/coverage compatibility, delta classification, query/chart/report/persistence integration and complete 1M/10M/resource qualification remain open. Public registration stays off. The final independent UX/UI/functional/security review has not been performed.

Fourteen Core cases cover arbitrary path IDs, both directions, explicit and unmapped roots, unmatched ancestry, case/normalization/encoding/raw-name distinctions, invalid Unix bytes, retained ownership/admission, actual-read cancellation, failed construction and retry, invalid pages, 14,000-entry paging and deep prefixes that the bounded memory comparison refuses. Both source revisions remain retained until actual result work and release finish; no partial index is published after construction failure or cancellation.

One additional native case scans two real fixture trees, each with 306 observations including a hard-link alias, on each platform. After the original dataset leases close, every correspondence is read in both directions from real private storage. Source payloads remain unchanged. This is filesystem/runtime qualification of this internal stage; it does not qualify the full desktop or large indexed comparison.

The r1121 batches completed with zero failures:

| Phase | Windows VM | macOS | Ubuntu VM |
| --- | --- | --- | --- |
| Exact-path preflight | 14/14 | 14/14 | 14/14 |
| Native storage preflight | 1/1 | 1/1 | 1/1 |
| Core | 1,617 pass, 48 existing skips | 1,642 pass, 5 declared platform skips | 1,642 pass, 5 declared platform skips |
| Analysis rendering | 680 pass, 1 existing skip | 681/681 | 681/681 |
| Settings | 35 pass, 1 existing skip | 36/36 | 36/36 |
| Windows native | 75/75 | — | — |

The Core filter also includes all six ZIP listing revision cases affected by the latest main change. All six pass on Windows. Five pass on each Unix platform; the sixth explicitly requires Windows mandatory byte-range locks and is skipped there. That exact additional skip name and reason are retained separately from the earlier four-skip Unix baseline. All other skip names remain unchanged.

Windows used `filecatdsa_r2` through the Interactive/Limited task route. The actual task completed and was unregistered. Every original deployment handle reached a terminal result before collection. All three post-run inventories report zero FileCat/dotnet processes. No host app or test was run. Five initial affected-project builds and the final Core test-only build each completed with zero warnings/errors.

The development attempts are retained:

- r1118 selected zero cases because class and method filters intersected; no product acceptance was inferred.
- r1119 used identical binaries with corrected runner filters. Thirteen of fourteen cases passed. The failed fixture had placed unpaired UTF-16 characters in InlineData, whose discovery transport replaced them. The corrected fixture constructs those code units inside the test and checks the raw inputs differ before matching. Product sources and all product inputs stayed byte-identical.
- r1120 was a host-only staging refusal because the Core test-pair allowlist lacked the existing checkout layout. No native r1120 process launched. The helper now supports that exact complete DLL/PDB pair in either layout, and r1121 used a fresh stage.

Runner-only reruns now refuse every input patch and verify all cached input hashes before making a fresh guest payload. The r1119 archives totaled 27,108 bytes across three machines, compared with the original 53–56 MB per machine. The final Core test-only patches were about 3.6 MB per machine. These changes reduce transfer/preparation work while preserving fresh result directories, process gates and original receipts.

[Exact sources, original failures, final receipts, binary hashes, cache plans and task/process provenance](source-manifest-r1121-comparison-paths.json) define the qualified scope. The completed finalizer is immutable and must not be rerun.
