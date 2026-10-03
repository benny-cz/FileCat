# E-V09-G4 — Windows source-write tracer controls

Links: V09/REC-002/TV-09, I09, I106, ENV-07. Preliminary instrumentation evidence only; no candidate exists.

The owner launches the prepared control elevated on the Windows 11 Insider 26220 host, with main at
`087917d80ff74be7549d4bb0cb49d01c6f5c9cc7`. This standalone PowerShell control does not build or run FileCat and
does not access the USB. It pins the installed Process Monitor 3.95 binary, requires its already accepted license,
refuses an existing capture and preserves the launching account's flat tracer configuration.

Run `control-8a15fbb06c284156873117bfa820f3be` starts at 12:10:13.9016861 UTC on 2026-10-03 and reports success at
12:10:52.7165503 UTC. Controller powershell.exe PID 56960 starts the owned tracer with `/NoFilter`, a native PML
backing file and a 60-second runtime bound. Its separate stop/export process records retain PIDs, start times,
parent PID, executable and exact arguments. No license acceptance or permanent tracer setting change is requested.

The controller writes 4,096 bytes of 0x5A to a unique off-source E: file, then reads it. The exported full CSV has
455,237 rows. A separate streaming Python verifier finds exactly one successful WriteFile and one successful
ReadFile for the controller PID and exact marker path, each offset zero/length 4,096. The marker bytes and SHA-256
match. A never-accessed unique path has zero events and does not exist. The verifier independently checks the
sixteen wrapper-listed files' sizes/hashes and additionally hashes result.json, seventeen files in total.

The runner verifies restoration of all original registry value types/data. A subsequent independent read compares
all thirty values against the saved snapshot, confirms zero subkeys and finds no remaining Procmon process.
No error/cleanup/restoration-error log exists. The first independent verifier's incorrect expected inventory count
is retained separately; correcting that count to the actual sixteen entries allows the full independent check.

Native PML (213,528,021 bytes), full CSV (76,502,378 bytes), original configuration, process records, marker and
verifiers remain private under `artifacts/release-evidence/v09-usb-trace-20261003`. Raw system-wide trace/configuration
contents are not committed. No event filtering or reduced export substitutes for these retained originals.

| Evidence | SHA-256 |
|---|---|
| installed Procmon64.exe, version 3.95 | `8822e28f46ba3c12256d947e5786ed30c3311c1829cf1ef86634f7fdf1a9710c` |
| bound control runner / launcher | `08535ab32b175b24c1d0fd9193dbdfdf1f6dec275a799b7b3b0d2926f44af319` / `fc81ed7681853f16ffa61d7635f9a104fc00dfeb6534614923e71ba2e75460ce` |
| native PML | `2ae1d88c8220c8657af34c814d01a313aa900eb6d20645054682998ec29803f0` |
| full native CSV | `7a55ae3570a4953f6417411c6539b4399f6fcdbdb64066b4b814d97c5c00c553` |
| control result / wrapper result | `855d7de1510105650bd23ace69dc3391b6776871577c4f08ddd5fe93958ccc98` / `6ed5878f626c00bac43c9e9d02f81af38fff86a704acde2c3c780e41a03f2aff` |
| independent event/file verification | `27ab7311c95867a33d1d95d9e295a7f55ff742dcc986284fbca648164bc83a8a` |
| independent configuration/process observation | `3609cef27cca029457146a55c76a21660b31ab5572b3c2562092e7a9a34e3f89` |

This establishes working capture/export and known filesystem read/write visibility on this installed tracer.
It does not establish raw-device visibility, complete USB source coverage, no lost events, source before/after
equality, FileCat/child/helper write safety, installed broker/consent behavior or final package qualification.
Those require separately identity-bound runs and evidence. I09/I106 remain open; recommendation remains NO-GO.
