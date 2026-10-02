# E-I96 — transient native Recycle Bin query broke ARM64 CI

Preliminary automated evidence, not candidate qualification.

## Observed failure

[CI 36985897644](https://github.com/benny-cz/FileCat/actions/runs/36985897644), source
`3316f1502a59e166c95de1d920ed2fcfcac72123`, failed only in the Windows ARM64 Platform.Windows suite:
`RecycleBinShellTests.The_system_drives_bin_is_counted_and_no_window_is_needed_for_it` expected S_OK (0),
but `SHQueryRecycleBinW` returned `-2147024713` / `0x800700B7` (`ERROR_ALREADY_EXISTS`). Suite totals:
132 passed, 1 failed, 33 skipped. This commit changed only documentation.

[CI 36987552356](https://github.com/benny-cz/FileCat/actions/runs/36987552356), source
`a5a3c0cb68dd0868e8642565dcf49cc93ddecee4`, passed all four lanes with the same Recycle Bin test and production
query code. The cause of the native response is **not established**; a Shell initialization race is not claimed as proved.
[Microsoft's API contract](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shqueryrecyclebinw)
documents S_OK on success and an HRESULT on failure, without a first-query success guarantee (read 2026-10-02).

## Remedy and validation

`5503262` makes the native integration test retry **only** `0x800700B7`, at most four times with 50 ms between attempts.
Persistent failure and every other HRESULT still fail. No test is skipped, no assertion is removed, and production
behavior is unchanged. This is a test-environment reliability finding, not proof of a new product data-safety defect.

Targeted Release run on the Windows Insider 26220 host: 1 passed, 0 failed, 0 skipped. The run used the working tree
of `021a885` plus the retry diff subsequently committed unchanged as `5503262`; it is not labelled as a build made
after that commit. Raw TRX and failed CI log: `artifacts/release-evidence/resume-20261002-3316f15/`.
Full [CI 36989092570](https://github.com/benny-cz/FileCat/actions/runs/36989092570), source
`55032629283473684b0b48864b8524444d6fa941`, passed all four lanes; the three release package jobs skipped.
The subsequent `ca1afe0` run (36989898493) also passed all four lanes. Final-candidate rerun and issue closure remain
outstanding. SHA-256 of retained records:

| File | SHA-256 |
|---|---|
| `ci-3316f15-failed.txt` | `1a85ab29bdde0eddafe128bc703165aab1df442fb7291846f4db1f4c15478cd8` |
| `recycle-bin-target.trx` | `f69b035a5e1895f58527fb6d76a794cecc2740e089757e85a8aeafdf617c8a45` |
| `ci-36989092570.json` | `62bceb107535d35c345267fdbef69d2cc36ba3234b98b9484632a674b0489c3c` |
| `ci-36989898493.json` | `46ff6bf455f78d4329f8b0231ee447453aa3b676229e4ebc85d38803437daea1` |
