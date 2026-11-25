# ADR-14: Per-plan administrator broker

**Status:** Decided for P4a (2026-09-28). TV-15's VM checks (UAC prompts, Administrator Protection, mapped drives and SUBST in the elevated session, crash and timeout) remain external release gates.

## Decision

A finished operation whose items failed with *access denied* offers **Retry as administrator** in the operations pane. The original FileCat window keeps its authority and the retry covers only the failed roots:

1. **FileCat builds one plan.** Files become volume-GUID paths (`\\?\Volume{…}\…`), resolved before elevation because drive letters are per logon session. Registry keys under HKCU become `HKU\<SID>`, so the plan names the requesting user's own hive even when the administrator is another account. The plan also holds a single-use ID, the time, the user SID, and the requesting process ID.
2. **Windows asks for approval.** `FileCat.PrivilegedHost.exe` is started through UAC with only the plan's path and SHA-256. Its manifest requires elevation, and it starts in its own folder.
3. **The broker verifies before it shows anything.** It runs only from Program Files, and a portable ZIP does not ship it. It reads the plan through a link-refusing handle and checks the hash. It then validates every field against a closed set of verbs: Registry change, delete tree, copy tree, move within a volume, rename, create folder, and ordinary attributes. It checks that the requester is the installed `FileCat.exe` of the named user and records the plan ID in HKLM, so a plan runs at most once. Plans older than 15 minutes are refused.
4. **The broker shows the exact steps.** UAC identifies only the program, so the broker's own window is the consent. Cancel is the default button.
5. **The broker runs the plan and exits.** It reports after every step through a link-refusing write in the plan folder, then exits. Canceling in FileCat leaves a stop marker, which the broker honors between steps and inside trees. Completed steps are never rolled back.

**Explicit Windows administrator window.** Tools → Open FileCat as administrator makes the existing administrator launch available through Windows UAC. It opens a separate instance using the existing Windows high-integrity state rules. Every administrator window starts through `FileCat.AdminLauncher.exe` beside FileCat.exe, a native program that removes the .NET runtime's environment settings (profilers, startup hooks, host and diagnostics settings), which UAC takes from the account's registry environment, before it starts FileCat with the same arguments (I644). Without the launcher FileCat opens no administrator window. The change-journal command offers the same launch for the selected NTFS or ReFS volume and verifies the mount against its captured volume identifier. Linux and macOS use their scoped device authorizations for recovery (ADR-08).

**Reviewed file security.** The permissions inspector of the installed x64 FileCat uses a version 2 request for a bound local NTFS object whenever the installed, administrator-protected helper is present (no opt-in since 23 November). Authorized Apply handles one ordinary file (owner, DACL rule or audit rule) or one DACL rule on a volume root; folders and Windows propagation have no privileged route. Inspection and Apply each have their own approval. The ordinary FileCat process and the native helper authenticate each other over a local named pipe. Before it holds the object or asks for consent, the helper checks that the approving token holds the backup, restore or security privilege the route needs, and otherwise reports which is missing. The helper shows the exact object and requested operation, then uses the rights needed for the selected descriptor section. Its consent is a task dialog on the user's desktop: a lower-integrity window can cover its text but cannot press, move or dismiss it (recorded residual; a secure desktop would close it). An administrator window needs no helper to read: it inspects read-only in its own process, with backup privilege, and security privilege for audit rules, enabled on a duplicate token for each read, and offers no change. The permissions plan records the native UAC, alternate-administrator, refusal and upgrade journeys, the signed x64 install and the remaining ARM64 gate. [Permissions and ACL implementation plan](../design/FILECAT_PERMISSIONS_ACL_IMPLEMENTATION_PLAN.md)

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
