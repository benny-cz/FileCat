# E-I18-P4 — read-only package producers and isolated draft consumption

2026-10-06. Preliminary partial I18 correction, baseline `ecac41c`; committed
hosted execution remains pending. All three package builder jobs at the baseline
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
are configured to execute and retain these controls on the committed successor.

This is separation for unsigned preview drafts. No preview becomes public, and
no tag/release/signing request is made in this slice. Approved final-manifest-only
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

Counts remain 139/161 preliminary Remediated, one Closed and 21 remaining issue
remediations. No candidate or stable GO exists. No guest/Mac setup or physical
source is changed; existing source/artifact evidence retains its original identity.
