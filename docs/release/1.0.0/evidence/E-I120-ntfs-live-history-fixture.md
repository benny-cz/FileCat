# E-I120 — NTFS integration checks distinguish current records from retained history

I120/I23/V10/D-56, low validation reliability defect. Production file-record code is unchanged. Original clean
`de1fd7145be13a4a64785cbe8ba88095a4f954e3` fails the Windows lane in
[CI run 37181433089](https://github.com/benny-cz/FileCat/actions/runs/37181433089); other three lanes pass.
All page-reader/quick-view cases pass (E-I119). Direct Windows platform XML has 164 passes, 33 skips and one
NullReferenceException at the existing MFT test's unconditional `log.Table!.Rows` assertion.

The captured report says its circular 64 MiB log holds 341,762 operations, LSN 9,097,334,963–9,114,111,982,
while this file's last change is LSN 9,097,104,009, older than that retained window. The empty table is accompanied
by that explicit explanation. Original raw log blocks and a scheduling/I/O trace were not retained, so this is
an observation of the report, not independent attribution of why the window advanced. The fixture's diagnostic
incorrectly describes every missing time-change sign as a delayed write, then still assumes history exists.

The MFT case now checks current file/name times, record attributes/content/layout, security and available USN
history independently. A separate live-log case keeps the bounded retry and the original creation/name/time-change
assertions. It declares a skip only for an empty table with the explicit older-than-window explanation, or after
creation/name assertions pass but the before/after time change does not arrive. Other missing-table reasons fail
with the complete report; retained complete history must satisfy all assertions. No skip is evidence that the
live log feature passed. Native parser, attribution, history bounds and runtime policies retain their prior code.

Working source: `de1fd7145be13a4a64785cbe8ba88095a4f954e3` plus only
`tests/FileCat.Platform.Windows.Tests/WindowsFileRecordsTests.cs`. The full host platform suite passes
**161/199 with 38 declared skips**. Its eleven file-record cases comprise seven passes/four explicit
administrator-prerequisite skips; this tool process has no elevated token. Nine golden NTFS log cases pass,
zero skips. Independent checks verify 130 captured input files, seven raw sources per lane, unchanged production
against canonical Git blobs, active assemblies/shared Core bytes, direct inventories and the original CI failure.
Elevated VM and clean successor CI execution are next; no native live-history qualification is claimed yet.

Private working root:
`C:/Users/marek/.codex/visualizations/2026/10/02/01a0fbbf-f37d-7042-9e13-028bfb0e5c33/FileCatReleaseEvidence/i120-ntfs-live-history-fixture-20261004`.
Original metadata/raw job log/full log/three server-digest-checked ZIPs/direct XML/independent inventory:
sibling `ci-37181433089`. The initial API log capture rejected terminal escapes; its empty output is retained.
The additive raw capture succeeds; its first display attempt fails on console encoding, without losing bytes.

| Evidence | SHA-256 |
|---|---|
| Original CI inventory | `ebdbd38c45118598a81210f945cb96f55df9f7bd04c63eef6b9119c377e40270` |
| Full host platform / golden log XML | `e3a8b8c2d91e93aba853691a7783942d66a87b1ff91752d69ea3038dbf8e055c` / `27830f683927e33331c83c9f99254d87c5b82dce263f6fad7c2ada794294defe` |
| Platform / Core input manifest | `7d1c3ba8551b019a43eee53ac4173ec0c4129cabdecf7a1c86c4083b149e682a` / `32dea300ea7bdf38544420786be3562f8009b5c035edf803c80aa538b630e335` |
| Independent working/original-case inventory | `bf557b658730ccfae374545461422aec880cc0e7e04d7e4cdb62011c4b1488c3` |

Candidate/GA file-record and native history validation remain. Overall **NO-GO**, stable human GO and physical
USB source hold remain; no physical USB test was run.
