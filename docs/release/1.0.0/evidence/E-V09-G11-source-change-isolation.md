# E-V09-G11 — read-only physical source-change observation

V09/I09/ENV-07. **Image accounting verified; source-write trace qualification FAIL.**
FileCat USB validation remains held after G6's unexplained differing source hashes. This diagnostic never launches
FileCat, its broker or the recovery test; it requests no source writes, formatting, dismounting or device changes.

Standalone preparation source: `c92d31a80849ffb61e92b002a24818c5cf047a28`, elevated Windows Insider 26220 host.
Private root: `artifacts/release-evidence/v09-usb-change-isolation-20261003`; run
`observation-72373954dd034746a99588f031ab82c5`, controller PID 57712. Pinned scripts, PS5.1 parsing, incremental
hash and truncated-input refusal controls pass before execution. Windows RunAs validates the administrator token.

The global campaign mutex and existing fixed serial lease remain exclusive through both observations. Before and
after each read, the controller rechecks serial `2F2000129618`, USB disk 5, 7,796,162,560-byte capacity, exact PNP
identity, NTFS volume `bd052877-2d83-11f1-a457-18c04da6742d` and partition bounds. Windows, profile, application-data,
lease, evidence and temporary directories map to physical disks distinct from source 5; image destination E: is
disk 2. The raw handle is GENERIC_READ only, and its native storage descriptor independently confirms USB bus and
the authorized serial before any source bytes. No source lock, dismount or flush is requested.

Each complete sequential image contains **7,796,162,560 bytes in 1,859 exact returned chunks**. Helper PID 61040
reads before at 18:20:41.8892561–18:24:27.5749555 UTC; after a twenty-second interval, PID 6548 reads after at
18:24:50.1751433–18:28:33.9528048 UTC on 2026-10-03. Both retained images independently verify to
`ad248796c88d82197e8fbe36701f6e72f21406056830484552ace428dbaa3e54`, equal to G6's after hash.
These are sequential reads, not atomic disk snapshots. G6's differing before hash remains unexplained; no G6
before image exists, so historical changed blocks cannot be reconstructed or attributed.

The continuous named built-in DiskIO/FileIO recording spans 18:20:37.2805911–18:29:07.7940093 UTC, including save.
Native decoding retains **7,183,611 events and 51,216 lost events**. All three live reports show their three loss
counters at zero; the stop output explicitly reports 51,216 dropped events. Loss provenance is unknown. The trace
records all 3,718 controlled disk-5 reads with exact process/offset/size and no disk-5 writes, but **missing events
prohibit a zero-write or attribution claim**. File controls and helper process lifetimes verify separately.

Independent checking verifies 67 run files, input pins, both full images and every chunk hash, native read ordering,
precise phase boundaries and process lifetimes. Two verifier failures remain preserved: PS5.1 nested DateTime
serialization uses milliseconds, and native QPC-derived timestamps differ from wall-call checkpoints by up to
3.195 ms. The successor retains all offsets and uses native ordering/lifetimes rather than inventing submillisecond
cross-clock precision; exact byte counts and the failed loss criterion remain unchanged.

Independent elevated cleanup confirms recorded workers and the owned named recording absent, with both serial
leases available. A standard-token cleanup attempt's lease access refusal is retained. Lossless NTFS compression
then changes storage allocation of the two owned E: images only: combined logical 15,592,325,120 bytes occupy
15,244,222,464 reported stored bytes. Both logical byte counts and hashes are independently reverified afterward.
The compression process's exit code was not recorded; completion text, absence and resulting file state are retained.

| Private evidence | SHA-256 |
|---|---|
| Preparation pins | `97eca477dec166d16cf62d5d89759bd70b7ec9aac36e62ac70def616027e7425` |
| Full ETL, 1,512,046,592 bytes | `20296a3bd3fda15d4cd5a20ca51bca333d9366dd00aca26ada8edf6ca6020bd2` |
| Native selected-event inspection | `9718eb0a28a0200f67e5f8c781ce4711e9c1a00a919ae7ec9738065f98d2acda` |
| Independent accounting and failed trace disposition | `4347a8849207ae4404150c9ca973fe5167a21cef4d9586bfb041b7143b5fc4cf` |
| Independent cleanup | `e1aee61ac7359f785da9e8190afcafd43b58996f4956c8a1c14ed4725b36edfb` |
| Independent post-compression verification | `afc9177eb939a83e13351519d2fbf062563367107d8a87c7db709c397999e04b` |

Next: qualify a narrower recorder over the required duration before another source-change observation. Equal
images do not resolve G6 or release the FileCat USB hold. I09/I106 remain open; no candidate exists, NO-GO.
