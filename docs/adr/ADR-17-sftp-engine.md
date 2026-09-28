# ADR-17: SFTP engine and remote-change safety (with the FTP/FTPS addendum)

**Status:** Decided for P6 (2026-09-28). TV-12 checks against varied servers (other implementations, reconnects under
load, host-key rotation in production) remain external.

## Decision

**SSH.NET behind a narrow channel.** SFTP uses SSH.NET 2026.0.0 (MIT) with its dependencies BouncyCastle.Cryptography
and Microsoft.Extensions.Logging.Abstractions (both MIT). It lives in `FileCat.Remote`; Core stays free of third-party
packages. FileCat talks to one interface, `ISftpChannel`: listings, stat, reads, exclusive creates, renames of its own
files, atomic replace, and times. The same interface has an in-memory server for tests.

**Changes go through listing entries.** SSH.NET's path-based `DeleteFile`, `RenameFile`, `Get`, and `GetAttributes` first
canonicalize the whole path with `SSH_FXP_REALPATH`. OpenSSH's realpath follows symbolic links, so deleting or renaming a
link through those methods acts on the link's target. FileCat therefore deletes and renames remote items only through
the entries of a fresh listing of their folder. Those act on the listed path itself: remove, rmdir, or rename. Its own
temporary files, never links, are renamed by path. Integration tests against a real OpenSSH server (9.6 on Linux CI,
macOS's own) confirm that a link is renamed and deleted itself while its target stays.

**Host keys.** FileCat keeps its own `known_hosts` beside its state. The user's OpenSSH `known_hosts` seeds trust
read-only; hashed names, `[host]:port`, wildcards, negation, and `@revoked` are understood. On first contact the user
sees the SHA256 fingerprint and chooses Connect once or Trust. A changed key defaults to Cancel. Replacing the
remembered key is a separate, danger-styled choice. FileCat never edits OpenSSH's files.

**Sign-in.** Password, private key file (OpenSSH, PuTTY, or PKCS#8), or keyboard-interactive. Authentication is tried at
most three times per connection. Secrets typed in a session stay in memory. They are saved only when the user asks, and
only in an OS store: Windows Credential Manager, keyed by the profile's ID. Elsewhere they last for the session only.
Changing a profile's server, port, or user forgets its saved secret.

**Connections.** Tabs, viewers, and jobs lease connections per server (at most 4). Idle connections are reused, broken
ones are closed, and only one connection attempt runs per server, so prompts never stack. Connecting happens on
background threads, and their questions reach the UI through the dispatcher; asked on the UI thread, they are refused.

**Transfers.**

- An upload writes a private temporary name, checks the size on the server, and publishes by renaming. Replacing uses
  `posix-rename@openssh.com` where the server offers it. Otherwise the old file is deleted just before the rename, and the
  job says so once. A link at the destination is replaced itself.
- A move from the local disk deletes each source only after its copy is published and checked.
- A move from a server to a local folder deletes on the server only the items that arrived completely.
- Moves within one server are renames.
- Downloads use the common stream engine and are marked ZoneId 3 with the server's host (never the user).
- Links to folders are never followed during recursive copies. This also fixed an infinite recursion in the common
  stream engine.

**Edit sessions.** The P5 sessions have a server kind. The base is the file's size and time when it was read. A commit
checks it again, and the upload job re-checks just before replacing (`ExpectedTarget`). A changed file is never
overwritten without an explicit, danger-styled choice, and a missing file is recreated only when nothing took its place.

**SSH terminal.** Open terminal on a server panel starts the OpenSSH client with the host after `--`. A crafted name
therefore cannot become an ssh option. The session lands in the panel's folder, which is quoted for the server's shell.

## Why

- SSH.NET is the maintained, permissively licensed managed implementation. Its internals (`ISftpSession`) are not public,
  which is why the listing-entry route exists instead of raw requests.
- Treating the symlink-following canonicalization as a data-loss hazard, rather than an edge case, follows the plan's
  rule never to follow links implicitly (§8.1).
- Temporary names plus rename keep a half-written file from ever appearing under the real name. Where the server cannot
  replace atomically, the job reports it rather than hiding it.

## Consequences and limits

- Resume where safe (2026-09-28), within a job. A download that breaks keeps FileCat's own staged copy. After
  reconnecting, it continues only if the source has the same size and time and its last 64 KiB before the break read the
  same. An upload continues this job's temporary file where the server's copy ends, once those bytes match the source
  there and the local file is unchanged (SFTP at any offset; FTP at the end, with APPE). Otherwise the file starts
  again, and the job says which happened. A download's first break retries on its own after a second. A temporary
  file left by a crash is named in the job's issues but is not reused or cleaned up automatically.
- No server-side copy; copies within a server stream through FileCat.
- SFTP version 3 times have one-second precision; "newer" comparisons allow two seconds.
- SSH agent sign-in (2026-09-28) needs no package: FileCat speaks the agent protocol itself (list keys, sign) to
  `SSH_AUTH_SOCK` or, on Windows, the OpenSSH agent service's pipe, which Pageant, 1Password, Bitwarden, and KeePassXC
  also serve. Agent keys reach SSH.NET as host algorithms whose signatures the agent makes; RSA is offered as
  rsa-sha2-512 and rsa-sha2-256 only, never SHA-1. The agent is used only while signing in and is never forwarded.
  Tested against a real ssh-agent and sshd. The terminal uses OpenSSH's own `known_hosts`, not FileCat's.
- Remote folders are not watched; Ctrl+R refreshes.

## Validation

- Unit tests run against the in-memory server: host keys, retries, saved secrets, listings with links, content,
  addresses, leases, every job, and edit sessions.
- The real-server tests start a user-mode sshd on Linux and macOS CI (Windows skips them).
- Headless UI tests cover connecting (key and password prompts), F7, F5 to the server, F8, and F4 with commit.

## Addendum: FTP and FTPS (P8, 2026-09-28)

**Same channel, different engine.** FTP servers use FluentFTP 55.0.0 (MIT) behind the same `ISftpChannel`. Listings,
transfers, uploads through temporary names, moves, deletes, and edit sessions therefore behave as they do on SFTP
servers. FTP profiles carry a protocol:

- `ftpes`: explicit TLS (AUTH TLS), required, never downgraded.
- `ftps`: implicit TLS.
- `ftp`: unencrypted.

A `ProtocolConnector` chooses SSH or FTP per profile. Locations keep the remote scheme; display paths and origin marks
show `ftp://`, `ftpes://`, or `ftps://`.

**Certificates.** A certificate the OS validates for the host is accepted. Any other is shown with its problems, subject,
issuer, validity, and SHA-256 fingerprint. The user may cancel (the default), connect once, or trust it, which pins the
fingerprint per host and port in `trusted_certificates`. A pinned server offering another certificate is reported as
changed, and Cancel is the default.

**Unencrypted FTP is explicit.** It is chosen in the connection dialog with a warning in view. For a typed `ftp://`
address, FileCat confirms once per session.

**Protocol differences, handled explicitly:**

- **No exclusive create, and RNTO may overwrite.** FileCat checks names before creating or renaming. Its own temporary
  names are random.
- **No atomic replace.** Publishing deletes the old file just before renaming the new one, and the job says so.
- **Names with CR or LF are refused**, because FTP commands end at a line break.
- **Folders are removed with RMD**, never FluentFTP's recursive delete.
- **Transfers are binary.** A seek in the viewer ends the current transfer and resumes at the offset (REST).
- **At most 2 connections per FTP server.**
- **Time precision depends on the server's listing.** MLSD times are exact UTC; older LIST times may be minute-precise
  or in the server's time zone.

**Evidence.** pyftpdlib (MIT) serves plain FTP and explicit FTPS in tests on all CI platforms. The tests cover:

- consent for plain FTP;
- listing, stat, reads at offsets, times (MFMT), exclusive create, rename refusal, and link-safe deletes;
- the refusal of names with line breaks;
- pinning and a changed certificate;
- no fallback from FTPS to plain FTP;
- an upload job that replaces a file with the non-atomic notice, and a marked download;
- the protocol, port, and user of typed addresses.

