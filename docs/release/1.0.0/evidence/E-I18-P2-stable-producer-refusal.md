# E-I18-P2 — refuse stable references at the producer boundary

2026-10-06. Preliminary partial I18 correction, baseline `4b2b9d7`; committed
hosted execution remains pending. The baseline workflow accepts every `v*` tag
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
Actual hosted stable-negative execution is not claimed; hosted non-stable guard
and control receipts will be validated on the committed successor.

Private `FileCatReleaseEvidence/release-assets-20261006`:

| Retained path | SHA-256 |
|---|---|
| producer-policy-host-v1/producer-reference-controls.json | bafa6c30f26cc478d8900593f3c9b666f728d1654bad8e69e7c39bd7b3aff888 |
| producer-policy-host-v1/independent-policy-v1.json | 915aab187d284f2f736840a126cc6c342d2bb1248fee3fcd40288fc16ce6087f |

Counts remain 139/161 preliminary Remediated, one Closed, 21 remaining issue
remediations. All final campaigns/candidate and explicit human stable GO remain.
