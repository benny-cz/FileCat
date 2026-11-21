# EFS record-query cancellation

The retained Windows file-record reader asks the EFS API separately for users and recovery agents. The F-LIFE boundary requires a cancellation observed during the first call to prevent the second call and prevent publication of the partial record.

`WindowsFileRecords.ForEachEfsQuery` now checks cancellation before and after each call, before formatting rows or looking up account names, and after each result append. The EFS native list is freed even when cancellation follows the call. The enclosing record reader checks again before publishing its report. A synchronous native call still owns its handle until it returns; cancellation does not interrupt or abandon that call.

The focused `Canceling_a_started_EFS_user_query_waits_for_return_and_skips_recovery_query` test held the first query, canceled the request, verified the worker had not completed, released the query, and observed `OperationCanceledException` with only the user query recorded and no appended rows. It passed 1/1 on Windows x64 using .NET 10.0.101. This is deterministic call-order evidence; it does not qualify native EFS API cancellation timing or the full F-LIFE matrix.
