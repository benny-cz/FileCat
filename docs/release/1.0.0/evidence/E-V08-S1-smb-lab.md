# E-V08-S1 — FileCat's file operations on a real SMB share of another machine

Preliminary automated evidence for plan V08 (network locations through the operating system). Not final qualification:
one server implementation (Samba 4.15.13-Ubuntu on the lent Ubuntu 22.04.5 VM, E-ENV-05), one client machine (the physical host,
Windows 11 26220), a development build.

## Setup

- **Server:** Samba 4.15.13-Ubuntu, share `fcshare` (`/srv/fcshare` on the VM's ext4 root, `read only = No`,
  `valid users = fctest`), default VFS modules (no `streams_xattr`, so no alternate data streams); host-private NAT
  address 192.168.58.129.
- **Client:** Windows' own SMB client on the host (dialect SMB 3.1.1, neither signed nor encrypted as negotiated), signed
  in to the share for the session (`net use … /persistent:no`),
  FileCat's job engine with the Windows file operations, as the app wires them, in `SmbLabTests` (gated by
  `FILECAT_SMB_LAB`; skipped everywhere else).
- **Oracle:** the server's own reading of the share (`FILECAT_SMB_LAB_SERVER_LIST`: SHA-256 of every file and every entry
  with its size and time, taken on the VM through VMware guest operations), not the client's possibly cached view.
- **Interruption:** `FILECAT_SMB_LAB_DROP` kills the smbd processes serving the test account (`smbstatus -p`), as a
  server restart or a network cut would.

## Results

Run 1 at `5183cd3` with the new tests (TRX `v08-smb1.trx` `3ec2b7f5e39fe76bb726fa4d034f2b58a78e5567782d6ddff9d01050c30b7d69`, console
`v08-smb1.txt` `1ba151ea9b23a7d2beb3eae16d2a138366a6398aa6c8af03169cccc5b4b667c1`): 6 of 7 passed. Run 2 at `6585024` (the I34
fix; TRX `v08-smb2.trx` `35ff1946e38d017c0905e19d95a75b4409a59663fcffe0ee240bebc800569eb5`, console `v08-smb2.txt`
`18c8fbafd893d36347e18dbf157ae865f40f21e38b433b2d47d423d490df7588`): 7 of 7 passed.

| Case | Result |
|---|---|
| A tree (40 files of 0–70 KB with set times, a 48 MB file published from a staged name, a folder "Žluťoučký kůň", an empty folder) to the share and back through Copy jobs | Passed: the server's digests equal the source's; nothing staged left; modification times kept exactly (drift 0); back again byte for byte |
| To the Recycle Bin on the share | Passed: the share has none, so nothing was deleted and the job said why; with the user's agreement to delete permanently what the bin cannot take, deleted |
| A 64 MB file moved within the share | Passed: renamed on the server (same file identity before and after, no copy through the client); the server's digest unchanged |
| A downloaded file (Mark of the Web) moved to the share | Passed: the share cannot store the mark, so FileCat asked before deleting the original; kept on request. Run 1 worded it "NTFS cannot store…" (Samba reports NTFS) — **I34**; run 2: "The network share cannot store its download origin (Mark of the Web)" |
| Replacing a file open on the share in FileCat's viewer | Run 1: refused by the server, reported as "Access is denied. If the destination is a protected folder, Windows Controlled Folder Access may be blocking FileCat." — **I34**; run 2: reported as in use ("… in use by another program or window (for example an open viewer or editor …)"), old content intact, no staged copy left |
| A 256 MB copy cancelled at a fifth | Passed: nothing left on the share |
| A 128 MB copy whose SMB session the server dropped at 30 % (killed smbd) | Passed both runs: run 1, Windows' client reported the loss ("The network location is no longer reachable …", offline) and FileCat's Retry completed the file; run 2, Windows' client reconnected by itself (the server's log shows the killed session and a new one) and the copy went on without a question. Both times the server's digest equals the source's, and nothing else is left |

## Limitations and next cases

- One server implementation (Samba 4.15, default configuration); a Windows server share and a NAS remain.
- The client ran on the physical host; the Windows VM as a client (as the owner suggested) remains.
- Not yet: a share that keeps alternate data streams (`streams_xattr`), a share without permission to write, very large
  folders, latency, and links on the share.
- Replacing a file open on a share cannot succeed while it is open: Samba, like Windows, refuses to rename over an open
  file, and there is no POSIX-semantics rename over SMB. FileCat now says it is in use; closing it and choosing Retry
  replaces it.
