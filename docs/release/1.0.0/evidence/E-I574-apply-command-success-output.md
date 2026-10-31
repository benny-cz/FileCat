# I574 — successful external-command output was absent from the job report

Recorded 2026-10-31 from exploratory expert journey 4. This is a user-visible result boundary, separate from AC-S2-036's accepted Settings command preview and from the Apply Command quoting, execution-budget and descendant-pipe controls. Those earlier cases were not rerun as a broad campaign.

## Before correction

On Ubuntu 24.04.5 in an isolated Git-built FileCat at `ab1f1b3a`, two marked owned files, `log one.txt` and `metric#2.txt`, were passed to the non-shell command `/usr/bin/sha256sum {file}`. The native preview showed two exact full-path invocations and said they would run one after another in each file's folder. The job finished **2 of 2, Completed, zero issues**. `Open report` then showed a 12-line report with no per-file exit code or SHA-256 output. The expected fixture digests, independently computed before running FileCat, were `bbb929fbb84673c5a8a4e9bce31d4bdeb0924fe50b1a82deb0d70a57b8c4a956` and `040b4ad6f60db0dd0b8e96218674689e37dc3fc54e584226dfee1fbef0b73922`. Captures are local ignored artifacts `exploratory-20261031-tool-{preview,result,report}.png`.

`ApplyCommandExecutor` already captured at most 2,000 text characters from each output stream per item, but used the captured text only in a failed-item issue. Successful output was discarded. The ordinary report enumerated issues only, so a successful hash, compiler or diagnostic command could not deliver its primary result inside FileCat.

## Scoped correction and checks

Keep a separate, bounded per-item command receipt: path, exit code, standard-output and standard-error tails, truncation and incomplete-pipe flags. Retain at most the first 64 receipts per job and report the omitted count; retain at most 2,000 characters per stream and item. Successful output is a result, not an informational issue. The report JSON-escapes paths and output, labels the text as a bounded tail, and preserves the existing failure issue and descendant-pipe deadline. No external command runs merely to open a report.

The correction is pushed at `ed87efc9ce89d5a9bc713e7bc164070826b53bfc`. Windows focused `ApplyCommandTests` and `ApplyCommandOutputDeadlineTests` passed **5P/one expected Unix-only skip/0F** after the change. The `OperationReportTests` class passed **6P/0F**, including zero-issue successful output, exact escaped path and newline, and a 65-result retention/omission check.

On the exact clean `ed87efc9` checkout in the Ubuntu VM, the Release app built with zero warnings/errors. Repeating the same two-file non-shell `sha256sum` preview and Run produced **Completed, 2 of 2, zero issues**. `Open report` showed two separate command results with exit code 0 and the expected SHA-256 strings above, with the correct full path for each. Independent `sha256sum` of both owned files after the job matched their before hashes. Captures are local ignored artifacts `exploratory-20261031-tool-final-{preview,report}.png`. This is a native source-build result, not an installed package result. Exact-source main CI [41151292748](https://github.com/benny-cz/FileCat/actions/runs/41151292748) passed producer, Windows x64, Windows ARM64 and Ubuntu, but macOS failed a separate `MarkdownParentLinkReviewTests` path-spelling assertion; it remains a failed run. Test-only `47e2aa3b` corrects that oracle and passes the focused test on a clean Apple Silicon Mac worktree; latest-source hosted CI remains separate.

The isolated app was closed, and the exact test-owned fixture and both app-state directories were removed after verifying their paths. The screenshots and source checkout remain for review.

The first documentation run after adding I574, [41152089425](https://github.com/benny-cz/FileCat/actions/runs/41152089425) at `e8932dcd`, failed only the producer's release-table validation before platform jobs started: the issue-register header still counted 573 although the table reached I574. The summary was corrected to **574 total / 553 remediated / two closed / 19 unresolved**. `python eng/validation/validate-release-issue-register.py` then passed locally, checking those 574 IDs, 920 unique evidence entries and 67 acceptance cases. The failed run remains failed; the corrective documentation run is separate.

## Finite 1.0.0 stopping rule

On the exact installed candidate, run a harmless owned command that prints a known line or digest for two marked files, one with a space or punctuation in its name. Preview must show both exact invocations before Run. The completed job and its report must show both paths, zero exit codes and matching output without claiming an issue; a failing command must still show its exit code and bounded reason. The two original files must retain their exact bytes. Reconcile producer, Windows x64, Windows ARM64, Ubuntu and macOS CI on the final product source. This preliminary source correction does not qualify or authorize stable release.
