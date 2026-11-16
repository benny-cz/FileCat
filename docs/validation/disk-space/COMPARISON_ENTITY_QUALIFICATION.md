# Indexed comparison entities — 2026-11-16

The internal comparison entity stage now covers File size, Unique file size, On disk and Files. It stores metric entities in authenticated private scratch, with exact membership and path-to-entity indexes. File size/Files retain separate path entities; verified unique/allocation cohorts share their observed aliases; identity-unverified paths stay separate; directory-self allocation remains a distinct entity. Aliases are paged, never materialized as an entire cohort array. Component selection, conflicting-observation handling and common-parent placement reuse the existing charge index; totals reuse the existing summary/charge accounting.

Each entity retains its exact measurement state, optional attribution parent (including the scope parent), observed identity/version, metadata timestamp, freshness, root mask and streamed metadata-uniformity facts. Both known zero and missing values remain distinct. Original stores may close while the entity owner retains their immutable data. Construction/cancellation returns no partial entity table. Admission lasts through actual release, and an unconfirmed close does not claim that its place was freed. The path/entity constructors also retain a newly created scratch arena until vector construction successfully takes ownership.

Review found a functional problem inherited from the memory comparison: incompatible cross-root allocation bases suppressed known per-file observations as well as the combined chart area. The fix preserves those observations for comparison only. For example, previous 80/current 96 remain visible while the delta and combined total remain unavailable. Public chart charges stay empty for incompatible bases, and no combined allocation area is introduced. Known values are observations, not physical-space or reclaimability promises.

The r1124 batches completed with zero failures:

| Phase | Windows VM | macOS | Ubuntu VM |
| --- | --- | --- | --- |
| Core preflight | 43/43 | 43/43 | 43/43 |
| Native/report preflight | 8/8 | 8/8 | 8/8 |
| Core | 1,646 pass, 48 existing skips | 1,671 pass, 5 declared platform skips | 1,671 pass, 5 declared platform skips |
| Analysis rendering | 687 pass, 1 existing skip | 688/688 | 688/688 |
| Settings | 35 pass, 1 existing skip | 36/36 | 36/36 |
| Windows native | 75/75 | — | — |

There are 29 new Core cases. They check all metrics against the memory accounting, verified/unverified identity, directory components, included components, conflicting observations, arbitrary IDs, missing-state precedence/zero, incompatible roots and unknown entries, two-owner admission, actual read failure/cancellation and retry, 14,000-alias paging in all metrics, nonuniform metadata, nullable versions and UTC metadata normalization. The explicit limited-basis case verifies known side values alongside unavailable delta/combined area. The preflight also reruns the preceding 14 exact-path cases.

Seven additional native cases run on each platform. Four scan real 306-observation fixtures with hard links, close the original owner, and check exact metric membership and totals through the retained entity index. Three export Markdown, HTML and CSV through native guarded destinations, retaining known side values while declaring incompatible totals and unknown deltas. The incompatible basis records in those report cases are generated data; they do not qualify actual mixed-volume/device reporting. The 14,000-alias Core fixtures use the in-memory private-file test driver, while the 306-observation cases use real native scan/storage. Neither substitutes for the remaining physical 1M/10M/resource matrix.

The initial r1123 entity implementation also passed all three platforms: 42 Core/5 native preflight cases, 1,693 Windows or 1,675 Unix Core cases, 685 rendering cases, Settings and Windows native. Its exact sources and receipts are retained separately. Review then identified the known-side-value issue described above; the changed product and added regressions were rebuilt and fully rerun as r1124. A host-only r1122 fixture compilation error was corrected before any native entity batch; that failed build log is retained and is not acceptance evidence.

All five final affected-project builds completed without warnings/errors. Windows ran as `filecatdsa_r2` in an Interactive/Limited task, with the actual account/session and terminal task result retained. Every original run reached terminal success before collection; the scheduled task was removed and all three post-run inventories show zero FileCat/dotnet processes. No app or test ran on the host. Unix skips consist of the previous four plus the explicitly declared Windows mandatory byte-range-lock case introduced into this filter by the recent main synchronization; other skip names are unchanged.

[Exact source snapshots, input hashes, initial/final receipts, original handle outcomes and process/task provenance](source-manifest-r1124-comparison-entities.json) define this checkpoint. The completed finalizer is immutable and must not be rerun.

## Factory ownership follow-up

Before committing, review found that returning an entity result from an `await using` temporary charge owner could orphan the result if that temporary owner's close failed. Publication now waits for actual producer closure. A failed producer close releases the unpublished entity result; cleanup waits for actual completion, attempts each owner once, and retains both failures if cleanup also fails.

Four additional Core controls exercise delayed successful publication, delayed unpublished-result cleanup, build plus producer-close failure, and both owner-close failures. The r1125 focused rerun passes 47 Core cases (33 entity-related plus the preceding 14 path cases) and all eight native/report cases on every platform. Actual Windows R2 Interactive/Limited task provenance and terminal/post-process receipts are retained. No task-owned instance remains.

The only production source difference from r1124 is this factory release barrier; the scalar/member/accounting and native report sources are unchanged, and the preceding 29 Core test bodies remain byte-identical. [The focused evidence and exact source comparison](source-manifest-r1125-comparison-entity-factory.json) link to the r1124 broad cohort. Broad suites were not repeated on the factory-only revision. Its four affected-project builds are clean. This narrows reruns to the changed ownership path without claiming another full broad execution or native close-fault qualification.

The stage remains internal. Two-input indexed entity matching, compatible identity/version movement, split/merge ambiguity, full metadata-set comparison, scope/basis/coverage compatibility and unknown classifications still need integration with indexed queries, charts, reports and persistence. Large indexed comparison parity is not yet qualified. Full resource/physical/package/desktop requirements and the final independent UX/UI/functional/security review remain open. Public feature registration stays off.
