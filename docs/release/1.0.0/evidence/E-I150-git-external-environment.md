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
