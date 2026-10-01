# E-I36 — FTP names refused, trimmed or redirected by the FTP library

Release issue I36; plan V08's specific static follow-up ("audit FluentFTP's exact-version path normalization against
FileCat's exact-name contract"). Preliminary automated evidence: library probes, the live vsftpd server on the Ubuntu VM
(E-ENV-05), a development build.

## Discovery (E-I36-D1)

The new odd-names lab test (E-V08-L1) uploaded a folder of names that shells, option parsers, URL encoders and Unicode
normalizers treat specially. Over SFTP every name arrived exactly (checked against the bytes of the names on the server's
own disk). Over FTP with TLS the job stopped with questions such as:

> Could not copy the file to the server: The FTP path "…/semi;colon" contains unix commands or newlines that might be used
> for FTP injection attacks! Set `SanitizeControlChars` to `false` to allow unix commands or newlines, or set
> `SanitizerMode` to `FtpSanitize.Rename` to silently cleanup such sequences.

The run that timed out left its partial upload behind; those that followed left " leading space" on the server, which
FileCat then failed to delete (`v08-ftp-leftovers-before-i36.txt`
`45861f4cdae0cfe8e2fb14033e3741741ad6986498723858077fd41c88e56406`).

## Mechanism (E-I36-M1)

FluentFTP 55.0.0, probed through its own code (`v08-ftp-sanitizer-probe.txt`
`88285bf3665b571207b8c59f9ee84fbc12df831b12e25ed0696893b1359c72ef`, `v08-ftp-listing-probe.txt`
`b9a0100f7dcfd8da42f4596b4ddc91f13e8d9175fb373211bb8c4470f12bf8ec`) and against vsftpd 3.0.5 (no MLSD;
`v08-ftp-listing-vsftpd.txt` `867b427e77f269a7d2222204b622a50e9c491a362b2ad9905995e3acbfddb4ad`):

1. **Refused names (default "Throw" mode):** `;` and `|` and tabs ("control chars"), `%` ("URL encoding"), `..`
   anywhere in a name ("traversal"), bidirectional marks ("unicode spoofing"); the library's advice reached the user.
2. **Silent changes (in every mode):** every path's backslashes become `/`, and spaces at both ends of every path are
   trimmed — `dir/back\slash` went out as `dir/back/slash`, `dir/ both ` as `dir/ both`: another item.
3. **Listing:** its parser of Unix-style listings (servers without MLSD) trims spaces at the edges of names, although
   the raw lines keep them: `" leading space"` was listed as `"leading space"`, `"trailing space "` as `"trailing space"`.
   MLSD listings were exact.

So over FTP, FileCat could show, read, rename or delete a different item than the one named — a look-alike without the
spaces, or `slash` inside folder `back` — the V08 fail condition "silent name normalization targeting a different
resource".

## Remediation `e50b9d4`

- The library's name heuristics are off; line breaks stay refused by it as well (defence in depth).
- Names are taken exactly from Unix-style listing lines (one space separates the time or year from the name), when the
  line has that shape and agrees with the parser apart from spaces at the edges; otherwise the parser's name stands.
- Paths FTP cannot carry exactly are refused before anything is sent, saying why: a backslash, a space at the end (or the
  start) of the path, NUL, line breaks — "…which FileCat's FTP connection would drop, reaching a different item; nothing
  was done with it. SFTP handles such names."
- Bidirectional marks travel as they are; FileCat's display escapes them (§18.3).

## Revalidation (E-I36-V1)

- `FtpJobTests`: what is refused and what passes unchanged (13 cases); exact names from vsftpd's lines (12 cases).
- `FtpIntegrationTests` (pyftpdlib, host): names FTP can carry travel exactly (`;`, `%`, `%20`, `..x`, a bidirectional
  mark, `-rf`; `|` and tab where the OS allows); a name with a backslash is refused and a look-alike stays (runs on Linux
  and macOS, where such a name can exist; skipped on Windows). On the previous adapter the names test fails (the
  semicolon refused) and the guard lets NUL and the backslash through; all pass on the fix.
- Live (E-V08-L1, vsftpd and OpenSSH): odd names arrive exactly and come back the same over SFTP and FTPS; names with
  spaces at their edges are listed exactly over FTP, those beginning with spaces are read and deleted as themselves,
  those ending with one are refused, and the look-alikes stay untouched.

## Limitations

- Names ending with a space cannot be reached over FTP through this library; FileCat refuses them with the reason
  (SFTP handles them). Other servers' listing formats (Windows/IIS, VMS, z/OS) still rely on the library's parser.
