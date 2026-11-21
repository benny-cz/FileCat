# EFS boundary for external command output — decision proposal

Status: **proposed, not adopted**. This is an amendment to E-OUT15/T-EXPORT/N-OUT in [the NTFS compression and EFS plan](FILECAT_NTFS_COMPRESSION_EFS_IMPLEMENTATION_PLAN.md). It does not enable EFS mutations or waive any key, identity, decompression, destination or native qualification gate.

## Observed route

`ApplyCommand.Execute` starts a user-selected external program with the selected item as an argument. It retains bounded stdout/stderr tails in `JobCommandResult`; on failure it also copies the last three nonempty lines into `JobIssue.Message`. `OperationReport.Write` includes both. The Operations drawer offers a report window with Copy and Save, a streamed complete report Save, and exact issue-reason Copy. The issue reason is also selectable text. Guarding only the report's Copy/Save buttons would leave the same bytes on the issue route.

The child program can read any resource available to its account, including files outside FileCat's selected scope. FileCat cannot derive a reliable EFS origin or key association for the program's output from the selected item's metadata. Labeling all output clear, EFS-derived, or protected would invent provenance. The current E-OUT15 rule refuses publication of content with Unknown provenance; applying it to captured command output would remove this existing output feature across all commands.

## Decision options

**A — external-command boundary with an explicit acknowledgment (recommended).** Before each ApplyCommand job, show an initially unchecked acknowledgment that the external program may read protected files, and that FileCat will retain bounded stdout/stderr and failure lines in its ordinary operation history. The review says those lines can appear in the report and issue details and can be copied or saved as ordinary text. It requires the user to decide whether the command and its output are appropriate for that destination and account. Treat only this user-launched child-process output as outside FileCat's EFS source-provenance promise, as with the external-editor guidance in E-OUT11. Keep the EFS destination replacement guards for report Save, the existing capture bounds, and all FileCat-owned output routes under E-OUT1–15. Record explicit UI and native checks for this disclosure. The acknowledgment does not assert that the child process or FileCat protects the output.

**B — suppress captured command output.** Do not retain stdout/stderr tails or copy them into issue reasons for any ApplyCommand job. Reports keep metadata, exit codes and generic failure status. This satisfies Unknown-content refusal without an exception, but removes the current command-output diagnostics and exact-copy behavior for every command. It does not control what the external program writes directly.

Until one option is adopted and implemented, ApplyCommand output remains an open E-OUT15 publication scope. Existing destination-guard changes protect an EFS report target; they do not qualify the source of captured command output. This proposal does not change the separate selected-value report refusal or the approved EFS pathname/decompression boundaries.
