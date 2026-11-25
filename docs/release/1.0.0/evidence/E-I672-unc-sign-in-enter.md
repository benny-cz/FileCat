# E-I672 — On Windows, Enter on a share that needs credentials went up instead of signing in

2026-11-25. Found by `v1.0.0-rc.3`'s final qualification on Windows, checklist c9 (I572's stopping rule: "UNC credential behavior must remain intact"), on the installed rc.3 (`1.0.0+60fe1731`) in the Windows 11 qualification VM.

## Finding

- **The share:** `\\192.168.58.129\fcqa` on the Ubuntu test VM (Samba, read-only, one test account, no guest). FileCat started on it with no connection and no stored credential.
- **What FileCat showed:** "\\192.168.58.129 requires credentials (Access is denied.). Press Enter to sign in. Ctrl+R retries · Backspace goes up", with the `..` row as the listing's only row, focused.
- **Enter:** FileCat went up to `\\192.168.58.129`, which also needs credentials and showed the same notice. Windows' credential prompt never appeared: no new top-level window. A second Enter would have gone up again.
- **No credential was typed:** the session was stopped before the owner was asked to sign in. Nothing was connected or stored, and the OneDrive guard found the owner's OneDrive unchanged.

## Cause

`0715f5c9` (2026-10-30, [I572](E-I572-denied-folder-parent-navigation.md)) lets a focused parent row escape a denied listing before Open treats it as a retry or sign-in. A failed share or server listing has only its parent row, focused, so Open (the Enter key) always went up and never reached the sign-in that the notice promises (NET-004). Ctrl+R only lists again. Every release since `v1.0.0-preview.4` carries it; rc.2's c9 recorded the share sign-in as open, so it was never run.

**Impact:** on Windows, FileCat could not sign in to a share or server that needs credentials. A user had to connect it elsewhere first (Explorer, `net use`, or Connect network drive), then reread. Linux and macOS are not affected: FileCat asks for SMB credentials in its own dialog while listing.

## Fix

`MainViewModel.OpenFocused` (Windows only): when a failed share or server listing reports that it needs credentials, Open signs in first (Windows' prompt, then the listing is read again), before the parent row's escape. Backspace still goes up; a local denied folder's `..` row still escapes it (I572); Ctrl+PgDn ("Enter folder or archive") still enters or goes up; Linux and macOS are unchanged.

## Tests

`DeniedParentNavigationTests.Enter_on_a_share_that_needs_credentials_signs_in_and_Backspace_still_goes_up` (App), with a share provider that lists only after a sign-in and a recording Shell:
- Backspace (Go to parent) leaves the failed share without asking for credentials.
- Enter (Open) on the failed share's only row: on Windows, one sign-in for `\\server\share`, then the share lists; on Linux and macOS, the parent row's escape, with no sign-in.

I572's own test in the class still passes.

**Runs** (Windows 11 host, medium integrity through explorer.exe, Debug build of the fix branch):

| Code | `DeniedParentNavigationTests` | The new test |
|---|---|---|
| Without the fix (the test only) | 1 of 2 failed | Expected `\\server\share`, actual `\\server`: Enter went up, no sign-in |
| With the fix | 2 of 2 passed | — |

## Not covered

- Windows' real credential prompt: the owner signs in to the test share on rc.4 (c9).
