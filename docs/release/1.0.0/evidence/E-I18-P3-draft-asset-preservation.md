# E-I18-P3 — preserve draft assets and verify selected uploads

2026-10-06. Preliminary partial I18 correction, baseline `317a9a5`; committed
hosted successor remains pending. The pinned action defaults to overwriting
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
both failures remain in tool history, with correction using explicit arrays.

A retained actual read-only GitHub inventory is empty. No real duplicate upload,
tag, release or signing request is made. The action's upload path, final download
verification, atomic promotion, human GO/preview approval, write-token separation,
protected refs, immutable storage/releases and exact candidate remain Open. This
does not close I18 or authorize publication. Package bytes and prior source-based
evidence retain their own original identities; changed guard/selector workflow
requires committed successor validation.

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| draft-guard-host-v5/draft-release-controls.json | 92f0d161665b3b9b4501098b3cf56c642cc099d552717739772fa13bf026e095 |
| draft-selector-host-v1/release-assets-controls.json | 13f7cce6185388355ba48cd11c118331a5db69509ecb2003fbabe2ce5194d6dc |
| draft-guard-host-v5/independent-guard-v2.json | 49a129b0711b60e1d71340e9f6202e85620209ec52e0770e79614ed94c30eb57 |
| draft-release-inventory-v1/receipt.json | f20dfa39951a9b3f44744b5157d0796314ee00042c6baf73dc1f67ca15a47f1c |

Counts remain 139/161 preliminary Remediated, one Closed and 21 remaining issue
remediations. No candidate exists; all final campaigns and explicit stable GO
remain required. No guest/Mac setup or physical source is changed in this slice.
