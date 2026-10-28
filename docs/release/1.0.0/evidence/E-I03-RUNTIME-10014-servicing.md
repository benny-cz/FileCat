# E-I03-RUNTIME-10014 — applied five-RID .NET 10.0.14 servicing

I03/I14/V20, 2026-10-28. **Applied preliminary servicing, not candidate qualification.** Commit `b20f9bacdf838c19e671efbba01b078cba798dea` changes FileCat's five self-contained runtime packs from 10.0.12 to 10.0.14. This removes the current pin from both vendor-affected ranges recorded by FCSEC-AC-04. It does not create or qualify a release candidate.

## Trigger and stopping condition

The 13 October signed-XML advisory lists 10.0.0–10.0.12 as affected and 10.0.13 as fixed. The 25 October Brotli advisory lists versions before 10.0.14 as affected and 10.0.14 as fixed. FCSEC-AC-04 found no FileCat caller for either named route, but I03 still owned normal runtime servicing. This bounded subcase stops when all five official packages, their metadata hashes and signatures, exact notice bytes, fresh published runtime identities and the notice gate agree on 10.0.14.

## Official package identity

All five packages were downloaded from nuget.org. Their registration/catalog records identify version 10.0.14, MIT, publication on 2026-10-25 and source commit `0427b9abbead3fa64c6655eb4290c537397131fa`. `dotnet nuget verify --all` returned zero for every retained archive.

| RID | Package archive SHA-256 | NuGet metadata/raw archive SHA-512 |
|---|---|---|
| `win-x64` | `a3ea61b4a2da6d28e562dd1cdacf80eb4dd6b9727b0a2b47f9d6aba90dbbcb1f` | `mLWGagTP5n7nMfJQI2XpXRYgvPcnyYvAc8ZiDfum8ShM0UcrPDyfW4Lqo45UtlCHCucERUHQuMTI6ybVweCN9A==` |
| `win-arm64` | `b939042d9e9a8c1fe7b7e8632a199e6809b630faad42a35e57e92c3d3c9eed51` | `VX6JXS+AXtCL8brqXN5qvXmOzrlHOraG5T8dpdn3O1IL2knDcR1XwDNWZc2lLWuyg3u1Muu4zme2C5VnLOBoMQ==` |
| `linux-x64` | `bcd40d8b1259dc7b88a88b638aff1fa15b1563d2cf2377ea9f7650ebc4005d40` | `IfDwQKE3gcK9u7iU7C/bOaqLNfoRf6zvXzGLHsJAbsC7Shwn5ztRCRWjD1mKRk5XUrPO9VWSC1+q8oimXRDlsg==` |
| `linux-arm64` | `116dbc39c4be97ae85f44c301f9f853cc0d13053f5d84cbb7bdface970077482` | `XVcOnSgh4EnuySXtcnNRlAer7C6LtXvyT61deqzgHHt4fn0nnpgwiC2zaLkhb7vl1LT1cHeboGc/FGjB6FG5pg==` |
| `osx-arm64` | `9c15146d70feb23de1b6d27074539380f87706cf293aac173190105a55bdaee7` | `aaaa/7gTMAMy7wlLle+AU0VZhYoMUBmImsZI9zcdrpSiZvb1GVVzsyQnwp4vEg+TvPRwm6qQuWisowO1q5AhKg==` |

## Reviewed notice bytes

All ten 10.0.14 notice members are byte-identical to the previously reviewed 10.0.12 members. The repository moves those exact bytes into versioned 10.0.14 paths and pins their official package identities.

| RIDs | File | Bytes | SHA-256 |
|---|---|---:|---|
| `win-x64`, `win-arm64` | `LICENSE.TXT` | 1,139 | `d7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b` |
| `win-x64`, `win-arm64` | `THIRD-PARTY-NOTICES.TXT` | 78,041 | `6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21` |
| `linux-x64`, `linux-arm64`, `osx-arm64` | `LICENSE.TXT` | 1,116 | `cfc21f5e8bd655ae997eec916138b707b1d290b83272c02a95c9f821b8c87310` |
| `linux-x64`, `linux-arm64`, `osx-arm64` | `THIRD-PARTY-NOTICES.TXT` | 76,623 | `66f1d4e44973185519bb4aa8a9718eb22fc7af2cc532e3ae9cfc4c127ee7fc54` |

## Fresh published identity and gates

Each RID was freshly published self-contained with `ReadyToRun=false` solely to establish its complete `FileCat.deps.json` identity. Each manifest contains exactly its corresponding `runtimepack.Microsoft.NETCore.App.Runtime.<rid>/10.0.14` entry. The reviewed notice tool then accepted each actual payload and copied exactly 55 verified files.

| RID | Fresh `FileCat.deps.json` SHA-256 | Notice binding |
|---|---|---|
| `win-x64` | `a1bf4704e01b877e07b88d22b0fec4d17d4c7ebb6d608d5dbc6391af0c64f3ac` | 55 files accepted |
| `win-arm64` | `6592b0ad448cf0aabdf155656c2ffb12a8d595aaf1c84ea71b19f89310735a98` | 55 files accepted |
| `linux-x64` | `1d9780f6ff4693874633763a8d2158d17e512439fc009bee8f5f05f147e539e4` | 55 files accepted |
| `linux-arm64` | `f890a9e45c91099d499cb3ddd36d005a18ca98cb45fdffaa809f7b351fad1a81` | 55 files accepted |
| `osx-arm64` | `aa59e08d0239ae678fb01ed134270b8b352be72f55cd078777d00101c6844b5e` | 55 files accepted |

The strict notice validator passes **63/63 controls**: seven permitted combinations and 56 deliberate refusals. It proves the original inputs and historical publish fixtures did not change, adapts only their historical 10.0.12 runtime identities in memory, and records that it does **not** qualify binary composition, native licensing, desktop behavior or a candidate. Its pre-commit record names base `ed1afdad` with a dirty tree and pins every changed input byte; the applied producer is the resulting `b20f9bac` commit.

Locked solution restore passes. The complete Release solution build passes with **zero warnings and zero errors**. The five temporary build/publish trees were removed after the fresh manifests and notice outputs were retained, reclaiming about 2.16 GiB.

## Raw evidence and limits

Raw packages, registration/catalog records, signature-verification output, fresh dependency manifests, actual notice outputs, 63-control results, restore/build logs and exact hashes are retained at `artifacts/release-evidence/i03-runtime-10014-20261028`. `files.sha256.json` covers **920 files** with zero verification mismatches; its own SHA-256 is `934237adcba1470af47739de1031619b5a469e08537fc0e7c6972f97bdd1ce6e`.

This accepts only stable child case `I03-RUNTIME-10014`. It does not provide a complete SBOM, static/dynamic native composition, installer/AppImage provenance, eligibility disposition, signed artifact, exact ReadyToRun package, frozen candidate or release GO. REL-I03-PROVENANCE and REL-FQ-CANDIDATE therefore remain open/blocked at their existing scope, and the product/package change reopens GATE-CI-FINAL until the required pushed-source lanes pass.
