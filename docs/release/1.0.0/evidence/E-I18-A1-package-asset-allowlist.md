# E-I18-A1 — package prerequisites and exact asset selection

2026-10-06. Preliminary partial I18/I03 release-control correction; I18 remains Open. Committed hosted execution and actual package-path validation are pending. No tag, public release, stable publication or human GO is created by this work.

Repository reality at **7b56b16** still gives all three package jobs only `windows` and `portable` prerequisites; Windows ARM64 is absent. Windows upload and draft release use `artifacts/*.*`, Linux uses extension globs and Mac uses `artifacts/*.zip`. These are static confirmed plan §10.1/§10.2 gaps. No failed-ARM64 tag publication or accidental real asset upload is claimed.

All three package jobs now require `windows`, `windows-arm64` and both portable matrix lanes. Their normal dependency conditions preserve [GitHub's required-job semantics](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds). A shared selector requires the exact current version's eight Windows package/inventory files, three Linux packages or one Mac ZIP, then emits their actual sizes/SHA-256 and source commit in a platform-specific JSON manifest. Both artifact upload and draft-release upload consume exactly the same selected path list, including that manifest. The [pinned release action](https://raw.githubusercontent.com/softprops/action-gh-release/efb35369e0ad2afab669f228072c1b0d510eae64/action.yml) supports explicit newline-delimited paths and failure on unmatched files; the latter is enabled.

The selector refuses incomplete, empty or directory outputs, existing manifests, invalid version strings, link paths and glob-bearing roots. Eight controlled host cases independently verify the external filename contract, all file hashes, preservation of an original manifest, exclusion of unrelated files matching each original platform glob, and refusal before emitting a manifest/action file list. The positive files are **synthetic fixtures**, not real packages. Both PowerShell files parse; the workflow YAML graph is independently read and its prerequisites, selector bindings, identical upload lists and draft/prerelease flags verify. The controls and receipts are added to all four required hosted lanes. The actual tagged publisher has not run.

Known existing limits remain explicit: Windows installer names still strip prerelease suffixes; the JSON records that mismatch. Current package inventories are not complete SBOMs. Signing, existing GitHub release-asset duplicate/hash checks, manifest-only promotion, separation of publisher credentials, repository/tag protection, immutable releases, human approval, platform decisions and final candidate qualification remain open. The file hashes are selection-time observations; they do not prove later upload immutability or downloaded final bytes. Draft/prerelease behavior is retained.

Private root `C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\release-assets-20261006`.

| Retained path | SHA-256 |
|---|---|
| host-working-v4/release-assets-controls.json | 1f3598c37e272049a3f61c4cbb2bb78fe428f94570d073fb598fd1083d4a120b |
| independent-working-v2.json | 5937c909093d941ebab4dffc1382b50ee78e78455afb25b43c9be8ce094171cf |

Counts stay 139/161 preliminary remediations, one Closed, 21 remaining issue remediations; all campaigns need final qualification. NO-GO.
