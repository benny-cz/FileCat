# E-V08-L2 — The remote lab against a second implementation of each protocol (ProFTPD)

Preliminary automated evidence for plan V08 ("at least two implementations each where applicable"). Development builds;
one server machine (the lent Ubuntu 22.04 VM, E-ENV-05); raw outputs under `artifacts/release-evidence/` (SHA-256 below).

## Setup

- **ProFTPD 1.3.7c** (Ubuntu's package) beside the lab's servers of E-V08-L1: explicit FTPS on 2121 and implicit FTPS on
  2990 (both with MLSD/MLST, which vsftpd lacks; ProFTPD's defaults otherwise, `AllowStoreRestart` off among them), and
  SFTP through `mod_sftp` on 2222 with its own host key (banner `SSH-2.0-mod_sftp`). The lab's certificate and
  throwaway account. Script `v08-l2-ubu-proftpd.sh` `2c685731cb242bea0a3e8c8b87f931c0b6f28daf4dd3cba6a741fa49c6b6c90a`.
- Installing ProFTPD made apt **remove vsftpd** (both packages provide `ftp-server`); its configuration stayed. vsftpd
  3.0.5's own package was unpacked beside ProFTPD and run by two units with the lab's configurations (`v08-l2-ubu-vsftpd-restore.sh`
  `43f7b88eacc8c99aa6740e988584e3fb6efd90f01391b6ca858366d58d6c1d0b`). The VM's snapshot revert removes all of it.
- The same lab cases (`RemoteLabTests`) pointed at ProFTPD by `FILECAT_REMOTE_LAB_PORTS="sftp=2222,ftpes=2121,ftps=2990,ftp=2121"`
  (`v08-l2-lab-run-proftpd.ps1` `fbb689c33f829f78723fa7ee0778e980ac074f0aa75c24fca8f151ffd107a10d`); server-side
  checks still through the lab account's shell on OpenSSH (22).

## First run (`e527a86`): 14 of 19

TRX `v08-proftpd-e527a86-remote.trx` `2c7d0e58e4495b701d36db1b7ee36fe56b635c531494d2de76f9de114ec5029e`, console
`v08-proftpd-e527a86-remote.txt` `184a576e6c49897ab7aea18ab38d78138c218a548e2086a12503e4e913a3927d`.

| Case | Outcome | Cause |
|---|---|---|
| Tree up and back, all three protocols | failed at the last check | The test expected the download origin without a port; FileCat names a server on another port than the protocol's own with it (`HostUrl=sftp://…:2222/`), rightly. Test corrected; content, names and times had passed. |
| Links renamed, moved, set aside, replaced, deleted — SFTP | **failed: targets moved** | **I46** (below) |
| Upload cut off by the server — explicit FTPS | **failed after 10 min 7 s** | **I47** (below) |
| The 14 others (trust, pinning, consent, refusal, cancel, odd names and edge spaces over MLSD, links over FTPS, socket buffers, the SFTP cut-off) | passed | — |

## I46 — ProFTPD's SFTP renames a link's target

FileCat's diagnosis run (`v08-proftpd-links.txt` `5668c1556a8c67531d363337a1bf3305f650baf2309ad1584644025ad6bc8f57`):
after renaming the link `file link`, the server held `renamed link` as a **regular file** and the link still dangling;
after moving the link `dir link` into `into`, the folder `real` it points to had moved there. OpenSSH's own `sftp` client,
sending each path as given, gets the same from that server (`v08-proftpd-link-probe.txt`
`a2381ff7c94f76878d28e52925b68caaf5152977af0c17b1b31592108a118e74`, script `v08-l2-ubu-sftp-link-probe.sh`
`65503bc65b5aa672d1a041b0974905ddb50267cbfcdb80c0ce59c609b8f3b85d`):

| Operation on a link | OpenSSH 8.9p1 | ProFTPD 1.3.7c `mod_sftp` |
|---|---|---|
| rename | the link renamed | **the target renamed**; the link left dangling |
| rename into a folder | the link moved | **the target moved** |
| rename of a link to a folder | the link moved | **the folder moved** |
| remove | the link removed | the link removed |

So it is the server's behaviour, and every client meets it. SSH.NET offers no way to read a link before renaming it, so
FileCat (`3f1b554`) renames, moves and sets aside links over SFTP only on servers that identify as OpenSSH; elsewhere the
item fails with the reason, without a retry question. After (`v08-proftpd-links-after.txt`
`d5d5d74daa1c15a1b216f521daf9f63722c031fd6dce1dfc0364a1be0e57d3cc`): over ProFTPD's SFTP the three are refused and every
link and target is where it was; over its FTPS (RNFR/RNTO) the links themselves are renamed and moved.

## I47 — An upload cut off on ProFTPD's FTPS never finished

A trace of every channel call (`i47-drop-trace.txt` `45b56181e355056d86fbb2d5618b7db26cd31aae87841ab52f91d1c84761133a`,
probe `i47-drop-trace-probe.cs.txt` `047d2ccf8df4e04b9d9c9c561961912e4f4601a134db3e2ed9e526573ca8dab0`; stack dump of the
stalled test `v08-proftpd-drop-stacks.txt` `027e7819b8b9e6b21878303586a226fffcc5b62f72054944a0ac532098c0e636`):

1. The write of the data connection failed at the cut (86 MB in), at once.
2. Closing the transfer waited **60,028 ms** for the server's final reply on the control connection, the whole read
   timeout: ProFTPD's session process was killed (`v08-l2-ubu-proftpd-drop-probe.sh`
   `e9bb17379ed325e60332e9755b5b12e5b8f00db63127c2c865f43e4940af62d1` confirms the drop ends it), and the closed control
   connection was not noticed. vsftpd's failed in 3 ms.
3. After Retry, continuing was refused: **`451 Append/Restart not permitted, try again`** (ProFTPD without
   `AllowStoreRestart`). FileCat asked again; every Retry was refused the same way, until the test's limit.

Fixes (`111ebcd`): a refusal to continue makes the upload start again, saying why (the channel's contract always said
so; the upload code did not do it). After a transfer breaks, FileCat waits 5 s for the server's verdict — keeping a
reason such as a full quota — then ends the connection without QUIT, reported as lost, so Retry reconnects; a probe
showed that ending it from another thread returns in 10 ms and frees the reply wait at once (`i47-abandon-probe.txt`
`8fcca36a46f5ec078fe8a72a9cc83a82ce03ca2375075b144ff5764c8c55632e`, probe `i47-abandon-probe.cs.txt`
`2b3eaa34679363cf38f06d657587057b1bf74849fdb55cf6d43aa1a2efc83ded`). After: ProFTPD's FTPS case went again from the
start with the server's reason, vsftpd's continued at 79,790,080 bytes, both questions now "connection lost … Retry
connects again" (`v08-proftpd-drop-after.txt` `a2c81ae6cce882f08ae3b4cc65a7f3a4c63a291f6037abf16d7d69d9942285f2`,
`v08-vsftpd-drop-after.txt` `c0b31de8c729a618e2ea1a97e9248167fda2763692720dcc63bd64f786029438`).

## I45 — Listing times as far as the server states them

vsftpd answers listings with LIST (the minute for the last half year, the day for older files); ProFTPD with MLSD (to
the second). The lab's tree case now also compares the local tree with its copy on the server by size and time: same
on both after `111ebcd` (`v08-vsftpd-i45-tree.txt` `17f91ea4f01f872c5cb3722136f195439ecd1bcc5a4d74fff9f886a96c8dde0e`,
`v08-proftpd-i45-tree.txt` `736f3f59be38bb9110754ad536f463413cd7d2aed7601b2a91d6f6b170668c34`). Before, with times
compared as exact, every file of that tree (dated 2021) would have differed from vsftpd's day-only listing by its time
of day; that run was not repeated on the old build.

## After the fixes (`111ebcd`, Debug)

| Lab | Result | TRX | Console |
|---|---|---|---|
| ProFTPD (2121, 2990, 2222) | **19 of 19**, 121 s | `v08-proftpd-111ebcd-remote.trx` `2c02e62321e565d9f54db90ed308c1590f9ada215d48b2e17cf2314ee934e675` | `v08-proftpd-111ebcd-remote.txt` `98b203bca5d8b02694e8e2d9abf69d5c7dae50ff7f6ba78e65335dcaa09e0411` |
| OpenSSH and vsftpd (22, 21, 990) | 19 of 19, 110 s | `v08-base-111ebcd-remote.trx` `29091ca3efc0366966be86193674ff4c6b4a4ae0c6ce9925a4857fadf0087a7f` | `v08-base-111ebcd-remote.txt` `52a52459798a7566a126ce0485dbe723e4c8182c8199ac7ec5833118e78b2051` |
| Samba (E-V08-S1) | 7 of 7, 37 s | `v08-base-111ebcd-smb.trx` `98ddd2596bc024ead3afd2e6d6603e701ccc329d4f1cc53191007b944d4506ed` | `v08-base-111ebcd-smb.txt` `b14bee4d88769045a167727b4ab770c81a63cec9329d14fd5410420f2d67acff` |

## The Windows VM as a client (`d228632`, Release)

The same lab run from the lent Windows 11 VM (build 26300, its own fresh profile), not the host: OpenSSH and vsftpd,
then ProFTPD, then the Samba share signed in with `net use`; the lab helpers from Windows PowerShell 5.1, their commands
base64-encoded (E-ENV-05). Bundle `fc-lab-d228632.zip` `6a610f15295d7775e1a90c693088b3c7c08b71c1aed8f017319de674c8220e02`.
Outputs under `vm-lab-d228632/`:

| Lab | Result | TRX | Console |
|---|---|---|---|
| OpenSSH and vsftpd | **21 of 21**, 104 s | `v08-vm-remote.trx` `caa55931bc61042f595f0f407a7b2ad3838efa1e750d85562c2e2ca303b7cc2f` | `v08-vm-remote.txt` `86e93a0c114d8c466158dd5a192554cbac8eda1697c8f871948280ab0ed7c120` |
| ProFTPD | **21 of 21**, 92 s | `v08-vm-proftpd.trx` `58f6428a0bc8b5e658b0800934b3f2eece26afc746e78ec5142712ec59691ed6` | `v08-vm-proftpd.txt` `aadeec0353b95794a86f096b7ed5119ed19cd275410b166ccedc1c7c014b2dcc` |
| Samba | **7 of 7**, 35 s | `v08-vm-smb.trx` `6b9b328a8f177d89e87483fa9021cbcb38dff5d0fda57c6f9851b6e7b0fbad73` | `v08-vm-smb.txt` `40e8a84bdb9701e54cd47cd8463e35e004635525ac136275156906bf88e08763` |

A first attempt (`febb51d`) is void: the lab VM's network adapter hung during it (E-ENV-05), and two of its helpers
failed in PowerShell 5.1 (quotes dropped from native arguments; fixed). It also showed the cut-off case depending on how
quickly the drop command ran; the case now holds the job while the server drops it (`d228632`).

## Limitations

- One version of each server, on one machine, with near-default configurations; the Windows VM as a client is still to
  do. ProFTPD with `AllowStoreRestart` on (where it continues uploads) was not run.
- Which SFTP servers rename links themselves is known only for OpenSSH (allowed) and ProFTPD (refused): FileCat refuses
  link renames on every other SFTP server, which may refuse some that would have been right.
