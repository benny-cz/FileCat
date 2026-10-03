# E-I108 — CI helper readiness and verification checkpoint

Preliminary test-harness correction `6cad380915a0f524e495210cc13f2a94fb91da3b`, 2026-10-03. All four successor CI
lanes pass. No product recovery
or progress behavior is changed, no live media is touched by these synthetic checks, and this is not candidate qualification.

## Original failures retained

- Windows x64 runs [37117221268](https://github.com/benny-cz/FileCat/actions/runs/37117221268)
  (`b5ce7441a03d852cba23865a25dee9e6acf6b034`) and
  [37118018218](https://github.com/benny-cz/FileCat/actions/runs/37118018218)
  (`a50b3b885b0685b884f5dab989855216dfa4e220`) fail only
  `LiveUsbLeaseTests.A_second_process_is_refused_and_process_death_releases_the_lease`. Both time out at line 50,
  waiting 15 seconds for the PowerShell child's redirected output, before testing lease contention. Ubuntu, macOS
  and Windows ARM64 succeed in both runs. The title change is not the failing assertion.
- Complete metadata/logs, original GitHub archives and independent inventories are retained under
  `artifacts/release-evidence/ci-37117221268` and `ci-37118018218`. Archive SHA-256 values match GitHub's digests:
  `4377a7ab362193e6797fb797999702061c9e9d26afdd87b44a215028a9eb9272` and
  `523d8b9d51ac2bc83e20a26eac626da5cbab61c3a03cab8f2ea9cd4d2b49b186` respectively. Safe member names,
  sizes and hashes were checked. Direct TRX counts in each: Windows 162 pass/1 fail/33 skip; App 248/0/15;
  Core 699/0/47; Remote 88/0/28. Independent inventory SHA-256 values are
  `37e6755b01d211e506606102bf84c1f68b29f939aba36e5fc21b0ce0900ef07d` and
  `0cfb46f7272ceeb66e3f0353b4172485cb3d92044c2e7eec68c78bf50513b8e8`.
- Earlier macOS run [37115045387](https://github.com/benny-cz/FileCat/actions/runs/37115045387), exact
  `bb2d748928330083678c1ce515867c41a59e8a8f`, fails
  `ProgressEstimatorTests.A_verified_copy_counts_its_reading_back_as_work`: sampled fraction
  `0.65104166666666663` outside `0.3–0.6`. Complete original metadata/log retained in the corresponding CI folder.
  That lane did not upload a Core TRX; the failure attribution here comes from its original log.

## Correction and limits

The lease helper uses an absolute Windows PowerShell executable path and atomically publishes a readiness file
after acquiring its exclusive file handle. The parent checks helper exit, bounds startup to 60 seconds, and records
PID, startup time, exit code and drained stderr. It still requires immediate refusal for the same serial, permits
another serial, kills the holding process, and requires the lease to become available within the existing five-second
post-exit bound. There are no test retries or skips. Original logs do not establish whether shell startup or its output
transport caused the original timeout; the new diagnostics make a further failure actionable.

The verified-copy test holds the real portable copy immediately after its return, before read-back verification.
It checks all copy bytes done, zero verification bytes done, total work three times the file size, and an exact
one-third fraction. It then releases the checkpoint, requires completion and both files' verification bytes. This
removes a polling race: while the observer sleeps or is descheduled, hashing continues after `BytesDone == BytesTotal`.
The 4 MiB fixture suffices once the boundary is controlled; its previous 256 MiB size was an attempt to observe that
boundary by timing. Product estimator, transfer executor and production interlocks are unchanged.

Original test sources, local baseline log and repaired build/TRX files are retained in
`artifacts/release-evidence/ci-reliability-20261003`. Local affected checks pass: 29 lease/guard/oracle and eight
progress tests, no failures or skips. The full repaired Core suite passes 700 with 46 explicit skips, 746 total.
Direct local inventories are independently parsed, SHA-256
`27a5a63d319cbab8392176612db27f783b44f9ce11ced8c16a061f12aca1be28`; exact 6cad380 input manifest SHA-256
`6f1567d4c0cde73e37431f04723ba1b9567e64529e223935f58f9f3ef7565026`.
A full-solution baseline before rebuilding passes the two affected tests but
fails a separate `WindowsFileRecordsTests.A_file_reads_with_its_exact_times_IDs_links_layout_and_permissions`
assertion on this host; it is not claimed green. A shared TRX filename caused baseline files to overwrite one another,
so only the original full log and the last App TRX survive. Repaired affected TRX files have distinct directories.

## Successor CI

[Run 37119313116](https://github.com/benny-cz/FileCat/actions/runs/37119313116), exact
`6cad380915a0f524e495210cc13f2a94fb91da3b`, completes successfully on Windows x64, Windows ARM64, Ubuntu and
macOS; the three tag/manual package jobs skip. Windows ARM64's native package startup/screenshot step passes.
Complete metadata/logs, original Windows archive and independent inventory are retained under
`artifacts/release-evidence/ci-37119313116`. Direct Windows TRX counts: App 248 pass/15 skip; Core 699/47;
Windows platform 163/33; Remote 88/28, no failures. All 29 lease/guard/oracle, eight progress, two account-title
and one window-title cases pass. The Windows child reports readiness after 9.7843344 seconds and empty stderr.
This establishes the corrected test's successful execution, not the exact cause of the original helper timeout.

| Evidence | SHA-256 |
|---|---|
| complete successful run metadata | `ee8ea25c8a032900d1010e6532bc156574c4a6ff3f75221068ef9bbc53e9d36f` |
| complete log | `a6a443ef843be6426a7ad8c07efd75cb66a7eff826f5f03c24624197daea9c31` |
| original Windows archive | `1fa2b7a9d1d9eee7565fa7f01adb24cccb96bfb20808d56245367dcef269357e` |
| independent Windows inventory | `54074e24361a5234d24094ee8c81c663a087ad55179a0457011297490f24d6e2` |

Later c162481 CI exposes a remaining asynchronous totals race and an over-broad global helper-start assertion.
Original failures, revised observer scopes and passing affected host evidence are retained in
[E-I108-P1](E-I108-P1-observer-scope.md). The earlier successful run above remains historical evidence;
it does not qualify the successor observer change.
