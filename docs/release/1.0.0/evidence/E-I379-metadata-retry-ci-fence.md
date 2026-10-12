# I379 — metadata retry test fence after sidecar invalidation

2026-10-12 CEST. **CI reliability finding; product behavior is not yet implicated.** Exact original-push I378 run [38221013049](https://github.com/benny-cz/FileCat/actions/runs/38221013049) at `98b0e1d8695e5d7ff37893b8d260ff2b4ab2dec7` failed Ubuntu Core in one pre-existing `MetadataDemandTests.A_sidecar_change_cannot_restore_an_inflight_old_verification` theory case (`invalidateAll: false`, `explicitAnalysis: true`). The raw assertion expected exactly two metadata producer calls and observed three. Its three new I378 result-bound cases passed. The failed Core step stopped later Ubuntu checks; their skips are not evidence of a pass. The original run remains failed regardless of later results.

The test's safety requirement is that the obsolete in-flight `Matches` result cannot be republished after the sidecar changes, and that every subsequent computation returns `Differs`. The original test already checked the visible final result but treated the number of current retry calls as a fixed contract. The correction records every producer state, waits for active producers to finish, requires at least one retry and requires **each** post-invalidation state to be `Differs`; it still checks unchanged file bytes and modification time. This strengthens the stale-result assertion while allowing another current retry. The reason for the third request in the original CI schedule has not been proven, so no product deduplication defect is claimed.

Two normal elevated local focused runs of the corrected four-case theory each pass four cases/zero skips/failures. An earlier restricted-sandbox attempt aborted before the testhost connected after 90 seconds; its TRX is retained separately and is not counted as product validation. Full Core and corrected-source CI remain separate evidence to be added after their results finish. No installed candidate or stable publication is qualified.

Private receipts are under `E:\FileCat\artifacts\release-evidence\i379-metadata-ci-fence-20261012-v1`:

| File | SHA-256 | Scope |
|---|---|---|
| `original-ubuntu-job.log` | `c4368d70d5672b16f5e3d2695d03d32013cf5d2a486c665f8a626d504e376bdb` | Complete failed job log, including assertion and downstream skips. |
| `original-ubuntu-core.trx` | `e7db2b36ebec431263f02d2c57311157f2386ccf4e30ff7bbe1457e2b586cdd2` | 4,024 passes, 96 explicit skips, one failure; three I378 passes. |
| `focused-fixed.trx` | `81ccad73c4c41ae63efd5a23035dd1b4f551b81c109b4523949e3e4618cfa466` | Four corrected local theory passes. |
| `focused-repeat.trx` | `20ad89b19fb4c73776633c783108e9fc4ce0e2c05723f7e95e69be45320ae9fb` | Separate four-pass repeat. |
| `restricted-testhost-aborted.trx` | See retained file | 90-second local testhost-connect abort, no test cases run. |
