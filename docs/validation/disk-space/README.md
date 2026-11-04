# Disk space implementation checkpoint

This branch is under development. W0, W1 and W2 are in progress; no release or security gate is closed. The new window is exercised directly by tests. `tools.diskSpace` is not registered in the product yet. Preview, jobs, destructive actions, content analysis and the privileged metadata helper remain off.

## Current evidence

| Run | Result | Receipt |
|---|---|---|
| Full solution build | No warnings or errors | [build](solution-build-r12.txt) |
| Windows Core analysis and command cases | 121 pass, 5 platform skips | [XML](windows-core-r12.xml) |
| Ordinary Windows host | 6 pass, no skips | [XML](windows-host-native-r16.xml) |
| Ordinary Windows guest | 6 pass, no skips | [XML](windows-vm-native-r12.xml) |
| macOS arm64 analysis and command cases | 126 pass, no skips | [XML](mac-core-r12.xml) |
| Skia headless presentation | 2 pass | [XML](analysis-render-r6.xml) |
| Ubuntu | Postponed while another tester's FileCat is running | No native Linux claim |

The Windows suite includes three native fixtures and three reparse-tag classification cases. Native fixtures cover hard links, junction exclusion, queued ancestor movement, explicit hard-linked file roots, and metadata access while primary content reads are denied. Mac fixtures cover no-follow enumeration, ancestor movement and explicit hard-linked file roots. These are development regression cases, not the full filesystem, cloud, race or resource qualification matrix.

The presentation test draws Treemap, Sunburst and Table at 1180, 600 and 380 DIP in all seven themes, at a fixed height of 780 DIP. It also checks that the latest filter wins, interaction freezes keep their targets, and marks/basket references survive filtering. The [render directory](renders) contains selected images from that run. Minimum-height, large-text, actual desktop, tooltip/DPI and assistive-technology qualification remain outstanding. Headless rendering does not establish native UI acceptance.

The suite starts no FileCat application processes. Mac and Windows guest checks after the run found no FileCat instances; the existing Ubuntu instance was left alone. Disposable-machine connection and account notes are kept only in ignored local continuation files.

## Remaining work

See [progress](progress.json) and the [capability manifest](capabilities.json) for the unfinished contracts and packets. In particular, the current review basket contains inert observation references and has only a preserving close action. It cannot submit an operation. Native action feasibility, private persistence/import, comparisons, recipes/content work, helper qualification and whole-tool acceptance are still required by the plan.

The in-memory fallback can stop at its resource budget and report partial coverage. It is not evidence for the 1M/10M qualification cases. No existing comparison-bound or destructive job guarantee was relaxed.
