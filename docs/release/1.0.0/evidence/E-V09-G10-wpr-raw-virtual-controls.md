# E-V09-G10 — WPR raw virtual-device visibility controls

Links: V09/REC-002/TV-09, I09, I106, ENV-07. **Preliminary raw virtual-device instrumentation controls PASS.**
The protected USB is not accessed by the control helper. G6's differing physical source hashes remain unresolved;
no FileCat, installed-broker, protected-source, GUI or candidate qualification is inferred.

Standalone preparation source: `c930c8fdd28f5947f46c12829fa6ec63d0284e6f`, Windows Insider 26220 host.
Private evidence root: `artifacts/release-evidence/v09-wpr-controls-20261003`.
Successful run: `raw-control-5e07baa122384519aa6616f25ed94730`, elevated controller PID 43864, helper PID 28624,
named instance `FileCatV09_e3d9a00f93a642a69fbee311eaa8957f`. Windows RunAs succeeds without further owner launch.
Preparation pins and Windows PowerShell 5.1 parsing pass. Private .NET 10 diagnostic builds have zero errors/warnings;
no production dependency is added. Native ETL decoding uses the pinned cached Microsoft TraceEvent 3.2.6 package.

The helper accepts only a new owned run directory, never a physical-device argument. It creates a fixed 64-MiB VHD
with no source or parent, verifies its entire data region is zero, and temporarily attaches that exact virtual
handle without a drive letter or permanent lifetime. Microsoft’s
[CreateVirtualDisk](https://learn.microsoft.com/en-us/windows/win32/api/virtdisk/nf-virtdisk-createvirtualdisk),
[AttachVirtualDisk](https://learn.microsoft.com/en-us/windows/win32/api/virtdisk/nf-virtdisk-attachvirtualdisk) and
[GetVirtualDiskPhysicalPath](https://learn.microsoft.com/en-us/windows/win32/api/virtdisk/nf-virtdisk-getvirtualdiskphysicalpath)
APIs supply the creation, lifetime and mapping semantics. Local Windows SDK 10.0.26100 headers are retained by hash.
Documentation does not supply test results.

Before raw access, the controller independently verifies exact image/device mapping, bus type 15 (file-backed
virtual), 67,108,864-byte capacity, zero partitions, RAW style, online/writable state, and non-boot/non-system
identity. The helper rechecks its virtual-handle mapping and queries length/device number on the opened raw handle.
This run maps to `\\.\PhysicalDrive6`; native trace alias is `\Device\Harddisk6\DR9`, not a guessed DR number.

The helper uses an aligned buffer with unbuffered/WriteThrough native I/O at byte offset 1,048,576. It reads
4,096 zero bytes, deliberately writes 4,096 bytes of 0xA5, flushes, reads them back, waits three seconds, and reads
again. Each call records process/thread, exact returned count, SHA-256 and UTC boundaries. Native FileIO and
DiskIO independently contain the same ordered **Read/Write/Read/Read**, correct PID 28624/thread 61084, exact
offset/size and device alias. Every selected native event falls within its corresponding recorded call boundaries.
The positive block hash is `f600eca824e84a43f0691b267bd620e462c50da165c5b80e17aecb7a924f1fa8`.

All disk-6 events are retained regardless of issuing PID. There are two additional System/PID-4 reads of 512 bytes
at offset zero during attachment; these are disclosed separately. The only raw disk write is the intentional
positive write. The adjacent negative block has no write. After successful detach, independent offline checking
verifies all 64 MiB: the single 4-KiB positive block and zero everywhere else. Data-region SHA-256 changes from
`3b6a07d0d404fab4e23b6d34bc6696a6a312dd92821332385e5af7c01c421351` to
`0419caa4b1698d81c5e92ee12829e13aff65441040fbb1e8b9bfb1aaf6da003e`; these intentional fixture changes are never
treated as protected-source writes. The extra 512-byte VHD footer is outside the data region and retained separately
in the full-file inventory.

The controller’s ordinary-file control also passes exact 4-KiB bytes, two typed FileIO write records for one stream
write, two reads and one corresponding physical disk write. Its last read follows native helper exit by at least
seven seconds. Child start/stop identify the controller parent; the untouched filename is absent from selected
filename/file/disk events and does not exist. The recorder spans 17:45:48.7890693–17:46:36.2977976 UTC on 2026-10-03,
including stop/rundown, with **2,057,617 native events and zero reported loss**. Both live reports have dropped-event
zero and both collector-loss counters zero. All recorded native phases exit zero. Independent checks find no
owned virtual attachment, named recording or recorded worker remaining; default recording stays inactive.

Two earlier attempts stop before raw I/O and remain preserved: `07e273d549b3476a8f3f3bfded6b556d` encounters
Get-Partition's empty-result error; `6a92fb1ebc0e4bdeabd090aabc08ef14` encounters the formatted BusType string.
The successor uses an empty-safe CIM partition query and the underlying numeric CIM bus property. Both aborted
fixtures independently remain entirely zero, without `raw-go`/raw-call records, and detach on helper termination.
Original runners/preparation pins are preserved. An independent verifier's initial dropped-event label mismatch
also remains recorded; correcting the label preserves all three required zero counters and the original captures.

Independent verification checks 44/45/65 files across the failed/failed/successful runs (154 total), 74 diagnostic
source/build files, pinned inputs, full fixture data, control calls/events, lifetime, native loss and cleanup.
No temporary ETL remains. Collection completion is kept separate from independent qualification.

| Private evidence | SHA-256 |
|---|---|
| Successful full ETL, 562,036,736 bytes | `4b861fa7e6f8c8b57329153d3f84aa6dd6b62cb7da9ea99ec8eedf24e9bef7bb` |
| Successful full VHD, 67,109,376 bytes | `61093f021a9db3e25f5ba7ed63cf5be058ffca43466d3c2eca6efb871b61d162` |
| Native selected-event inspection | `352abc563c0e3dcffbd006f7045680638b3a7c6c296d6f09f92911620639f79d` |
| Independent inventory/byte/event qualification | `28fe37505915941036024ecfafdf25efadf2bb1f7a988c277b97bbdf474293b5` |
| Independent successful cleanup | `a391c32c6b63161f8c065e5f2c583d5b9f596f8cf2d05bea171b48520f6615ed` |

Exact c930c8f CI [37140417717](https://github.com/benny-cz/FileCat/actions/runs/37140417717) passes all four test
lanes; three packaging jobs skip. Private run JSON SHA-256
`33929cc316ad397131dcf8d999bc8746acfaec9f2f92780fc358214c5d62c2e6`, full-log SHA-256
`818757c2a54213f69a01b88806e6e3ad00ed9ec2ad9e7a30e41c10b80a502b07`.
This CI result does not run these standalone diagnostic controls or qualify a release artifact.

Next: isolate the unexplained physical source changes under qualified tracing and strict ownership interlocks.
Further FileCat USB validation remains held. I09/I106 remain open, no candidate exists, recommendation NO-GO.
