# E-I38, E-I39 — Remote transfers at a 100 ms round trip (V08 latency)

Release issues I38 (FTP) and I39 (SFTP). Preliminary automated evidence: the lab of E-V08-L1/E-V08-S1 with 100 ms added
to every packet the server sends (`tc qdisc … netem delay 100ms` on the Ubuntu VM; ping 101–102 ms), a development build.

## The latency run before the fixes (at `02acee6`)

The whole lab at 100 ms (TRX `v08-lat100-02acee6-remote.trx` `bc33498172e64abf92a822dfbda66fe5aadf9b952c1ef8c9852fd44b99b9a757`, console `v08-lat100-02acee6-remote.txt`
`fce613391f8a3520ae0a5bee62966c299bbd17566a3657906e09bda4751ddfb2`; SMB `v08-lat100-02acee6-smb.trx` `b8e20a9dbbda459de7b670f0e567c48aa5f29b19d7c31c05f14ef50c8ae37183`): **correctness held** — trust and pinning, consent,
byte-exact round trips over SFTP, explicit and implicit FTPS and SMB, cancels leaving nothing, a refused upload, odd names,
names with spaces at their edges, an FTPS upload cut off and continued — 15 of 16 SFTP/FTPS cases and 7 of 7 SMB cases
passed. The 16th, an SFTP upload cut off by the server, did everything right (FileCat asked, reconnected, continued at the
checked 41,349,357 bytes) but was still running at the test's five-minute limit. The times showed why:

| Case | No latency | 100 ms |
|---|---|---|
| Tree up and back, SFTP | 15.5 s | 6 min 4 s |
| Tree up and back, explicit FTPS | 20.9 s | 6 min 18 s |
| Tree up and back, implicit FTPS | 17.3 s | 6 min 15 s |
| 256 MB upload cancelled at a fifth, SFTP | 8.3 s | 3 min 4 s |
| Tree up and back, SMB (Windows' client) | 7.9 s | 1 min 17 s |

## I38 — FTP: a stat listed the whole folder (fixed `f93f919`)

A trace of FileCat's FTP channel against vsftpd at 100 ms (`v08-ftp-trace-lat100.txt` `b7e2a19041d35828cfacbf899227ea4b5d606d083c55d10ba39c08fb198e1ccf`): to stat one file,
FluentFTP's `GetObjectInfo` — on a server without MLST — opened a data connection and listed the whole parent folder
(`EPSV`, `LIST /home/fctest/.`), then FileCat asked `SIZE` and `MDTM`; opening the file for reading did all of it again;
then `EPSV`, `RETR`. **2.65 s for a 1 KB file**, and each listing grows with the folder: copying a 10,000-file folder from
such a server would list the folder 20,000 times. It also reported a link rather than following it, against the channel's
contract. Fix: without MLST a stat is `SIZE` and `MDTM` (which follow links); content whose length is known opens without
another stat. After (`v08-ftp-trace-lat100-after.txt` `49eb34fbe80392c373e71fde53d7d8b3e0823c836cca497a7071174c6184e74f`): stat 0.22 s, open and read 0.63 s.

## I39 — SFTP: uploads wrote one request at a time (fixed `2ba114e`)

SSH.NET measured over the same link (`i39-sshnet-throughput-lat100.txt` `983fd22d24a868a509ca82e5954ede9f3bb8fab1fe4549777d58ee8a130b5985`), 16 MB each way:

| Way | Throughput |
|---|---|
| Stream writes (FileCat's uploads before the fix) | **0.29 MB/s** |
| `UploadFile` (requests in flight together) | 5.71 MB/s |
| Stream reads (FileCat's downloads; SSH.NET reads ahead) | 4.71 MB/s |
| `DownloadFile` | 5.21 MB/s |

Downloads keep SSH.NET's read-ahead even though FileCat's content source sets the position before each read (it does
not move it): 4.98 MB/s against 4.81 for plain reads (`i39-sshnet-readahead-lat100.txt`
`1fa467d5412765bc62345c552010bc4a268af18def440411026044c092c2a566`). So only uploads needed changing.

A new upload now goes through `UploadFile` (still created exclusively), fed by a stream that counts what it reads, paces
it to the job's speed limit and lets a pause or a cancel take effect; a continued upload (after a break) still appends with
stream writes where the server's copy ends. The speed limit now counts from each attempt's own start (it counted the
bytes already on the server after a resume).

## After the fixes

The whole lab again, without latency and then at 100 ms, on a Release build of the remote code as `2ba114e` committed it
(built at 03:19 from the working tree, which `2ba114e` committed at 03:29 with no change to `src/FileCat.Remote` in
between; `FileCat.Remote.dll` `0f0af049524f5055690c64472bebdd675f61ce8a55e5adfbfde4d3d04b57e84c`, hash taken at the
time; the binary was rebuilt over and not kept):

| Run | Result | TRX | Console |
|---|---|---|---|
| No latency, SFTP/FTPS | 16 of 16, 92 s | `v08-base-i39-remote.trx` `ac838821f1822a0e7e691c0affdd6d3c401baec3e64ce5f208e2ae677d502cbf` | `v08-base-i39-remote.txt` `0f00b97a58407c089853191756b434feb7c1ea8768d5b629b9df2e06e491eb24` |
| No latency, SMB | 7 of 7, 33 s | `v08-base-i39-smb.trx` `61b7a2498cb2ed646a242b754ba147b7158080fabeeff325c016504a73bc1111` | `v08-base-i39-smb.txt` `c5e21c4213fc44406530c6ed012d3d385d4182d697b6fd9c1699b28035e5c1df` |
| 100 ms, SFTP/FTPS | **16 of 16**, 1,236 s | `v08-lat100-i39-remote.trx` `82fda70191f3daaa2475dad28cabaefe960adb65cc23624a945d2e4d54a0f283` | `v08-lat100-i39-remote.txt` `db6282beab38626030317d4a4c320f5d377239e367b2b40003e3cd6d28183006` |
| 100 ms, SMB | 7 of 7, 145 s | `v08-lat100-i39-smb.trx` `fe7f38d2baa33cee259d60e089e6fe3e6ef8f1eec18b873ffcb76fc921a1ed38` | `v08-lat100-i39-smb.txt` `8cc2afed2182c0ef196a4644e19743b65182ed4ffd685a772ef08e5052401418` |

Per case (TRX durations; the tree is 40 files of up to 70 KB, a 48 MB file and two folders, up and back):

| Case | No latency | 100 ms before | 100 ms after |
|---|---|---|---|
| Tree up and back, SFTP | 10.0 s | 6 min 4 s | 3 min 45 s |
| Tree up and back, explicit FTPS | 10.7 s | 6 min 18 s | 3 min 2 s |
| Tree up and back, implicit FTPS | 9.7 s | 6 min 15 s | 3 min 6 s |
| 256 MB upload cancelled at a fifth, SFTP | 5.6 s | 3 min 4 s | **36.9 s** |
| The same, explicit FTPS | 5.7 s | 17.5 s | 12.1 s |
| 128 MB upload cut off by the server and continued, SFTP | 19.6 s | 7 min 28 s, **failed** (time limit) | 6 min 59 s, passed |
| The same, explicit FTPS | 12.6 s | 41.9 s | 28.4 s |
| Odd names there and back, explicit FTPS | 5.0 s | 2 min 8 s | 55.5 s |
| Odd names there and back, SFTP | 5.9 s | 51.2 s | 49.4 s |
| The seven other cases (trust, consent, refusal, edge spaces) | 7.2 s | 43.8 s | 41.3 s |
| **SFTP/FTPS total** | 1 min 32 s | **33 min 52 s** | **20 min 36 s** |
| SMB total (Windows' own client; not changed) | 33 s | 2 min 19 s | 2 min 25 s |

What changed and what did not:

- A new SFTP upload is about 5× quicker over the slow link (the cancelled 256 MB upload reached its fifth in a sixth of the
  time); FTP stats no longer list folders (odd names: 2 min 8 s → 56 s).
- The trees still take three minutes at 100 ms against ten seconds without: the large file is about 20 s of it, so most is
  spent per small file — round trips, not throughput (investigated separately).
- The cut-off SFTP upload now passes, but its remainder (after the break) still went one request at a time: about 90 MB at
  0.3 MB/s.

## A cut-off upload on a slow link starts again when that is quicker (`1dce2c2`)

SSH.NET has no pipelined write at an offset, so continuing an SFTP upload after a break stays at one request per round
trip. After the break FileCat now compares the two ways, once the part on the server is checked: the new upload's
measured pace against one 32 KiB request per round trip (the resume check's own stat timed). It starts again only when
that is at least twice as quick, and says so in the job's issues; over a LAN, and over FTP (which appends at full speed),
it continues. Unit theory: `SftpJobTests.An_interrupted_upload_starts_again_only_where_that_is_quicker`.

The cut-off case alone at 100 ms on a Debug build of that change (`FileCat.Remote.dll`
`a9cde4ce0e7a170f617e1bde05c566e9237d276828fc22f6fb7f41d75aa088e2`, hash taken at the time, binary not kept; TRX `v08-lat100-anew.trx`
`18152ec192cfa626a1859292d8a4001281bbc8faa0f5816eea3183e7df7decb9`, console `v08-lat100-anew.txt`
`923302feae606852b929c69b3f812831449e51021e7d9eea9d4f5b2aff559cb9`): 2 of 2 passed.

| Case at 100 ms | Before (`2ba114e`) | After (`1dce2c2`) | What the job said |
|---|---|---|---|
| 128 MB SFTP upload cut off at 30% | 6 min 59 s | **3 min 31 s** | "The upload was interrupted and went again from the start: over this connection that is quicker than continuing where the server's copy ends." |
| The same over explicit FTPS | 28.4 s | 28.4 s | "The upload was interrupted and continued at 115 851 264 bytes, after the part already on the server was checked." |

The lab case accepts either way over SFTP (the link decides) and only continuing over FTP.

## Limitations

- One link shape (100 ms added one way, no loss, no bandwidth cap); one server implementation per protocol.
- These runs were made before I41 was found: FileCat's SFTP connections were still held to 137 KB socket buffers (1.2–1.7
  MB/s at 100 ms). E-I41 has the change and the whole lab after it.
- Over SFTP, an upload cut off when most of it is on the server still continues one request at a time (starting again
  would not be twice as quick); SSH.NET offers no pipelined write at an offset.
