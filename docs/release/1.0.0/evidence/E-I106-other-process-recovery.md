# E-I106 — Recovery discovery misses a separate portable installation

Discovered 2026-10-02 after the I105 remedy, while continuing V09/I09's broader discovery audit.
Potential Critical (deleted-data safety), must fix. Status Open at discovery; no unsafe device scan performed.

SDK-free Ubuntu 26.04.1/GNOME 50.1 in the identity-bound VM of E-ENV-07. A second owned portable installation
uses the I105 working overlay: App `4e74ce23704de182115fd6f879ceded8b689aa2f4a8efe7b7b3afd53bb0ff4f7`,
Core `5b66ee2f63dc4a0f210efb64c769507693ed2bd42a7b6c3b07e0c274b3d0dbd0`, unchanged smoke probe
`f9e43055ed6649a49e21abde49a425e7c425793885d473c44e040fe16f8648df`. This matches I105 production source
at `1cd803c5b2a764572d806313228b0331c19159d0`, but is not a rebuilt CI artifact.

Actual second-installation GUI PID 12176, profile cross-2ead864b8f45489e, has a busy Data/profiles/... lease
(independent Python fcntl oracle). Its own-base probe returns true; the first installation's probe returns false.
Both return false after graceful close. The production lookup only inventories its own portable base and per-user
roots. A separate installation or another --data root is outside that inventory; absence there cannot prove that
another FileCat process is not writing to the source. Required behavior: refuse device recovery when such another
process remains live or its presence cannot be established safely. The device guard must apply regardless of the
recovering window's own state mode; image-file parsing remains separate.

Private root: artifacts/release-evidence/portable-fallback-20261002.

| Before evidence | SHA-256 |
|---|---|
| cross-installation-before-results.json | `6793d71590f95cc4d42757835be77ec53b3493c494592ac801f9a5ffd7e8292c` |
| cross-installation-before.tar.gz, 65,466,898 bytes | `4652cd9171e37cc707bfefdc2572a53d91d9930e701d82521a2f2df0dcd76998` |

Archive guest/host hashes agree. Owned fixture token 2ead864b8f45489e923055eb050db960; full payload, owned
portable/per-user scratch state, native PID/executable/window records, probe logs and driver script retained.
## Preliminary remediation and Windows verification

Device recovery now takes a read-only process census before admitting a source, regardless of the current
state mode. Another deployed FileCat process, or an unavailable census, causes refusal and asks for other
windows/helpers to finish. The usual-root checks remain as an additional control. The census does not make
a global registry or write outside the selected state roots. Ordinary election/forwarding is unchanged.

The deployed FileCat process name and, for an actual FileCat entry assembly, its own apphost/runtime name are
checked; the current PID is excluded. A shared runtime name can be ambiguous, so the message makes no claim
that another process's state is known. Arbitrarily renamed other copies, visibility across accounts and a
process starting after the snapshot are not proved by this preliminary correction; wider re-audit remains open.
Image-file parsing does not use this device gate.

Baseline guard regression: true/unknown presence both fail; absent-process control passes (1/3). After the
fix, Windows recovery subset passes 14/17 with three Unix-only skips. A controlled real child process proves
liveness detection; current PID and an empty inventory are ignored; an access-denied inventory returns unknown.
Storage-oracle tests provide a separate empty process inventory, so an owner's interactive window cannot
contaminate their storage assertions. Final Windows App suite passes 229/244, zero failures, 15 explicit skips.
Smoke tool builds with zero warnings/errors and has an other-processes diagnostic role for native validation.

| Windows result/input | SHA-256 |
|---|---|
| before/before.trx | `b46ca221a63d3da0a2c8f5d01870e7532412aa8496e3f0cace79978e80c5ea74` |
| after/after.trx, before storage-fixture isolation | `954998ea4ceb47f8c8e4c5aa6882e27bd5cda7434b618c1aefc29f20aa446820` |
| full-app-windows/app.trx, final fixture inputs | `85c01e4b2e765b1e1f0d23dfc75c38c21db134ecbc45bde700a628daa5079beb` |
| Final Windows FileCat.dll | `ed12ed6e6b28996bf750c1a40ce7b4a5e4a93254ff7a6072d2e18a56a75a986d` |
| Final Windows Core DLL | `d4c734e184cdf070c15c4ac3507fc6b93d5126342a02529c243f5ad3ac8eba15` |
| Final Windows test DLL | `628589d4b823d41b5e6d20e27d8110986981794a1869d55ea21f138d8a744ee4` |
| Final LF SingleInstance.cs | `00be298f1848c58962d9af52d488b3a4d0a083aaf32322b0a26c71eb10667353` |
| Final LF MainViewModel.Recovery.cs | `e617ec90453ff29cd211cf001463bcfcee20756df9b4d90c1dfb693e4c97f942` |
| Final LF RecoverySafetyTests.cs | `995e9320d8d7133b7e237bcb893c6251d970505b43038af622aee2d25a5b51c6` |
| Final LF Unix harness | `f915a97c55a9f05a7fee6edc69d339103c4697446202fcadfdcf9d43f60617a2` |
| Final LF smoke Program.cs | `4d03c2d28455662acd56209090a28fccd5ab6023f6821cdbd3960200590263e2` |
| final-inputs/manifest.json | `cf2ab026acc1608630cd41b3975f1212f7c6b068572842e9ac93086edc45a304` |

Windows raw root: artifacts/release-evidence/i106-other-process-20261002. Before/final source and binary snapshots
retained. Earlier targeted test inputs are not relabelled as final. Updated strict Unix inventory requires 51
results across three scenarios (47 pass/four explicit skips); this count has not yet executed for I106.

Status: remediated and Windows-verified preliminarily, **not Closed**. Native process/GUI after cases, all four
affected CI lanes, rebuilt packages, broader census audit and exact-candidate tracing remain pending. I105 CI
passes at its own source identity; it cannot qualify the new guard. No source-device scan, tag, signing or publication.
