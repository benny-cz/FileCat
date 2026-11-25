# E-I672 — On Windows, Enter on a share that needs credentials went up instead of signing in

2026-11-25. Found by `v1.0.0-rc.3`'s final qualification on Windows, checklist c9 (I572's stopping rule: "UNC credential behavior must remain intact"), on the installed rc.3 (`1.0.0+60fe1731`) in the Windows 11 qualification VM. **Corrected the same day by c9 on rc.4:** the share itself never offered a sign-in, and the first fix reached only the server's share list.

## Finding

- **The share:** `\\192.168.58.129\fcqa` on the Ubuntu test VM (Samba 4.19, read-only, one test account, `map to guest = never`). FileCat started on it with no connection and no stored credential.
- **What the share showed,** on rc.3 and on rc.4 alike: `\\192.168.58.129\fcqa: The user name or password is incorrect.`, then `Ctrl+R retries · Backspace goes up`. These are Windows' own words for ERROR_LOGON_FAILURE. The `..` row was the listing's only row, focused. Nothing offered a sign-in, and the words suggested that a password had been typed.
  - **Correction:** the first version of this record gave the server's notice as the share's. The screenshots of both runs show the share's own words above.
- **Enter** took FileCat up to `\\192.168.58.129`, whose share list Windows refused: `\\192.168.58.129 requires credentials (Access is denied.). Press Enter to sign in.` On rc.3, Enter there went up again, and Windows' credential prompt never appeared.
- **No credential was typed on rc.3:** the session was stopped before the owner was asked to sign in. Nothing was connected or stored, and the OneDrive guard found the owner's OneDrive unchanged.

## Cause

There were two gaps; the first fix closed one of them.

1. **The server's share list** (`NetworkShareProvider`, NET-004) says "requires credentials … Press Enter to sign in". But `0715f5c9` (2026-10-30, [I572](E-I572-denied-folder-parent-navigation.md)) lets a focused parent row escape a failed listing before Open treats it as a retry or sign-in. A failed listing has only its parent row, focused, so Enter always went up. Every release since `v1.0.0-preview.4` carries this gap.
2. **A share opened as a folder** (`\\server\share`, or a folder on it) is listed by the Windows file system provider, which passed Windows' own error on. Nothing said that a sign-in would help, and nothing offered one. Every release carries this gap.

**Impact:** on Windows, FileCat could not sign in to a share or server that needs credentials. A user had to connect it elsewhere first (Explorer, `net use`, or Connect network drive) and then read it again. Linux and macOS are not affected: there FileCat asks for SMB credentials in its own dialog while listing.

## Fixes

- **First fix, in rc.4:** `fc26061a`, PR #57, merged as `3934b843`. In `MainViewModel.OpenFocused` (Windows only), when a failed share or server listing reports that it needs credentials, Open signs in first, before the parent row's escape: Windows' prompt, then the listing is read again.
  - Unchanged: Backspace still goes up, a local denied folder's `..` row still escapes it (I572), Ctrl+PgDn ("Enter folder or archive") still enters or goes up, and Linux and macOS behave as before.
  - It reached only the server level (gap 1).
- **Second fix, for rc.5:** `8679dd50`, [PR #60](https://github.com/benny-cz/FileCat/pull/60), for gap 2. The Windows file system provider now reports a share as needing credentials for the share in two cases:
  - its server refused the logon: ERROR_LOGON_FAILURE and the other logon refusals (invalid password, account restriction, logon hours, workstation, expired or must-change password, disabled, logon type, account expired, locked out);
  - the share denied access at its root.

  The share then says `\\server\share requires credentials (…). Press Enter to sign in.`, and the first fix's sign-in opens Windows' prompt for the share.
  - A denied folder inside a share keeps its plain refusal, so its `..` row still goes up (I572).
  - A server's share list uses the same set of logon refusals.
  - Both listing routes, native and managed, map alike.

## Tests

**First fix:** `DeniedParentNavigationTests.Enter_on_a_share_that_needs_credentials_signs_in_and_Backspace_still_goes_up` (App). It uses a Network-scheme share that lists only after a sign-in, and a recording Shell.
- Backspace (Go to parent) leaves the failed share without asking for credentials.
- Enter (Open) on the failed share's only row: on Windows, one sign-in for `\\server\share`, after which the share lists. On Linux and macOS, the parent row's escape, with no sign-in.

**Second fix** (Windows platform tests, with the scripted find API):
- `WindowsEnumerationTests.A_share_whose_server_refused_the_logon_offers_a_sign_in_to_the_share`, 5 cases: logon failure at the share and in a folder of it, access denied at the share, account restriction, locked out. Each asks for the share's sign-in.
- `Other_failures_keep_their_own_error`, 5 cases, each keeping its own error:
  - a local folder;
  - a denied folder inside a share;
  - a server root;
  - an unreachable server;
  - a credential conflict.
- `The_managed_route_offers_the_same_sign_in`: .NET's own exceptions map alike.
- App test `DeniedParentNavigationTests.Enter_in_a_share_whose_server_refused_the_logon_signs_in_to_the_share` (Windows). A file-system share's server refuses the logon. Backspace goes up without a sign-in. Enter in a folder of the share signs in to the share (not the folder) and reads the folder again.

**Runs** (Windows 11 host, Debug builds; App tests at medium integrity through explorer.exe):

| Code | Result |
|---|---|
| First fix: without it (the test only) | `DeniedParentNavigationTests` 1 of 2 failed: Enter went up to `\\server`, no sign-in |
| First fix: with it | 2 of 2 passed |
| Second fix: provider unchanged (stubs returning no sign-in) | 6 of the 11 new platform cases failed: the 5 sign-in cases (IOException or UnauthorizedAccessException) and the managed route |
| Second fix: with it | `WindowsEnumerationTests` 23 of 23 run passed (3 environment-gated skips, as before); `DeniedParentNavigationTests` 3 of 3 |

## c9 on rc.4, with the owner (2026-11-25)

Installed rc.4 (`1.0.0+16f9379a`), the same share, no connection and no stored credential. The OneDrive guard found nothing changed before or after each run.

- **First attempt** (`batch-c9-owner.ps1`): the share showed Windows' "The user name or password is incorrect.", and Enter went up to the server. The release session stopped the run before the owner signed in, and corrected this record.
- **Retry at the owner's request** (`batch-c9-server.ps1`), along the route rc.4 offers:
  - Enter on the share went up again (gap 2).
  - At the server, Enter opened Windows' "Enter network credentials" prompt for 192.168.58.129. The screenshot shows it; the harness's own window check missed it, which is a limit of the harness.
  - The owner signed in with the share's test account, typing the user name and password themselves and leaving "Remember my credentials" unticked.
  - FileCat then listed the server's shares, opened `fcqa` (readme.txt, sub), and opened sub (nested.txt).
  - The only connection was a temporary one to `\\192.168.58.129\IPC$`, and the script removed it. No credential for the server was stored: 0 before, 0 after.
- **Conclusion:** rc.4 closes gap 1, verified with Windows' real prompt and a real sign-in. Gap 2 is the second fix's.

## The second fix in the VM, before rc.5

A local build of both rc.5 fixes (`1.0.0-dev+633ab423`, not a candidate) ran on the same share:
- The share now says `\\192.168.58.129\fcqa requires credentials (The user name or password is incorrect.). Press Enter to sign in.`
- Enter opened Windows' "Enter network credentials" prompt for 192.168.58.129.
- Nothing was typed into the prompt. It closed when the probe stopped FileCat; nothing was connected or stored.

## Not covered

- The share-level sign-in with the owner, on the candidate's installed bytes: c9 on rc.5.
