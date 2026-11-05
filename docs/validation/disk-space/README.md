# Disk space implementation checkpoint

This branch is under development. W0, W1, W2 and W3 are in progress; no release or security gate is closed. The new window is exercised directly by tests. `tools.diskSpace` is not registered in the product yet. Preview, jobs, destructive actions, content analysis and the privileged metadata helper remain off.

## Current evidence

| Run | Result | Receipt |
|---|---|---|
| Full solution build | No warnings or errors | [build](solution-build-r19.txt) |
| Windows Core analysis and command cases | 158 pass, 8 platform skips | [XML](windows-core-r19.xml) |
| Ordinary Windows host | 9 pass, no skips | [XML](windows-host-native-r19.xml) |
| Ordinary Windows guest | 9 pass, no skips | [XML](windows-vm-native-r19.xml) |
| macOS arm64 analysis and command cases | 166 pass, no skips | [XML](mac-core-r19.xml) |
| Skia headless presentation | 6 pass | [XML](analysis-render-r19.xml) |
| Ubuntu | Postponed while another tester's FileCat is running | No native Linux claim |

The Windows suite includes six native cases and three reparse-tag classification cases. Native fixtures cover hard links, junction exclusion, queued ancestor movement, explicit hard-linked file roots, metadata access while primary content reads are denied, duplicate/contained roots in both selection orders, denied metadata with readable siblings, and exact native DOS-reserved/trailing names. The denial fixture uses an explicit native ACE because the managed Deny-rule constructor strips SYNCHRONIZE. Mac fixtures cover no-follow enumeration, ancestor movement, explicit hard-linked file roots, root overlap and unreadable-directory sibling continuation. Failure may occur before recursion on macOS, so counters distinguish metadata failure from a failed requested enumeration. These are development regressions, not the full filesystem, cloud, race or resource qualification matrix.

The [schema-1 stream codec](../../design/FILECAT_DISK_SPACE_INVENTORY_SCHEMA.md) preserves tagged raw names and historical root text, enforces typed order and strict schema/resource bounds, verifies a mandatory SHA-256/count trailer, validates relationships and recomputes all four metrics with checked arithmetic. Every load is imported, unverified and inert. Tests include updated-checksum poisoned records, invalid encoding/numbers/enums, cycles and references, aggregate overflow, truncation, duplicate properties, depth/line/file bounds, fragmented nonseekable input and shared-memory refusal/release. Filename storage/publication, named saves/history, comparison and export/import UI remain unfinished; this codec is not a security-gate qualification.

The presentation tests draw Treemap, Sunburst and Table at 1180, 600 and 380 DIP in all seven themes, at a fixed height of 780 DIP. Separate cases check 380 × 430 with 20-DIP text and 4,000 virtualized rows, keyboard opening of details and custom overlay cancellation, root-separated incompatible allocation, uncertain predicates, captured timezone, and frozen membership. The [render directory](renders) contains selected images. Broader text/DPI, actual desktop, tooltip and assistive-technology qualification remain outstanding. Headless rendering does not establish native UI acceptance.

Root containment is a request to the held native sources, not a string-prefix proof. A covered root records its active binding and relative observation scope. Unverifiable overlap is excluded with partial coverage instead of being counted twice. Selection leases retain the exact published revision and frozen real members; neither these leases nor historical binding fields are operation approvals.

The suite starts no FileCat application processes. Mac and Windows guest checks after the run found no FileCat instances; the existing Ubuntu instance was left alone. Disposable-machine connection and account notes are kept only in ignored local continuation files.

## Remaining work

See [progress](progress.json) and the [capability manifest](capabilities.json) for the unfinished contracts and packets. In particular, the current review basket contains inert observation references and has only a preserving close action. It cannot submit an operation. Native action feasibility, private persistence/import, comparisons, recipes/content work, helper qualification and whole-tool acceptance are still required by the plan.

The in-memory fallback can stop at its resource budget and report partial coverage. It is not evidence for the 1M/10M qualification cases. No existing comparison-bound or destructive job guarantee was relaxed.
