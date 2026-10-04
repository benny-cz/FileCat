# E-V13-F2 — saved searches and duplicates within a result set

V13, SEARCH-001/002 and UX-007. Preliminary component/headless validation on Windows Insider 26220,
SDK 10.0.401. Source base `aed64a7bd1d9f88672137f6a9dc2ff9c20e2dedb` plus the single test overlay
`tests/FileCat.App.Tests/FindRemainingScopeTests.cs`. No production change or product defect discovered.

Four Find flows save literal, regex, hex and Unicode criteria through the actual Save dialog, reload the actual
settings JSON using FileCat's state reader, discard the in-memory saved searches, then reopen Find and search.
Every persisted criterion is compared, including masks/exclusions, folder, recursion, hidden/archive flags,
case/whole-word/content mode and file-only/minimum/maximum size criteria. Independent known bytes and expected
names check both matches and exclusions: case, word boundaries, nonmatching expressions/bytes, undersized and
oversized files, excluded names, subfolders and a matching directory name. Unicode checks UTF-8 and both UTF-16
byte orders at odd offsets without a BOM. This is a disk reload/new Find window, not a process-restart test.

The fifth flow opens Find within a six-file result subset and uses the actual duplicate dialog. Two known byte
groups survive; equal-sized different bytes and an identical file outside the subset never enter the groups.
Original identities, relative paths and source-set membership survive. Select extra copies marks exactly one
file per two-file group. Every original file, including the excluded identical one, keeps its exact bytes.

All five cases pass. The full App suite passes **263/284 with 21 declared skips**. Direct XML case/outcome
inventory, ten source copies, the active assembly and all 106 retained input files independently verify.
An initial five-case smoke run is retained; qualification uses the final captured inputs/full-suite run.

Private root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/v13-saved-duplicates-20261004`.

| Evidence | SHA-256 |
|---|---|
| Final input manifest | `445c93a01a3d624ddecbb7a26c72ec6c153924d14dd487e815d7757fa986ffc5` |
| Full App XML | `27d0050e699e407c5908779c19b866497bc1ba3d1537b7753d1aca9320585f75` |
| Independent input/case/skip inventory | `e2e7dd02e64db2ce53a50a4548fd6e11c8d2fbb797793a13d5e441f3dc9e721f` |

Clean `ab919ed78eae0dfa8287249fd4ec07d38cbdc37c` passes all four required
[CI lanes](https://github.com/benny-cz/FileCat/actions/runs/37177512600); three package jobs skip. Six direct
XML inventories verify Windows Core 728/47 skips, App 269/15, platform 165/33 and Remote 88/28; Linux/macOS App
each 248/36. All five affected App cases pass on Windows, Linux and macOS. Three actual artifact ZIPs match
their server digests. Private root: sibling `ci-37177512600` in the same authorized evidence store.

| Clean CI evidence | SHA-256 |
|---|---|
| Metadata / complete log | `f88bd87fb124043687d73ca0b5da4f173a8d9234ab53b56bf7c62348e354936f` / `158364e6449d135b5a342feddbb8720b24fb17fef2eda2d97c5da578a325943d` |
| Independent artifact/case/skip inventory | `cc930899b4a5f95862d7ae80d819e7975b9b54a2554dad6ae1fa9e365d795bf2` |

All five cases, plus two Network-place App controls and eight Core discovery controls, pass without skips
on clean successor `da3a3d6` in the elevated Windows 26300 VM. Exact payload/source pins, retrieved case names
and process/temp cleanup independently verify in [E-I117's combined guest record](E-I117-network-discovery-cutoff.md).
Controller/workers are absent and owned temp is empty. This is native OS execution of headless controls.

Native desktop/AT, process restart and exact-candidate
qualification remain open. Physical USB source hold and overall **NO-GO** remain.
