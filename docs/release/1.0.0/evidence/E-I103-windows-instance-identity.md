# E-I103 — Windows instance identity disagrees with its profile folders

Discovered 2026-10-02 on the Windows execution host, after I102. A profile and its uppercase alias share the same
case-insensitive state directory. The ordinary instance owns a raw-case-derived mutex, so the production usual-instance
probe for its case alias returns false. Baseline regression fails 1/1. V09/I09 recovery safety impact; must fix.
This is controlled instance detection, not a physical-device scan. No candidate or GA qualification claim.

| Baseline record | SHA-256 |
|---|---|
| artifacts/release-evidence/i103-windows/before.trx | `971b98229a1697e6caf39ca475dd6cf797549fa2623e31228240464a7793521c` |
| Working Windows FileCat.dll | `018d1721a586187cb4c7e261f0344e5203d3bb1b4476a5cdec3f4a5b916ba2a8` |

Source baseline 5b69fba plus the recorded regression. Status Open at discovery. Required remedy: derive Windows
identity from the actual selected state directory, with alias normalization, while keeping distinct state roots distinct.

## Process reproduction and remedy

Four fresh-process production API cases reproduce the case alias under both usual and --data roots, and the
portable/usual collision. Only the distinct default versus profiles/DEFAULT state-root control passes before (1/4).
The portable server's retained log reports "Fixture profile already running"; the harness's ready-file wait expires.
This confirms an existing owner from another state root accepted the launch. No desktop/physical-device experiment
is claimed by this harness.

Windows now hashes the actual selected LocalDirectory for its mutex/pipe, normalized as a full Windows path.
Case aliases share an owner. Portable/usual roots and the literal default/profiles/DEFAULT roots stay separate.
The owner retains that selected identity through server startup; the usual-instance probe calculates it without
creating state and retains a read-only legacy-name check. Mixed-version forwarding and every filesystem alias
are not claimed qualified by these cases.

| Input/payload | SHA-256 |
|---|---|
| LF SingleInstance.cs | `2170d38b9a6b014b5c06f6eb7b61e731c76b7803a67e962c664935087c58d433` |
| LF RecoverySafetyTests.cs | `4c18f16795f6765b36e3f909ca0747aebe20bf99b346fd6891102028a7378510` |
| LF Unix boundary harness | `8bd159d2a6684a43efafa6150f0d45d6017ca64d8d6c21f19310de24d0f2be8c` |
| Windows process harness source | `bbec446a7d1b90a2943bba20a16728a7f11b17845d18cf1a1ea51007749bec37` |
| Working Windows FileCat.dll | `41e4a30aaa51f9e8d6bb92f2b26c5ca89630f426c41a34538c9e5ebc26012256` |
| Working Windows smoke DLL | `5f306ee5ee8b8a6a4180288942b34ef3d7e3988e84118ef4bc76e8d6b4b14407` |
| Native Unix FileCat.dll, after normalized replacements in retained I102 tree | `8351192b4e9659a4a56d6e2311a413b5820e74fbe916f88be8913a4e9adfbbe4` |
| Native Unix test DLL | `0f4bbf74c9afbf07c24ff80004337e475a74d7ae6e9573b6ff53aeb2f470746a` |

| Result | Outcome | SHA-256 |
|---|---|---|
| i103-windows/process-before/results.json | 1/4 pass; retained failures | `87bb678ec3be0464f97569b39f264a207b8978f01fecac53c559dae872ffc9af` |
| i103-windows/process-after/results.json | 4/4 pass; correct Unicode arguments reach each owner | `b8ed2bc03755d3459b5de1c854741f19ebaf19ab9cc1fa21670d916b190b43f5` |
| i103-windows/after.trx | Targeted guards: 6 pass, 3 Unix skips | `bd47a6d3ce91ff95a88a38396b38f065ca655020a4738685287647358aebc839` |
| i103-boundary-r1/results.json | Ubuntu: 23 pass, 4 explicit skips (Windows-only plus edge fallback) | `6a028d2078912db257650c3e6a5cb8d0fe24d4f65240742c2c320ea5fa427cd4` |

Original Windows DLL/test inputs, full before/after fixture bundles, owned profile state snapshots, logs and payload
hashes retained privately. Ubuntu record copied to host and hash-verified. Unix protocol is unchanged; its strict
boundary/guard harness passes after rebuilding, with the new Windows-only test explicitly skipped. Full affected
Windows suites and macOS boundary cases will run in CI; the Windows process harness now runs there and retains
case logs and JSON records. This is preliminary Windows Insider host evidence; no GA release qualification claim.

Status Remediated and verified preliminarily, not Closed. CI, rebuilt packages, wider alias/session/installation
discovery, re-audit and exact candidate evidence remain pending. Nothing was signed or published.
