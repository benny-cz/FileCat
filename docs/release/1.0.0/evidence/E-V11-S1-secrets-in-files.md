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

## Observation (no defect claimed)

A password answered with "save" is written to the store **before** the server has accepted it
(`SftpConnections`, `GetSecret`), and the profile is marked as having a saved password. So a mistyped password that the
user then gives up on stays saved, and the next connection tries it first — one failed login per reconnect until it
is corrected. Nothing leaks by this, but on a server that locks an account after a few failures it could add to them.
Saving only after the server accepts the password would avoid it; recorded here for a decision, not changed.

## Still open in V11

Linux (Secret Service) and macOS (Keychain), including a store that is absent, locked or refuses; settings files of an
older, newer, corrupt or truncated schema; competing instances, read-only profiles and unwritable portable folders;
crash reports and exports; and the argument-recording half, which E-V24-G1-T2 covers for tools.
