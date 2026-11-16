# Comparison chart exports

The comparison report chooser now exports the selected Previous/Current Treemap or Sunburst as PNG or inert SVG. Image choices use the current chart scope, reset and disable the table-report scope selector, and explain when no chart is available. Cancel and path omission remain defaults. The selected chart's full captured hierarchy is exported; table query/cohort filters do not restrict the image.

The shared renderer uses the recorded side's accounting area and growth colors. Captions include both observation intervals, the selected side, metric/scope value, comparison assurance/limitations, both coverage/freshness states and joined privacy classification. Either partial input produces a partial watermark. Growth colors describe observed changes rather than signed area or exclusive reclaimable space. Scope totals and the exact selected input identity are checked before encoding. Redaction removes source names/locations and identifiers from the complete scene, including labels; SVG has only generated geometry, text and local clipping references.

Images retain both immutable inputs, comparison rows and the captured scope until actual output and release finish. Changing or closing the original views cannot retarget an encoded image. The existing guarded create-new destination, protected-state policy, dimension/encoded-size limits and image-memory admission remain in force. Source-release failure now still releases encoded image buffers and their memory reservation while propagating the unconfirmed-close failure. Comparison text reports also name their case-sensitivity setting and explain abbreviated display paths.

The r1117 native cohort passes on all three platforms:

| Phase | Windows | macOS | Ubuntu |
| --- | --- | --- | --- |
| Chart/report preflight | 48/48 | 48/48 | 48/48 |
| Core | 1,597 pass, 48 existing skips | 1,623 pass, 4 existing skips | 1,623 pass, 4 existing skips |
| Analysis rendering | 679 pass, 1 existing skip | 680/680 | 680/680 |
| Settings | 35 pass, 1 existing skip | 36/36 | 36/36 |
| Windows native | 75/75 | — | — |

There are 33 additional cases: both sides/chart types/formats/privacy choices, nested/Other scope and exact amounts, both intervals and partial/taint captions, safe SVG/PNG output, existing-file preservation, detached-owner lifetime, mismatched captions, blocked-output cancellation, failed release, seven-theme geometry bounds, and chooser defaults/unavailable states. Windows used the ordinary R2 Interactive/Limited task route; the task ended and was removed. All task-owned test instances exited.

The initial r1115 preflight passed 46/48 and stopped before broad suites. Two tests checked button state before the dialog's documented 200 ms refresh. The corrected tests additionally attempt an unavailable action during that transition and verify refusal, then await its disabled appearance. Production inputs stayed byte-identical; cached reruns transferred only the test DLL/PDB, approximately 638 KB per machine. A host-side staging attempt also exposed CRLF-sensitive template matching; the preparation helper now normalizes line endings before matching and a fresh staging revision was used.

[Exact source, original failures, passing receipts, unchanged skips, cache plans and input/task provenance](source-manifest-r1117-comparison-charts.json) retain the tested scope. The completed finalizer is immutable and must not be rerun.

Large indexed comparison parity and remaining replacement behavior, physical desktop/resource/package qualification, later W0–W8 work and the final independent UX/UI/functional/security review remain open. These checks do not qualify the entire tool. Public feature registration stays off.
