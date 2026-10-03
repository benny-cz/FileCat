# E-I108 — CI helper readiness and verification checkpoint

Preliminary test-harness correction, 2026-10-03. Required successor CI validation is pending. No product recovery
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
progress tests, no failures or skips. A full-solution baseline before rebuilding passes the two affected tests but
fails a separate `WindowsFileRecordsTests.A_file_reads_with_its_exact_times_IDs_links_layout_and_permissions`
assertion on this host; it is not claimed green. A shared TRX filename caused baseline files to overwrite one another,
so only the original full log and the last App TRX survive. Repaired affected TRX files have distinct directories.
