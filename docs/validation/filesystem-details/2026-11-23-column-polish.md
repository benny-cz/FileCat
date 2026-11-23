# Column polish from the audit — 23 November 2026

The column audit of 22 November ([column meanings](2026-11-22-column-meanings.md)) listed a few smaller problems besides the ones fixed on their own. This receipt covers them.

| Audit finding | Now |
|---|---|
| **Allocated** showed exact bytes (`N0 B`) in a 110-pixel column, so a value of about 100 GB and up was cut to its leading digits, hiding its magnitude. | Allocated reads as Size does: exact bytes below a million, then three figures and a unit (KB to EB). Its tooltip starts with the exact bytes, and it sorts by them. One formatter serves both columns. |
| **Origin** values had no tooltip, so "Marked" and "Zone 5" did not say what they meant. | Pointing at a value says what the mark rests on. On Windows: the zone number and its meaning, and the download and referring addresses the `Zone.Identifier` stream records (`HostUrl`, `ReferrerUrl`). "Marked" says whether the stream names no zone or is too large to read; an unknown zone says it is not one of Windows' zones 0 to 4. On Linux: the addresses in `user.xdg.origin.url` and `user.xdg.referrer.url`. On macOS: who set `com.apple.quarantine`, and when. An address is cut to 400 characters, and each line is escaped. |
| The extra-data view called an NTFS extended attribute "NTFS attribute", while its summary says "extended attributes". | Its Kind says "Extended attribute", as the Linux and macOS views now do too. |
| **Reparse type**'s default width of 110 pixels cut off "Junction or volume mount". | It is added at 170 pixels. |
| Flat view, comparison and checksum-problem lists left Folder blank for items at the top of the list. | Those items show ".", the folder the list was made from, as the other items' relative folders read. |
| The deleted-items list showed live folders with a blank State and Evidence. | Fixed in `d11b53cab`: they say "Not deleted" and how many deleted items they hold. |

## Origin on FAT32 and exFAT

The audit could not determine what Origin shows on a volume without alternate data streams; it depended on the error Windows returns there. On the Windows 11 VMware guest, as administrator, a probe made a 64 MB FAT32 and a 64 MB exFAT virtual disk and opened a file's `Zone.Identifier` stream on each with `CreateFileW`, as .NET does:

| Volume | Opening `a.txt:Zone.Identifier` |
|---|---|
| FAT32 | error 123, ERROR_INVALID_NAME |
| exFAT | error 123, ERROR_INVALID_NAME |
| NTFS, a file without the stream | error 2, ERROR_FILE_NOT_FOUND |

.NET raises error 123 as a plain `IOException`, and Origin caught only `FileNotFoundException`, so every file on a FAT32 or exFAT stick showed **error**. Origin now treats that refusal as no mark, and the cell is blank, as for an unmarked file on NTFS. The probe detached and deleted both disks. SMB shares without stream support were not tested.

## Tests

- `FileDetailsFieldsTests.Allocated_reads_as_sizes_do_with_the_exact_bytes_in_its_tooltip` (Core): 4,096 bytes and 123,456,789,012 bytes.
- `MetadataTests` (Core): on Windows, a zone stream with download and referring addresses, an unknown zone and a stream that names no zone; on Linux and macOS, `Unix_origin_column_says_where_the_mark_came_from`.
- `FatDriveRecordTests.A_file_on_FAT32_has_no_download_mark_rather_than_an_error` (Windows): runs in CI on the FAT32 virtual disk the Windows job already makes.
- The existing Origin, extra-data, Find window, comparison, checksum and recovery tests, with their expectations updated for the new texts.

## Test runs

| Platform | Core | App |
|---|---|---|
| Windows x64, medium integrity | 39 run (metadata, details fields, extra data, link target), 32 passed, 7 skipped | 126 run (refresh, details columns, extra data, header help, formatters, Find window, comparison, checksum, recovery and name classes), 124 passed, 2 skipped |
| Ubuntu 24.04.5 | 64 run, 53 passed, 11 skipped | 61 run, 58 passed, 3 skipped |
| macOS 27.2.1 | 64 run, 53 passed, 11 skipped | 61 run, 59 passed, 2 skipped |
