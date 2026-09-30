# E-V08-L1 — FileCat's remote client against real servers of another machine

Preliminary automated evidence for plan V08 (remote protocols). Not final qualification: one implementation per
protocol, one server machine (the lent Ubuntu 22.04 VM, E-ENV-05), a development build.

## Setup

- **Servers (Ubuntu VM, host-private NAT address 192.168.58.129):** OpenSSH (SFTP subsystem) on 22; vsftpd 3.0.5 with
  FTP and explicit TLS on 21; a second vsftpd with implicit TLS on 990; one self-signed certificate. Throwaway account
  `fctest`.
- **Client:** the physical host (Windows 11 26220), FileCat's remote stack as the app wires it (`SftpConnections`,
  `HostKeyTrust`, `CertificateTrust`, the job engine), in `RemoteLabTests` (gated by `FILECAT_REMOTE_LAB`,
  `FILECAT_REMOTE_LAB_USER`, `FILECAT_REMOTE_LAB_PASSWORD`; skipped everywhere else).
- **Oracle:** the server's own reading of what arrived, through a separate connection (every file read back and hashed),
  and the files downloaded again.

## Results at `3ec60cc` (11 of 11 passed; TRX `v08-lab.trx` `cfa46f7ed3f0f09fcb995e25c976647a290b8b0039bc74aa124b850e78237c5a`, console `v08-lab.txt` `b9e66d0363ba40ff20b06919fd114c5765674ff4012cf2462ba1723cfd87b3b8`)

| Case | Result |
|---|---|
| SFTP: an unknown host key is asked about once and remembered; a different key under the host's name is reported as changed, and refusing it stops the connection before a password is sent | Passed |
| FTPS explicit and implicit: the self-signed certificate is asked about; refused, nothing is listed or remembered; trusted and remembered, the next connection does not ask; a different certificate than the pinned one is asked about again as changed | Passed (both modes) |
| Plain FTP: FileCat asks before a password would travel unencrypted; declined, nothing is sent | Passed |
| A tree (40 files of 0–70 KB, a 48 MB file, a folder named "Žluťoučký kůň", an empty folder) up through a Copy job over SFTP, explicit FTPS and implicit FTPS: every file on the server has the source's bytes (read back separately), nothing half-written is left; down again through a job: byte for byte; every downloaded file is marked with its origin (Internet zone, `HostUrl=<protocol>://192.168.58.129/`); cleanup through FileCat's own Delete job | Passed (three protocols) |
| An upload the server refuses (the root folder) is reported as a permission error and leaves nothing | Passed (SFTP, explicit FTPS) |
| A cancelled upload (256 MB, cancelled at a fifth) leaves nothing under the file's name or any other | **Failed before `3ec60cc`**: the partial copy stayed under its hidden temporary name (54 MB) over SFTP and explicit FTPS — I33; passes since |

## Limitations and next cases

- One implementation per protocol; a second FTP server (for example Pure-FTPd or ProFTPD) and a second SSH server remain.
- Not yet run: a connection dropped mid-transfer (the resume path), high latency (netem), SMB shares through the
  operating system's client, very large folders, links on the server.
