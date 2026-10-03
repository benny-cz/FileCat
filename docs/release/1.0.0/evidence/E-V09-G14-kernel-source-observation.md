# E-V09-G14 — loss-free read-only source observation

V09/I09/ENV-07. **The read-only diagnostic observation passes.** Both complete physical images match; the
qualified native trace records all 3,718 source reads and no source disk or file writes. This run does not launch
FileCat, qualify a product path, or resolve G6's historical source difference. FileCat USB validation remains held.

Standalone preparation lineage is `6267331ee1e00027986822baa0e0b5bd5ce6c826`, with separately pinned diagnostic
inputs, on the elevated Windows Insider 26220 host. Private evidence root:
`C:\Users\marek\.codex\visualizations\2026\10\02\01a0fbbf-f37d-7042-9e13-028bfb0e5c33\FileCatReleaseEvidence\v09-kernel-source-observation-20261003`.
This authorized workspace stores the images/ETL on C: backing disk 3. The marked short trace scratch is
`E:\FileCat\artifacts\release-evidence\wpr-short-11d1ea31529241de9a6f1b8b5e8249db`, backing disk 2.
Both are distinct from the source USB disk 5. No candidate or release artifact exists.

The runner requires G13's independently verified nine-minute raw control and its exact profile hash before
source access. The custom profile is copied byte for byte: one kernel collector, 512 buffers of 256 KiB, seven
kernel keywords, SuppressHighVolume false, no stacks or user collector. An initial request fails before source
access because a text copy changed the profile's line endings/hash. Its script, preparation, mismatched profile
and exit evidence remain retained. A separately pinned successor uses the exact profile; no historical inputs
are overwritten or reclassified.

Successful run `observation-c171e715981f4f1fb63e6252035fa02a`, controller PID 39860, starts at 19:53:51.3602481
UTC and exits zero at 20:02:50.4987508 UTC on 2026-10-03. The global campaign mutex and serial-bound exclusive
lease cover the operation. Source identity is checked before/after each image and against each opened native
handle: serial **2F2000129618**, USB bus 7, disk 5, capacity **7,796,162,560 bytes**, G: NTFS, partition 1 at
1,048,576 with length 7,795,113,984, volume `bd052877-2d83-11f1-a457-18c04da6742d`.
Protected Windows/profile/appdata, lease, evidence and temporary locations have independently disjoint backing
disks. The snapshotter requests only GENERIC_READ; no source write, lock, dismount, flush, format or device flag
change is requested. Sequential images are not atomic snapshots.

Before helper PID 57524 reads 19:53:59.2418854–19:57:45.1650392 UTC; after helper PID 61944 reads
19:58:07.623339–20:01:52.7046272 UTC. Each image contains all 7,796,162,560 bytes through 1,859 exact chunks;
native short reads are refused. Both SHA-256 values are
`ad248796c88d82197e8fbe36701f6e72f21406056830484552ace428dbaa3e54`, matching G6's after image hash and G11.
G6's different before hash is still unexplained: its before image was not retained, so a historical byte delta
cannot be reconstructed from these newer images.

Named recording `FileCatV09_b373e73999dc4a70a618076ab5a2e6d9` spans 19:53:54.7888887–20:01:53.9826803 UTC:
**5,634,333 native events, zero reported loss**. All three live status reports also show both counters zero.
The native reader identifies exactly 1,859 DiskIO reads per helper on the derived source alias, with complete
offset/size and ordered call accounting, and **zero source DiskIO writes**. Independent supplementary FileIO
checking matches all 1,859 reads per helper and **zero source FileIO writes**. Ordinary-file early write/read,
late read, negative filename absence and controlled lifetimes pass. Precise native ordering and phase bounds
are authoritative; WinPS 5.1 nested dates have millisecond precision and cross-clock offsets remain disclosed.

Independent verification checks 65 run files, exact pins, both full images and per-chunk hashes, native counts,
lifetimes, positive visibility, loss and cleanup. The named watchdog has a twelve-minute deadline and a nominal
six-GiB metadata bound; no other recording is changed. An independent elevated cleanup check confirms all
recorded workers and the named recording absent, with campaign and fixed-serial leases available, without
accessing the USB. The independent result explicitly sets ProductQualification false and preserves the G6 hold.

| Retained private evidence | SHA-256 |
|---|---|
| Successful runner | `3da038f90bb4d2ddbf1feff41b0110b1f16bdbbe69f44ac1735be1bfbc2665e2` |
| Successful preparation pins | `8d6a5b3e7c45533be11466d8a5d790959fcbed3f6d4794879d9401d60e654597` |
| Exact G13 custom profile | `32db96404ccd1b36ba98e5d070471782120b7c7244592878781ee79822255e90` |
| Required independent duration proof | `3f4630ec454251b4d5da17d792e68b2d62a55e8cf8f36b726469085105957de0` |
| Snapshotter source | `32248495a864ef1cce24ce8893a0200ab8a9e38e76b332c655164401009cfda9` |
| Full ETL, 449,314,816 bytes | `3acbc063e2b3ec768368d754df97830b43365707cf6e36a7ffb95faf73b48bba` |
| Native selected-event inspection, 22,040,247 bytes | `b47f51aa07e0d09143eeb3ecbb92d28efcd56f8c4ae9d7d36f0e8fae3fffcbde` |
| Independent image/event qualification | `7083ea5ded866311adc4989032bfb9c4da44aa1f228a8beb703bdbde8b82196d` |
| Supplementary FileIO accounting | `9048dbaf8d2ccf500f095cb6ce3ce7e45dcaf0670fde3a129b25379a29811d60` |
| Independent cleanup | `fa48148b1d2f94162b9b702a72f4160aa6c907d126edfe65e717bb3e3b3c1b98` |

Exact documentation head b477783 CI [37149161239](https://github.com/benny-cz/FileCat/actions/runs/37149161239)
passes all four test lanes; three package jobs skip. Retained run JSON hash
`7e6394cef448fbe235ccfdd53b12d400732bbcf612abcd8bff97113b3914726b`, full-log hash
`4cd277d120d6e35968f8cca0397bcbc6e73d4a745cd47b330d0cc281bf7c0b68`.
CI does not execute these standalone diagnostics. I09/I106 and historical source-change attribution remain open;
recommendation **NO-GO**.
