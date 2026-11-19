# Disk space comprehension study

The independent participant study is pending. On 2026-11-19 the project owner deferred it because two participants were unavailable and approved closing the implementation goal with the study still open. The project owner later reviewed all five screenshots in a guided walkthrough recorded below.

Show readers the five FileCat screens without the criteria below. Ask them to explain each answer in their own words and identify the label or value they relied on. Record each response under an anonymous participant identifier. Keep any missed or ambiguous answers with the screenshot build and theme so the revised wording can be tested again.

| Screens | Ask the participant | What the answer should establish |
|---|---|---|
| Treemap and Sunburst, shared selection | What do the two byte totals describe, where is the shared amount shown, and what could happen if one name is removed? | Two paths reference one file. The chart counts the measured amount once at their common parent and another name can still reach the file. |
| Unreadable folder | What can you conclude about the contents and their contribution to the result? | Zero paths were observed in this folder. Its contents and their size remain unknown. The overall total covers observations and may omit that folder's contents. |
| Volume capacity | Why do the reported used space and the chart total differ? | The chart describes the scanned folder and the capacity figure describes its containing filesystem. The capacity view also identifies possible shared filesystem capacity. |
| Recycle / Trash | What would same-volume recycling do to recoverability and free space? What action is available? | Files may remain on the volume and recoverability is separate from observed free space. This review keeps the files because the recycle operation lacks the required proof. |

## Project-owner walkthrough

The project owner viewed the r1795 screenshots one at a time and gave the following interpretations before receiving feedback.

| Screen | Interpretation |
|---|---|
| Treemap | One total counts both names and the other counts the file once. The shared amount sits under their common folder. Removing one name leaves the file reachable through the other. |
| Sunburst | The shared slice sits under the common folder. The Selection totals have the same meanings as in Treemap. |
| Unreadable folder | Its contents and size are unknown. The overall total can omit the contents. |
| Volume capacity | Reported used space covers the volume, including files outside the scanned folder. The chart covers what FileCat measured inside that folder. |
| Recycle / Trash | A file in Trash could be restored. Free space would stay the same while the file remains on the volume. FileCat offers Keep files. |

The responses matched the main interpretation on all five screens. The shared-capacity qualifier and the reason recycling was unavailable remain subjects for the independent study.

## Capture evidence

The r1737 capture ran the current analyzer on the Mac's regular desktop at 2× scale in Classic Dark. It used a native fixture with one shared 1,048,576-byte hard-linked file and one unreadable directory. FileCat captured both charts, the partial observed count, volume capacity and the cleanup review. It restored the fixture's directory permissions, preserved the source bytes and exited with an empty process inventory. The capture receipt SHA-256 is `ca2ab0c1e758b69099e56d010d3696ae8a31509dee5b1f88a2ef5f7ecfd87c5a`. The participant handout SHA-256 is `900a7d7b92aaada07161adeccd584d5a6a2b0443d201aa59f6722a1de769cd26`.

The r1795 rerun used source `7b17b2df` and application SHA-256 `3e735e78f3b9a130423feb9590b57f8ea1a299064aa547e906c7ca324f0ae99e`. Its 2× compositor capture showed the complete localized Sunburst amount with MiB and the partial-coverage qualifier in the summary. All five captures were collected. FileCat preserved the fixture and exited with an empty process inventory. The capture receipt SHA-256 is `d67e80287bd7102a74145fdd34444748fee99ff6dc86eeefc2a9a34fe80b1888`. The current participant handout SHA-256 is `d17bb1f09935662872a1b26be388c2eefb47e3f91a53b00c044ae8b7469459fd`.

The Mac-only capture mode is `DiskSpaceHarness --comprehension-probe --replacement-compositor-capture`. Run it on an idle native desktop with the approved compositor collector. Then collect the five owned-window captures and the closed-run receipt.
