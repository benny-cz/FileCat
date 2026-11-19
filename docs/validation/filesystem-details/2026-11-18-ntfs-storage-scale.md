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

## Native Windows Operations under load

On Windows 11 x64 build 26340, the [native Operations driver](../../../tests/FileCat.NativeAcceptance/win-ax-ntfs-storage-scale.ps1) used Windows UI Automation in an isolated service-session desktop at medium integrity (RID 8192) with compatibility rendering. The fixture used 10,000 ordinary 8,192-byte files on fixed 4 KiB NTFS. It opened the real compression review from the File menu, confirmed recursive scope, and paused the running job at 763 checked items. Operations displayed discovery counts without percentages or transfer speed history. A Create Folder request on the other panel showed the exclusive-storage wait reason and had no output while compression was paused.

The driver opened the real operation report, read call pages beginning at calls 1 and 65, and matched the durable SHA-256 shown on both pages. The processed count stayed unchanged while the job was paused. Resume finished the storage job with 10,001 changes, including the directory, and the unrelated queued folder was then created. All 10,000 files carried the compressed attribute. The first and last file hashes matched the fixture bytes.

| Measurement | Result |
| --- | ---: |
| Fixture creation | 12.16 s |
| Compression through completion, including automation and pause | 263.38 s |
| Observed paused interval | 100.03 s |
| UI Automation Pause to available Resume | 2.28 s |
| Open recorded calls to first available page | 2.62 s |
| Navigate to next available page | 2.52 s |
| Durable storage journal | 13,219,660 bytes |
| Main process handles, sampled initial to peak | 1,788 to 6,990 |
| Main process working set, sampled initial to peak | 383,696,896 to 591,126,528 bytes |

The dispatcher probe recorded four delays above 50 ms during the observed work intervals, with a maximum of 114.8 ms. The worst delay in the entire run was 1,858.9 ms and fell outside those intervals. The samples include the cost of UI Automation. Attribution of the additional process handles and measurement on the signed-in desktop remain open.

The initial attempt refused a shared-writable journal directory. A private disposable profile resolved that fixture issue. It also reproduced the shell taskbar API returning `E_NOTIMPL` in the isolated session. The updated app treated this optional API as unavailable and the completed run produced no crash log. The Windows component SHA-256 was `54E7A82C7FADA85BD0497808EC0C87987251D5A0A7D3356E923883ECF2C55571`. The fixtures and dispatcher receipts are `operations-scale.json` and `r2-operations-dispatcher.json` under `.tmp/filecat-storage-ui-scale-cdd562ad-r1/`.

### Idle handles by rendering configuration

A follow-up launched the same app twice at medium integrity using fresh profiles, a one-file folder and the same isolated Windows session. Neither launch ran a storage job or loaded a UI Automation client. Four Windows Process Snapshotting samples per launch tracked the total and the reported kernel object types between about 7 and 22 seconds after start. [Windows Process Snapshotting](https://learn.microsoft.com/en-us/windows/win32/api/processsnapshot/ns-processsnapshot-pss_handle_entry)

| Rendering configuration | Total process handles | Event handles | File handles |
| --- | ---: | ---: | ---: |
| Default FileCat configuration | 708–790 | 180–191 | 100–102 |
| `FILECAT_RENDERING=compat` | 2,534–5,895 | 1,928–5,358 | 100–102 |

The compatibility run had thousands of event handles at idle. A separate UI Automation comparison also showed increases during its idle intervals, including 3,499 to 8,195 before enumeration and 5,897 to 10,092 after client collection. These controls establish that the observed handle growth can occur without storage jobs or UI Automation enumeration. The default rendering configuration still needs measurement through the full workload and on the signed-in desktop. The receipts are `idle-handle-types.json` and `uia-handle-comparison.json` in `.tmp/filecat-storage-ui-scale-cdd562ad-r1/`.

### ARM64 storage jobs under load

The isolated GitHub Windows 11 Enterprise build 26200 runner executed the same 10,000-file benchmark under native ARM64 at medium integrity on fixed 4 KiB NTFS. The exact TRX contains one executed, passing test for commit `6a56cfd40dd9fb0bd93f4ced4f7f43f33c254c3c`. It used the framework-dependent `win-arm64` FileCat app as the Cloud Files worker, with executable SHA-256 `2F425D4012EC58C5979B85D0566E4E35481275663813DB7C0DC4CD7F051CD5AA`. The test assembly SHA-256 was `57E2BAC99FFF117CCB6F433A5B4DECD574CFEB381731F9FE6D58B881A153AAC1`. [Native ARM64 run and receipts](https://github.com/benny-cz/FileCat/actions/runs/44500467560)

| Measurement | Result |
| --- | ---: |
| Fixture creation | 3.55 s |
| Compression including pause and live review | 196.81 s |
| Live history and call page read at 101 items | 31.0 ms |
| First journal length | 13,350,510 bytes |
| Fresh pass over the completed prefix with one new file | 34.67 s |
| Fresh-pass journal length | 5,491 bytes |
| Process handles, sampled initial to peak | 290 to 380 |
| Working set, sampled initial to peak | 55,009,280 to 80,232,448 bytes |

The unrelated Create Directory job stayed queued while storage work was paused and completed after Resume. The first pass recorded 10,001 changes including the directory, and one flushed intent at which Pause won admission before a native call. Both boundary files preserved their bytes. The fresh pass found 10,001 already-satisfied objects and compressed the new file with one recorded call. This receipt covers the engine, scheduler and report reads. The full ARM64 Operations window still needs a separate native run.
