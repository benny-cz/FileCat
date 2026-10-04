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
These unelevated host skips do not qualify live history. The clean-source guest and CI results below
provide separate preliminary positive controls.

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

Clean source `9074cf654123467829e4085becaf52b9695ea5ab` passes all four required lanes in
[CI run 37183143181](https://github.com/benny-cz/FileCat/actions/runs/37183143181), with three skipped
package jobs. Six direct XML inventories verify: Windows Core 744 passes/47 skips, App 269/15,
platform 166/33 and Remote 88/28; Linux/macOS App each 248/36. All eleven Windows file-record cases
pass, including the complete live-history creation/name/before-and-after-time assertions. All 65 selected
Windows cases and seven affected App cases on each Unix lane pass, zero affected skips. Three raw artifact
ZIPs match their server digests. ARM64 lane/log success is retained without a direct XML artifact.

The exact self-contained source passes nine golden log and eleven file-record cases in the elevated
Windows Insider 26300 guest, **20 passes, zero skips**, ending **2026-10-04 06:44:27 UTC**. The complete
live-history case passes again. All 625 payloads/626 ZIP members/seven raw/canonical source copies,
script pins and retrieved output inventories verify. Controller PID 13568 and workers 5944/8176 are absent;
the owned temp folder is empty at 06:47:42 UTC. Guest UUID `9D224D56-1161-A849-ABA7-2581A980895C`,
root `C:\Users\Public\FileCat-ntfsrecord-validation-42caec442e1d4a2795e3dd7b6e3f37e1`.

The runner explicitly requests raw guest system-volume metadata reads for MFT/log/security inspection,
with owned fixture writes. It requests no physical USB or native desktop interaction. This is neither
recovery-source zero-write evidence nor final candidate qualification. Retained host evidence is
`clean-9074cf6` below the private I120 root; successor CI evidence is sibling `ci-37183143181`.

| Clean-source evidence | SHA-256 |
|---|---|
| CI metadata / full log | `5cd0d1d95434b2c7c0213721ba7b55889d808b26ad230b12ec5977518fc7c10a` / `6d72647a0a138a8ebcecb747d931eaa8c0a5134308aa4309095b0528ad68852f` |
| Windows / Linux / macOS ZIP | `eedbdfd9f5b9b004a249da71070bfe33a1f404d5a916610339ade4932d0e74d9` / `8bb75c4d389c41a437fb4e2d86eaddfc84aa813efa720fc3c795168bcc7e5270` / `77381729f8a26728c0dbf95efe77e73487a5b3780bf75b9c42f742a2ecd422c4` |
| Independent CI inventory | `547377c0039e2a00a9941ddaf9e1e67fa18cbbbd8e6183ccc2791999ade1a839` |
| Guest ZIP / manifest | `6d9af9e5745455d7cd0048aa28922978c24e0f6740c997191bc901ff53739e64` / `ed224a75ab7e2237da171d9ee915d4def33eba2c6a333aecc45640861be6e7f0` |
| Runner / cleanup script | `b081a0185a641ed287bda938f1784584d4b1d94624866da2ca8869b510fafe02` / `1e7b549cc3b17a8817766e4170609bb07b852384b72f40291ed846539a441119` |
| Guest Core / platform XML | `985a077efd7b2fbbcd7ea5ab2cfb2912d056d5cff3c64dcc7c79ae0904c63424` / `af5685458b74ce3703e7f4ced562c3c10c58d7c5c1baeb8f7b6b03e9d1d3019c` |
| Cleanup / independent guest inventory | `b17ce0253b074c6d1483bdf1a7d46f65cb08b6a1d29dbe6abb10b288c58d8f9d` / `76694f03788d3cbef2d3e129ecf9044acf108637c7259914d73f57c7a2d02582` |

Candidate/GA file-record and native history validation remain. Overall **NO-GO**, stable human GO and physical
USB source hold remain; no physical USB test was run.
