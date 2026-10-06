# E-I18-P4 — read-only package producers and isolated draft consumption

2026-10-06. Preliminary partial I18 correction, baseline `ecac41c`; committed
source `246ce18993d9238227182e22a3150f3a032e0154` is sealed through original
[push 37467658278 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37467658278)
and [development dispatch 37467776324 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37467776324).
All three package builder jobs at the baseline
have `contents: write` and also mutate releases. Plan §10.2/§11.2 requires
read-only builders and separated release-write authority.

The three producers now inherit the global read-only repository permission.
They build/package/upload their existing exact artifacts and expose only the
selected manifest digest as a job output. A separate draft-only job depends on
the policy, all four required test lanes and all three successful producers. It
alone receives release-write permission. Stable references still fail at the
read-only policy ancestor; main/branch development runs skip the draft job.
Checkout credentials remain disabled everywhere.

The draft job downloads three exact artifact names from its own workflow run into
distinct platform directories. The official download action is pinned to
`3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c` (v8.0.1); artifact digest mismatches
fail ([pinned action inputs](https://raw.githubusercontent.com/actions/download-artifact/3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c/action.yml)).
No cross-run token/run override or artifact glob selects input. A shared wrapper
checks all three producer-digest manifests through E-I18-P3 before emitting
exactly fifteen paths (twelve package/inventory files and three manifests). One
replacement-disabled action uploads the unsigned draft. Postflight validates the
combined uploaded set, unique IDs/names across platforms, each platform's bytes
and refreshed remote identities. No build, signing or repackaging occurs in that
job. Successful and partial check receipts are retained without deleting assets.

Thirteen host synthetic cases pass: complete preflight/output and upload,
missing platform, wrong manifest pin, changed final-platform bytes, existing
final-platform asset, incomplete/extra uploaded sets, cross-platform reused
IDs/names, wrong-platform output, changed final-platform remote digest and stable
refusal. A denied final platform cannot emit the combined action file list.
Independent YAML inspection verifies the sole writer, three read-only producers,
all prerequisite edges, nine expanded jobs, exact pinned current-run downloads,
disabled credentials/replacement, ordered checks and no failure bypass/build or
signing step in the draft job. Both new scripts parse. All four hosted test lanes
execute and retain these controls on the committed source.

Both committed runs pass the policy and four required test lanes; ARM64 startup,
drawing and installer compilation pass. Their fifteen/nineteen selected artifact
ZIP server digests, four/six clean exact-SDK build receipts, fourteen complete TRX
inventories per run/all 409 App identities, sixteen picture cases, sixteen policy
controls, 128 draft controls and 52 combined-set controls per run verify against
source/run pins. Push skips all package/draft jobs. Development dispatch builds,
installs/starts and verifies actual Linux tar/deb/AppImage packages and the Mac
ad-hoc signature/start/icon frames under read-only permissions; retrieved outer
artifact bytes and every selected package manifest hash match. Its Windows tag
package and draft job skip. No tag, draft action or public release runs. These
development packages have their recorded development version, not a 1.0.0 candidate.
The original failed main run `37454794034` remains retained.

This is separation for unsigned preview drafts. No preview becomes public, and
no tag/release or production signing request is made in this slice. Approved final-manifest-only
promotion, human preview/signing/stable approval, protected refs/environments,
immutable owner storage/releases, actual tagged transport/upload, final download
verification and candidate qualification remain Open. Provider/DEC-09 decisions
are not inferred from a passing test. Permission semantics are documented by
[GitHub](https://docs.github.com/en/actions/tutorials/authenticate-with-github_token).

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| draft-set-host-v1/draft-package-set-controls.json | bf0e0d008aec575ab46062cb16dbdbedf4dd1f8ed035b5a632a0274d498cb424 |
| draft-set-host-v1/independent-separation-v1.json | 417f3b6af4ca1cb5c9901d4a4e0f20fc1480100887d46283e6120db3e77d6e03 |

Private `FileCatReleaseEvidence/ci-37467658278-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | b9c8bf0cafaa50cdaa6a72046db3f254fa81442dec94754f431d9a94f0b5b623 |
| independent-fixture-ci-v1.json | 4e5a780ceee62a51c8c5cef36d0d89955093eb0c435c16e131ce010409f855ce |
| independent-producer-policy-ci-v1.json | 31c7e3beaf8dc25219af315dd904e7d94d3d25fd7c0a9ba050da731cf48e9811 |
| independent-draft-guard-ci-v1.json | 41f0a8fe256d7a25bfbd1d05c2c8845adf12f4f8b7851e19505fc5ec979cfa84 |
| independent-separation-ci-v1.json | 3bfcca884790c208f59b04a6dd8237ac6644e598275bbe5b8868d267cce396bc |

Private `FileCatReleaseEvidence/ci-37467776324-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 8fc985a263f56cf828305c2da07097675b52d0e83b43a2c556d0a724406ad872 |
| independent-fixture-ci-v1.json | 2853d4ad1d3844d74dad4f05ba5de09413fb2caa992dd1881826d46b5e889c35 |
| independent-producer-policy-ci-v1.json | 5a82a29479a9878a33c2781352d779043de87f646f5e3a03a0a872e0f96a4eee |
| independent-draft-guard-ci-v1.json | 826ff86c18637f62b8e73e190889d508dfd4572215bba18722ed4ca8bb89826b |
| independent-separation-ci-v1.json | 352d0e8fb34e627ce509a2396843f5d68e6ca0426db2cd2aad2d22fe2e622d6d |

Counts remain 139/161 preliminary Remediated, one Closed and 21 remaining issue
remediations. No candidate or stable GO exists. No guest/Mac setup or physical
source is changed; existing source/artifact evidence retains its original identity.
