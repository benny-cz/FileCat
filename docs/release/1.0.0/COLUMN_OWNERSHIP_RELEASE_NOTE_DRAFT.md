# Column ownership — unreleased note

This describes the implementation on `codex/column-ownership`. It does not change the published preview.10 release
or claim that these changes are in a downloadable package.

## What changes

Windows x64 users can add **Owner** in Settings → Columns. Existing layouts stay as they are. The column shows
the account name or its Windows SID; long account SIDs have a short cell form. Hover an explained or clipped cell,
or press **Ctrl+Shift+I** to inspect the full identity and unavailable reason. Ownership identifies the account
reported by the operating system; it does not identify the creator, last editor or effective access rights.

Owner reads follow visible rows, including a visible focused row's horizontally hidden columns. Sorting uses
captured results and starts no ownership or name lookup. **View → Analyze folder** explicitly reads the listed
files and applicable folders, reports current, last known and unsuccessful outcomes, then applies the captured
order. It does not read descendants. Large analyses report the actual retained coverage; rows outside it keep
unknown sort keys until another sort or analysis. A temporary refresh failure can show a visibly marked last known
owner. Reread clears ownership and account-name answers.

Linux x64 Owner/Group keep names or numeric IDs, with detailed failures and separate bounded name lookup. They
remain in the Unix Full profile; Permissions appears in Details and Full and describes mode bits with octal detail,
which do not establish effective ACL rights. Windows ARM64, Linux ARM64 and the new guarded macOS Owner/Group
reader remain unavailable pending native validation; macOS Permissions retains its existing behavior.

FileCat requests ownership metadata without content access, ownership changes or elevation. OS traversal,
authentication, auditing and name-service traffic can still occur. Detected Offline/recall entries are skipped;
this is not complete cloud-placeholder detection. UNC/SMB, FAT/exFAT, ReFS, domain and Unix mount/NSS diversity
remain qualified. Where persistent ownership storage is unverified, FileCat keeps the actual OS-reported identity
with an explanation rather than inventing an owner or claiming none exists.

## Settings compatibility

The settings schema, built-in profiles and profile shortcuts are unchanged. New Windows recognizes an existing
`meta:owner` key imported from Unix settings. Older Windows versions omit that unknown key from the parsed layout;
editing a profile or resizing columns there can save the layout without it. Ordinary SaveSettings alone need not
remove the original raw key. Older Unix versions already know Owner. Carrying profiles between platforms retains
the existing treatment of unsupported keys; there is no broader profile migration.

## Validation reference

The [finite acceptance record](../../validation/column-ownership/ACCEPTANCE-20261116.md) and linked package records
separate native passes, deterministic checks and withheld routes. Windows native Owner/Inspect evidence used
SDK 10.0.101 and runtime 10.0.15; the Ubuntu WSL2 native core check used SDK 10.0.101/runtime 10.0.1. The full
integration matrix records its own exact build provenance. These checks do not qualify every runtime, filesystem
or directory service, or establish native compositor latency.
