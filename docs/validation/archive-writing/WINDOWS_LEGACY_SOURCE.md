# Windows read-only legacy edit source checkpoint

9 November 2026. This is a source-admission component, not a migration or product release gate.

`WindowsLegacyEditSource` holds the old root, session, `files`, `session.json`, and working file
through non-reparse native handles under the ordinary user's token. The old directory leaves carry
`READ_CONTROL` so the owner and DACL are reviewed through those handles. The root/session/files
names, single-link regular files, bounded complete record and working bytes, file metadata, and
the working origin stream are reviewed again before and after a copy. Extra record/working streams,
broader access grants, and changed objects are refused. `CopyTo` only writes to a supplied stream;
the component has no publication, registration, or deletion method.

The first VM run, v48, exposed an access-denied defect: the legacy directory handle carried
traverse/attributes but no `READ_CONTROL`. Narrowing the requested descriptor to owner/DACL in
v49 was insufficient. v50 added a separate read-control directory pin and passed 7/7 cases.
The expanded v51 ordinary-user run passed **10/10**, including an actual ZIP member edited into
the historical version-1 record shape, origin mark copy, hard-link refusal, extra NTFS stream
refusal on both record and working file, and exposed file/folder ACL refusal. v52 passed
**168**, skipped **139** existing platform/crash guarded cases, and failed **0** across the legacy
source, Windows archive storage, edit directory, and origin suites. Those skips are not passes.
The guest Release build had zero warnings/errors. Its standard-user test process exited with code
zero; the VM had no FileCat/test process after the run.

The copied guest source for eight Core/Windows/test files exactly matched the isolated worktree.
Three guest DLLs, the build log, raw v51/v52 TRX files and test log were retained under
`.tmp/storage-evidence/windows/legacy-source-v52`; the reproducible verifier is
`eng/validation/archive-writing/verify_windows_legacy_source_v52.py`. Its verified manifest
SHA-256 is `dfe38d56ac23dc84ed4e5afeb8466e64429c2e9920647109aa72072ecda9355c`.

This component remains unregistered. The native destination transaction, preserved origin
application, durable active/retained relationship, exact-object retirement, connected all-platform
qualification, and final review remain open. The current Windows test is a direct guest test of
the isolated component; it does not enable old-folder adoption in the application.
