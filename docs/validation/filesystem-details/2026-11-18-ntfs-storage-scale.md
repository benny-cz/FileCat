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

The native evidence class passed 18 tests with one skipped FAT/exFAT export fixture. The measurements come from one run in `.tmp/filecat-storage-quota-r1-tests.xml`. The fixture reached the quota through pending-review reservations. Full-size occupied journals, native Operations UI under load and cross-process changes during the census remain separate qualification cases.
