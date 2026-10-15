# E-CI-I108 — picture revision fixture fails on Windows x64 hosted CI

The exact `main` push `e0e009bc4dcc70cea9a73fd74180c14f8ceb06fd` [run 38656838365](https://github.com/benny-cz/FileCat/actions/runs/38656838365) is **failed**, and remains failed. Windows x64 App failed three `PictureRevisionStatusTests.Cached_picture_status_distinguishes_changed_source_from_current_bytes` theory rows at the initial `PictureLoad.WaitAsync(TimeSpan.FromSeconds(10))`; the other four required jobs passed and the four conditional jobs skipped. The failure is a `System.TimeoutException` in each case, not a pixel or revision mismatch. Adjacent `6fea12b7` and `480dad83` runs also failed the same three rows. The earlier `95e7096a` [run 38655407242](https://github.com/benny-cz/FileCat/actions/runs/38655407242) passed all five required jobs and those same rows.

The official failed run retains 26 server-digest ZIPs, 2,752 members and 14 TRX: **33,549 passed, 1,242 explicitly not executed, three failed**. The missing `windows-instance-results` ZIP is recorded as unavailable because the job did not reach that upload. The earlier green run retains 27 ZIPs, 2,778 members and 14 TRX: **33,552 passed, 1,242 explicitly not executed, zero failed**. A member-level comparison of all 34,794 test rows finds only the three Windows App Passed→Failed changes; their names, original failures and messages remain in the raw TRX and comparison.

TRX timing narrows the scheduling hypothesis: the three failures each spent roughly ten seconds in the fixture wait, at 23:58:49–23:59:20 UTC. The held-feed picture admission/demand tests started eight to nine minutes **later** in that Windows x64 run. An initial proposal to put those classes in one xUnit collection was therefore withdrawn before commit; overlap with those tests does not explain the failure. The actual hosted delay has not been proven. The green run's same six picture-revision theory rows took about 0.2 seconds each.

The fixture's initial and repeated picture-load waits have been increased from ten seconds to `PictureDecoder.Timeout + 15 seconds` (45 seconds), allowing the product's own 30-second decoder timeout plus startup/scheduling margin to complete. The production decoder, pixels, byte hashes, statuses, revisions and cleanup assertions are unchanged. This is a test-boundary correction, **not** evidence that the underlying hosted delay is fixed. The separate `CheckForChanges` ten-second wait was not implicated and remains. Local final-source and corrected-source CI results must be appended before calling the new bound qualified.

Every official artifact ZIP was checked against its GitHub server SHA-256 digest. The three retained archives under `V:\FileCat\artifacts\release-evidence` independently rehash:

| Archive | `file-manifest.json` SHA-256 | Audit/comparison JSON SHA-256 |
|---|---|---|
| `ci-i496-last-green-20261015-v1` | `62a4d0ab292b8a28ebe39427f7ebf3ed1415f69a86b79244521860d8d76c29fc` | `d6142286a1635a7700d22c0191ad07944f8b73f632be562183d859128da72827` |
| `ci-i108-picture-fixture-failed-20261015-v1` | `58accb8fac3e2a46801e7b8361b1b83622043820283fe5ec0a2a92a8a38f15b0` | `b39f47152e381b8cd7d2a41c05421f5d3cb34900bf8cf748a7099a209888b69a` |
| `ci-i108-picture-failed-vs-green-20261015-v1` | `5b81bb1e776443cc9a768b76f106249e46429665aed80f4ce75e89fb50af846f` | `91cfe2730b65c19d90f6f47de713e39dd4cb6d74d95e43f59e3c26fb7d426f4a` |

No candidate, contract freeze or stable release is inferred from either run.
