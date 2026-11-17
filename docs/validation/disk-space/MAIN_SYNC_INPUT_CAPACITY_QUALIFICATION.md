# Main integration and input/capacity qualification

The isolated branch merged main `68a435dc` in `b96dda68`. The merged product and subsequent analyzer input/capacity changes have passing native-filesystem and headless suites on the Windows VM, Mac and Ubuntu VM. The full implementation remains in progress; this checkpoint does not enable public entry or complete the final independent review.

External imports accept upper- and mixed-case `.fcdsa`/`.fcdsc` suffixes while preserving the exact chosen path. The picker routes comparison suffixes without case sensitivity and offers an all-files fallback for mixed-case names. Private/output naming policy and native read-only containment are unchanged. Twelve Core cases check suffix acceptance, format boundaries and parent traversal. Four actual filesystem cases check inventory/comparison imports, inert provenance, unchanged bytes and cleanup; case-sensitive filesystems also use a distinct invalid lowercase-suffix decoy.

The existing two entity places permit one indexed comparison. Open/Compare dialogs now explain that limit, refuse another comparison before loading its inputs, and keep saved-copy browsing/deletion available. The preliminary check is advisory; actual entity admission still enforces its atomic cap. Two native/headless cases verify refusal before the loader/builder, preserved inputs, a retained reader keeping capacity occupied, and successful opening after actual closure.

| Suite | Windows | Mac | Ubuntu |
|---|---:|---:|---:|
| New Core preflight | 12 passed | 12 passed | 12 passed |
| New native/UI preflight | 6 passed | 6 passed | 6 passed |
| Analyzer Core | 1,944 passed / 48 existing skips | 1,969 passed / 5 existing skips | 1,969 passed / 5 existing skips |
| Analyzer rendering | 778 passed / 1 existing skip | 779 passed | 779 passed |
| Settings | 35 passed / 1 existing skip | 36 passed | 36 passed |
| Existing Windows native actions | 75 passed | — | — |
| Metadata/ownership Core | 216 passed / 21 skips | 228 passed / 9 skips | 228 passed / 9 skips |
| Windows ownership/native | 53 passed | — | — |
| Owner App integration | 61 passed | 60 passed / 1 platform skip | 60 passed / 1 platform skip |

Exact skip identities/reasons, source snapshots, input hashes, original terminal handles, failed attempts and actual runner scopes are retained in [the immutable provenance](source-manifest-r1170-main-sync-input-capacity.json). The metadata wildcard also selects existing archive metadata tests: its independent C-header observer was not enabled on Unix. That opt-in check is explicitly unqualified here. Other supplemental skips identify the opposite operating system or its native APIs.

The product is r1168, with only rendering-test DLL/PDB changes in r1169 and only Core-test DLL/PDB changes in r1170. Unix broad successes from r1169 were retained; the affected metadata phase alone reran in r1170. Windows ran the new candidate broadly and repeated the changed baseline benchmark. Its three unchanged successful Owner enabled/listing/font checks came from the original merged-product r1166b run, saving approximately eleven minutes. No binary identity between the earlier merged product and r1168 is claimed. The Owner synthetic 100K/1M listing measurements are separate from disk-space traversal/resource qualification.

Two fixture assumptions were corrected without weakening production guards. Headless capture can drain extra queued paints, so the baseline now requires all four panels on every requested frame and records the actual paint count. Ubuntu's configured NSS returned error 2 for the unknown UID; the fixture now permits an unavailable name with a reason, while still requiring known-user resolution. Deterministic tests retain the distinction between a successful null result and a native error. The original 960-versus-980/1044 paint failures, native NSS probe and invalid runner-filter failures remain retained.

Windows used the ordinary interactive `filecatdsa_r2` account. Every final task process exited and final occupancy inventories were empty; other workers were never stopped. Host work was compilation and artifact verification. Physical picker/pointer/keyboard, accessibility/theme/DPI/package, full scale/global resource, remaining source/action/helper/recipe and final independent review gates remain open. Public feature registration stays off.
