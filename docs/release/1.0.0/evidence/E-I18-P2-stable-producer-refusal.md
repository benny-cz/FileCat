# E-I18-P2 — refuse stable references at the producer boundary

2026-10-06. Preliminary partial I18 correction, baseline `4b2b9d7`; committed
source `317a9a5d6e90bb65c246da8a71a786326efa0c3d` and original CI
[37462073457 attempt 1](https://github.com/benny-cz/FileCat/actions/runs/37462073457)
are sealed. The baseline workflow accepts every `v*` tag
and rebuilds packages, including stable references. No actual stable tag or
publication is created to demonstrate this static defect.

A read-only producer-policy job now precedes all four test lanes and all three
package jobs. Its standalone guard permits normal development/PR references and
explicit supported prerelease tags, and rejects stable tags, stable build-metadata
tags, malformed versions and mismatched event/ref inputs. A stable manual run is
also refused. Existing draft/prerelease flags and selected asset lists remain.
No dependent job has a failure bypass. GitHub documents that a failed prerequisite
skips the dependent chain unless explicitly overridden ([required-job semantics](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idneeds)).

Sixteen host synthetic event/reference cases pass; five separate PowerShell
processes independently verify real zero/one exit codes and no eligibility output
on stable refusal. Both PowerShell sources parse. Independent YAML reading verifies
the gate/steps/read-only policy and test permissions, all required dependency edges,
absence of bypasses, expanded eight-job graph and preserved selected draft assets.
The initial output sink opens before its directory exists; that failed capture is
retained in tool history. Correcting only capture ordering permits the original
controls to run successfully. No tag/ref, release API or signing provider is used.

This guard intentionally provides no stable promotion capability. It does not
implement approved-manifest-only promotion, human GO/signing/preview approval,
duplicate release-asset/hash refusal, protected refs, immutable releases or
publisher credential separation. Those broader I18/DEC-09 gates remain Open.
Actual hosted stable-negative execution is not claimed. The committed run passes
the policy job and all four required test lanes; all three package jobs skip.
Fifteen selected artifact ZIP server digests, four clean exact-SDK build receipts,
fourteen complete TRX inventories and all 409 App case identities verify. The
sixteen hosted policy cases match the committed helper/validator bytes and run
identity; the actual main/push guard step succeeds with development eligibility
and stable promotion false. All sixteen hosted picture fixture cases pass.
ARM64 startup/drawing/installer checks pass. Original failed CI `37454794034`
remains retained; this success does not change its historical conclusion.

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| producer-policy-host-v1/producer-reference-controls.json | bafa6c30f26cc478d8900593f3c9b666f728d1654bad8e69e7c39bd7b3aff888 |
| producer-policy-host-v1/independent-policy-v1.json | 915aab187d284f2f736840a126cc6c342d2bb1248fee3fcd40288fc16ce6087f |

Private `FileCatReleaseEvidence/ci-37462073457-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 4826df77b9e4beb68f195cab4ac0dd8dbe7194fb3b2caa805ee96c26197e88ec |
| independent-fixture-ci-v1.json | 9c2578ea8ca51da04b12ff90370d41c43d0f1841eb153bf93282792ed148b104 |
| independent-producer-policy-ci-v1.json | 3bc9955822a974f5ddd5b58a442bc5a77ef2e1db4b4fd2cff7456e5f339176be |

Counts remain 139/161 preliminary Remediated, one Closed, 21 remaining issue
remediations. All final campaigns/candidate and explicit human stable GO remain.
