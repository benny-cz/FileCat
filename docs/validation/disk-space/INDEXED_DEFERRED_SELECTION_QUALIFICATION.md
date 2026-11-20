# Indexed after-scan selection qualification

An internal indexed analysis can capture the displayed query and scope while the scan is in progress. Once the scan finishes, its review shows the exact matching regular-file paths in pages of 128. Explicit approval opens a separate recorded-result tab with those paths marked. The original preview, marks and review basket stay intact.

The checks cover canceled or stopped scans, ownership transfer and rollback, closing while native metadata reads are queued, retry after those reads finish, capacity changes across windows, folder and grouped Other scopes, and unchanged source files. The review shows the captured location, case-sensitivity and uncertain cohort. Long paths wrap at narrow widths, and Enter and Escape default to closing the review without applying it.

| Check | Windows VM | Mac | Ubuntu VM |
| --- | --- | --- | --- |
| Broad rendering suite, r1310 | 841 passed, 1 platform skip | 842 passed | 842 passed |
| Final deferred selection checks, r1324 | 40 passed | 40 passed | 40 passed |
| Final native window, three themes and 50,000 matching paths | Passed, r1327 | Passed, r1326 | Passed, r1326 |

The broad rendering run preceded the path wrapping and review summary corrections. The final focused tests exercised those corrections on every platform. The native tests opened a real desktop window and used its control events. The Mac compositor also confirmed the three visible layouts at 2× scaling. Physical keyboard and pointer coverage, screen readers, package qualification and the independent final review remain open. The public command remains disabled.

The test machines were clear of test processes after collection. A Windows native run stopped its own process when another worker appeared. The passing retry reused the same verified binaries. The original failed and interrupted runs, the exact tested source and the final receipts are retained in the [evidence manifest](source-manifest-r1328-indexed-deferred.json).

The [repository receipt archive](repository-receipts-r1328-indexed-deferred.tgz) retains the test results and application-only images. The full desktop images remain in the locally retained original archive. Their hashes and the repository archive hash are recorded in the [receipt index](repository-receipts-r1328-indexed-deferred.json).
