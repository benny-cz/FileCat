# E-I41 — SFTP held to SSH.NET's socket buffers (V08 latency)

Release issue I41. Preliminary automated evidence on development builds: the lab of E-V08-L1 (host → lent Ubuntu VM,
OpenSSH 8.9p1) with 100 ms added to every packet the server sends (`tc … netem delay 100ms`), as in E-I38-I39. Raw
outputs under `artifacts/release-evidence/` (SHA-256 below). The probes were scratch tests, not committed; the
throughput probe's source is kept (`i41-throughput-probe.cs.txt`
`afcfff43c9dec85b5bd7f683b0539b531bf258c497d2086d6edfaac1ad17bd42`).

## Discovery

After I39, SFTP trees still took 3–4 minutes at 100 ms. A trace of every call FileCat's channel made while a job took a
tree of five 1 KB files and a 32 MB file up, back and away (`i41-roundtrip-trace-lat100.txt`
`8abb0e8acb7ed5533b705976d0951c8ef493a3877dd4eca88e76651cdd9459ef`, build `1dce2c2`) showed the 32 MB file going up in
18.4 s (1.8 MB/s) and down in 25.7 s (1.3 MB/s) — where SSH.NET alone had measured 5.7 and 4.7 (E-I38-I39). FTPS on the
same link moved it in 3.3 s and 2.3 s.

## Cause

The same 32 MB, back to back on the same link (`i41-throughput-before-lat100.txt`
`deddf0d5bc4da9905f3d782a1d410507332f645056320ddf7cd37caacb7c437d`), MB/s:

| Path | Up | Down |
|---|---|---|
| SSH.NET alone (`Connect`) | 5.29 | 5.27 (stream read), 5.53 (`DownloadFile`) |
| FileCat's channel | 1.73 | 1.24 |
| FileCat's jobs | 1.62 | 1.23 |

Clients made four ways, 16 MB each (`i41-client-variants-lat100.txt`
`6e56fab5b98f24518259643779ec53fedf57dea5f0d8ee67ad7334db8af02da7`):

| Client | Read | Upload | Socket buffers after connecting |
|---|---|---|---|
| Default constructor, `Connect` | 4.40 | 4.77 | 685,360 |
| Default constructor, `ConnectAsync` | 1.21 | 1.67 | 137,072 |
| Made as FileCat makes it (connection info, keep-alive, timeouts), `Connect` | 4.46 | 4.52 | 685,360 |
| Made as FileCat makes it, `ConnectAsync` | 1.21 | 1.67 | 137,072 |

So FileCat's settings were not the cause; `ConnectAsync`, which FileCat uses so that connecting can be cancelled, was.
SSH.NET sets both socket buffers once connected — 2 × its 68,536-byte largest packet after `ConnectAsync`, 10 × after
`Connect` — and a buffer set explicitly turns off the system's receive-window tuning: one buffer per round trip is
1.37 MB/s at 100 ms.

The buffers raised after connecting, 32 MB each way (`i41-buffer-variants-lat100.txt`
`12745c718859c6b719b5560c71e35da7968750e8b41e09d363e3bd6030872bf1`):

| Connection | Stream read | `DownloadFile` | `UploadFile` |
|---|---|---|---|
| `ConnectAsync`, as SSH.NET left it (137,072) | 1.24 | 1.25 | 1.74 |
| `ConnectAsync`, raised to 4 MiB | **12.52** | **14.32** | **8.42** |
| `ConnectAsync`, raised to 16 MiB | 1.24 | 1.25 | 9.15 |
| `Connect`, as SSH.NET left it (685,360) | 5.15 | 5.57 | 5.64 |
| `Connect`, raised to 4 MiB | 12.66 | 15.80 | 9.86 |

16 MiB did not help reads; that was not investigated further (16 MiB is just past the largest window a scale factor of
8 can advertise, which may be why). 4 MiB is the size chosen.

## Fix (`4c6b910`)

After connecting, FileCat raises both buffers to 4 MiB. SSH.NET offers no setting for its socket, so FileCat reaches it
through SSH.NET's private session field; where that fails, the connection works as SSH.NET made it. Tests:

- `OpenSshIntegrationTests.SSH_NET_keeps_its_socket_where_FileCat_widens_it` — every platform, no server: fails if an
  SSH.NET upgrade moves the socket, instead of losing the speed silently.
- `OpenSshIntegrationTests.A_connection_is_not_held_to_the_small_socket_buffers_SSH_NET_sets` — CI's Linux and macOS
  lanes against a local sshd. Linux caps what may be set at `net.core.rmem_max` (212,992 by default) and reports twice
  what it keeps, so the test asks for more than SSH.NET's 274,144 there and for 4 MiB elsewhere.
- `RemoteLabTests.An_sftp_connection_gets_socket_buffers_for_long_links` — Windows host → the lab: 4,194,304.

After, the same 32 MB at 100 ms (`i41-throughput-after-lat100.txt`
`7a6d5b54855694f21d844ec7905daf8875a8b6de438b7241b7b4a9e35c9182d1`, build `4c6b910` in Release):

| Path | Up before → after | Down before → after |
|---|---|---|
| FileCat's channel | 1.73 → **7.82** | 1.24 → **12.91** |
| FileCat's jobs | 1.62 → **8.17** | 1.23 → **11.58** |
| SSH.NET alone (`Connect`, for reference) | 5.27 | 5.26 |

## The whole lab after

The lab of E-V08-L1 (now 19 SFTP/FTPS cases: the link and socket-buffer cases were added) and E-V08-S1, without latency
and at 100 ms, on a Release build of `4c6b910` (`FileCat.Remote.dll`, kept as `build-4c6b910-release-FileCat.Remote.dll`
`3b779c29752c909c663488c84e6527f8b739e82e0a47dc30f8fae90aaedc3198`):

| Run | Result | TRX | Console |
|---|---|---|---|
| No latency, SFTP/FTPS | 19 of 19, 112 s | `v08-base-i41-remote.trx` `63ddb1eca94642bd10e5ba68f13b922ebb12ebbccd7bef66e446fffe1e981e6c` | `v08-base-i41-remote.txt` `8ccc19271ea248b38b91479e25f938457b49e7389d2f802771c901d31abe5030` |
| No latency, SMB | 7 of 7, 39 s | `v08-base-i41-smb.trx` `565f4722a00b9dc050d864719e4bee1c7108c1f48ff748b6661511230d21279c` | `v08-base-i41-smb.txt` `99ec9f3c6d19ad23ce9cd9a79a51499612f03ff50c1108c6654e207d937e0701` |
| 100 ms, SFTP/FTPS | **19 of 19**, 788 s | `v08-lat100-i41-remote.trx` `0dbc9a184c56c6f10fa28cebf7373486a4d5c4fb2f1c7810968ff6a60b66283f` | `v08-lat100-i41-remote.txt` `d92f6fdfde0f539dafbaca86b87119ccf820b51077b26287db08c84af2b53dbc` |
| 100 ms, SMB | 7 of 7, 144 s | `v08-lat100-i41-smb.trx` `855b23055679af3a35aa8f8ecc0b38df6269c215705644c0329d2f53726033b5` | `v08-lat100-i41-smb.txt` `ef00dde0a2114f3560809b75256366355a4bbb957dcf4d36635eb9c6fd375f08` |

The SFTP cases at 100 ms across the three builds (TRX durations):

| Case at 100 ms | `02acee6` | `2ba114e` (I38, I39) | `4c6b910` (I41) |
|---|---|---|---|
| 128 MB upload cut off by the server and continued | 7 min 28 s, failed (time limit) | 6 min 59 s | **41.0 s** |
| 256 MB upload cancelled at a fifth | 3 min 4 s | 36.9 s | **12.6 s** |
| Tree up and back | 6 min 4 s | 3 min 45 s | **2 min 15 s** |
| Odd names there and back | 51.2 s | 49.4 s | 51.4 s |
| **All SFTP/FTPS cases** | 33 min 52 s (16 cases) | 20 min 36 s (16) | **13 min 8 s** (19) |

The 41 s of the cut-off case is the restart choice of `1dce2c2` on a fast connection: the new upload runs at 8 MB/s, so
starting again wins. The trees and the odd names are now round trips per file (below); FTPS and SMB are unchanged.

## What one file costs: round trips (I42, open)

From the same trace, each small file at 100 ms (one request answered per round trip; times rounded):

| Step | SFTP | Explicit FTPS (vsftpd) |
|---|---|---|
| Upload: write the temporary copy | 0.42 s | 0.85 s |
| Upload: set its modified time | 0.42 s | — (not done; see I43) |
| Upload: check its size | 0.21 s | 0.21 s |
| Upload: take the name | 0.32 s | 0.42 s |
| **Upload, per file** | **1.4 s** | **1.5 s** |
| Download: check the item | 0.21 s | 0.21 s |
| Download: open, read, close | 0.55 s | 0.64–0.74 s |
| **Download, per file** | **0.76 s** | **0.85–0.95 s** |
| Listing a folder | 0.52 s | 0.73–0.90 s |
| Deleting an item | 0.10 s | 0.10 s |

The SFTP timings fit two round trips per path-based request — SSH.NET has the server resolve the path (`REALPATH`)
before acting on it, as FileCat's channel notes — and setting a time is two such requests (read the attributes, write
them back): about 14 round trips per uploaded file. Files go one at a time over one connection, so a folder of a thousand
small files 100 ms away takes about 23 minutes up. Fewer requests per
file, or several files in flight over the connection pool's four connections, would cut that; neither is in 1.0.0.

## Limitations

- One link shape (100 ms added one way, no loss, no bandwidth cap); one SFTP server implementation.
- On Linux FileCat cannot raise the buffers past `net.core.rmem_max`/`wmem_max`, and SSH.NET's own setting has already
  turned off Linux's window tuning; Linux clients gain less (not measured).
- FileCat relies on a private SSH.NET field; the guard test turns an upgrade that moves it into a test failure.
