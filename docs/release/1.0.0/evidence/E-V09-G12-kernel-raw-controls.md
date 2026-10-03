# E-V09-G12 — custom kernel recorder raw controls

V09/I09/ENV-07. **Preliminary short raw virtual-device instrumentation controls PASS.**
No USB access or FileCat launch. G11's long trace loses events; G6's historical source difference remains unresolved.

Standalone preparation source: `c92d31a80849ffb61e92b002a24818c5cf047a28`, elevated Windows Insider 26220 host.
Private root: `artifacts/release-evidence/v09-wpr-controls-20261003`; run
`raw-control-0f43c9e852a245eeb52da091edc164e8`, controller PID 57816, helper PID 56904,
named instance `FileCatV09_1d46526e4f7743e38744e2203f51fe96`.

The additive pinned runner uses one custom system collector, 256 buffers of 256 KiB, and CpuConfig, ProcessThread,
Loader, DiskIO/DiskIOInit and FileIO/FileIOInit keywords. No user-mode event collector or stacks are configured.
Microsoft's [profile authoring documentation](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/authoring-recording-profiles)
describes configurable sessions, keywords and buffers; the local WPR profile-details command confirms the actual
accepted configuration. Documentation does not supply qualification evidence. Original G9–G11 inputs remain retained.

The unchanged G10 helper creates a new entirely zero 64-MiB fixed VHD with no source or parent. Exact image/device
mapping, virtual bus 15, RAW style, zero partitions, capacity and non-boot/non-system state are verified before access.
Native mapping is PhysicalDrive6 / `\Device\Harddisk6\DR10`, derived from this run. Aligned native calls at 1 MiB
read 4 KiB of zeros, deliberately write 4 KiB of 0xA5, read it back and read again after three seconds. Native
FileIO/DiskIO independently contain all four operations with exact process/thread/alias/offset/returned count and
call-boundary timestamps. Two System/PID-4 attachment reads of 512 bytes at zero are disclosed separately; the only
raw write is the intentional positive fixture write. Offline verification of all 64 MiB finds only that positive
block, with every other byte zero. The adjacent negative block is unchanged.

The ordinary parent-file control independently matches exact bytes, two typed FileIO write records for one stream
write, two reads and one disk write. Its late read is at least seven seconds after helper exit. The untouched filename
is absent. Native child start/stop match the controller parent. The trace spans
18:44:04.7958905–18:44:21.0864679 UTC on 2026-10-03: **609,387 events, zero reported loss**. Both live reports
show dropped-event zero and the sole system-collector loss zero. Recorded native phases exit zero.

Independent verification checks 61 run files and 74 diagnostic source/build files, pins, full fixture bytes,
control events, lifetimes, loss and cleanup. The image is detached, owned named recording stopped and recorded workers
absent. Initial verifier failures for an empty failure-run list and the display collector name remain preserved.
WPR uses its generated instance-specific system-collector name; the corrected assertion requires that exact sole
collector, exact buffers and both zero counters. It does not weaken loss or control requirements.

| Private evidence | SHA-256 |
|---|---|
| Custom WPR profile | `ed7f48610fccf2f5d2978c1ec6edbe55feb714a19681b68e2b1f50733258af44` |
| Preparation pins | `94a2a7110988932557c4f58057e542cf81bebd16f68946bb1ea53f1d35208b19` |
| Full ETL, 103,022,592 bytes | `b1e212247f5a2f3e2fb219aadc6224fa32275cc11d0f8637a7939dd031175740` |
| Full VHD, 67,109,376 bytes | `f4469a3362fb8e09bc10d6b99ea236afc5addb2908bc158854db92b2d12bd104` |
| Native selected-event inspection | `38d0892b9d09155aef3a49166a184cfade20139242dbbecbd4201ac82f8503bf` |
| Independent byte/event qualification | `012434dc3bb8ed4a2fc14aeb3ab8217538cd7908764898b99754bc5fcb03e986` |
| Independent cleanup | `09daadd994128ca80c6fcad454e49b6fc12a46a884a51798832f9cf871d81412` |

This short control does not establish long-duration loss behavior, physical-source zero writes, installed-broker or
candidate evidence. Continue a duration control before another source observation. FileCat USB validation remains held;
I09/I106 remain open, no candidate exists, NO-GO.
