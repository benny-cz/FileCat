# E-I03-INNO — pinned Windows installer compiler and recipe provenance

Date: 2026-10-06. Scope: I03/I18, V19/V20/B09, plan §10.2/10.3. Preliminary build-tool correction; no candidate or stable publication.

The previous workflow used the runner's existing Inno Setup 6 through Chocolatey. Commits `472f7d01c2301d38479a6b51f6298f5a9b0b9026`, `9b0415e9193c29fc92e5b4ec4e27df126f154154` and final `0646053b3b06165465304c96430f1483a0d7991b` replace that with an exact official package, verify 119 frozen compiler inputs and retain the 122-file installed inventory. The three generated uninstaller files are inventoried but excluded from static pins. The compiler helper hashes the actual published payload, recipe/license/icon, checks them unchanged after compilation, revalidates the frozen compiler and retains the invocation, exit, logs and produced installer size/SHA-256. Existing output is refused. Main's ARM64 build retains these receipts; tag packaging uses the same helpers. Broader release-control gaps remain.

Official [immutable 6.7.1 release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_1): release ID 287210512, asset ID 357294783, `innosetup-6.7.1.exe`, 10,619,024 bytes, SHA-256 `4d11e8050b6185e0d49bd9e8cc661a7a59f44959a621d31d11033124c4e8a7b0`. The official metadata digest and downloaded bytes agree. The installed `ISCC.exe` SHA-256 is `eb6f4410c8db367a5f74127e8025ad2ccacc0afabbe783959d237df3050f97fb`; its PE FileVersion/ProductVersion are **0.0.0.0**, while actual compile output reports Inno Setup 6.7.1. Consequently identity uses content pins. Native Authenticode status Valid, signer Pyrsys B.V., certificate thumbprint `E0AB19C8D38CBF9C44709925122A7A02F8C70CB7` is retained as corroboration.

The [exact upstream license](https://raw.githubusercontent.com/jrsoftware/issrc/is-6_7_1/license.txt) is stored without Git newline conversion and installed at `licenses/InnoSetup-6.7.1.txt`, SHA-256 `2e5346868c2a18434489824e11d65c3031620f792fefc415d05f19cd441abf5c`, matching the native tool's license bytes. THIRD-PARTY-NOTICES now accounts for redistributed setup/loader/uninstaller code and resolved Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2. The compiler's non-commercial banner prompted an upstream check: [purchase policy](https://jrsoftware.org/isorder.php) says purchase is not strictly required; no paid-use prerequisite was inferred from the banner. This does not settle broader I14/provider eligibility or complete runtime notices.

Native VMware Windows 11 Insider build 26300, ordinary headless guest operations as Admin with a full token, PowerShell 5.1. Final-source run exports **11 exact raw Git files** from 0646053, verifies them before/after and performs nine successful steps:

- One-byte-altered official package is refused before tool/receipt directories exist.
- Production acquisition verifies all 119 frozen pins and retains 122 installed files.
- A second acquisition refuses the shared per-user registry installation before mutation.
- One-byte-altered ISCmplr.dll is refused; original bytes restored and all pins revalidated.
- x64 and ARM64 owned control scripts compile with exit zero.
- The actual production installer recipe compiles on two deliberately inert six-input payloads, records exact output identity and refuses an existing output on each architecture.
- Owned compiler uninstalls with exit zero; tool files, per-user compiler registry entries and owned processes are absent.

The two recipe controls contain inert text named FileCat.exe and are never executed. They validate recipe/receipt behavior, **not a real FileCat package, installation lifecycle, ARM64 execution or native GUI**. Exact-source CI 37410913442 attempt 1 is still running; its real ARM64 publish/compile and five server artifact digests will be sealed separately. No result is inferred while pending.

Private retained root: `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence`. Final native root: `C:\FileCatReleaseValidation\inno-acquire-0845b722fdbf4a96b5a7466b87ffbcba`. Raw source/transport/result inventories are retained. The independent verifier checks all 19 output-file pins, all 119 frozen input pins and 122 inventory entries.

| Relative private path | SHA-256 |
|---|---|
| `compiler-provenance-20261006-v3/upstream-release.json` | `c53c921e7989f43354f2b9581d7fcff77489e0b1e3f4b9ba6afe1e8a7c3f2647` |
| `compiler-provenance-20261006-v3/innosetup-6.7.1.exe` | `4d11e8050b6185e0d49bd9e8cc661a7a59f44959a621d31d11033124c4e8a7b0` |
| `compiler-provenance-20261006-v3/independent-compiler-v1.json` | `cbf1846cda51c559a9c0461762a9b2719df52b8ecfa9119b7d626669f13dd792` |
| `pinned-compiler-20261006-v2/independent-pinned-compiler-v1.json` | `40d452147c49a3d373961e0fb120127caa324a6a1b71659371218b6db4e8ba31` |
| `pinned-compiler-20261006-v3/independent-pinned-compiler-v1.json` | `10f3b053b56c74e728089fc3a157c96e0fc17fbdf2545be8905e4ef86983b6b1` |
| `pinned-compiler-20261006-v4/independent-pinned-compiler-v1.json` | `107f214e67209b67102c4e044eaceb245fa751de38a096551fa7754d017a1950` |
| `pinned-compiler-20261006-v5/inputs.json` | `1095a2c970b54ab9b25c6e6e4dae479f8af957fb01cbde79397afec5c792b38b` |
| `pinned-compiler-20261006-v5/native-run.ps1` | `c2630958d816988eb286e0e1b61e6daa1ef2c06067ee323c5c131b6656d503fe` |
| `pinned-compiler-20261006-v5/outputs.zip` | `38498f73b9dfd3871e519f4a862bf8fa7db43de9c0d29f0d1f3541fbe747da34` |
| `pinned-compiler-20261006-v5/retrieved/result.json` | `1fa48404d19b25d7b04e7554d3c1389ad1581882c04a2f19c797e212a8926698` |
| `pinned-compiler-20261006-v5/independent-pinned-compiler-v1.json` | `bbd5e5297d78cfa98433d3bb609cb6901c62272b1f17cd5a37d365eeb19b61d8` |

Original compiler probes v1/v2 and failures remain: native help exit 1 was initially mishandled by strict PowerShell native stderr; finally blocks uninstalled both owned tools. A decorated Get-Content string then generated a 312 MB observer JSON; bounded partial transfers, original metadata, failures and corrected plain IO.File.ReadAllText observations are retained. Fresh v3 uses ProcessStartInfo, requires actual compilation exits zero and verifies cleanup. The first pinned-helper prelaunch validator counted seven expanded jobs rather than six YAML definitions; no guest operation ran. A separate source comparison helper lacked its repo variable; its fresh exact-source driver had already been written and ran independently. Both helper failures are annotated, never passed as product results. Working/committed newline differences are preserved; raw committed v3/v5 repeats verify actual Git bytes. The final license preservation is explicitly re-staged under -text.

Broader I03/I18 remain open: reproducible restore/full per-artifact native/runtime/helper SBOM, other SDK/actions/server inputs, final binaries/loaded libraries/signatures, candidate promotion and immutable assets, branch/tag controls, platform clean lifecycle and external signing/owner decisions. Notices and installer-byte changes invalidate earlier installer/candidate qualification. Counts stay 129/151 preliminary remediations, one Closed and 21 remaining; all 24 campaigns need final qualification. No stable GO.
