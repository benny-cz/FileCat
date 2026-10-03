# E-V09-G3 — overlapping USB attempts and exclusive successor

2026-10-03. Preliminary harness and physical component evidence; exclusive successor completed successfully.
Guard correction: `1883eb475633f6a1185885c31ed56ab780154bdd`.
Cancellation refinement and clean successor payload: `090a2b63e5978b6416c6facfcfd33728de3fec8c`.

## Retained overlapping attempts

After the owner reports an elevated launch, repository reality shows two native campaigns targeting the same
authorized USB serial `2F2000129618`, disk 5, G:, capacity 7,796,162,560 bytes and the same volume GUID.
The earlier launcher starts at 10:02:44 UTC and the stronger launcher at 10:03:26 UTC. Their scenario execution
intervals overlap. Both were privileged and verified their respective 300-file input manifests.

| Run suffix and source | Preflight | Filesystem outcomes | Recorded output hashes independently verified |
|---|---|---|---|
| `6be3ff1773974c45b54520b49a2e9878`, 1df5dff | 1 pass | FAT32/exFAT/NTFS fail | 12 |
| `01cba1a5ed344070835c706a9bd6fbd8`, 85bb17d | 1 pass | FAT32 fails; exFAT/NTFS pass | 16 |

Both attempts report device-not-ready failures during fixture writes; the earlier exFAT capture also fails because
G: is absent during a volume query. Complete XML, stdout/stderr, wrapper results, errors and the stronger run's
available expected/observed manifests remain retained. Neither overlapping attempt qualifies the stronger
physical rerun, including its passing cases. The earlier isolated positive component run in E-V09-G2 remains
separate evidence. No conclusion that every error has the same cause is required for this invalidation.

The independent inventory retains the original guard/caller sources before changes, verifies every recorded output
hash and parses each XML case directly. A process census after completion finds no remaining physical test process.
The shared xUnit collection serializes one test process only; neither old launcher nor old payload provides an
interprocess lease. This is a validation harness defect, not evidence of recovered-byte corruption.

## Correction and controls

Every physical guard now holds a non-waiting exclusive file handle keyed by the exact USB serial in the fixed
ProgramData `FileCat/Validation/LiveUsbLocks` directory. The guard checks that directory's backing disk before
creating the lease, then repeats identity/topology validation after acquisition. Every caller disposes its guard
after the complete async test scope. A conflicting or inaccessible lease fails closed before source mutation/read.
The file remains after handle disposal; process death releases its handle without a stale semaphore count.

Controlled tests show same-serial refusal while held, an independent serial proceeding, reuse after disposal,
refusal against a separate PowerShell process and acquisition after that process is killed. The first crash-control
attempt fails when Windows briefly retains the handle after signaling process exit; its source and failure are
retained. The control now bounds handle-release observation to five seconds, leaving production acquisition
non-waiting. The affected inventory passes 29 with seven explicit hardware skips, 36 total. Shared App compilation
succeeds and its one physical case explicitly skips. Cancellation refinement passes both lease cases without skips.

The successor launcher also holds a per-serial global mutex throughout preflight and all three filesystem cases.
It refuses any active legacy/direct native test, records child PIDs/start times and retains early refusal logs.
Three Windows PowerShell 5.1 controls pass without input/USB access: duplicate launch refusal, available-launch
success and recovery of an abandoned mutex after a child dies. An initial validation-only setup refusal is retained;
the final bound runner passes all three controls. These controls do not qualify source write behavior.

Original earlier runner/launcher bytes are retained in `retired-original-launchers` under their respective roots.
Their old entry points now refuse with the successor path. A distinct name avoids confusing the two old identical
launcher names. No old executable is silently replaced or attributed to the new source.

## Concrete successor and completed elevated run

The self-contained Release/win-x64 payload is built with the repository clean at exact 090a2b6. All 300 files and
all 300 original archive member streams are independently size/hash verified. Native checker 13, identity guard 14
and lease controls two all pass, no skips, no physical opt-ins. The host has an SDK; this is not an SDK-free result.
The intermediate clean 1883eb4 build is also retained separately.

`artifacts/release-evidence/v09-usb-exclusive-20261003/LaunchUsbRecoveryExclusive.cmd` binds the source/input and
campaign helper hashes. The same exact USB is present on the host, online/writable and nonboot/nonsystem.
One passing identity/topology preflight is required before the three destructive filesystem cases. Expected and
observed per-file hashes remain off-source; each native phase is bounded to 45 minutes.

The owner launches the exclusive runner from an elevated shell. Run
`host-admin-c4d70f463a8141748871d389d31e07c1` starts at 10:42:00 UTC and completes at 10:48:21 UTC, with exact
clean 090a2b6 payload, 300 verified inputs and privileged execution on Windows 11 Insider 26220. Both native
phases exit zero: preflight one pass, scenario three passes; no failures, errors or skips. Parent PID 59612 starts
preflight PID 52644 at 10:42:01.8637106 UTC and scenario PID 5788 at 10:42:07.0641835 UTC. Campaign/guard leases
cover their respective full scopes. The USB remains exact serial `2F2000129618`, 7,796,162,560 bytes, disk 5, G:,
USB/nonboot/nonsystem, partition 1 at offset 1,048,576 with size 7,795,113,984, and volume GUID
`bd052877-2d83-11f1-a457-18c04da6742d` throughout the three formats.

| Filesystem | Native seconds | Generated deleted files recovered exactly | All complete recovery claims independently matched |
|---|---:|---:|---:|
| FAT32 | 139.201019 | 325 | 327 |
| exFAT | 118.5850385 | 325 | 327 |
| NTFS | 116.1184665 | 325 | 326 |

Each expected manifest has 330 unique entries; each observed manifest covers all 328 non-kept entries. Independent
verification checks the 300 staged inputs again after execution, all nineteen wrapper-recorded output hashes,
direct XML class/method/counts, child/phase records, pinned device/partition identity and every Recoverable item's
full length, no missing ranges and expected SHA-256. FAT32/exFAT also recover the gone and fragmented-role entries;
their overwritten entry is classified Overwritten and differs. NTFS recovers the fragmented-role entry, with gone
and overwritten entries not found. No Partial claims occur; observed hashes alone would not independently replay
their intervals. Fixture roles do not establish actual fragmentation or reuse layout without separate layout evidence.

The live unelevated observer finds only one native testcase process; its post-run census at 10:54 UTC finds no
remaining recorded/native process. It cannot inspect every elevated command/module, so this is not a whole-system
process trace. This remains physical generated-fixture component recovery through an in-process raw-read server.
Installed helper/broker qualification, GUI/consent, production census availability, independent source-write traces,
full source before/after hashes and exact candidate evidence remain open; recommendation stays NO-GO.

[Guard CI 37116287957](https://github.com/benny-cz/FileCat/actions/runs/37116287957) and
[successor CI 37116435180](https://github.com/benny-cz/FileCat/actions/runs/37116435180) both pass all four lanes;
the three package jobs skip. Exact identities, complete logs and original Windows archives are retained. GitHub
archive digests and every extracted member's size/hash verify. Independent direct TRX inventories in each show
App 248 pass/15 skips, Core 699/47, Windows platform 163/33 and Remote 88/28; no failures. All fourteen identity
controls, thirteen oracle cases and both lease cases pass in each direct Windows inventory. This remains CI
component evidence, with physical cases explicitly skipped.

Private evidence: `artifacts/release-evidence/v09-usb-exclusive-20261003`, plus the two run directories under
`v09-usb-physical-20261003` and `v09-usb-physical-strict-20261003`, and `ci-37116287957`/`ci-37116435180`.
Raw logs, binaries and scripts remain ignored.
The unchanged preparation inventory covers 731 retained files, including both overlapping attempts, successor
preparation, the intermediate native build and both complete CI evidence sets. A separate completed-run inventory
covers 25 physical outputs, observations and verifier files; the earlier inventory is not rewritten.

| Evidence | SHA-256 |
|---|---|
| independent overlapping case/output/source inventory | `bd28c2b93f89f5a0d4b1bd832630fd90ab98506b1929d504053b902bae4b80f1` |
| independent affected test inventory, including retained failure | `3ff867e0723e1e5cfe20e83c5b79553de9790d1a6b2bc516ac6e9ca99a7f3062` |
| clean successor 300-file input manifest | `d20388ef99dac8e9f2371e261241580e8e83cb6e0837a4e850912a138189144b` |
| native bundle, 55,509,014 bytes / independent archive inventory | `53bb9d7ad8dce986ace6bc5c97241c130c827cdd67e1c671ff7e6c7b02106eb1` / `e8d635267d9d8f469dca57aecda181283393ad06f1600910abc50ffcc00060f9` |
| native synthetic inventory, 29 cases | `7ced6234d6c1ad722d6e8d298705dcfcef0081101eeb834b69f979185d92a19d` |
| final three campaign lease controls | `eda861d5f53a50ed516d9bb0844d9c5163fca4f5996dc6a89f2fded9ea200cc3` |
| bound runner / campaign lease helper / distinct launcher | `787eb8db37d4f26152c41c5951fa27874a368b054e51632862a39f1660a94cc0` / `f8945c979d9a9a9d1ca4b5a88a5c20e2231dafa62690a1b3ecf6245bf0430bbd` / `3bd08c542306caee9ccabf1d4c8051408c321987db1328b1e58c279ff63034c1` |
| successor preparation / retained original launcher inventory | `a72b94e8bedab0449e2f809a23b08ba0bff32a2d723b7be55e5ccc3123a415ba` / `36dedbdd37cba5f9077d6bf24cd1efb988bdd6b62771cfaba73ad16707055247` |
| guard CI direct Windows inventory / original archive | `8253594bb50c83201114ea306ea93d9e41db982d8a2ee389b43e9ffbe2716fdb` / `4ae0321861223181813422ddb01da19f4bbad498ebe0974b0d02b039f7ef73bc` |
| successor CI run / direct Windows inventory | `7fe6b71a8be5944cd54ca8b3fede8502ae462b89c5251642e36f6c0cd7312aea` / `6e38d03725f0c6a9e140ce4326b836907784926c69a6ddf15e22b29c1040f302` |
| successor CI original archive / complete log | `b8953f10451695a0fe0ea8d4da42e5b68a8b28fa84996ae6b8236c85e9a5c505` / `23f70b5c8652b58ebed91cfd2355a9b062995231e9ac32f44e0d8953de25430e` |
| preparation evidence inventory, 731 files | `b96a572370be262b4408704c558eb72365c046c36546552d4fd4d0f57d99f4ef` |
| privileged successor identity / scenario XML | `325859be8063bf8ab4678faabed12583993a687567bfa0b2900c284d00c242d1` / `abeb504b4e8cdf8a902648c57f75a04bdf3ec247f85e3bb393fd26c163ecef56` |
| successor wrapper result / independent physical inventory | `3bc267caad0824d4bd77bc01c5b58dc15e624340aad027f227323d3275095b88` / `642640fb4b7c8f6e1ef8d832356444915c4582912053b6877084dbc66d9d026c` |
| each filesystem's expected 330-entry manifest | `ede86fc54ce1cc7ec8aeebd067a28a3210bbee2979ccfee7071c2f20b562cc5e` |
| FAT32/exFAT observed manifest / NTFS observed manifest | `5144e092b768d002189235a7b20190ff56a56605881acff3d0770a75b00d61fd` / `5804e412173132a7fdce3fb919f79d80c3e3550268ab139388239f9bf19e32bb` |
| completed physical evidence inventory, 25 files | `56492a8704b293018b79a4cd4c5ecc2fd82b2f14583fa33b21f29b1dfd38af42` |
