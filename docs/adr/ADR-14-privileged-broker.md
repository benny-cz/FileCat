# ADR-14: Per-plan administrator broker

**Status:** Decided for P4a (2026-09-28). TV-15's VM checks (UAC prompts, Administrator Protection, mapped drives and SUBST in the elevated session, crash and timeout) remain external release gates.

## Decision

FileCat never runs elevated. A finished operation whose items failed with *access denied* offers **Retry as administrator** in the operations pane. The retry covers only the failed roots:

1. **FileCat builds one plan.** Files become volume-GUID paths (`\\?\Volume{…}\…`), resolved before elevation because drive letters are per logon session. Registry keys under HKCU become `HKU\<SID>`, so the plan names the requesting user's own hive even when the administrator is another account. The plan also holds a single-use ID, the time, the user SID, and the requesting process ID.
2. **Windows asks for approval.** `FileCat.PrivilegedHost.exe` is started through UAC with only the plan's path and SHA-256. Its manifest requires elevation, and it starts in its own folder.
3. **The broker verifies before it shows anything.** It runs only from Program Files, and a portable ZIP does not ship it. It reads the plan through a link-refusing handle and checks the hash. It then validates every field against a closed set of verbs: Registry change, delete tree, copy tree, move within a volume, rename, create folder, and ordinary attributes. It checks that the requester is the installed `FileCat.exe` of the named user and records the plan ID in HKLM, so a plan runs at most once. Plans older than 15 minutes are refused.
4. **The broker shows the exact steps.** UAC identifies only the program, so the broker's own window is the consent. Cancel is the default button.
5. **The broker runs the plan and exits.** It reports after every step through a link-refusing write in the plan folder, then exits. Canceling in FileCat leaves a stop marker, which the broker honors between steps and inside trees. Completed steps are never rolled back.

## Why

- **Consent equals scope (AI-13).** One approval covers one displayed, immutable plan. There is no standing elevation, unlike Total Commander's `tcmadmin.exe` with `AdminTimeout` or FAR's session-wide elevation.
- **No confused deputy.** The broker accepts no commands, scripts, or libraries from the requester. File work is handle-relative (`NtCreateFile` with a verified root handle and `FILE_OPEN_REPARSE_POINT`), which has three effects:
  - A folder swapped for a link after planning is refused.
  - A link inside a deleted tree is removed as a link.
  - A link inside a copied tree is neither copied nor followed.
- **Registry semantics match unelevated edits.** Registry steps go through the same `RegistryChangeRunner` as ordinary edits: expected prior values, link refusal, and subtree fingerprints.
- **No recycle for elevated deletes.** With Administrator Protection, an elevated recycle would use another account's Recycle Bin. Elevated deletes are therefore always permanent and say so.

## Limits

- **UAC is not a security boundary against same-user code.** A program running as the user can ask for a plan. The design limits the blast radius to what the broker displays; it cannot stop a user from approving a malicious plan.
- **Scope of retries:**
  - Network locations are not retried, because local administrator rights do not change share permissions.
  - Moves between volumes are not retried: copy as administrator, then delete.
  - Recursive attribute changes and changes to times are not retried.
  - Copies retry items from one folder at a time.
- **Copy fidelity:** copies keep timestamps, ordinary attributes, and Mark-of-the-Web. They report other alternate data streams and lost marks, and do not copy ACLs.
- **No undo** is recorded for elevated steps.

## Evidence

Unelevated automated tests run in CI:

- A junction inside a deleted tree survives with its target intact.
- A folder swapped for a junction after planning is refused.
- A copy skips links, keeps Mark-of-the-Web, and honors the replace policy.
- Move, rename, create, and attributes run through verified handles.
- Strict plan validation refuses unknown fields, relative or root paths, streams, cross-volume moves, HKCU keys, and stale plans.
- A report cannot be redirected through a planted junction, and a plan folder behind a link is refused.
- The runner stops between steps.
- An access-denied Registry job becomes an `HKU\<SID>` retry plan.
- Without the installed helper, the elevated job fails with its reason.
- The Program Files and portable checks hold.
