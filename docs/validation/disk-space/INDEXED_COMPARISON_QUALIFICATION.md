# Indexed two-input comparison — 2026-11-16

The internal indexed comparison now builds paged results for File size, Unique file size, On disk and Files. It retains both immutable datasets, uses the exact captured-root/raw-component path index first, then matches metric entities through private forward/incoming edge indexes. Repeated aliases do not inflate edge degrees. Split/merge or multiple incoming relationships remain separate unknown rows, without a zero baseline. A historical removed row remains inert data.

Identity/version fallback requires compatible captured host/profile/root-object/connection context, mapped fresh observations, a unique current identity/version candidate and one eligible earlier candidate with exactly equal metadata. Explicit structural mapping cannot upgrade identity evidence. Metadata equality is exact: uniform cohorts use bounded field captures; nonuniform cohorts use sorted, deduplicated sets over retained observations. Neither matching timestamps nor unchanged metadata claims unchanged content.

Compatibility checks cover policy fingerprint, mapped active roots, raw-name representations, component/basis/inclusion signatures, freshness and complete namespace/enumeration/issue/stale-scope evidence. Basis hashes only locate candidates; actual signature equality is checked before equality or compaction. An empty file scope may use its declared compatible basis, while directory allocation signatures remain independently checked. Missing prior coverage yields Previously unknown, and incomplete current coverage prevents a removal claim.

Rows retain exact signed UInt128 deltas and distinguish zero from missing. The default index ranks absolute magnitude before percentages; ties use stable derived row IDs. Compatible identity/path movement and hard-link common-parent attribution remain distinct from growth. Known side values survive limited basis comparisons while their combined total/delta remains unavailable. Scalar pages defer representative-path and directory-time details; a separate bounded side read and independently paged memberships preserve large raw names without combining two large detail payloads into one page.

Exact compaction in the existing external sorter is opt-in. Only equal binary keys reach the trusted equality callback, and every input is accounted for as either retained or explicitly equivalent. General graph/member/identity indexes keep their previous noncompacting behavior. Hash collisions cannot erase distinct basis signatures. Callback failure closes temporary owners before retry.

The new matrix includes 70 Core comparison cases, two exact-compaction cases and four native two-input cases. It checks all metrics against the bounded memory comparison across growth/replacement/addition/removal, alias changes/splits/merges, policy/basis changes, partial/unknown coverage, qualified and unqualified identity context, exact nonuniform metadata sets, full-range unsigned values with signed deltas, large-name detail deferral, 14,000-entry/cohort paging, retained input lifetimes, failure/cancellation and retry. Native cases scan two real 306-observation fixtures including hard links, close their original owners, and check both sides' exact membership and values. These remain distinct from the unqualified full 1M/10M, physical mixed-device/filesystem and process-resource gates.

The r1130 cohorts reached terminal success on all three platforms:

| Phase | Windows VM | macOS | Ubuntu VM |
| --- | --- | --- | --- |
| Core preflight | 119/119 | 119/119 | 119/119 |
| Native/report preflight | 12/12 | 12/12 | 12/12 |
| Core | 1,722 pass, 48 existing skips | 1,747 pass, 5 declared platform skips | 1,747 pass, 5 declared platform skips |
| Analysis rendering | 691 pass, 1 existing skip | 692/692 | 692/692 |
| Settings | 35 pass, 1 existing skip | 36/36 | 36/36 |
| Windows native | 75/75 | — | — |

All four initial affected-project builds and the final Core fixture-only build completed with zero warnings/errors. The final test-only rerun verified every cached input, copied a fresh guest payload and patched only the Core test DLL/PDB (about 3.6 MB per platform). The preceding 47 entity/path Core cases and eight native/report cases remain included in the preflights. Unix skips retain the earlier four plus the explicitly declared Windows mandatory byte-range-lock case; other skip names are unchanged. Every original deployment handle reached terminal success before result collection. Windows used the R2 Interactive/Limited task route; its task was removed. All three post-run inventories report zero FileCat/dotnet processes. No host app or test was run.

[Exact sources, input hashes, original failures, passing receipts and actual task/process provenance](source-manifest-r1130-indexed-comparison.json) define this checkpoint. The completed finalizer is immutable and must not be rerun.

The initial r1129 preflight passed 106/119 on each platform and stopped before broad work. Thirteen test-fixture failures were corrected: removed nodes require the existing sparse-ID writer; two UInt128-max aliases intentionally overflow the independent File-size reference total, so the active-value boundary case now uses one alias; lossless raw names may use their reversible compact text representation. The r1130 rerun changes only the Core test DLL/PDB, with every product input verified identical. Original failure receipts remain retained.

Windows access was coordinated with the separately user-authorized Owner/NVDA tester. This task left its R2 desktop free, supplied the known Interactive/Limited launch workaround, waited for the other tester's explicit release and checked process occupancy before deploying. It did not switch accounts, alter sessions or send desktop input during the handoff.

The comparison remains internal. Public indexed comparison UI, predicates/cohorts/order options, chart colors and scopes, reports and `.fcdsc` persistence/import still need integration and qualification. Full large indexed comparison parity, remaining W0–W8 work and the final independent UX/UI/functional/security review remain open. Public feature registration stays off.
