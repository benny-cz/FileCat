# The details window's compression link (I-DETAILS-C) — 23 November 2026

The owner asked for the optional link the two plans describe: **NTFS compression for this item…** in the File-system details window. This receipt covers how it is built, the tests, and the Windows x64 desktop and NVDA runs.

## What it does

- The link shows wherever the build offers NTFS compression, beside the report's other actions. Without compression (Linux, macOS, an unqualified build) it is absent, and the details still read.
- It reviews exactly the item the report was opened for. The item is captured once, from the report's own path. No panel selection, marks, Find window or listing generation is consulted, so a report that outlives the panel or Find it came from still reviews its own item.
- The review opens in the main window, using that window's own dialog service (not a Find window's while one runs a command). The main window comes to the front, restored if it was minimized. The item is read again there with fresh native pins, as for a panel selection.
- Until the review is actionable, it belongs to the report: closing the report, canceling the review or shutting down closes it, and nothing is submitted. From the actionable milestone on, it belongs to the main window, and closing the report changes nothing.
- The job has no listing origin. Completion clears no panel's marks, and it refreshes the affected folder as every job does.
- The link says why it cannot open a review now: the item is not a file or folder, the drive is not NTFS (from the report's observation; the review decides), a dialog is open in the main window, the main window is unavailable, or FileCat is closing. It opens one review at a time and says when one is open. The reasons are its tooltip, shown when it is disabled too, and its accessible description.
- The review says it is for the item File-system details was opened for, and nothing else. For a folder it offers **This folder only** or **This folder and its existing contents**.

## A dialog bug found on the way

The first desktop run showed both folder choices of the review as "Avalonia.Controls.TextBlock". Every dialog gave its check boxes and radio buttons a wrapping-text template that matches only string labels. A label that was already a control fell back to its type name. The review's folder choices had used such labels since the compression review was written, so a folder selected in a panel showed the same text. Dialogs now apply the template to string labels only, as the disk space window already did, and a control label shows as itself.

## Desktop run, Windows x64

The Windows 11 VMware guest (build 26340) ran a self-contained release build of this work at medium integrity in its signed-in desktop. The fixture was a 66,000-byte text file and a folder on C:, NTFS.

| Step | Observed |
|---|---|
| File-system details on the file (Ctrl+Alt+Enter) | The report shows the link, enabled and keyboard-focusable, with its description ([details](receipts/2026-11-23-details-compression-link/details-with-link.png)) |
| Tab to the link, Enter | Reached in 11 presses. The review opened in the main window, which came to the front with the focus on **Compress**. The link was disabled meanwhile. |
| Space, then Enter | Compress chosen, then approved ([review](receipts/2026-11-23-details-compression-link/review-in-main-window.png)). The file had the Compressed attribute 1 second later; its name turned blue and Attr read AC ([after the job](receipts/2026-11-23-details-compression-link/after-job.png)). |
| Back in the report | The link was enabled again. |
| Closing the report | The main window came to the front, with the focus on the file in the panel. |
| The folder, the same way | The review offered "This folder only" and "This folder and its existing contents" ([folder review](receipts/2026-11-23-details-compression-link/folder-review.png)). Esc closed it, and the folder stayed uncompressed. |

## NVDA, Windows x64

`tests/FileCat.NativeAcceptance/win-nvda-filesystem-details.ps1` gained a `-CompressionLink` stage. It ran with NVDA 2026.3.1 on the same guest, against the same build, after the runner's existing summary, full-record and copy steps ([result](receipts/2026-11-23-details-compression-link/nvda-details-link-result.json)).

- Tab reached the link. NVDA read "NTFS compression for this item…, button", then its description.
- Enter opened the review. NVDA read the main window, "Choose an action to continue." and "NTFS compression… dialog, grouping".
- Space: "Compress, radio button, checked". Enter approved, and the file was compressed.
- NVDA read the outcome: "Compress "alpha-details.txt" · selected items only: 1 changed, 0 already satisfied, 0 skipped, 0 failed, 0 uncertain, 0 not processed. The requested scope was fully checked."
- Esc closed the report, and NVDA read the file's row in the panel.
- The fixture's data and named stream were unchanged.

As in the [compression release run](2026-11-21-compression-release-x64.md), the completion refresh replaced the listing while the panel had the focus. NVDA first described the old row as "unavailable. This item belongs to a previous listing and cannot be acted on.", then read the result and the refreshed row.

Windows on Arm has no desktop run of the link: the UTM guests were deleted at the owner's request on 23 November. Its headless tests run in CI, where the hosted Arm runner gives FileCat's window no keyboard focus.

## Tests

`DetailsCompressionLinkTests` (App), with fixture compression adapters:

- The link reviews exactly the captured item in the main window, while the panel has marked other items, and opens one review at a time. The job's only source is that item, no listing origin is registered, completion refreshes the panel, and its marks stay.
- Closing the report, or shutting down, before the review is actionable closes it. The held native pin drains, and nothing is submitted.
- Closing the report after the review is actionable leaves it in the main window, where it is approved and runs.
- Canceling the review submits nothing, and the link opens again.
- A folder's review shows both folder choices as text (it failed against the old dialog template).
- A report opened from Find reviews its item after Find has closed, without touching the main panel's marks.
- Without compression the link is absent, and the details still read.
- The link says why it cannot open a review: not NTFS, not a file or folder, a dialog open in the main window.

Two mutations were checked: registering the active panel as the job's origin fails the first test, and the old dialog template fails the folder test.
