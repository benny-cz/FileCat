# E-I18-P3 — preserve draft assets and verify selected uploads

2026-10-06. Preliminary partial I18 correction, baseline `317a9a5`; committed
source `6a6af3be08b2b18a4023ec66725dd6e38f0a97a4` and original CI
[37464968767 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37464968767)
are sealed. The pinned action defaults to overwriting
same-name assets. Its false setting preserves existing files but skips a known
duplicate. Therefore that setting alone does not establish refusal
([pinned action source](https://raw.githubusercontent.com/softprops/action-gh-release/efb35369e0ad2afab669f228072c1b0d510eae64/src/github.ts),
[pinned input/output contract](https://raw.githubusercontent.com/softprops/action-gh-release/efb35369e0ad2afab669f228072c1b0d510eae64/action.yml)).

Each of the three draft action steps now has a read-only preflight and replacement
disabled. The selector emits its manifest path and digest. The guard rechecks
that digest, exact platform filenames and every package size/hash, source/version,
remote tag target (including bounded annotated-tag peeling), paginated release
and asset inventories, draft/prerelease state, and conflicting names or labels.
Existing assets refuse even when their digest matches. Missing authentication,
API failures and ambiguous inventories refuse without an eligibility receipt.

A second read-only check follows the action. It requires the complete selected
upload output, unique positive asset IDs, the expected release ID, matching sizes
and SHA-256 server digests, and independently refreshed remote IDs/digests. A
duplicate skipped between preflight and action therefore fails the complete-set
check. Changed local bytes, unexpected uploads or changed remote identities fail.
Both successful receipts are retained by their workflow steps. No automatic
cleanup deletes partially uploaded draft assets; failures remain reviewable.

Thirty-two host synthetic cases pass: all platform preflights, annotated tags,
same/different-digest duplicates, manifest/label/case conflicts, a second asset
page, changed local bytes/source/version, malformed names, remote source changes,
stable refs, public/immutable/ambiguous releases, API/auth failures, excessive tag
depth, complete uploads and nine denied upload-output/remote-change cases. Eight
selector controls pass with the new manifest output identity. Four scripts parse;
independent YAML inspection verifies ordered guard/action/postflight chains,
disabled replacement, selected outputs, token handling through environment,
no failure bypass and controls in all four hosted test lanes. Initial control
harness assertions incorrectly apply scalar `.Count` to the one-file Mac fixture;
these failures remain in tool history, with correction using explicit arrays.

A retained actual read-only GitHub inventory is empty. No real duplicate upload,
tag, release or signing request is made. The action's upload path, final download
verification, atomic promotion, human GO/preview approval, write-token separation,
protected refs, immutable storage/releases and exact candidate remain Open. This
does not close I18 or authorize publication. Package bytes and prior source-based
evidence retain their own original identities.

The committed run passes the policy job and all four required test lanes; all
three package jobs skip. Fifteen selected artifact ZIP server digests, four clean
exact-SDK build receipts, fourteen complete TRX inventories/all 409 App identities,
sixteen picture cases, sixteen policy controls and all 128 draft controls verify
against source/run pins. ARM64 startup/drawing/installer checks pass. The original
failed `37454794034` remains retained. The later `ecac41c` corrects only this
record's proof filename; the unchanged original proof digest verifies.

A separate affected regression uses the original twelve actual package files
from source `b9526b9` (eight Windows, three Linux, one Mac), with their original
versions and pins. Owned same-file hard links avoid copying or changing those
bytes. All nine preflight/complete-upload/same-digest-duplicate phases pass with
explicitly substituted remote metadata. New selector manifests/output paths
match every original package size/hash; original/linked identities and bytes
verify before and after. The guard source is `6a6af3b`; these are not packages
built from that source and no real upload is claimed.

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| draft-guard-host-v5/draft-release-controls.json | 92f0d161665b3b9b4501098b3cf56c642cc099d552717739772fa13bf026e095 |
| draft-selector-host-v1/release-assets-controls.json | 13f7cce6185388355ba48cd11c118331a5db69509ecb2003fbabe2ce5194d6dc |
| draft-guard-host-v5/independent-guard-v1.json | 49a129b0711b60e1d71340e9f6202e85620209ec52e0770e79614ed94c30eb57 |
| draft-release-inventory-v1/receipt.json | f20dfa39951a9b3f44744b5157d0796314ee00042c6baf73dc1f67ca15a47f1c |
| draft-retained-packages-v1/independent-real-byte-guards-v1.json | 93a6257f39562525f1b88f7bb999fddc2420d0b8054c07308a8dd5f08f0a7b8a |
| draft-retained-packages-v1/controls.json | a869106f52744fcc801d51df4d8949737258360fe491371ad920bb7290529009 |

Private `FileCatReleaseEvidence/ci-37464968767-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | b2b31a4578de2c4d6c77ca90d639588ff597a1828ea0268a6bb5398c7f54f54d |
| independent-fixture-ci-v1.json | e380b75acd63b02fdb487a42cc2f28a14d28bd7a53cd20bcdec84d8f8478ee00 |
| independent-producer-policy-ci-v1.json | 4acf2108e3c83777682f47f5edf9512a971378be7ac6a9db3e5a5e2bfadac4e9 |
| independent-draft-guard-ci-v1.json | 7b04f62b59815d0e105f928258005a1d6c9efb2e1dfec00b599fd4b43a0c43bd |

Counts remain 139/161 preliminary Remediated, one Closed and 21 remaining issue
remediations. No candidate exists; all final campaigns and explicit stable GO
remain required. No guest/Mac setup or physical source is changed in this slice.
