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

A follow-up on one ordinary held file measured 264 to 569 process handles across 300 calls to `CfGetSyncRootInfoByHandle`. The same number of volume-name, external-backing, compression, flush and process-sampling calls produced no handle growth. The cloud membership query needs a bounded implementation that preserves the current refusal policy before the scale gate can pass. Native Operations UI, quota and startup census measurements remain open.
