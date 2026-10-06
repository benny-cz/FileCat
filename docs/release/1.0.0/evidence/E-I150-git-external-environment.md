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
