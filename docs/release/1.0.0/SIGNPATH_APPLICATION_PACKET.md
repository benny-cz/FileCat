# SignPath Foundation application packet — FileCat 1.0.0

**State:** the owner submitted the SignPath Foundation application on 2026-10-30. On 2026-10-31, SignPath Foundation declined it because FileCat does not yet have enough independent public visibility and trust signals. This packet is a historical record, not an active application or signing policy. See the [Windows signing route reassessment](WINDOWS_SIGNING_ROUTE_REASSESSMENT_20261031.md). No provider, certificate or signed candidate is recorded. Stable 1.0.0 remains **NO-GO**.

## Project and public release

| Field | Verifiable value |
| --- | --- |
| Project and source | [FileCat](https://github.com/benny-cz/FileCat), public GitHub repository; owner `benny-cz` |
| Project license | [MIT](../../../LICENSE); [third-party notices](../../../THIRD-PARTY-NOTICES.md) and package-specific notices/SBOMs are shipped and indexed in the [release ledger](FILECAT_1_0_RELEASE_EXECUTION_REPORT.md) |
| Existing release | [Unsigned `v1.0.0-preview.3`](https://github.com/benny-cz/FileCat/releases/tag/v1.0.0-preview.3), public since 2026-10-30T16:17:53Z; GitHub release ID `428910888`, immutable annotated tag object `17491a88630d455fcee5506ebe3f0ec772c901b0`, source `8320e18a5bd418011a2706c18ac4bebb2e0e33c4` |
| Download and functionality | The public [download page](../../DOWNLOAD.md), [README](../../../README.md), [capability matrix](../../CAPABILITIES.md) and [preview notes](PREVIEW3_RELEASE_NOTES.md) describe the app, supported systems, unsigned status and limitations |
| Artifact evidence | [Exact 24-asset inventory and three producer manifest digests](evidence/E-PREVIEW3-TAG-20261030.md#exact-hosted-inventory-for-the-separate-public-preview-decision); [tag CI](https://github.com/benny-cz/FileCat/actions/runs/41033296020) passed all source, package and private-draft jobs; independent unauthenticated post-publication check matched all hosted names, byte sizes, server SHA-256 digests and notes |
| Security reporting | [Private vulnerability reporting and response policy](../../../SECURITY.md) |
| Privacy | [Public privacy policy](../../PRIVACY.md) covers local state, user-requested connections, opt-in update checks and diagnostic export |

## Proposed signing scope and provenance

- Windows 11 x64 is the intended stable tier: installer, portable ZIP and framework-dependent ZIP. Windows ARM64 remains a labelled preview. macOS remains an ad-hoc-signed preview; Linux has its own package forms. These are owner decisions recorded in the [blocker register](FILECAT_1_0_RELEASE_BLOCKERS.md).
- Sign only FileCat-owned Windows executables and assemblies approved by the provider. Keep upstream runtime/vendor signatures and enumerate unsigned upstream libraries separately. The installer must contain the final signed app/helper payload and a signed uninstaller; the outer setup is signed last. Recompute each SBOM and package manifest after the last byte-changing step.
- Produce unsigned inputs from the protected source/ref on GitHub-hosted runners, upload them as GitHub workflow artifacts, submit their artifact IDs using the provider's GitHub trusted-build integration, retain signing request/output IDs, and verify returned bytes, chain, timestamp, product metadata and exact package membership. The current workflow has only an unsigned packaging path; the RC unsigned-draft guard deliberately prevents it from uploading an RC as a preview.
- `release/1.0` is PR-only with five required checks; release tags have update/deletion protection. The [positive/negative control record](evidence/E-REL-PIPELINE-release-branch-20261030.md) covers those configured GitHub rules. The provider must decide whether its own source/pipeline policy accepts this setup. No RC tag is authorized until the signing path and candidate prerequisites pass.

## Provider questions and owner-only inputs

1. **Eligibility:** FileCat contains forensic file-inspection and recovery features. They inspect program structure and filesystem/security metadata but do not intentionally scan for exploitable services or exploit protections. Ask SignPath Foundation to assess this against its [no-hacking-tools condition](https://signpath.org/terms); do not infer eligibility from the repo's license or a public preview.
2. **Component/license scope:** confirm the provider's treatment of the shipped LGPL RAR 1.5 assembly, the separately sourced AppImage runtime/libfuse material, compiler/runtime system libraries, FileCat-built SharpCompress subset and Inno Setup loader/uninstaller. The [license/provenance ledger](FILECAT_1_0_RELEASE_EXECUTION_REPORT.md) records source-level work; provider/legal acceptance is not presumed.
3. **Identity and roles:** the owner is the recorded release/security/validation/GO approver. SignPath must approve the submitter, reviewer and signing-approver roles and verify the required MFA on GitHub and SignPath. The owner must provide account/project/policy identifiers and credential placement through the provider/GitHub secret settings, never this document.
4. **Public code signing policy:** The [download page](../../DOWNLOAD.md#code-signing-policy) discloses the unsigned current preview and declined Foundation application; the [privacy policy](../../PRIVACY.md) describes user-requested connections and opt-in release checks. A future provider's actual policy and attribution must be reviewed and published before offering signed binaries. No Foundation credit is currently applicable.
5. **Artifact configuration:** agree on the FileCat-owned files, consistent `ProductName`/version metadata, source/ref constraints and the signed-uninstaller workflow. Record the provider's exact artifact configuration before implementing its integration. The [current GitHub integration guide](https://docs.signpath.io/trusted-build-systems/github) requires a hosted workflow artifact ID and origin metadata; a local file upload does not prove the intended build origin.

## Stop rule

Do not create `v1.0.0-rc.N`, treat an unsigned preview as a signed candidate, or call `REL-SIGN-WINDOWS` Accepted until the provider has accepted the project and policy, the protected pipeline has passed negative/positive signing controls, and the exact final Windows package bytes pass signature, metadata, loader and Smart App Control checks. Provider refusal or an incompatible eligibility ruling returns the Windows signing route to the owner for a separate decision; it does not change the already published preview or approve stable release.
