# E-SIGN-DRYRUN — hosted Windows signing pipeline, throwaway test certificate

2026-11-20. **Preliminary automated; no FileCat signing identity was used and no signed byte left the runner.** This proves the hosted signing and verification machinery with a test signer that Windows does not trust. It does not satisfy I02, EXT-01 or `REL-SIGN-WINDOWS`.

## What runs

- `eng/publish.ps1 -Sign` calls `eng/signing/Sign-WindowsPayload.ps1` after the notice checks and before the SBOMs and archives, so every later byte pin names signed files. It records `unsigned-inputs.json` before anything is signed. It leaves vendor-signed files untouched, refuses to sign over a signature Windows rejects, and signs each distinct unsigned PE file once. Identical self-contained and framework-dependent files receive the same signed bytes. `signed-outputs.json` records every file's input and output SHA-256.
- `eng/Compile-WindowsInstaller.ps1 -Sign` gives Inno Setup a sign tool for Setup and its uninstaller, with a fresh signed-uninstaller folder per build. That folder holds Inno Setup's signature cache, which Setup applies when it writes `unins000.exe`.
- Every signature goes through `eng/signing/Invoke-Jsign.ps1`: Jsign 7.6, pinned in `eng/toolchains/jsign.json` (SHA-256 `6cf58aa080e7ff82656f320cff8d4a3bff30408df8766806dcba3740dd839fa5`). It signs SHA-256 with an RFC 3161 timestamp, from SSL.com first and DigiCert as fallback. The identity comes from the environment only: a throwaway test certificate, SSL.com's eSigner sandbox or eSigner.
- `eng/signing/Test-WindowsSignatures.ps1` verifies independently of the signing tool. Each file needs WinVerifyTrust, a recomputed image digest and the CMS signature check. A FileCat-signed file also needs the signer SHA-256 pin and exactly one RFC 3161 timestamp that covers the signature, uses a SHA-2 imprint and chains to a timestamping authority Windows trusts. A vendor-signed file must be byte-identical to a vendor input in the signing manifest. ZIPs are extracted, and a silent install, verification of the installed tree and an uninstall run on the disposable runner. Tampered, stripped, untimestamped and foreign-signed copies of real outputs must each be rejected for that reason.
- `.github/workflows/windows-signing-dry-run.yml` is manual (`workflow_dispatch`) with `contents: read`. It uploads manifests, receipts, SBOMs and the report, never signed files. The test certificate exists only in the job, and the test root is passed to the verifier as its sole custom trust anchor.

## Results

| Run | Source | Result |
|---|---|---|
| [44798673239](https://github.com/benny-cz/FileCat/actions/runs/44798673239) | `1295c69c` | Pass. 857 PE files verified, 271 FileCat-signed, 586 unchanged vendor-signed, 0 failed, negative controls 4/4 rejected. The verifier also listed the signed-uninstaller folder as a source; it contained no PE file. |
| [44800140081](https://github.com/benny-cz/FileCat/actions/runs/44800140081) | `a89a33ea` | Pass with the same counts. The uninstaller is verified only as installed, and a source with nothing to verify is now refused. |

Run 44800140081 details:

- **Toolchain:** Jsign 7.6 on OpenJDK 21.0.13, runner `windows-2025-vs2026`.
- **Sources:** both payload folders, the portable and framework-dependent ZIPs, and Setup (`FileCat-0.0.0-win-x64-setup.exe`, 81,989,392 bytes, SHA-256 `6038dbacb5324ea67b37e994750e607a292eddfec01af679ac61749bf4535a9d`).
- **Signing:** 55 distinct payload files signed and copied to 108 places; 197 vendor PE files unchanged; 300 non-PE files. All 271 FileCat signatures carry RFC 3161 timestamps from *SSL.com Timestamping Unit 2026 E1*.
- **Vendor signers:** .NET ×546, .NET DAC ×9, Microsoft Corporation ×26, AvaloniaUI OÜ ×5.
- **Installer receipt:** `Signed: true`, payload inputs unchanged during compilation, and Setup's signer equal to the run's expected test signer.
- **Install round trip:** exit 0; 248 installed PE files verified, including `unins000.exe` signed by the test signer; uninstall exit 0; installation folder removed.
- **Negative controls:** derived from `win-x64/Avalonia.Base.dll` (tampered, stripped, untimestamped) and `win-x64/av_libglesv2.dll` (foreign signer). Each was rejected for its expected reason.
- **Uploaded artifact hashes (SHA-256):** report `8e0dab2e8dc220f5beb9ed44cf334ae94a6b2eb2d653125ca4af267c5ad3b8b4`, `signed-outputs.json` `5504c4bc8983a2f48c98b701abccbf41ffbd5d63e56deef1ad80fd03c2c76457`, `unsigned-inputs.json` `4f2be2f661beff136e1386c5fd943b0d7ef1e18c89d40d3a7cedce71337a58a0`.

Before the hosted runs, local controls on the development host exercised the same scripts. They signed sample payloads through Jsign with a test PKCS#12 key, signed a file named the way Inno Setup names its uninstaller, built tampered, stripped, untimestamped and re-encoded copies, and rejected unsigned and untrusted-root files.

## Signing volume

ReadyToRun recompiles third-party assemblies and removes their vendor signatures. At least BouncyCastle, CommunityToolkit.Mvvm, SkiaSharp, HarfBuzzSharp, two Microsoft.Extensions assemblies, System.CodeDom and System.Management are affected. Those files are therefore FileCat outputs and are signed with the rest. Smart App Control blocks unknown unsigned binaries. One win-x64 package build costs **57 signing operations**: 55 distinct payload files, Setup and the uninstaller. Windows ARM64 would add about the same again.

## Certificate state

The owner obtained an SSL.com individual-validated code-signing certificate on 2026-11-20. Only its public chain was inspected, offline on the development host:

- RSA 3072, the CA/Browser Forum non-EV code-signing policy, and 365-day validity.
- Code-signing EKU only, with no lifetime-signing EKU, so timestamped signatures outlive the certificate.
- It chains through SSL.com Code Signing Intermediate CA RSA R1 to SSL.com Root Certification Authority RSA, which is in the Windows root store.

No private key is present on the development host. Key custody (SSL.com eSigner or a hardware token) is not yet confirmed. The certificate's subject and thumbprint are deliberately not recorded in this public repository.

## Not shown

- FileCat's real identity, Windows-trusted chain validation, and revocation.
- The eSigner sandbox and production paths. The sandbox needs SSL.com's published demo values as repository secrets; production needs the owner's eSigner credentials in a protected environment.
- Smart App Control, SmartScreen and ARM64.

The signed bytes stayed on the runner. The [route and stop rule](../WINDOWS_SIGNING_ROUTE_REASSESSMENT_20261031.md#2026-11-20-addendum-sslcom-certificate-and-hosted-signing-dry-run) are unchanged: no RC tag, signed-asset claim or stable publication before a real-identity run, final-byte verification, Smart App Control evidence and the owner's GO.
