# Disk space implementation checkpoint

This branch is under development. W0, W1, W2 and W3 are in progress; no release or security gate is closed. The new window is exercised directly by tests. `tools.diskSpace` is not registered in the product yet. Preview, jobs, destructive actions, content analysis and the privileged metadata helper remain off.

## Current evidence

| Run | Result | Receipt |
|---|---|---|
| Full solution build | No warnings or errors | [build](solution-build-r20.txt) |
| Windows Core analysis and command cases | 161 pass, 8 platform skips | [XML](windows-core-r20.xml) |
| Ordinary Windows host | 9 pass, no skips | [XML](windows-host-native-r20.xml) |
| Ordinary Windows guest (previous checkpoint) | 9 pass, no skips | [XML](windows-vm-native-r19.xml) |
| macOS arm64 analysis and command cases | 169 pass, no skips | [XML](mac-core-r20.xml) |
| Skia headless presentation | 7 pass | [XML](analysis-render-r20.xml) |
| macOS native startup/close only | 509 generated regular paths unchanged; owned process exited | [receipt](mac-native-startup-r20.json) |
| Ubuntu | Postponed while another tester's FileCat is running | No native Linux claim |

The Windows suite includes six native cases and three reparse-tag classification cases. Native fixtures cover hard links, junction exclusion, queued ancestor movement, explicit hard-linked file roots, metadata access while primary content reads are denied, duplicate/contained roots in both selection orders, denied metadata with readable siblings, and exact native DOS-reserved/trailing names. The denial fixture uses an explicit native ACE because the managed Deny-rule constructor strips SYNCHRONIZE. Mac fixtures cover no-follow enumeration, ancestor movement, explicit hard-linked file roots, root overlap and unreadable-directory sibling continuation. Failure may occur before recursion on macOS, so counters distinguish metadata failure from a failed requested enumeration. These are development regressions, not the full filesystem, cloud, race or resource qualification matrix.

The [schema-1 stream codec](../../design/FILECAT_DISK_SPACE_INVENTORY_SCHEMA.md) preserves tagged raw names and historical root text, enforces typed order and strict schema/resource bounds, verifies a mandatory SHA-256/count trailer, validates relationships and recomputes all four metrics with checked arithmetic. Every load is imported, unverified and inert. Tests include updated-checksum poisoned records, invalid encoding/numbers/enums, cycles and references, aggregate overflow, truncation, duplicate properties, depth/line/file bounds, fragmented nonseekable input and shared-memory refusal/release. Filename storage/publication, named saves/history, comparison and export/import UI remain unfinished; this codec is not a security-gate qualification.

Raw display now exposes invalid Unix bytes and unpaired UTF16 units, escapes trailing name characters and distinguishes literal backslash sequences. Text predicates on names or paths that lack lossless text return Unknown; they do not match replacement characters. Logical-size uncertainty is independent of allocation: a zero-length file remains zero even when allocation is unavailable, while missing logical size is labelled Unknown in rows, tooltips and the inspector. Exact detail values remain integer byte counts.

The presentation tests draw Treemap, Sunburst and Table at 1180, 600 and 380 DIP in all seven themes, at a fixed height of 780 DIP. Separate cases check 380 × 430 with 20-DIP text and 4,000 virtualized rows, keyboard opening of details and custom overlay cancellation, root-separated incompatible allocation, uncertain predicates, captured timezone, and frozen membership. The [render directory](renders) contains selected images. Broader text/DPI, actual desktop, tooltip and assistive-technology qualification remain outstanding. Headless rendering does not establish native UI acceptance.

Root containment is a request to the held native sources, not a string-prefix proof. A covered root records its active binding and relative observation scope. Unverifiable overlap is excluded with partial coverage instead of being counted twice. Selection leases retain the exact published revision and frozen real members; neither these leases nor historical binding fields are operation approvals.

The CLI suites start no FileCat application processes. A separate [native desktop harness](../../../tests/FileCat.NativeAcceptance/DiskSpaceHarness/README.md) now opens the real window with generated sources and a separate profile. On Mac, a narrow startup/close test recorded 1180 × 763 DIP at 2× and preserved all 509 generated regular paths. It did not start a scan or exercise desktop input, and is not desktop/a11y acceptance. The Windows attempt could not activate windows or obtain accessibility; captures showed only background/lock-screen content, and the user unlock check is pending. Its owned process was closed. Mac checks after its test found no FileCat instance; Ubuntu was left alone. Disposable-machine connection/account notes remain in ignored local files.

## Remaining work

See [progress](progress.json) and the [capability manifest](capabilities.json) for the unfinished contracts and packets. In particular, the current review basket contains inert observation references and has only a preserving close action. It cannot submit an operation. Native action feasibility, private persistence/import, comparisons, recipes/content work, helper qualification and whole-tool acceptance are still required by the plan.

The in-memory fallback can stop at its resource budget and report partial coverage. It is not evidence for the 1M/10M qualification cases. No existing comparison-bound or destructive job guarantee was relaxed.

After the full implementation/testing loop, the user requires a separate independent review for UX, UI, functional and security bugs. The [final-review checklist](FINAL_REVIEW_CHECKLIST.md) is pending; every finding must return through fixes and relevant testing before completion.
