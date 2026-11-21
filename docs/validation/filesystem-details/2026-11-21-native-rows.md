# Native platform rows — 21 November 2026

This receipt maps the N-WIN, N-LINUX and N-MAC expectations in the ledger to native evidence. It adds one Windows test, `WindowsDetailsLongNameTests`.

## New test

`WindowsDetailsLongNameTests.Details_describe_a_255_unit_name_beyond_MAX_PATH` reads a file with a 255-unit Unicode name at the end of a path longer than 400 characters. The summary and the metadata-only full report describe the same object, with its exact length and identity and no payload. It passed on the Windows 11 x64 VM ([TRX](receipts/2026-11-21-native-rows/long-name-vm.trx)).

## N-WIN

| Expectation | Evidence |
|---|---|
| NTFS, ReFS, FAT32 and exFAT files | [volume matrix](2026-11-18-ntfs-compression-volume-matrix.md) (NTFS, FAT32, exFAT identity and allocation), [ReFS summary and full report](2026-11-18-storage-capacity-refs-smb.md), [native listing parity](2026-11-21-windows-native-listing-x64.md#parity-on-the-windows-11-x64-vm) on all four |
| Symlinks, junctions and mounts | the 11 native target cases and `WindowsDetailsNativeGapTests` ([Windows native details receipt](2026-11-21-details-windows-native.md#fixtures)); `FileDetailsVolumeTargetTests` |
| Streams, EAs and WOF | `WindowsHiddenDataTests`, the [transparent WOF backing fixture](2026-11-17-supplementary.md#native-finding-transparent-wof-backing), and the [omission sentinels](2026-11-21-export-sentinels.md#sentinel-fixtures) |
| Long names | the new test above; 255-unit names in the listing parity |
| Unavailable tags | WSL, app-alias and unknown tags in the native target cases |
| Allocation meanings | `WindowsDetailsNativeGapTests` (cluster rounding, sparse allocation, folders), the [bounded native collectors](2026-11-18-ntfs-allocated-range-details.md) |
| API, OS, filesystem, architecture and actual refusals recorded | each receipt's environment and refusal records |

**Windows on Arm:** CI's hosted Windows ARM64 jobs run every Windows test that needs no prepared volume, including the sentinel, long-name, column and target fixtures. The [native ARM64 details fixture](2026-11-18-windows-enumeration-scale.md#cancellation-at-a-published-batch) adds held hard-link names, sparse and ordinary extents, allocated ranges and value refusals on 4 KiB NTFS. **Open on ARM64:** prepared FAT32, exFAT and ReFS volumes.

## N-LINUX

| Expectation | Evidence |
|---|---|
| statx masks, no-follow and special nodes, mount provenance | `Statx_masks_allocation_units_kinds_and_attribute_support_are_independent`, `Linux_summary_of_FIFO_does_not_open_it_for_data_or_wait_for_a_writer`, `Unix_details_describe_a_character_device_from_metadata_without_opening_it`, `Linux_gets_the_mount_type_of_the_held_item_through_a_parent_symlink` |
| Directory link count and allocation | `UnixFileDetailsColumnsNativeTests` (the folder's own allocation against `stat`, no Hard links value for folders) and the summary tests |
| F5 xattr views and names | `UnixHiddenInventoryNativeTests.Linux_bound_inventory_and_values_use_the_inode_and_refuse_special_objects`, `HiddenDataTests.Linux_inventory_uses_the_qualified_kernel_route_and_preserves_permission_outcomes` |
| FIEMAP sharing and placement | `Linux_FIEMAP_reports_Btrfs_reflink_sharing_and_omits_the_encoded_physical_locations`, `Linux_XFS_reports_reflink_sharing_sparse_ranges_and_unwritten_extents` |
| ext2/3/4 c, s and u wording; permission, set-ID and capability limits | `Linux_details_label_historical_ext_flags_without_claiming_compression`, `Unix_details_explain_recorded_mode_and_execution_limits_without_inferring_access`, `Linux_ext4_full_details_report_only_the_held_files_recorded_capabilities` |
| Btrfs, XFS, ZFS and fscrypt probes | the prepared-adapter tests (`Linux_full_report_reads_Btrfs_policy_and_subvolume_from_the_held_object`, the ZFS accounting and mount tests, the fscrypt policy tests) |

**Evidence:** Ubuntu 24.04.5 x64 (ext4), plus the prepared Btrfs, XFS, ZFS and fscrypt volumes in their receipts. **Open:** the `linux-arm64` package needs results on ARM64 Linux, and no ARM64 Linux machine was available.

## N-MAC

| Expectation | Evidence |
|---|---|
| Case variants, returned masks and slots, no-follow, minimal summary requests | `Mac_full_report_describes_APFS_case_and_normalization_on_the_observed_volume`, `Mac_full_report_distinguishes_HFS_Plus_and_HFSX_name_behavior`, the Mac packed-slot tests, `Mac_summary_of_a_large_folder_is_a_minimal_request_without_an_entry_count` |
| F5 resource-fork-only and decmpfs inventories, dataless variants, managed roles | `HiddenDataTests.A_Darwin_resource_fork_without_ordinary_attributes_has_its_own_inventory_category`, `HiddenDataTests.A_real_Darwin_compressed_file_discloses_that_the_ordinary_view_omits_backing`, `Live_Mac_File_Provider_metadata_keeps_prepared_files_dataless`, `Live_Mac_File_Provider_directory_metadata_keeps_the_prepared_folder_bound`, `HiddenInventoryTests.Managed_roles_and_download_mark_exceptions_use_the_typed_category` |
| APFS sharing and private fields keep their scope | `Mac_full_details_distinguish_APFS_shared_private_and_partly_shared_files` |
| FileVault | `Mac_full_report_distinguishes_FileVault_and_encrypted_backing_on_the_selected_volume` and the APFS protection tests |

**Evidence:** macOS 27.2.1 on Apple silicon, plus CI's portable runs on macOS 26. FileCat ships only an `osx-arm64` Mac package, so Intel Macs are outside N-MAC's scope. **Open:** FileVault on more hardware, which was not available.

## Live File Provider rerun

The prepared File Provider fixture domain on the Mac was still enabled. Its provider serves no data. Both live tests passed against it ([TRX](receipts/2026-11-21-native-rows/mac-provider-live.trx)):

- `Live_Mac_File_Provider_metadata_keeps_prepared_files_dataless`: the file kept its `dataless` flag afterwards.
- `Live_Mac_File_Provider_directory_metadata_keeps_the_prepared_folder_bound`: the folder kept its identity and the thread materialization policy.

The fixture folder carries only the `uchg` flag, not `dataless`. Dataless-flagged folders and packages therefore remain open for F-PARSER.

## Hosted ARM64 runners

The only ARM64 machines available are GitHub-hosted runners. The manual workflow `details-arm64-qualification.yml` (commit `8d20739bd`) passed in [run 44935244040](https://github.com/benny-cz/FileCat/actions/runs/44935244040).

**Windows on Arm** (`windows-11-arm`, medium integrity): the runner created owned FAT32 (4 KiB), exFAT (32 KiB) and ReFS Dev Drive (4 KiB) volumes. 15 tests passed and 3 skipped, because the 64 KiB, read-only and no-filter variables were not set ([TRX](receipts/2026-11-21-native-rows/windows-arm64-volumes.trx) `ca3eadfa…`, [volume log](receipts/2026-11-21-native-rows/windows-arm64-volumes.log)). The tests that passed:

- details on all three volumes;
- native and managed listing parity on all three;
- the denied folder and the compatibility junction;
- case-distinct and 255-unit names;
- the NTFS omission sentinels, the long name beyond MAX_PATH and the native columns.

**Linux ARM64** (`ubuntu-24.04-arm`): the Unix details, columns, supplementary data, search and reparse classes. 145 tests passed and none failed ([TRX](receipts/2026-11-21-native-rows/linux-arm64-details.trx) `9efe88b1…`). That includes the ext4 inline_data sentinel on a loop-mounted volume and the native column test against `stat`. The skips are fixtures scoped to Linux x64 (the qualified ext-family and Btrfs fixtures) and the macOS-only cases.
