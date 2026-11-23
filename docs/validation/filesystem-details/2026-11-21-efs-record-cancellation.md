# EFS record-query cancellation

The retained Windows file-record reader asks the EFS API separately for users and recovery agents. The F-LIFE boundary requires a cancellation observed during the first call to prevent the second call and prevent publication of the partial record.

`WindowsFileRecords.ForEachEfsQuery` now checks cancellation before and after each call, before formatting rows or looking up account names, and after each result append. The EFS native list is freed even when cancellation follows the call. The enclosing record reader checks again before publishing its report. A synchronous native call still owns its handle until it returns; cancellation does not interrupt or abandon that call.

The focused `Canceling_a_started_EFS_user_query_waits_for_return_and_skips_recovery_query` test held the first query, canceled the request, verified the worker had not completed, released the query, and observed `OperationCanceledException` with only the user query recorded and no appended rows. It passed 1/1 on Windows x64 using .NET 10.0.101. This is deterministic call-order evidence; it does not qualify native EFS API cancellation timing or the full F-LIFE matrix.

The legacy record read also checks cancellation before path normalization or acquisition. `A_precanceled_record_read_never_opens_the_item` uses a nonexistent path to distinguish cancellation from an attempted open.

The EFS certificate-list parser now rejects an entry whose declared `cbTotalLength` is shorter than its native pointer-bearing header. The focused malformed-list test passed 1/1 on Windows x64. The record reader also treats a root reported as `DRIVE_REMOTE` as remote when handle remote-protocol information is unavailable, so the user/recovery EFS queries are skipped for a mapped network drive. That mapped-drive fallback compiled on Windows x64; it still needs a prepared native mapped-share fixture. Neither change closes the wider N-WIN or F-LIFE gates.

Malformed lists no longer display **none** as though Windows had returned an empty list. The summary field says **not completely read** when every entry was rejected, or marks a displayed count as partial when some entry data was rejected; the specific problem remains in the section lines.

The parser now also flags a missing certificate hash and a description that reaches its 1,024-character display limit as incomplete. This makes the retained record report disclose missing or possibly cut data, and makes the EFS setup association gate refuse that incomplete list. The existing focused synthetic-list fixture passed 1/1 on Windows x64 after these checks, with a short repository-local temporary path for the native-bootstrap test build. This is parser evidence, not a VM native EFS qualification.
