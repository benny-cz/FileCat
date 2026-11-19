# Held NTFS compression at 10,000 files

The Windows 11 build 26340 x64 VM ran the real review, scheduler, held NTFS compression and durable history path as its ordinary user. The fixture used 10,000 compressible files of 8,192 bytes each on the VM's fixed NTFS volume.

| Measurement | Result |
| --- | ---: |
| Fixture creation | 8.06 s |
| Recursive compression, including Pause | 181.50 s |
| Read live history and call page after 101 changes while paused | 43.9 ms |
| First job journal for 10,001 changes, including the directory | 13,289,674 bytes |
| Fresh pass over 10,001 already-satisfied objects and one pending file | 34.10 s |
| Fresh pass journal for one mutation | 5,317 bytes |
| Process handles, initial to sampled peak | 265 to 110,783 |
| Working set, initial to sampled peak | 79,126,528 to 107,229,184 bytes |

The live prefix remained readable while an unrelated file job waited for the storage writer. Both jobs retired after Resume. The journal had an intact terminal record, and the first and last files retained their bytes. The benchmark accepts and verifies a durable no-call record if Pause intervenes after intent and before the Windows call, which occurred on the first run. The fresh pass made one native call for the file that needed work. The test is opt-in via `FILECAT_TEST_STORAGE_SCALE=1`. Results are in `.tmp/filecat-storage-scale-qualified-tests.xml`.

A follow-up on one ordinary held file measured 264 to 569 process handles across 300 calls to `CfGetSyncRootInfoByHandle`. The same number of volume-name, external-backing, compression, flush and process-sampling calls produced no handle growth. The path-based Cloud Files call and the handle call with a Cloud Files open both showed the same unbounded growth. Managed garbage collection did not reclaim the handles.

FileCat now duplicates only the held object's read-attributes access to a worker running the same executable. The worker runs the same Windows Cloud Files query and retires after 256 requests. A failed or timed-out response leaves the item's cloud membership unknown. No path lookup or cached membership substitutes for the held-object query.

The benchmark passed again using the built FileCat executable as its worker. Parent process handles peaked at 372 from a baseline of 265. The initial operation took 183.49 s, the paused history and calls took 57.7 ms, and the fresh pass took 52.43 s. The two journals were 13,289,674 and 5,317 bytes. The working set ranged from 79,355,904 to 117,284,864 bytes. Results are in `.tmp/filecat-storage-scale-worker-tests.xml`. The prototype separately ran 5,120 repeated queries in 4.10 s with a peak sampled child count of 444 and ending parent count of 192 from 181.

## Private history at the process quota

The same VM ran the native evidence tests at medium integrity (RID 8192). The opt-in test seeded 128 valid pending intent journals using the Windows private evidence backend. They occupied 120,169 bytes and reserved 268,435,456 bytes for review. The bounded startup query returned 64 descriptors and a continuation. The independent census counted all 128 journals. The actual reservation logic refused a new job and preserved all 128 pending journals. Exact review and call reading still returned the pending intent with uncertain effect.

| Measurement | Result |
| --- | ---: |
| First bounded history page | 379.9 ms |
| Full accounting census | 3,218.9 ms |
| Managed bytes allocated on the census thread | 32,909,536 bytes |
| Admission and retention checks ending in quota refusal | 5,949.1 ms |
| Exact review and call page at quota | 18.5 ms |

The native evidence class passed 18 tests with one skipped FAT/exFAT export fixture. The measurements come from one run in `.tmp/filecat-storage-quota-r1-tests.xml`. The fixture reached the quota through pending-review reservations.

## Near-limit occupied journals

On 2026-11-19 the Windows 11 x64 VM (build 26340, RID 8192, 4 KiB NTFS) seeded four valid compression histories using the private journal writer. Their completed records represented attempts that made no native call. Each history ended with one pending intent and was flushed before opening it for review. The four ordinary files occupied 255,946,304 bytes and required another 8,388,608 bytes of review reservations. Startup discovery read 2,120 header bytes for all four rows.

Admission refused a new reservation and preserved all four histories. Exact review returned the two tail calls, including the unresolved intent. A bound export matched the complete prefix length and SHA-256. Acknowledging one exact journal allowed byte-pressure pruning to retire that journal and admit a new reservation. The other three journals and the verified acknowledgment/report pair stayed present. Native Operations interaction under load and changes during a second process's census are separate qualification cases.

The reader now scans for record endings within each read buffer and parses the framed record from reusable memory. It disposes the JSON document before reusing the bytes. Framing limits, CRC and prefix digest verification still apply to every read. [System.Text.Json buffer lifetime](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsondocument.parse?view=net-10.0)

| Measurement on the same VM | Baseline | Updated reader |
| --- | ---: | ---: |
| Full census | 3.47 s | 2.46 s |
| Managed bytes allocated during census | 1,110,931,160 | 854,436,904 |
| Admission ending in quota refusal | 10.02 s | 7.08 s |
| Exact review and final call page | 1.55 s | 1.08 s |
| Bound export of the verified prefix | 1.14 s | 1.02 s |
| Acknowledgment, measured separately | 2.54 s | 1.78 s |
| Pruning and successful admission retry | 16.43 s | 12.14 s |

The updated Windows evidence selection passed 19 tests with one FAT/exFAT fixture skip. Five EFS job tests passed, with eight prepared-fixture skips. Both Windows selections ran on .NET 10.0.15 from base `3aab13f2` plus the reader and fixture changes. The updated test bundle SHA-256 is `3115BE1346DBF496C154D1D83B79364DA974306EE0BA2FEE6D1A075938820A02`. Ubuntu passed 103 portable history, framing, retention and EFS evidence tests. Build-host receipts are `.tmp/filecat-occupied-evidence-3aab13f2-r1.xml`, `.tmp/filecat-occupied-evidence-3aab13f2-r2.xml`, `.tmp/filecat-occupied-evidence-3aab13f2-r2-efs.xml` and `.tmp/filecat-occupied-evidence-3aab13f2-r2-portable.trx`.
