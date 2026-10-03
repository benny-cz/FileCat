# E-I111 — delayed first rows suppressed by streaming batch coalescing

I111/V12/V16. Medium severity; listing responsiveness and focus coverage. Preliminary source/remediation evidence,
base `324ed29` plus the exact overlay retained in [E-I92](E-I92-background-quick-search.md).

The initial affected full Core run times out in the existing user-moved cursor case while waiting for its first
row. After an empty or parent-only view is published, the geometric batching condition waits for at least 64
additional entries even though no real entry is visible. A slow provider yielding fewer entries keeps them hidden
until enumeration ends; this is a production publication defect, not a reason to enlarge the test deadline.

A controlled provider waits until the initial empty/parent-only sorted view is observed, then publishes one file
and stays blocked before its second file. All four baseline cases time out waiting for that first real row:
root and parent-row listings, untouched and explicitly moved cursor. Exact failing source/test DLL/XML are
retained in `first-batch-before`. The test's cleanup was also corrected to retire its model on failed assertions.

Coalescing now waits only after real entries have already been published; unchanged batches still wait. All four
controls pass before the provider is released. Final independent names/order and cursor assertions remain:
untouched cursor stays first; a user-selected file remains focused after the second, earlier-sorting file arrives.
The original three streaming cases and nine background-search cases also pass, 16/16 without skips.

Full corrected Core **714 pass/46 skips**, App **249 pass/21 skips**, zero failures; E-I92 retains the exact inputs
and independent inventories. Baseline four-failure XML SHA-256
`4664328a95f20256161f4478f7ff2af277c045dccbcefb05f177805a6c63df4a`; corrected targeted XML
`2d63a949a7badf06ac443661929cdd927a154ce7e8b4cfe514b4d056dc172e0f`.

Successor CI and native/candidate performance remain required. No claim of reference-machine or source-device
qualification, no candidate identity or stable GO.
