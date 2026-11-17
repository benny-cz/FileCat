# Indexed comparison reports and streaming bundles — 2026-11-17

The retained indexed comparison can export bounded Markdown/HTML detail, complete spreadsheet-safe CSV, paired-input PNG/SVG charts, and complete schema-1 `.fcdsc` bundles. The streaming reader validates both embedded inventories into private indexed graphs, with shared scratch/profile accounting and no source reconnection. Named saves and external exports preserve existing destinations. Imported observations remain unverified and grant no file actions.

The [immutable r1157 evidence](source-manifest-r1157-indexed-comparison-bundle.json) contains exact compiled sources, earlier source deltas, binary input hashes, raw guest XML/log archives, original-controller receipts and 32 output artifacts per platform. Verification used the Windows VM's ordinary interactive account, Mac and Ubuntu VM. No live tests ran on the host during this continuation.

| Runtime | Broad Core | Broad rendering | Settings | Windows native |
| --- | --- | --- | --- | --- |
| Windows VM | 1,915 pass / 48 existing skips | 753 pass / 1 existing skip | 35 pass / 1 existing skip | 75 pass |
| Mac | 1,940 pass / 5 declared skips | 754 pass | 36 pass | — |
| Ubuntu VM | 1,940 pass / 5 declared skips | 754 pass | 36 pass | — |

There are 48 new Core cases (31 report, 17 streaming-bundle) and 41 new native-filesystem/headless-view cases (39 outputs, two bundle workflows). Unix broad phases use r1156 product inputs with the unchanged test-only Core correction; all three also pass the r1157 child-termination fixture checks. Windows ran the complete r1157 batch. Ubuntu's original r1156 run stopped at `BUSY phase=appsettings`; its passing earlier phases are retained verbatim, and only the missing settings phase was completed in r1157. Final r1157 inventories are empty on all three machines.

The development history is preserved. Earlier Markdown and timing fixture failures, the Windows verifier timeout, 12 abrupt-child-release sharing/cleanup failures and a per-handle quota assertion mistake remain in their original failed receipts. The child fixture now waits at most five seconds for native sharing errors after its exact child has exited; production guards are unchanged. An actual Sunburst export caption overlap was fixed, with centered PNG/SVG text and clipping bounds inside the hole.

This qualifies the report/chart outputs and streaming bundle engine at the tested API/native-filesystem/headless-view scope. Comparison save/open interface integration and automatic chart-to-table location are subsequent work. Full large-resource, physical input, accessibility, package and remaining W0–W8 gates are open. Public feature registration remains off. The final independent UX/UI/functional/security review has not been performed.
