# E-SIGN-DRYRUN — hosted Windows signing pipeline: test-certificate dry runs and the eSigner pilot

2026-11-20. **Preliminary automated; no FileCat signing identity was used and no signed byte left the runner.** This proves the hosted signing and verification machinery with a test signer that Windows does not trust. It does not satisfy I02, EXT-01 or `REL-SIGN-WINDOWS`.

## What runs

- `eng/publish.ps1 -Sign` calls `eng/signing/Sign-WindowsPayload.ps1` after the notice checks and before the SBOMs and archives, so every later byte pin names signed files. It records `unsigned-inputs.json` before anything is signed. It leaves vendor-signed files untouched, refuses to sign over a signature Windows rejects, and signs each distinct unsigned PE file once. Identical self-contained and framework-dependent files receive the same signed bytes. `signed-outputs.json` records every file's input and output SHA-256.
- `eng/Compile-WindowsInstaller.ps1 -Sign` gives Inno Setup a sign tool for Setup and its uninstaller, with a fresh signed-uninstaller folder per build. That folder holds Inno Setup's signature cache, which Setup applies when it writes `unins000.exe`.
- Every signature goes through `eng/signing/Invoke-Jsign.ps1`: Jsign 7.6, pinned in `eng/toolchains/jsign.json` (SHA-256 `6cf58aa080e7ff82656f320cff8d4a3bff30408df8766806dcba3740dd839fa5`). It signs SHA-256 with an RFC 3161 timestamp, from SSL.com first and DigiCert as fallback. The identity comes from the environment only: a throwaway test certificate, SSL.com's eSigner sandbox or eSigner.
- `eng/signing/Test-WindowsSignatures.ps1` verifies independently of the signing tool. Each file needs WinVerifyTrust, a recomputed image digest and the CMS signature check. A FileCat-signed file also needs the signer SHA-256 pin and exactly one RFC 3161 timestamp that covers the signature, uses a SHA-2 imprint and chains to a timestamping authority Windows trusts. A vendor-signed file must be byte-identical to a vendor input in the signing manifest. ZIPs are extracted, and a silent install, verification of the installed tree and an uninstall run on the disposable runner. Tampered, stripped, untimestamped and foreign-signed copies of real outputs must each be rejected for that reason.
- `.github/workflows/windows-signing-dry-run.yml` is manual (`workflow_dispatch`) with `contents: read`. It uploads manifests, receipts, SBOMs and the report, never signed files. The test certificate exists only in the job, and the test root is passed to the verifier as its sole custom trust anchor.

Since `af71f8df` this runs as two jobs, with credentials kept away from the build; see [Credential-isolated two-job pipeline](#credential-isolated-two-job-pipeline-and-the-esigner-pilot).

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

## Credential-isolated two-job pipeline and the eSigner pilot

The dry run now runs the way a real signing run does, on two machines:

- **Build job, no credentials.** `publish.ps1 -PayloadOnly` publishes and checks both win-x64 payload folders. `eng/signing/Invoke-WindowsSigningRun.ps1 -Phase Build` writes `payload-manifest.json` with every file's size and SHA-256 and reports the manifest's SHA-256 as a job output. The unsigned payload goes to a one-day workflow artifact; it carries no signature or identity.
- **Signing job.** `Prepare` checks the download against that SHA-256 and every manifest entry. It then does a locked restore, which runs no package code, so the SBOMs can check restored archives; acquires Jsign and Inno Setup; and makes the test signer. Credentials reach only `SignPayload` and `SignInstaller`, which run Jsign and the pinned Inno Setup compiler but no NuGet or MSBuild code. `Package` (`publish.ps1 -FromPayload`) writes the SBOMs and archives and shuts down build servers before the installer is signed.
- **Canary.** With a pinned signer (`FILECAT_EXPECTED_SIGNER_SHA256`), one canary file is signed and checked before the rest. In a local control, a wrong pin stopped after exactly one signature.
- **The pilot.** `.github/workflows/windows-signing-pilot.yml` runs this with FileCat's eSigner certificate. `eng/signing/Assert-SigningEnvironment.ps1` refuses to continue unless `signing` exists, requires a reviewer, refuses administrator bypass and allows only `main` and `v*` tags. The verifier also checks revocation and prints no signer identity (`-PrivateIdentity`). Signed files, manifests and the report go only into a private draft release.

| Run | Source | Result |
|---|---|---|
| [44803829456](https://github.com/benny-cz/FileCat/actions/runs/44803829456) (pilot) | `af71f8df` | Refused at its first step because `signing` does not exist. No build or signing ran, and afterwards the environment still did not exist, so no unprotected environment was created. |
| [44803783149](https://github.com/benny-cz/FileCat/actions/runs/44803783149) | `af71f8df` | The build passed, and the signing job matched all 605 downloaded files to the manifest. The locked restore then failed (NU1004), because `dotnet restore -r` replaces the projects' locked runtime list. Fixed in `21b4c683` by passing RuntimeIdentifier, as `dotnet publish -r` does. |
| [44806445513](https://github.com/benny-cz/FileCat/actions/runs/44806445513) | `21b4c683` | Payload signing with the canary, packaging and installer signing passed. Verification failed because `publish.ps1 -FromPayload` deleted the signing manifests. Fixed in `e18125e5`. |
| [44809030050](https://github.com/benny-cz/FileCat/actions/runs/44809030050) | `e18125e5` | Pass, with the same counts as the single-job runs. |
| [44812780579](https://github.com/benny-cz/FileCat/actions/runs/44812780579) | `24f2e661` | Pass at the final source. The log now shows Jsign's 55 per-file lines, with the canary first. |

Run 44812780579 details:

- **Payload manifest SHA-256:** `67C8996D823FCDE7C5035C10ECD7BA9600B63DC9658648E6DC181BD4B24D273B`.
- **Setup:** 81,984,296 bytes, SHA-256 `965506497889b36ee69d40417d315970de1934ea4c875166810d1ba77ade211e`. Signed by the pinned test signer, with payload inputs unchanged during compilation.
- **Verification:** 857 PE files, 271 FileCat-signed, 586 vendor-signed, 0 failed; negative controls 4/4 rejected.
- **Install round trip:** 248 installed PE files, including the signed `unins000.exe`; folder removed.
- **Hashes (SHA-256):** report `80136710a7a756ef552e4f3224ce9834a5d229bec14ce549fc755c4cdcfe53c0`, `signed-outputs.json` `77768f7d8e3f8e739b7f9c4ab797ab883fd2bccb56fe462c631395cd0da2dc3f`, `unsigned-inputs.json` `4d7efb7144b2f25b49b6df880bb1cf94017cbc5e929fb1148df02f00d23023cf`.

Not yet shown: everything eSigner-specific. That covers authentication with the account's TOTP secret, the credential ID, whether the account makes SSL.com scan files before it signs their digests, Windows-root trust with revocation, and the private draft. The first pilot run tests these.

## Real-identity eSigner pilot

The owner confirmed eSigner custody: the order uses eSigner with an OTP-app second factor, so the key is in SSL.com's cloud HSM, not on a token. The owner added the five `signing` environment secrets and approved each signing job. Agents never approved a deployment. In every run, the guard passed against the real environment.

| Run | Source | Result |
|---|---|---|
| [44817968726](https://github.com/benny-cz/FileCat/actions/runs/44817968726) | `56cae306` | Cancelled by the owner before the build, to add the missing credential ID. Nothing was signed. |
| [44818777169](https://github.com/benny-cz/FileCat/actions/runs/44818777169) | `56cae306` | Payload, Setup and uninstaller signed with FileCat's certificate, canary first. Verification against Windows' roots with revocation passed: 857 PE files, 271 FileCat-signed, 586 vendor-signed, 0 failed, 4/4 controls rejected, install round trip. Creating the draft then failed with HTTP 403: GitHub refuses a workflow token a release whose target commit's workflow files differ from the default branch, and `8023853a` changed `ci.yml` on main during the run. Fixed in `aa5b1ca4`: the draft targets the default branch, creates no tag, and names its source in its notes and `run.json`. |
| [44821102973](https://github.com/benny-cz/FileCat/actions/runs/44821102973) | `aa5b1ca4` | The canary and 11 more files were signed. eSigner then answered HTTP 504 on its pre-signing hash scan (`cs.ssl.com/scan/hash`), and Jsign retries only timestamps. Fixed in `e31e3187`: up to four attempts, each sending only the files still unsigned. |
| [44822592170](https://github.com/benny-cz/FileCat/actions/runs/44822592170) | `e31e3187` | Pass end to end with no retries needed, and the same verification counts. Private draft release 453215135 (`signing-pilot-44822592170`, target `main`, no tag created) holds 13 files. |

**Independent re-verification on the development host** after downloading the draft:

- All 13 assets match GitHub's server-side SHA-256 digests and sizes.
- `run.json` records ESigner mode at `e31e3187`, pinned to the certificate whose public chain was inspected locally.
- `Get-AuthenticodeSignature` reports Setup as Valid: issued by SSL.com Code Signing Intermediate CA RSA R1, signed by the pinned certificate, timestamped by SSL.com Timestamping Unit 2026 E1.
- `Test-WindowsSignatures.ps1` with revocation over Setup and both ZIPs found 304 PE files: 107 FileCat-signed, 197 vendor-signed, 0 failed; 4/4 controls rejected.

**Asset SHA-256:**
- Setup (82,012,152 bytes): `d164b14224dd48af0c7f9ccd61da3fbb6338b0eca1175954eea542d29f111dd0`
- Portable ZIP: `5e8e2469467d35276c8f9c75f589ad0a2879c04bf8ce1d014b0a7a61f5047df1`
- Framework-dependent ZIP: `a1e104b1491c412aa72aea5147daed5def5c8f09818cb4b3217234c812f7e104`
- CI report: `f772305c438bf4dde2a02e42d3e54d64270ac8e34ac6ed04e0a749b56ccbcf35`
- `signed-outputs.json`: `11ef8e49b5ef3a8783549758c6d8878c4fac13b048cf26acd5a3bdef161b3aae`

**eSigner behavior observed:**
- The account requires SSL.com's pre-signing scan, which Jsign performs on each digest.
- The service can answer a transient 504.
- The pilot runs used about 126 signings in total.

**Not shown:**
- Smart App Control on the GA tier (a preliminary Insider check follows), SmartScreen reputation, ARM64, a GA physical Windows x64 PC, and the signed release pipeline itself.
- The owner's decisions on publishing the certificate subject and on replacing the SignPath-specific contract (both made later on 2026-11-20: [DEC-13 and DEC-12](E-DEC-2026-11-20-signing-decisions.md)).

The draft stays private and is deleted after the owner's checks. No RC tag, candidate or public signed asset exists.

## Smart App Control check (preliminary, Insider VM)

On 2026-11-20 the owner switched Smart App Control on in the Windows test VM: Windows 11 Insider 26H2, build 26340.10013, under VMware. `citool -lp` showed `VerifiedAndReputableDesktop` enforced and authorized, and the registry showed `VerifiedAndReputablePolicyState` 1. Before each step the guest ran no `dotnet` process, so no other worker's run was disturbed. The pilot draft's Setup and portable ZIP went in through VMware Tools.

**Signed pilot files.** Launches ran in a batch session with PowerShell in FullLanguage mode.

| Launch | Result |
|---|---|
| Setup, silent into a test folder | Exit 0, so Inno Setup's extracted engine was allowed |
| Installed `FileCat.exe` | Ran for 25 s with 82 modules from its own folder loaded |
| Installed `FileCat.PrivilegedHost.exe` | Ran |
| Installed `FileCat.ShellHost.exe` | Started and exited 2, its usage code |
| Portable `FileCat.exe` | Ran for 25 s with 82 modules from its own folder loaded |
| Uninstaller | Exit 0; removed the files and the uninstall entry |

Code Integrity logged 0 events and Defender logged 0 events during this run.

**Controls in the same session:**

| Control | Result |
|---|---|
| Unsigned copy of the signed `FileCat.ShellHost.exe` (signature stripped) | Refused: "An Application Control policy has blocked this file" |
| Tampered copy (one byte of the COFF header) | Refused, same message |
| Unsigned copy with a download mark (`ZoneId=3`) | Refused, same message |
| Signed `ShellHost.exe` on its own | Started |
| Signed portable `FileCat.exe` with a download mark | Started |

Each refusal logged events 3077, 3033, 3089 (twice) and 3118. All 15 Code Integrity events name only the three controls; none names a signed file. Enforcement was therefore active and discriminating, and FileCat's signed files passed it.

**Limits:**
- It is an Insider build in a VM, not the GA tier the release requires.
- Launches used `Start-Process` in a batch session, so the download prompt Explorer would show (SmartScreen) is not covered.
- Only startup was exercised. Smart App Control checks each binary as it loads, and the verifier shows every shipped PE file carries a trusted signature, so binaries this check did not load meet the same rule.
- Smart App Control stays on in the VM by the owner's choice. While it is on, other unsigned test builds there are blocked.

## Candidate signing in the tagged pipeline

2026-11-23. [PR #7](https://github.com/benny-cz/FileCat/pull/7) brings the piloted signing into the tagged pipeline. It merged into main as `3069448f` and reached `release/1.0` through PR #8. Nothing ran it for real until `v1.0.0-rc.1` on 2026-11-24 ([rc.1](E-RC1-TAG-20261124.md#tagged-run)).

### What a `v1.0.0-rc.N` tag now runs

- **After the tests.** As for every tag, the package jobs start only after every test job has passed.
- **Package job.** It checks the `signing` environment's protections, then publishes both Windows runtimes unsigned and hands them to a new `sign-candidate` job with the payload manifest's hash. The unsigned installer, asset-selection and Windows upload steps run only for other tags.
- **`sign-candidate`.** It runs on a fresh `windows-2025-vs2026` runner in the protected `signing` environment. The owner approves each run, administrator bypass is off, and only main and `v*` tags deploy. It checks those protections again (`Assert-SigningEnvironment.ps1`), then checks the payload against the package job's manifest.
- **Signing.**
  - It signs the win-x64 and win-arm64 payloads.
  - It writes SBOMs and archives of the signed payloads.
  - It signs both Setups and their uninstallers.
  - The eSigner credentials reach only the two signing steps.
- **Verification.** Against Windows' roots with revocation, it checks both payloads, the ZIPs, both Setups and a silent install and uninstall of the x64 Setup. The runner is x64, so the ARM64 Setup is verified as a file. The four negative controls run as before.
- **Private drafts.** Nothing is published.
  - The signing records, which name the signer, go into one private draft, `signing-records-<tag>-<run>`.
  - The 13 signed Windows assets join the Linux and macOS producer outputs, and all three producer manifests are checked.
  - The 24 assets go into the candidate's own private draft. Its upload IDs and server digests are then checked.
- **Draft checks.** `Assert-DraftReleaseAssets.ps1` and `Invoke-DraftPackageChecks.ps1` gain `-SignedCandidate`. It accepts only a `v1.0.0-rc.N` tag and refuses to reuse a draft that is not a signed-candidate draft. Previews keep the unsigned draft path, still made by `draft-preview` in the `release` environment. The validators now hold 42 and 17 cases.
- **Signing run script.** `Invoke-WindowsSigningRun.ps1` takes `-Runtimes`, `-ProductVersion` and `-CandidateRef`, and its payload manifest is schema `filecat-payload-manifest/2`. The manual dry-run and pilot workflows gain a runtimes input.

### Signing volume

Each runtime needs 55 distinct payload files, Setup and the uninstaller signed, so a candidate costs **114 signing operations**.

### DEC-09

DEC-09 makes drafts in the `release` environment. PR #7 proposed an amendment:
- For RC tags, the owner-approved `signing` job creates the candidate's private draft, so signed bytes never pass through workflow artifacts.
- Previews keep `release` as their only release-write environment.

The owner had PR #7 merged on 2026-11-23. On 2026-11-24 they accepted the amendment by answering yes to tagging `v1.0.0-rc.1`, a question that said the yes would be recorded as this decision ([owner decision](E-RC1-TAG-20261124.md#the-owners-decision)).

### Test-certificate dry run of the candidate path

[Run 45243254377](https://github.com/benny-cz/FileCat/actions/runs/45243254377) ran on `1a0a431c` (branch `signing/candidate-pipeline`) with both runtimes. Both jobs passed: the unsigned build for win-x64 and win-arm64, and the signing job.

- **Verification:** 1,466 PE files: 486 FileCat-signed, 980 unchanged vendor-signed, 0 failed. Negative controls: 4 of 4 rejected.
- **Signing:** 55 distinct files signed per runtime.
- **Install round trip:** the x64 Setup installed, with 248 installed PE files verified. It uninstalled with exit 0, and its folder was removed.
- **Test signer:** it and its root stayed in the job, and no signed byte was uploaded. Revocation is not checked against a test root.

### Not shown

- Installing the ARM64 Setup, and Smart App Control on ARM64.
