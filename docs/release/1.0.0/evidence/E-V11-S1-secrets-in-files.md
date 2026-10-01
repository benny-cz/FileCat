# E-V11-S1 — passwords in logs and state files (V11)

Plan: V11 ("Place unique sentinels in secret values, paths and file content, cause representative errors, and search
each default log/export for leakage … No plaintext-secret fallback"), SEC-003/006, STATE-001.

## E-V11-S1-W1 — Windows, the remote stack as the app builds it

`SecretLeakTests.Passwords_reach_neither_the_log_nor_any_file_FileCat_writes` (App tests, gated on the remote lab's
throwaway account) builds FileCat's services as the app does — the Windows platform registered first, so a saved
password goes where the app puts it, Credential Manager — with every file FileCat writes in one fresh folder, and
**diagnostic logging on**, the most FileCat ever writes to its log. Against the lab's real servers on the Ubuntu VM
(OpenSSH; vsftpd with explicit TLS), build `8daf1f7`, host:

| Step | What FileCat said |
|---|---|
| SFTP, a wrong password three times, each answered "save" | `RemoteAuthenticationException: … did not accept the credentials: Permission denied (password).` |
| SFTP, the right password, "save" | listed 8 entries; **Credential Manager holds the password** |
| FTP with explicit TLS, a wrong password three times, "save" | `… Code: 530 Message: Login incorrect.` |
| FTPS, the right password, "save" | listed 8 entries; **Credential Manager holds the password** |
| SFTP to a closed port, a third password | `IOException: Could not reach …:2999: … actively refused it.` |

Each failure was also written to FileCat's log on purpose, with its whole exception chain, so that whatever the
SSH and FTP libraries put in their messages was in the log to be found. Then every file under the data folder was
searched for the three secrets — the wrong-password sentinel, the closed port's sentinel, and the real password — each
as UTF-8, UTF-16 and Base64:

| File | Bytes | Secrets found |
|---|---|---|
| `settings.json` (the three profiles, with "save" recorded as a flag) | 2,547 | none |
| `settings.json.bak` | 2,547 | none |
| `local/known_hosts` | 96 | none |
| `local/trusted_certificates` | 143 | none |
| `local/diagnostics/filecat.log` | 591 | none |

What the test saved in Credential Manager was removed afterwards (`cmdkey /list` shows no `FileCat/sftp/` entry).

A first run without the Windows platform registered — as the headless test host is unless told — kept the passwords
for the session only ("the secret store is this session's only"): that is the portable fallback, which keeps secrets
in memory and never writes them (`SessionSecretStore`), not what the app on Windows uses. The test now registers the
platform the way the app does and asserts that the store is the persistent one on Windows.

## Found in passing, and changed (`f9adb51`)

A password answered with "save" was written to the store **before** the server had accepted it (`SftpConnections`,
`GetSecret`), and the profile marked as having a saved password. So a mistyped password the user then gave up on
stayed saved, and the next connection tried it first — one failed login per reconnect until it was corrected. Nothing
leaked by this, but on a server that locks an account after a few failures each of those would count. It is now kept
once the server has accepted it, and not before. `SftpProviderTests.A_secret_is_saved_only_once_the_server_has_accepted_it`
— three wrong answers leave nothing stored and the profile unchanged, and of a wrong one followed by the right one only
the right one is kept. Against the code before the change it fails, the store holding the last wrong password
("still wrong"). Remote suite afterwards: 116 total, 0 failed.

## Still open in V11

Linux (Secret Service) and macOS (Keychain), including a store that is absent, locked or refuses; corrupt and
truncated state files; competing instances, read-only profiles and unwritable portable folders; crash reports and
exports; and the argument-recording half, which E-V24-G1-T2 covers for tools. A newer schema is covered for every state
file now that the layout honours it too ([I71](../FILECAT_1_0_RELEASE_ISSUES.md#i71--an-older-filecat-saved-over-a-newer-filecats-window-layout)).
