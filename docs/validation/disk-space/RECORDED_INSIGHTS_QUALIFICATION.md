# Recorded metadata insights

Candidate review offers folder small-file concentrations, logical and allocated size differences, and unmeasured or unreadable locations. Each preview uses explicitly selected captured locations, names the revision and rule, and offers inspection of the recorded path.

- Folders qualify with at least 32 direct file paths whose recorded lengths are at most 64 KiB. Ranking uses the path count. The known allocation portion uses verified single-link files with compatible recorded bases.
- File differences use the absolute gap between primary logical length and the recorded file allocation. Each row shows the amounts, the allocation basis and the direction of the difference.
- Issue results use selected location bindings. Root-only issues remain attached to their recorded roots. Unlocated issues and incomplete issue sampling are disclosed independently of the scoped count.
- Large extra data displays an unavailable measurement for stream and fork sizes. These captures have ordinary primary-allocation observations.

The r1379 suite passed 20 cases per platform, including the new insights in memory and indexed views. Native-filesystem fixtures include a real hard link, a nested small file, a partial observation, and a file with a known allocation difference. The issue case injects a failed observation into the fixture scan and verifies preservation of its real file bytes. Separate cases exercise reported allocation components, unknown sizes, and incomplete issue sampling.

The r1380 native windows on Windows, macOS, and Ubuntu showed a folder with 500 directly observed small files, an allocation-difference ranking, and an unavailable extra-data measurement. Marks, the basket, and fixture contents were preserved. The native drivers reused the same product binaries from the rendering suite. All task-owned FileCat processes exited.

## Largest folders

The preview uses the captured subtree totals for the active measure and lists up to 128 folders in the selected locations. Logical size, verified unique size, reported allocation, and file count use the same accounting as the hierarchy. Ancestor and descendant rows overlap. The preview reports unmeasured folders separately from measured zero and identifies ranked folders with partially measured contents. Incompatible captured allocation bases disable allocation ranking for this preview.

The r1382 suite passed all 20 cases on Windows, macOS, and Ubuntu. Native-filesystem fixtures exercise memory and indexed views. They check the change in ranking from logical size to file count, the distinction between a captured empty folder and an unmeasured folder, partial evidence, the recorded inspection path, and the preserving default.

The native workflow uses the same FileCat binaries. It ranks a 500-file folder while the indexed table has a one-file filter applied, verifies its recorded path, and preserves the existing marks, basket, and fixture. The first Mac capture run requested the previous preview frame. The corrected r1384 harness passed on macOS and Ubuntu and captured the Largest folders window on the Mac. The Windows guard deferred r1384 when another process appeared. The byte-identical retry r1386 passed once the VM was idle.

The exact source and result packet is recorded in [the r1385 manifest](source-manifest-r1385-largest-folders.json).

## Detailed NTFS named-stream allocation

Detailed allocation on local NTFS can record one extra-allocation total per verified file object. The analysis queries the already opened metadata handle and validates the returned default stream against primary length and allocation. It sums the reported allocation fields for complete named data stream records. The capture retains the amount, method, basis, time and state. Stream names and contents stay outside the capture. [Microsoft FileStreamInformation layout](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/f8762be6-3ab9-411e-a7d6-5cc68f70c78d)

The query uses a 64 KiB native buffer and accepts at most 256 named streams. Incomplete, malformed, unsupported or changed evidence remains unavailable. Measured absence is recorded as zero. Primary logical bytes stay in File size. The On disk total charges the primary and additional allocation separately and charges hard-linked objects once. The preview ranks positive captured extra allocation by path and warns that multiple names can represent the same object.

The r1394 suite exercises memory and indexed captures, multiple real NTFS named streams, a real hard-link alias, measured zero, primary-size preservation and unavailable results on macOS and Ubuntu. The r1395 native workflow shows 76 KiB of reported named allocation for an archive on Windows and the unavailable preview on Ubuntu. Named-stream bytes and primary fixture bytes are preserved. The initial Mac capture stalled waiting for the capture acknowledgement. The r1397 Mac retry uses the same FileCat build with an atomic request and acknowledgement in the harness. [Exact evidence](source-manifest-r1396-ntfs-streams.json)

## Detailed APFS resource-fork allocation

Detailed APFS scans query the held file descriptor with `fgetattrlist`. The response must identify the requested attributes and report consistent total, data-fork and resource-fork allocation. The fork value sits under the existing total-file allocation in the stored component graph. A missing field, inconsistent total, unreadable metadata, or compressed file leaves separate fork allocation unavailable. The scan preserves the standard reported total. [Apple attribute definitions](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/getattrlist.2.html)

The Mac metadata probe reported zero allocation for a file without a resource fork, 4,096 allocated bytes for a 48-byte fork, and 81,920 allocated bytes for a 77,829-byte fork. In each sample, the total-file allocation already included the resource fork.

The r1398 suite passed all 54 cases per platform. The native filesystem tests cover memory and indexed captures, hard-link accounting, measured zero, retained bytes, and agreement between the allocation total and its components. Additional parser checks cover missing fields, negative values, truncated responses and inconsistent totals. The r1399 native windows showed reported resource-fork allocation on macOS, named-stream allocation on Windows, and the unavailable state on Ubuntu. All owned processes exited. [Exact three-platform evidence](source-manifest-r1400-apfs-forks.json)

Extended attribute allocation, stream-specific actions, physical input, and assistive technology qualification remain open.
