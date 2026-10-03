# E-I109 — explain an unavailable Windows cluster layout

Preliminary correction, 2026-10-03; baseline `c042d91df1598e2608709186e9f1aa75530cb537`. Successor CI pending.
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
CI runners' supported-layout branch remains to be revalidated on the successor.

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
