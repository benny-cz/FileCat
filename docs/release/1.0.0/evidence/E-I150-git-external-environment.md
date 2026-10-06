# E-I150 — automatic Git inherits external execution settings

Preliminary native component defect evidence for I16 / V23 B10 / V24.
No desktop or installed-candidate qualification; no stable GO.

## Baseline reproduced

Source `313d40be9f84babe7bab704ce477f46bf6ca4113`, actual published
`FileCat.dll` SHA-256 `cf4e32e4dd015a167e7d9ac7448def3f2b2c3bc53ef2bd55c016227040debadd`,
native Windows 11 build 26300, `DESKTOP-A60F1NE\Admin`.
Actual `GitStatusReader.ReadAsync` is invoked through reflection from its pinned
assembly; the probe does not implement the product route. Git is
`C:\Program Files\Git\cmd\git.exe`, SHA-256 `fec691d80fccc35fcc309fbc9f720536c1d795b8a562ec169f28c9923da9600f`.

A tracked modified file selects an owned benign clean filter through its
`.gitattributes`. The repository configuration contains no filter. An owned
global configuration supplies the command in G03; inherited
`GIT_CONFIG_COUNT/KEY/VALUE` pairs supply it in G05. Both actual badge reads execute
the recorder, return an available Modified badge and complete in under one second.
The recorder writes an owned marker with PID/start time/executable identity and
copies stdin to stdout. G01 without the external settings does not execute it;
G02/G04 direct Git controls execute the same recorder. No user/global/system
configuration file was modified and the child test environment restores exactly.

This requires external/global or inherited settings; it does not demonstrate a
repository-only execution exploit. It demonstrates the wider configuration scope
that the release plan explicitly requires, beyond the repository-file guard.
Git documents [global/system configuration selection](https://git-scm.com/docs/git)
and [environment configuration pairs](https://git-scm.com/docs/git-config).

## Provenance and cleanup

Private base:
`C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\browse-traces-20261006`.
Native root `C:\FileCatReleaseValidation\git-boundary-51ad37ae9ca14205be8e88edffa83f07`. All 354 production and 546 combined payload
pins verify before/after; five retained output pins verify independently.
Native exit zero, no timeout/problems, owned processes absent. No physical disk,
GUI, device, global configuration or persistent service change.

| Raw record relative to private base | SHA-256 |
|---|---|
| windows-git-global-v2/transport-proof.json | 52178d38d0aa1a55bd04fe4090b87bda2faf2c3f476b9de3fa60d33f7d06bdd1 |
| windows-git-global-v2/retrieved/probe-result.json | 6cb49aa2c01d409c7ce1223012cec87a2b9629d6d3345da0bc810afb0630a77a |
| windows-git-global-v2/independent-baseline-v1.json | 7c49e3b7f52a7a09c976b8e48858abfe24cd29c29b102710e5c4f2cad960e4a5 |

Original `windows-git-global-v1` remains failed: its private probe used the project
name instead of published `FileCat.dll` and stopped before the API/fixture ran.
The transport observer subsequently failed on the same filename assumption;
`failed-observer-proof-v1.json` independently preserves the native failure and four
retained pins. V2 corrects only the assembly lookup/metadata in a fresh version.

## Disposition

High severity for unexpected automatic program execution; must fix. Correct child
environment/configuration boundaries and revalidate actual API, positive controls,
ordinary badges, environment preservation and exact source/native/CI outcomes.
Other indirect/reparse/protected-file/native/candidate I16 scopes remain open.

## Correction and first qualification attempt

`874b7ae826f392bfc2fb05dd16ecfc6dbe0fcc2f` restricts the child Git environment,
global/system configuration and separate ignore/attribute files. Repository
`.gitignore`/`.gitattributes` rules remain; automatic badges can differ from a
user's configured command-line status. The capability record states that scope.
Six new working-source regressions fail before and pass after; affected host
14 pass/three declared opt-in skips and full App 371 pass/23 declared skips/394
cases verify. Both compiled C# files match raw committed bytes. Markdown checkout
line endings differ explicitly; the original strict observer failure is retained.
Independent host proof `independent-git-host-v2.json`, SHA-256
`02293285f1533b40c0e43837186767815a944d4298c60d8df8f62bd6491ba49a`.

An identical native Win11 probe replaces only `FileCat.dll` in the original
354-file payload. Both actual global/inherited badge reads preserve Modified
without running the recorder; the two direct Git positives still execute it.
Same probe executable SHA-256
`fbd51d4f3ea5e05c8989becf7d7c2e12bca51643192cd35b08d0557044fad0ad`;
corrected DLL SHA-256
`10bed0818960e830db6618a3017c3583351bd280c5dec82221ee02d9f99d56ad`.
Native transport proof `windows-git-global-v3/transport-proof.json`, SHA-256
`9bdd896b0dad4bc13576a326e4c89ca15949e4d62a636f404e3158b7ae9e448f`.
Fresh clean raw export has 889 Git blobs; source ZIP SHA-256
`dee55f5114455e827aede3432503dc1e0e0f7b4063d8b853ca22f58ce6e710f0`.
Clean Windows 14/3 and Mac 13/4 declared skips pass all six new cases,
354/350 payload pins and owned process/temp cleanup. Proofs respectively
`e8e19d6bf91646d4e7bfd9643b1ca8f417cf09d4d35f5386581513e487fb7b7b`
and `0c5076f0473a06a75b29cef5a2784f88e2af830d339f326e2839e6636257b4d6`.
Mac's temporary `caffeinate -i` wraps only this completed command; no power settings
were changed. Ubuntu's original lane is not qualified: Git was absent, seven
required Git cases (all six new plus ordinary badges) skipped; the test process
exited zero but the required wrapper correctly rejected those missing controls.
Original outputs remain; Git is now installed in the disposable guest for a fresh run.

CI [37400693720 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37400693720)
on exact 874b7ae fails ARM64 while the other three required lanes pass. ARM64 logs
retain seven failures: the six new fixture setups report `NUL: Invalid argument`,
and the existing ordinary badge test gets no snapshot. Full ARM64 summary is
370 pass/17 skips/seven failures/394 cases. No passing CI or package-start claim
is made for this attempt. Logs remain in private `ci-37400693720-attempt1`.

The successor uses [Git's documented null path](https://git-scm.com/docs/git)
`/dev/null` on all platforms. Affected host 14/3/17 passes; exact successor
native/CI qualification remains required. Private preparation and unavailable-job
log retrieval failures are retained without overwriting their originals.

## Sealed successor qualification

Preliminarily remediated at `52df3d77844c79290289892a9ef8b26b18c38423`; I16 wider scopes and installed-candidate
qualification remain open. Fresh clean raw export verifies all 889 Git blobs;
source ZIP SHA-256 `bb13ef7b9e8d36d3f18e68f3af4777429425cf12b8ed72e19dc172a2b7a2e39f`, producer SHA-256
`ca23a2664d6b3e9073741fb6fcd8b7f180d38ecdabb87169d10a765104ade01c`. Clean native Windows 14 pass/three declared
opt-in skips; Mac and Ubuntu each 13 pass/four declared skips. All six new cases
pass in every native lane, with 354/350/350 payload pins before/after, retained
outputs, owned process absence and owned temp cleanup independently verified.
Ubuntu Git is 2.53.0; installation added distro git/git-man/liberror-perl in the
authorized disposable VM. The original seven missing-prerequisite skips remain
unqualified and independently sealed; no product failure inferred from them.

The identical final test DLL
`cd43db2c18abc2e64f72be9bbeb679db1e2bd991a6e0b7657a8309bd98cdf6a1`
produces six failures/eight positives/three opt-in skips with the original
313d40b App DLL, then 14 positives/three skips with the final App DLL. All 17
case names and all 354 payload files compare exactly except `FileCat.dll`.
An independent unchanged native probe also compares all 546 combined pins with
only `production/FileCat.dll` differing: both actual external filter cases
execute before and neither executes after, while ordinary Modified badges and
both direct positive recorders remain. Final actual App DLL SHA-256
`448bb5c0ced81eb4940b652c10a97865fc4053b28dd4d44a2d199dc31dc5d83c`.

[Successor CI 37401754075 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37401754075)
on exact 52df3d7 passes all four required lanes, four server artifact digests and
six complete unique TRX inventories. All 394 App names match host bytes exactly;
all six new cases pass in each retained App inventory. Windows App 377/17 skips,
Mac 346/48, Ubuntu 344/50; Windows Core 817/57, Platform 166/33, Remote 88/28.
ARM64 logs 377/17/394 plus native package start/drawing and installer compilation
pass; per-case ARM64 TRX and physical/candidate qualification are unavailable.
Unchanged archive controls all 77/0 and 30 new archive cases pass; six native PE
path case-name differences remain explicitly retained instead of normalized.
Package-release jobs are skipped on this ordinary main push; no stable publication.
The failed original CI is independently retained with three digests/six full
inventories, three passing required lanes and the original seven ARM64 failures.

Mac `caffeinate -i` existed only around each completed native Git command; a fresh
ordinary-user read independently confirms the final owned wrapper absent. No new
Mac power preference change; the prior lid/awake restoration remains in E-ENV-MAC-1.
Original assembly-name, host Markdown line-ending, private script-replacement,
collector-root and premature job-log retrieval failures remain preserved.
The corrected collector roots immutable downloads in the intended CI directory;
exact names/digests verify without rerunning the product to repair observation.

The final status observer initially counted the table's ID header as an issue after
writing these records. Its original script and failed count are retained; fresh
read-only sealing uses 150 exact unique numeric IDs and independently confirms
128 preliminary Remediated, one Closed and 21 remaining remediation. Final seal
`sealed-i150-v2.json` SHA-256
`d6fce9547f7742dd25e20ec59d5f85c222ab1bb8ec1fb51674eb753f144911c5`.

| Independently sealed record relative to private base or sibling CI directory | SHA-256 |
|---|---|
| Probe comparison: `browse-traces-20261006\independent-git-probe-comparison-v1.json` | 558e21caa419a36da4fb4f9cf2c79061cab6e8e9043b7db31d155e96846126a9 |
| Final test comparison: `browse-traces-20261006\independent-git-test-comparison-v1.json` | 412a023516da2d7925ef2df9c2ea97dadb66c7bea77650c86dbb910fdb32d4d6 |
| Working host: `browse-traces-20261006\independent-git-host-v2.json` | 02293285f1533b40c0e43837186767815a944d4298c60d8df8f62bd6491ba49a |
| Native Windows: `browse-traces-20261006\git-clean-v2\windows-executed\independent-guest-v1.json` | f7f9819d8e4bb75c76163ca2cf8b1adf6a961c48b57761bd5f006edd31dbb6e3 |
| Native Mac: `browse-traces-20261006\git-clean-v2\mac-executed\independent-native-v1.json` | 5f04bbb72100cc9a9296f98c9ba0f08e060515f552894f38e29098106f02e88e |
| Native Ubuntu: `browse-traces-20261006\git-clean-v2\linux-executed\independent-guest-v1.json` | 312b4a82f654f7619560bfc2d8fc16fbc09bb9b3a1d9becd97a6c3327fbc1229 |
| Ubuntu missing prerequisite: `browse-traces-20261006\git-clean-v1\linux-executed\independent-failed-prerequisite-v1.json` | 89c4ffd54da6bca9ba04de4cd4d81ea4121de7fcf7d0bca081338315c810ff33 |
| Successor CI: `ci-37401754075-attempt1\independent-ci.json` | afaafecc0dd9fc5a1f2ad135e3c020ebc714d0801f08d87c6f40a131d8c07541 |
| Failed original CI: `ci-37400693720-attempt1\independent-failed-ci-v2.json` | d440e3c112090cc5337cda338704c7f1e2ca734903f762a88fae050421ac8d66 |
| Mac bounded awake process absence: `browse-traces-20261006\mac-git-awake-absence-v1-stdout.log` | 68b8e9451724a564a1f0b493c82bb57ea8ff79f70f7167ffa99ec8568d98ce2b |
