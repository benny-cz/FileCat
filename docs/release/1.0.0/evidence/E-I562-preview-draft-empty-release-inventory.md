# E-I562 — empty release inventory stops the protected preview draft

Recorded 2026-10-29. **The tagged workflow failed; no draft or public release
was created.** This record separates the failed immutable tag execution from
the corrected main-source preflight. Neither is candidate qualification.

## Failure and attribution

- The owner approved the protected `release` environment for
  `v1.0.0-preview.1`, the immutable annotated tag at
  `be40902eeea5eac6c1f3eb2e417e0cbe42dc265d`.
  [Run 40936654935](https://github.com/benny-cz/FileCat/actions/runs/40936654935)
  passed the producer, both Windows lanes, Ubuntu, macOS and all three package
  jobs. Its **Draft preview** job failed in the first, read-only
  `Invoke-DraftPackageChecks.ps1` step. The upload and post-upload verification
  steps did not run.
- The exact GitHub releases endpoint returned `[]`: there was no existing
  release to collide with. PowerShell's `Invoke-RestMethod` emitted that JSON
  array as one empty array object. `@(Read-GitHub ...)` wrapped it again, so
  the loop inspected an array as a release row and StrictMode raised
  `The property 'tag_name' cannot be found on this object`. The same wrapping
  risk existed for an empty asset page of an existing draft.
- The authenticated release inventory still contained zero releases after the
  failure. The protected approval did **not** publish or upload assets.

## Corrected source and bounded checks

Commit `d3fa34fd7df7abb233ee809b2e823c14874c086b` assigns both API pages
directly, normalizes a scalar row, and rejects null or malformed responses.
The draft guard SHA-256 is
`733ec495ae77360991704079cffed8a32145a55d8db0761402b31fa7893becba`.
Synthetic transport controls now reproduce the actual empty-array output:
**36/36** draft-release controls pass, including malformed-row refusals; the
three-platform package-set controls pass **13/13**. Their retained JSON SHA-256
values are `eb9839f6178dfdb1f59932558a662b1975141955ac1530a4522c22db06fed366`
and `1819e5164f4752d22d1cfdd2cd3658cfee763963ad382aef2eba81454a2187e3`.

The corrected code also passed a **read-only live GitHub preflight** on the
exact tagged packages. The three producer manifests matched the hashes passed
to the failed hosted step:

| Platform | Manifest SHA-256 | Official artifact ID / server ZIP SHA-256 | Members |
|---|---|---|---:|
| Windows | `9d5550c3d2bdbf02a4738dd85c7a909cebcf13a2f6078bc361c04a457484c02b` | `12928114155` / `2797a44a287f429f4195072232af060171d282598172f239c767b378516e1892` | 13 |
| Linux | `f85b1ef19f157040fc917e6251290c34bded92996660d16a2f25c7526ba040c4` | `12928027466` / `e8042defdc954c1b5bb1ca90c74e941e101a4350caa3b2846280098c87cb2749` | 8 |
| macOS | `513cb09454df6a1b91c829e932d42617206d2f2eb599a6cab66b37e70d2f2b55` | `12927388870` / `e6142a3892d893d068eca5501bfe157d6be5e47b86034170496c979abf03cd74` | 3 |

Each downloaded artifact ZIP matched its GitHub server digest; all **24** ZIP
members were byte-identical to the independently downloaded package inputs.
The patched preflight verified every package against its producer manifest,
resolved the remote tag to the exact source commit, found no release, and
emitted exactly 24 upload paths. It created no release. The three receipt
SHA-256 values are Windows
`3dcc0bbedfb1fdb17366c66521b4d99d68ebbf2270c17213d801b9646d356ef7`,
Linux `763f538f007097b0572a77bd667377ed7e59db7b539e76a5d15f6bb9652339f7`,
and macOS `bd8a9a45772169c4b821d7b07cdf2fdff37e8766f051f4f0abc73ad56b174233`.

The 39-file private archive of logs, actual API responses, synthetic controls,
receipts, official ZIPs and their extracted members independently rehashes;
manifest SHA-256 `485c0d5e2995b17434542527a4214e473c1fa7e48b065fd4868fad23f65f384e`.
Root: `V:\FileCat\artifacts\release-evidence\preview-draft-empty-api-fix-20261029`.

## Stopping condition

The failed tag cannot be changed under its protection rule. Rerunning that
workflow would check out the same faulty script. I562 and REL-PIPELINE remain
open/failed until an owner-approved **new preview tag** on a CI-green corrected
commit runs the protected draft job through its preflight, private upload and
post-upload digest verification. Public preview still requires the owner's
separate decision on the exact assets and hashes. The failed old tag predates
AC-59; any decision to use its artifacts must explicitly disclose that limit.
Stable publication remains NO-GO.
