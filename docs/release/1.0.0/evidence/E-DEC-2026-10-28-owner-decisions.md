# E-DEC-2026-10-28 — owner decisions and executed release settings

Recorded 2026-10-28 by the release session working for the owner (benny-cz). Source at recording: `main` `f986ad59`, then `0eabfc27` for the two implementation changes below.

## What the owner was asked and answered

The questions were put through the session's structured question tool, each with a recommended option and alternatives. Answers are the owner's own selections; "own words" marks a typed answer.

| ID | Question (short) | Owner's answer |
|---|---|---|
| DEC-01 | Who holds release, security and validation ownership and the final GO approval? | The owner holds all four roles. |
| DEC-02 | What support label does each platform get in 1.0.0? | Windows x64 stable; Windows ARM64, macOS (Apple Silicon) and Ubuntu 24.04/26.04 x64 as labelled previews. Stable x64 still needs a GA (non-Insider) physical Windows PC and signing. |
| DEC-03 | How is the macOS app distributed? | The ad-hoc-signed, non-notarized preview with the documented "Open Anyway" flow. |
| DEC-04 | What is the rule for the elevated helper in the framework-dependent ZIP? | No helper in the FDD ZIP; administrator retry only in the installed build. |
| DEC-05 | Media and D-56 record-field claims exceed the implementation; what is the rule? | Implement the missing D-56 record fields before 1.0.0. The media player stays out (it was not offered as an option and remains unpromised). |
| DEC-07 | Approve publishing a clearly labelled unsigned prerelease (SignPath prerequisite)? | Approved in principle once safety gates pass; the exact tag, assets and hashes still need the owner's explicit yes. |
| DEC-08 | SECURITY.md promises GitHub private vulnerability reporting, which is disabled. | Enable it; the owner responds within SECURITY.md's commitments. |
| DEC-09 | How are release refs protected while agents push to `main`? | Tag rulesets plus a gated publisher now; `main` stays open; `release/1.0` with required checks at the freeze. |
| DEC-10 | Where are sealed raw evidence and packages kept? | Read-only `V:\FileCat\artifacts` (writable only by the owner at sealing) with hashes in git. |
| EXT-02 | SharpCompress's RAR decoder mirrors UnRAR code (non-OSI), blocking SignPath's OSI-only signing. | Own words: "decide how to make RAR archives fully accessible for 1.0.0". The session chose the route recorded below. |
| EXT-04 | The AppImage runtime statically links libfuse (LGPL-2.1). | Ship with a corresponding-source bundle (pinned libfuse and runtime sources plus a rebuild script). |

## EXT-02 route chosen under the owner's delegation

RAR stays fully accessible, and the shipped code becomes OSI-licensed so the free SignPath Foundation route (D-40, DEC-07) stays open without a paid certificate:

- A FileCat-owned managed RAR reader in its own replaceable assembly. RAR 5.x is ported from libarchive's `archive_read_support_format_rar5.c` (BSD-2-Clause, Grzegorz Antoniak). RAR 2.9/3.x is ported from libarchive's `archive_read_support_format_rar.c` (BSD-2-Clause, Tim Kientzle and Andres Mejia), extended with the Itanium filter and filters inside PPMd blocks, which libarchive 3.8.1 refuses ("Parsing filters is unsupported", no Itanium fingerprint). PPMd var.H comes from `archive_ppmd7.c` (public domain, Igor Pavlov, after Dmitry Shkarin). The RAR 1.5 and 2.0/2.6 methods, which libarchive does not implement, come from The Unarchiver's XADMaster (LGPL-2.1-or-later), which makes that assembly LGPL-2.1-or-later.
- SharpCompress keeps serving 7z, xz, bz2 and zstd, built from pinned upstream source without its UnRAR-derived RAR code and its PPMd var.H code. Upstream 0.50.5 keeps the full UnRAR source under `reference/unrar` (154 files), and its RAR decoders (`Compressors/Rar/UnpackV1`, `UnpackV2017`, `VM`) were ported from it.
- Parity target: every RAR 1.5–5.x compression method, all standard filters, solid and multi-volume sets under both naming schemes, CRC32/BLAKE2sp verification, Unicode names, times and links. Encrypted members stay unsupported, as for every format; FileCat does not ask for archive passwords.
- Acceptance: byte-exact differential tests against the retired SharpCompress path on every corpus archive, with a checksum check on every member. A decoding defect therefore fails as a data error rather than producing silently wrong bytes.

**Route as built (2026-10-29).** The sources changed after this record: RAR 2.0 through 7, PPMd var.H included, was ported from rardecode v2.4.5 (BSD-2-Clause), and only the RAR 1.5 method comes from XADMaster (LGPL-2.1-or-later). libarchive supplied the BSD-2-Clause test corpus. See [E-I14-RAR](E-I14-rar-reader.md).

## Executed settings (owner-approved in DEC-08 and DEC-09)

All reads and writes used the owner's authenticated `gh` CLI against `benny-cz/FileCat` (public repository, owner user id 5889050).

Before, at about 22:20 UTC:

- `GET /private-vulnerability-reporting` → `{"enabled":false}`
- `GET /rulesets` → `[]`
- `GET /environments` → no environments
- `GET /immutable-releases` → `{"enabled":false,"enforced_by_owner":false}`

Changes and reads after:

- `PUT /private-vulnerability-reporting` (exit 0) → `GET` reads `{"enabled":true}`.
- Ruleset **26082784** "1.0.0 release tags: no update or delete", created 2026-10-28T22:28:18Z: target `tag`, include `refs/tags/v1.0.0*`, rules `update` and `deletion`, **no bypass actors**, enforcement `active`.
- Ruleset **26082787** "1.0.0 release tags: only admins create", created 2026-10-28T22:28:19Z: target `tag`, include `refs/tags/v1.0.0*`, rule `creation`, bypass actor `RepositoryRole` 5 (admin) `always`, enforcement `active`.
- `PUT /immutable-releases` (exit 0) → `GET` reads `{"enabled":true,"enforced_by_owner":false}`.
- Environment **`release`**, created 2026-10-28T22:28:31Z: required reviewer `benny-cz`, `prevent_self_review: false` (the owner pushes tags and must be able to approve), custom deployment policy, tag policy **64721622** `v*`.
- `.github/workflows/ci.yml` (commit `0eabfc27`): the `draft-preview` job, the only job with `contents: write`, now declares `environment: release`.

Limits: every agent pushes with the owner's credentials, so the creation ruleset cannot distinguish an agent's tag push from the owner's; the no-update/no-delete ruleset applies to everyone, including admins. No tag was pushed and no release created in this step, so the gate's negative and positive controls (approval wait; refused tag update/delete) remain for the first preview tag.

## DEC-04 implementation check

`eng/publish.ps1` at `0eabfc27` publishes `FileCat.PrivilegedHost` only into the self-contained payload and throws if any `FileCat.PrivilegedHost.*` file appears in the framework-dependent payload. A local `win-x64` publish of that commit in a separate worktree (`V:\FileCat\worktrees\claude-verify`, version `1.0.0-dec04.1`) produced:

- framework-dependent payload executables: `FileCat.exe`, `FileCat.ShellHost.exe`; no `FileCat.PrivilegedHost.*` file;
- self-contained payload: `FileCat.PrivilegedHost.exe`, `.dll`, `.deps.json`, `.runtimeconfig.json` present as before.

The same run then stopped at the existing dependency-notice check ("Notice package identities differ from the App dependency lock"): `licenses/dependencies/index.json` still names SharpCompress 0.50.4 while the locks request 0.50.5 since `d1757bca`. That predates this change and blocks every package build until the notice snapshot is refreshed; it is tracked with the I03 provenance work. The portable ZIP, SBOMs and FDD ZIP were therefore not produced by this run.

## Packaging recheck after the notice refresh (`c201ca71`)

`c201ca71` moves the SharpCompress notice entry to 0.50.5: NuGet content hash `wAEAxPV67rmS…5y3g==` (equal to the lock), archive SHA-256 `af0585a88897018b6e0bf34cc741d65f0c4eb676c3e807e77be1067a223c0229` (the cached `.nupkg`), and package-declared source commit `e4193af83b6ec8bdd5fb4f6ebffc2315dd3fc626`. The upstream `LICENSE.txt` at that commit is byte-identical to the retained 0.50.4 text (SHA-256 `b7ca2b6174cee11afe41d78b48527e3d7659a4435c25019a4a5e072299a2f8ed`, Git blob `94e2f5bc83ac404584fd92c25cfcc305ea2f8942`), so the same bytes move to the new revision path.

A fresh `win-x64` publish of `c201ca71` (version `1.0.0-dec04.2`, separate worktree) exits 0. It copies 56 verified notice files per payload and writes three SPDX SBOMs: setup payload 311 files, FDD 120 files, portable 308 files; all three list `SharpCompress@0.50.5`. The FDD and portable ZIP listings contain no `FileCat.PrivilegedHost` entry; the setup-payload SBOM names the helper.

| Output | SHA-256 |
|---|---|
| `FileCat-1.0.0-dec04.2-win-x64-fdd.zip` | `3e5c58876cbced15f7104c88fbf1fcca3c3e1f79d2d4e73ea6c0de3b129da16a` |
| `FileCat-1.0.0-dec04.2-win-x64-portable.zip` | `a3108d03d9a2c339c9cf6887c63d6bbd06699569ecccfa4d5f9eb42f82410704` |
| `sbom-1.0.0-dec04.2-win-x64-fdd.spdx.json` | `ce9356da76bfefc08141b4910fca8305e610fd262fccaf9e2d18898a79567c1f` |
| `sbom-1.0.0-dec04.2-win-x64-portable.spdx.json` | `0f143032b8d6a66f81bc3a47c14af70e5046fef7de72e574acd83772353b3767` |
| `sbom-1.0.0-dec04.2-win-x64-setup-payload.spdx.json` | `166482fcda4af93c3dc06546bf3d157e71e92ee28338460abdf2d58dc8b42678` |

These are local preliminary outputs (the installer and other RIDs were not built); they are not candidate artifacts. The dependency-notice manifest fixtures under `eng/validation/fixtures/dependency-notices/` still capture 0.50.4-era `deps.json` files and need requalification with the next notice qualification run.
