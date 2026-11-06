# Restoration cancellation and injected native failures

6 November 2026. Follow-up to `c720d3cb`. These changes add test-only wrappers and native qualification cases; production code, codec dependencies, runtime bundles and writer-profile registration are unchanged.

## Cases and required behavior

Nine actual native cases cancel after claim, after completed preparation, after durable restore intent, immediately after native commit and after post-commit review; or inject an IO failure after backup sealing, at restore intent, immediately after commit and after post-commit review. Every restoration uses a real retained NTFS scope and distinct confined assessment/verification processes. Production has no injected fault callbacks.

The tests check that cancellation before commit leaves the current published version intact, while cancellation after a public effect still returns and records the committed restored version. An exception after commit leaves an explicitly uncertain restoration with the actual restored bytes retained; it cannot claim a rollback. A failure after sealing the new rollback copy but before recording it confirms native rollback and keeps the uncataloged copy for review. Both old and fresh job catalogs remain separate, and every eligible cleanup is freshly reopened against the exact current target. Early partial histories do not acquire catalog deletion authority. File identity, complete strongest origin and unrelated sentinel bytes remain unchanged.

The existing thirteen restoration crash/retry cases run again because the test-only wrappers now share cancellation/failure dispatch. The complete native lane runs with both an administrator token lowered to medium integrity and a real standard account with its profile loaded and raw token-group checks. The new Windows-specific tests also compile on Mac and Ubuntu and skip before acquiring native authority.

## Validation and evidence

The complete Windows native lane passes 209 cases under the administrator token and 204 under the genuine standard
account, with five expected administrator-only skips. All nine new cancellation/failure cases pass in both lanes;
the existing thirteen restoration crash/retry cases also pass. Raw token facts confirm medium integrity and absent
Administrators membership for the standard user. The final runner confirms successful completion and no remaining
FileCat/test process. Mac and Ubuntu each pass the build and 141 expected Windows-only guard skips with zero failures
and final idle checks. Their previous common/UI/independent-fixture regressions at `c720d3cb` cover unchanged production
code. The Mac temporary keep-awake assertions remain active. No production fix was needed for these nine cases.

The transferred complete test/harness/ordinary-probe output trees contain 310 files, all checked by SHA-256 and directory count. The actual app/probe worker bundles reuse the fully checked 469-file manifest and 261-file application SPDX inventory from the preceding checkpoint; all actual files are checked again before execution. No new package or license is introduced.

Raw development evidence is retained in the private worktree's ignored `.tmp` directory.

| Artifact | SHA-256 |
| --- | --- |
| `restoration-failures-source-v1.tar.gz` (three test/runner files over `c720d3cb`) | `0E5AF8BF9A09DE28E2491E0CB12E69F6D784260CFD6A418095E0A868E24CFC3B` |
| `restoration-failures-windows-v1.tar.gz` (complete test output transfer) | `6BE79844DAA8960586B7978D9E108858F208357DA0C7F2C5868477E8B9B4565F` |
| `restoration-failures-tests-v1.json` (310 test output files) | `77AA29CB83E65FBEB91D59DEBC7E2A8DEC038BFF526BB5ACDFC13209A6CC08A6` |
| `restoration-failures-windows-evidence-v1.tar.gz` | `A0D6FB896CA49C32D90F5339C61B64E10D4B5AD127BEAECE3097D8EA9264D1B6` |
| `restoration-failures-mac-evidence-v1.tar.gz` | `03E902A4007BE43A871B78E38D24EC2C9EA62082C32005A9E777D8525590C6F9` |
| `restoration-failures-ubuntu-evidence-v1.tar.gz` | `3E23368D32A6C8C6378A86769866268B418811EB24B35972AA005647442CD721` |

## Remaining work

Restoration cleanup-deletion crash points, broader worker/IO/resource failures, complete metadata/security/alias/durability qualification, production recovery UI/actions and the remaining format/platform/workflow gates still require work. These tests do not qualify Unix update publication or power-loss durability. No writer profile is enabled. RAR analysis and feasible implementation remain before the final independent UX/UI/functional/security review. The full goal remains active.
