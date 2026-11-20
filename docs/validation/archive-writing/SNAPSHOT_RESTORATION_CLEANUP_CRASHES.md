# Restoration cleanup process crashes

6 November 2026. Follow-up to `df893042`. This checkpoint extends test-only wrappers and qualification cases; production and worker bundles are unchanged.

Eighteen additional cases externally kill the actual restoration parent at each of six fresh-job artifacts' durable deletion intent, native unlink and recorded outcome. The fresh rollback copy of the version being replaced is last. Each case has already completed the real restoration with distinct confined assessment/verification workers. Test-only stage wrappers delegate native creation, origin, exact-object review and deletion; native transactions receive the underlying creator-owned stages. Production has no injected checkpoints.

After process rundown the tests compare physical presence with the durable prefix. A fresh native cleanup scope reports an unrecorded unlink as observed absence, preserves the previously confirmed prefix and deletes only exact remaining objects. It also resolves a crash after the last recorded deletion but before completion. The restored archive's exact bytes, identity and complete strongest origin remain unchanged. All old-source artifacts and its original-byte snapshot survive the new-job cleanup; a separate fresh old-journal scope then reconciles that catalog as `RestoredIntact`. Unrelated sentinel bytes are preserved.

The complete Windows native lane passes 227 cases under the administrator token and 222 under the genuine standard
account with five expected administrator-only skips. All 31 restoration crash/retry cases (including all 18 new
cleanup boundaries) and all nine cancellation/failure cases pass in both lanes. Raw token facts confirm medium
integrity and absent Administrators membership for the standard user. The final runner confirms successful
completion and no remaining FileCat/test process. Mac and Ubuntu each pass the build and all 159 expected Windows-only
guard skips, with zero failures and final idle checks. Their previous common/UI/independent-fixture checks cover
unchanged production code. The Mac temporary keep-awake assertion remains active. No production fix was needed.

The complete 310-file test/harness/ordinary-probe trees are hash/count checked after transfer. All 469 actual app/probe worker files are rechecked against the preceding checkpoint's complete runtime manifest, with the same 261-file application SPDX inventory. No codec or dependency is added.

Raw development evidence remains under the private worktree's ignored `.tmp` directory.

| Artifact | SHA-256 |
| --- | --- |
| `restoration-cleanup-source-v1.tar.gz` (two test files over `df893042`) | `6256DAF328622AF743D504101AE2CC12F00AA55DCD76D3044DACA53CA66C9544` |
| `restoration-cleanup-windows-v1.tar.gz` | `B7E71CED41880995C9DFA4FB30B490527B3D30D4796AADE90D4C484BCEFD9B2D` |
| `restoration-cleanup-tests-v1.json` | `08B04B22D561A11344E7E0F1589C48D936E4DE040FF3EA7C5CD0C60AE6914A1D` |
| `restoration-cleanup-windows-evidence-v1.tar.gz` | `1E42FC598ABEF46EA426CF07E5A989CEADF241572195A626B898FFA625AFE430` |
| `restoration-cleanup-mac-evidence-v1.tar.gz` | `F07E52B23E3CBB1C99C0B192CF1C304F9F471C3821653B82B905B1D361B9560B` |
| `restoration-cleanup-ubuntu-evidence-v1.tar.gz` | `5FDACDCC437621FF151A25FA81E47FEAE58DAD7E7876A85B79C52DF511191193` |

Wider fault/resource, metadata/security/alias/durability coverage, production recovery UI/actions and remaining format/platform/workflow gates are unfinished. These process-kill tests do not establish power-loss durability or Unix update publication. Writer profiles stay disabled. RAR analysis and feasible implementation remain before final independent UX/UI/functional/security review; the goal remains active.
