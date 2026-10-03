# E-I109 — explain an unavailable Windows cluster layout

Preliminary correction `cfcc9e63a9a5cd70b5e122c3972929be4e2f5fce`, 2026-10-03;
baseline `c042d91df1598e2608709186e9f1aa75530cb537`. All four successor CI lanes pass.
The CI repair I108 is already validated; this is a separately reproduced host integration issue.

The full-solution baseline and isolated recheck both fail the file-record test's unconditional Fragments assertion.
A separate native probe on a newly created 300,000-byte NTFS temporary file opens the file successfully with three
access masks (metadata/security, metadata plus data, and Generic Read). Each Windows allocation query returns
error 50, request not supported, on the unelevated Windows 11 Insider 26220 host. This is not established to be a
permission failure, and the particular filesystem/filter cause is not established. No USB access occurs.

The inspector previously suppresses native errors 1 and 50 in its layout section. It now reports the same explicit
"Its clusters could not be listed" explanation used for other query errors. The existing integration test asks
Windows independently whether this fixture's allocation query is supported. If supported, Fragments and the
extent table remain mandatory. Only errors 1/50 select the unavailable-layout branch, which requires no fabricated
table/fragments and the exact native explanation. All other errors fail. Times, identity, hard links, allocation
size and security assertions still execute. There are no new skips, privilege changes or data-reading fallbacks.
The [Microsoft API documentation](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_get_retrieval_pointers)
identifies this native query as the file's allocation/location map.

With the updated native-oracle assertion and unchanged product, the test fails because the explanation is absent.
After the one-line inspector correction, the full Windows suite passes 159 with 37 explicit hardware/platform skips,
196 total. A second native probe still returns error 50 for all three masks, and the report now includes its
unavailability explanation. This proves honest fallback reporting, not that the host can expose the cluster map.
The successor native-oracle integration case passes in CI. Its chosen native branch is not separately logged;
the earlier unchanged test's successful CI runs establish supported-layout controls. Exact candidate reruns remain required.

Original sources, reports, native JSON, failed-before/passed-after TRX, working input hashes and independent
inventory are retained under `artifacts/release-evidence/host-record-layout-20261003`. The private probe's initial
compile-input mistake is retained; it never executed a fixture. Original reports are not overwritten by the rerun.

| Evidence | SHA-256 |
|---|---|
| original native query results | `878c6866db5c23e398189e5190a3c16cb60ae095aa787898c9496f05de25d11e` |
| original report | `8308155ac99b49ce5d2e1163dac3e242869da13475cb206230376b1a5ad93dad` |
| unavailable-explanation baseline TRX | `ad49f8aac37a8b660aab371c3ea73bd9aa632bc7e2384f8e6ba92bd5cc968fb6` |
| full corrected Windows TRX | `1af59ebfe853e9fb70216d77eaf0df0420f5305f04744e4cb96b53fce853ed5b` |
| corrected report | `729220e77fd075db8d7897510e96a462b3a797536098bc1972c4158c480eae5d` |
| independent 19-file and direct-case inventory | `b5b9465de18602de2bb36d6cb972e7e9c975a6f15fa47ff6517aee4959b731f0` |

[Successor CI 37121005911](https://github.com/benny-cz/FileCat/actions/runs/37121005911) at exact cfcc9e6 passes
Windows x64, Windows ARM64, Ubuntu and macOS; three package jobs skip. Complete metadata/logs and original Windows
archive are retained under `artifacts/release-evidence/ci-37121005911`. GitHub archive digest and safe member
size/hash checks pass. Direct Windows inventories confirm App 248 pass/15 skips, Core 699/47, Windows platform
163/33 and Remote 88/28, no failures. The file-record case and all earlier affected I108/I104 cases pass.

| Successor evidence | SHA-256 |
|---|---|
| complete run / complete log | `2b2622ea10c225823b6891370eebac2cdabc2a6d9cf68654d8388aedafd99709` / `3178e4799a6ccb404601db2bc9ff1724a86c80ee529da48d1415e81dc8124f2c` |
| original Windows archive / independent direct inventory | `0ceba5dae0f96bb236d66701332d32939497efe261f589699395f2c289ba3900` / `51352b36a39b71f92d85c0fd55f3d35651bb23e43825aaff23729cde0d014b05` |
