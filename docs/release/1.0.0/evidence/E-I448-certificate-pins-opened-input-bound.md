# E-I448 — bounded FTPS certificate-pin input

**Scope:** High trust-input memory and persistence integrity within I06/V08. This is preliminary local/native qualification, not an installed or candidate test.

At exact parent `74d17e38fedce8eb8519138c566dde7f8a1b08f6`, `CertificateTrust` used `File.ReadAllLines` on its writable pins file. An owned valid **8 MiB** pins file, ordinary or behind a symbolic link, still produced an automatic `Pinned` result; the new controls expected safe refusal and failed. A third baseline control showed that `Remember` replaced an oversized existing pins file with a short new one. Together with the SSH controls, the exact-parent focused run was **3 passed / 6 failed**.

The final reader checks the opened file against **1 MiB**, reads exactly those bytes and checks EOF before parsing. It also caps line count at **32,768**. An oversized file, or one grown beyond the accepted length during the read, produces no automatic pin match; `Remember` leaves an unreadable/oversized existing file untouched instead of replacing it. The three new FTPS controls and six SSH controls (three new, three existing) pass in the final Windows focused **9/9**. The complete Windows Remote suite passes **2,370 passed / 156 explicit skips / zero failed**, and a Release solution build has zero errors (163 warnings).

One verified **44-member** Remote package, SHA-256 `eac627d6360fd2ae48d27c58c9e7cc552465748460da784358df9898fd7d9ec8`, passes both trust classes **9/9** on Mac and Ubuntu; all six new FTPS/SSH controls pass on each. Each native machine's 50 staged/result files were hashed before removal, and both owned roots/stages are absent. Package manifest SHA-256 is `33cf3324acffdab11bf24c8a95b1075cb1c170d3cb7ac50c434afb2e5138e80c`.

The combined private `V:\FileCat\artifacts\release-evidence\i448-i449-trust-input-20261014-v1` archive retains exact parent/final source, tests, baseline/corrected/full TRX and logs, package, native results and cleanup receipts. Independent rehash of all **72 files** and package members passed. Self-excluding manifest SHA-256: `838bff685173a6c1651997e5d7f78d87592fae5c8600a957237e5be61ebb06ae`; audit SHA-256: `c1dceaf971b203747b764b3fbe99ca0622a1a485fc4e0085f8fd20bc543c58a5`.

The evidence qualifies local trust-file handling. A legitimate pins file above the new budget is ignored for automatic trust and left intact; the next connection requires a fresh decision. A live FTPS server, whole-process peak memory, concurrent replacement, exact hosted CI and candidate scope remain.
