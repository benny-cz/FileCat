# E-V09-G2 — physical USB recovery and byte-checker correction

2026-10-03. Preliminary component evidence; stronger physical rerun is prepared, not executed.
Physical run source: `1df5dffe42bb820d21dd9fae86004a9eccedc02e`.
Checker correction: `2e6dffef18b93b8d142f17fa17dde549cb856f22`.
Observation manifests and prepared clean payload: `85bb17d413a299fd6114e5f21d715a062943132f`.

The owner reports executing the host launcher in an elevated shell. Its record confirms privileged execution,
Windows 11 Insider 26220, all 300 expected inputs verified, and USB serial `2F2000129618`, disk 5, G:,
Storage capacity 7,796,162,560 bytes. The identity/backing-disk preflight passes one case, no skips, exit 0.
The authorized fixture is formatted and populated separately for FAT32, exFAT and NTFS. Generated expected
file sizes, roles and SHA-256 hashes remain on E:, outside the source. Native source identity is rechecked
before mutation phases and raw source access, with volume GUID addressing as recorded in E-V09-G1.

The three native XML cases pass, no skips, phase exit 0. Their complete logs and generated manifests are
retained; an independent parser verifies every case and all fifteen wrapper-recorded output hashes.

| Filesystem | Case duration | Generated deleted files recovered exactly | Fixture entries retained |
|---|---|---|---|
| FAT32, 4 KiB clusters | 118.994 s | 325/325 | 330 |
| exFAT, 32 KiB clusters | 116.595 s | 325/325 | 330 |
| NTFS, 4 KiB clusters | 111.087 s | 325/325 | 330 |

The intended fragmented-role file also compares exactly in each run. These roles alone do not independently
prove physical fragmentation or reuse. FAT32/exFAT report the old overwritten-role item as Overwritten;
NTFS does not find it. No Partial-state item is reported. Recovery uses a volume reader and the helper's read
protocol in the test process. Installed broker placement/authorization, GUI consent/refusal/removal, independent
write tracing, full before/after source hashes and candidate qualification remain open. No zero-source-write or
production admission result is inferred. I106 still refuses unknown process census results.

## Checker defect and correction

During the hardware run, source audit finds that one missing byte excludes its entire 4 KiB block from the
truthfulness check. A Recoverable claim can also pass with missing ranges or a matching truncated prefix.
The original sources are retained before change. Extracting the original predicate unchanged into a test helper
and running thirteen controlled cases produces ten failures and three controls passing, exit 1. The controlled
failures concern the checker; they are not observed corruption of the physical fixture.

The corrected checker requires complete original bytes without missing ranges for Recoverable, and compares
every byte outside the exact union of valid missing ranges for Partial. It rejects wrong lengths and invalid
range bounds without overflow. All thirteen controlled cases pass. The affected inventory passes 27 cases with
seven explicit hardware opt-in skips, 34 total. An earlier setup attempt sets empty opt-in strings through a
PowerShell method binding; seven live cases refuse malformed drive text before source access. Its complete failure
is retained. Removing the environment entries explicitly gives the declared skips; none counts as a device pass.

85bb17d adds an off-source observed-item manifest with state, declared/read lengths, actual SHA-256 and exact
missing ranges. A complete claim also requires reading its full declared length. The affected inventory again
passes 27 with seven skips; direct TRX outcomes are independently parsed. First-run exact positive tallies remain
meaningful, while broader truthfulness validation needs the stronger physical rerun.

[Checker CI 37113482391](https://github.com/benny-cz/FileCat/actions/runs/37113482391) and
[successor CI 37113817438](https://github.com/benny-cz/FileCat/actions/runs/37113817438) both pass Windows x64,
Windows ARM64, Ubuntu and macOS. Package jobs skip. Direct Windows inventories in each independently verify
App 248 pass/15 skips, Core 699/47, Windows platform 161/33, Remote 88/28; no failures. All thirteen checker cases
pass. Run identities, complete logs and original test archives are retained; GitHub archive digests and every
extracted member's size/hash are verified.

## Ready host rerun and setup gate

The self-contained Release/win-x64 test payload is built with the repository clean at exact 85bb17d, before
further documentation edits. All 300 inputs and all 300 original archive member streams are hash-verified.
Native checker cases pass 13/13 and guard cases 14/14, no physical opt-in. This host has an SDK; no SDK-free
guest execution is claimed for this payload.

The prepared `artifacts/release-evidence/v09-usb-physical-strict-20261003/LaunchUsbHostTests.cmd` binds the new
manifest/source hashes and the same authorized USB. It requires one passing preflight without skips before the
three destructive filesystem scenarios, retaining observed hashes and expected manifests off-source. Each phase
is bounded to 45 minutes. Windows PowerShell 5.1 and the preparation shell both parse the runner without errors.
The earlier expanded PowerShell status metadata is retained; the new runner reads a plain status string.

The agent process is unelevated. The next interaction is an owner launch from the elevated host shell, or normal
launch with UAC approval. It will format the same disposable USB again under the existing authorization. No rerun
has started, and no stronger physical pass is claimed. Both VM power states are left as previously authorized.

Private evidence is under `artifacts/release-evidence/v09-usb-physical-20261003/host-admin-a686f32ba43c4f0c83b2ae67b118827e`,
`v09-usb-oracle-20261003`, `v09-usb-physical-strict-20261003`, `ci-37113482391` and `ci-37113817438` within
`artifacts/release-evidence`. Raw logs and binaries are ignored, not published.

| Evidence | SHA-256 |
|---|---|
| first privileged host identity / native scenario XML | `cc6d49ee728dbcc554740cb2a1f9b34b4402dd52e36ed1160b0f6edd87de2f8d` / `2454f1a845b067943d5a5b4b46412e7f5de8207fcb57e2327a37cacae5b2070c` |
| original native wrapper result | `3767a3de33cd9fd6d446fe150f8fead7c6b3be96c24f3222a972d68c3d70daaa` |
| each filesystem's generated 330-entry expected manifest | `3e0326a7bf06857e44b9743a156263d6521695c1b33f1840eefae7956222130f` |
| original checker audit | `1ae9cdd2ae837dcfefddaf97685fe7ec5c797b029e69b86f28aea7e62ae6173e` |
| baseline / corrected working input manifests | `bf2043c5f6363f807572522a255aaf24e0cb0b1c9b2d16854ddd571f8c358556` / `d42de020bc95bd3441dc334b3619176d5651fab13411587d6d911cc6b789dc38` |
| independent physical/baseline/corrected inventory | `1d5ee2a30b5130e0665a36c30d2fc02aab7100ccba9fa8764bf2b0863234cc3d` |
| independent observed-manifest affected inventory | `f7b697017bc6020b9e9b58ba5f28f3982eca5c0e07682f36d3c433ba5e21317d` |
| checker CI direct Windows inventory | `e673547b5f19b0dc224736298d28df58cb81cb79f816c143fd75dafc710c27c5` |
| successor CI run / direct Windows inventory | `cdd0b9e01b003f15a0d2a576178cda0da53b7b4eb85cd3c510c360b1c592b270` / `faad221d6dc51dffc489b9a7ef1b95ed75aad27e88661ecbc6ca73f67bc930ba` |
| successor CI original Windows archive / complete log | `b65f4236c5306211e162ca5b879d2064c5f5f919cde98a4720e6e080e61964e5` / `66a904736d1bd323abbdbe923a0dc3900457be1be297f6fdf1b10012788ead19` |
| clean 85bb17d native input manifest, 300 files | `af79ae1ecae6acfb0a3464d078a5654a327b337a3a70b4ad81b64296990356f2` |
| native bundle, 55,505,764 bytes / independent member inventory | `dc9abb3d1c4caa4d99039fac20709c68a99c610b0652ac782e0dfea41fc8f343` / `01c51d18179a8dbe110942d3cc26047f4c19cbdd0264cec460571e86c3da52fd` |
| native synthetic verification | `673723f144737828514f77b8340c287ccba9ee675b56527d452c25b5aadd90d7` |
| prepared stronger runner / launcher | `923f3fc9901aecb58fc452e646c11380b3b7c7c8eb16856e24a1b308ad8c5edb` / `92d8ff9470d6fb963ad85276259b7684acf085cfa3a421e6b7e074fc99db0d7d` |
| preparation / Windows PowerShell parse record | `16e935044dfc044d9f7dd024fd126d156af51fa7246cf0450e878765d3c1047a` / `db6111de035c80b7e2834ae233ecccccc6734117b8eacd20f8da7cad9bf069a5` |
| cumulative preliminary evidence inventory, 378 files | `f454773bf01c512a4478a7d31884c62bc614e9cddc533b228f73eb59aece64e5` |
