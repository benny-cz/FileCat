# E-A02 — CI run 36821398706: what every lane skips, and what no lane runs (step 3)

Completes step 3's skip inventory for the lanes E-A01 could not cover (no TRX): the Windows ARM64 lane and the
portable lanes (ubuntu-latest, macos-latest); the Windows x64 lane is included again for the comparison.

- Source: `dd1e326c6929f34bfa4f5203bbc9b065d5046cf1` (main), workflow `CI`, 2026-10-01T05:46:27Z–05:53:18Z, all four
  test jobs green (Ubuntu 110237539828, Windows ARM64 110237539832, macOS 110237539888, Windows 110237540062); the
  three package jobs skipped, as on every push.
- Input: the run's whole log (`gh run view --log`), `a02-skips/run-36821398706.log`
  `a6f90e17152268080127ae251ec4d40d4fb5d24e28b43425d2787a045b9533a8`, read by `a02-skips/skip_inventory.py`
  `4741e44571685308cc85d1de6228bce1a43ebf2886a0fc1e28adb239d79a52e7` (output `a02-skips/inventory.md`
  `3363962dba0653e6bb37fff76ef4ea97144c9eb7b4b8fbb6a20664e3e6aef667`, appended below).
- Method: the console logger names each skipped test but not why, so each reason is read from the test's source — an
  `Assert.Skip…` in the test, or in the helpers of its file it calls (followed three calls deep). Where a test can skip
  in more than one place, every possible reason is listed; the log cannot tell which applied. A test that a lane's full
  run skips but one of the lane's targeted steps runs (a class filter with the gate's variables set, e.g. Samba,
  WebKitGTK under xvfb, a block device, FAT mounts as root, a FAT VHD) counts as run on that lane.
- Limitation, as in E-A01: a test that returns without asserting is reported as passed; those were inventoried
  separately (E-S01).

## Per lane

| Lane | Core | Platform.Windows | Remote | App | Targeted steps (classes) |
|---|---|---|---|---|---|
| Windows x64 | 591 passed, 42 skipped | 99 passed, 22 skipped | 86 passed, 28 skipped | 177 passed, 6 skipped | FatDriveRecordTests |
| Windows ARM64 | 591 passed, 42 skipped | 99 passed, 22 skipped | **not run** | 177 passed, 6 skipped | — |
| Ubuntu | 594 passed, 34 skipped | — | 92 passed, 22 skipped | 173 passed, 10 skipped | UnixDeviceTests, SecretStoreTests, UnixNetworkTests (Samba, GVFS), UnixFatRecordTests (root), LinuxPageEngineTests (xvfb) |
| macOS | 593 passed, 35 skipped | — | 92 passed, 22 skipped | 173 passed, 10 skipped | UnixDeviceTests |

Skipped counts are test cases (theory rows); the tables below count each test once.

## Findings

- **The ARM64 lane never runs the Remote tests** (SFTP, FTP, FTPS, WebDAV clients, SSH agent): on Windows on ARM64 that
  code is exercised by no CI lane. The workflow's ARM64 job runs Core, Platform.Windows and App only.
- **37 tests run on no lane** (the first table below): all are gated on resources CI does not have — the remote and SMB
  labs (run by this campaign against real servers: E-V08-L1, E-V08-L2, E-V08-S1), the USB test drive, a phone (MTP:
  PPL-03), the disk images Windows made (E-V09-W1), and the benchmarks. Nothing in CI vouches for them; their
  evidence is the campaign's runs, which must be repeated on the candidate.
- Every other skip is a platform's own (a Windows feature on Linux, a Linux feature on Windows, a macOS-only check), and
  each such test runs on the lane of its platform.

## Inventory (generated)

### Run on no lane

| Test | Lanes that ran its assembly | Skip reason in the source |
|---|---|---|
| `App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` | 4 | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()); Scanning a drive without the installed helper needs the test to run as administrator. (in GuardedDrive()) |
| `Core.Tests.ArchiveBenchmark.Archives_scan_open_extract_and_update_within_budgets` | 4 | Set FILECAT_ARCHIVE_BENCH=1 to measure archives. |
| `Core.Tests.CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable` | 4 | Set FILECAT_COMPARE_BENCH=1 to measure comparison. |
| `Core.Tests.RecoveryBenchmark.Scan_and_preview_a_large_image` | 4 | Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images. |
| `Core.Tests.RecoveryEngineTests.Deleted_files_on_images_Windows_made_come_back_as_they_were` | 4 | Set FILECAT_RECOVERY_IMAGES to a folder of disk images Windows made (artifacts/vm/win-recovery-images.ps1). |
| `Core.Tests.SearchBenchmark.Searching_by_name_and_content_is_fast_complete_and_cancelable` | 4 | Set FILECAT_SEARCH_BENCH=1 to measure search. |
| `Platform.Windows.Tests.LiveDriveRecoveryTests.A_usb_drive_is_scanned_through_the_helper_protocol_and_signed_files_recover_exactly` | 2 | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()) |
| `Platform.Windows.Tests.LiveDriveRecoveryTests.An_elevated_FileCat_reads_the_drive_itself_exactly_as_the_helper_serves_it` | 2 | Reading a drive directly needs the test to run as administrator.; Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()) |
| `Platform.Windows.Tests.LiveDriveScenarioTests.Files_Windows_deleted_come_back_as_written` | 2 | Formats the drive: set FILECAT_RECOVERY_LIVE_DESTRUCTIVE=1 as well. (in DestructiveDrive()) |
| `Platform.Windows.Tests.MtpRobustnessTests.Canceling_part_way_leaves_nothing_behind_and_never_loses_the_file_being_replaced` | 2 | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Empty_files_Unicode_names_and_names_differing_in_letter_case` | 2 | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Listing_a_folder_of_a_thousand_files` | 2 | Set FILECAT_MTP_BENCH=1 (and FILECAT_MTP_TEST=1) to measure listing on a device.; Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Moving_to_the_device_removes_the_originals_only_after_their_copies_are_complete` | 2 | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpTests.A_device_file_reads_completely_and_again_from_an_earlier_offset` | 2 | Set FILECAT_MTP_READTEST=1 with an unlocked device to run the read-only check.; No portable device is connected.; The device offers no file to read.; The device offers no file to read through the provider. |
| `Platform.Windows.Tests.MtpTests.A_device_folder_can_be_created_filled_read_renamed_and_removed` | 2 | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpTests.Jobs_upload_a_tree_download_it_rename_and_delete_inside_the_test_folder` | 2 | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.RegistryBenchmark.Registry_listing_and_search_are_fast_bounded_and_cancelable` | 2 | The Registry is part of Windows.; Set FILECAT_REGISTRY_BENCH=1 to measure the Registry. |
| `Platform.Windows.Tests.SmbLabTests.A_copy_cut_off_by_the_server_arrives_intact_after_retrying` | 2 | Set FILECAT_SMB_LAB_DROP to a command that drops this machine's sessions on the server.; Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.A_copy_to_the_share_cancelled_part_way_leaves_nothing` | 2 | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_downloaded_file_moved_to_the_share_asks_before_its_origin_is_lost` | 2 | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_file_open_on_the_share_is_replaced_or_reported_in_use` | 2 | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_move_within_the_share_renames_on_the_server` | 2 | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.A_tree_goes_to_the_share_and_back_byte_for_byte_through_jobs` | 2 | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.Deleting_on_a_share_keeps_the_items_unless_permanent_deletion_was_agreed` | 2 | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Remote.Tests.RemoteBenchmark.Transfers_through_jobs_stay_close_to_the_connection_itself` | 3 | Set FILECAT_REMOTE_BENCH=1 to measure remote transfers.; No FTP or SFTP test server here (pyftpdlib, or sshd on Linux/macOS). |
| `Remote.Tests.RemoteLabTests.A_cancelled_upload_leaves_nothing_under_its_name` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_goes_up_and_comes_back_byte_for_byte_through_jobs` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_moved_off_the_server_arrives_and_only_then_leaves_it` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_sftp_connection_gets_socket_buffers_for_long_links` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_cut_off_by_the_server_continues_and_arrives_intact` | 3 | Set FILECAT_REMOTE_LAB_DROP to a command that drops the test account's connections.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_the_server_refuses_says_so_and_leaves_nothing` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Ftps_asks_about_a_self_signed_certificate_once_and_pins_it` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Links_on_the_server_are_changed_themselves_never_their_targets` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Names_with_spaces_at_their_edges_are_listed_exactly_and_never_reach_another_item_over_ftp` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Odd_names_arrive_on_the_server_exactly_and_come_back_the_same` | 3 | Set FILECAT_REMOTE_LAB_SERVER_NAMES to a command that lists a server folder's names as bytes.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Plain_ftp_sends_no_password_without_consent` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Sftp_trusts_a_host_key_on_first_use_and_refuses_a_changed_one` | 3 | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |

### Windows (build, all tests)

- FileCat.Remote.Tests: 86 passed, 28 skipped, 0 failed of 114
- FileCat.Core.Tests: 591 passed, 42 skipped, 0 failed of 633
- FileCat.Platform.Windows.Tests: 99 passed, 22 skipped, 0 failed of 121
- FileCat.App.Tests: 177 passed, 6 skipped, 0 failed of 183
- FileCat.Platform.Windows.Tests: 1 passed, 0 skipped, 0 failed of 1 (step `Run $letter = [char[]](75..90) | Where-Object { -not (Test-Path "$($_):\") } | Select-Object -Last 1`, only FatDriveRecordTests)

#### FileCat.App.Tests: 6 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `App.Tests.HexEditorPosixTests.A_link_is_refused_and_a_byte_another_program_changed_blocks_the_save` |  | Windows keeps other writers out instead (HexEditorSmokeTests). |
| `App.Tests.HexEditorPosixTests.Save_as_keeps_no_copy_of_a_file_another_program_wrote_meanwhile` |  | Windows keeps other writers out while the file is open here. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_a_markdown_file_and_asks_the_web_for_nothing` |  | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` |  | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` |  | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()); Scanning a drive without the installed helper needs the test to run as administrator. (in GuardedDrive()) |
| `App.Tests.PermissionsDialogTests.Boxes_and_octal_agree_and_only_the_chosen_permissions_change` |  | POSIX permissions are a Linux and macOS feature. |

#### FileCat.Core.Tests: 39 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Core.Tests.ArchiveBenchmark.Archives_scan_open_extract_and_update_within_budgets` |  | Set FILECAT_ARCHIVE_BENCH=1 to measure archives. |
| `Core.Tests.CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable` |  | Set FILECAT_COMPARE_BENCH=1 to measure comparison. |
| `Core.Tests.ComputerProviderTests.Unix_lists_folders_but_not_bound_files_or_memory_scratch_space` |  | Mount points that are files or tmpfs exist on Linux and macOS. |
| `Core.Tests.FreedesktopIconsTests.Types_folders_and_drives_find_their_theme_icons_and_draw` |  | Freedesktop icon themes are a Linux desktop matter.; No icon themes or MIME database on this machine.; The icon theme {icons.ThemeName} has no text icon here.; gdk-pixbuf is not installed. |
| `Core.Tests.HiddenDataTests.A_files_own_streams_and_attributes_are_listed_read_and_deleted` |  | Windows' streams and NTFS attributes are tested with the Windows platform (WindowsHiddenDataTests).; This file system keeps no extended attributes here (errno {Marshal.GetLastPInvokeError()}).; No streams or attributes here. |
| `Core.Tests.HiddenDataTests.Find_finds_files_carrying_attributes_besides_their_download_mark` |  | Windows' streams are searched in WindowsHiddenDataTests.; This file system keeps no extended attributes here. |
| `Core.Tests.InspectorCrossCheckTests.ELF_reports_agree_with_readelf` |  | readelf and ELF system files are on Linux.; readelf is not installed. |
| `Core.Tests.InspectorCrossCheckTests.Mach_O_reports_agree_with_otool_and_codesign` |  | otool, codesign, and Mach-O system files are on macOS.; otool or codesign is not available. |
| `Core.Tests.MacIconsTests.Types_folders_and_the_home_folder_draw_as_Finder_shows_them` |  | NSWorkspace exists only on macOS. |
| `Core.Tests.PathAndStateTests.FileCats_own_folders_are_its_users_alone_on_Linux_and_macOS` |  | Windows keeps the user's application data private by its ACLs. |
| `Core.Tests.RecoveryBenchmark.Scan_and_preview_a_large_image` |  | Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images. |
| `Core.Tests.RecoveryEngineTests.Deleted_files_on_images_Windows_made_come_back_as_they_were` |  | Set FILECAT_RECOVERY_IMAGES to a folder of disk images Windows made (artifacts/vm/win-recovery-images.ps1). |
| `Core.Tests.SearchBenchmark.Searching_by_name_and_content_is_fast_complete_and_cancelable` |  | Set FILECAT_SEARCH_BENCH=1 to measure search. |
| `Core.Tests.SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret` |  | No system keychain or desktop keyring answers here.; The keychain is locked or missing here:  |
| `Core.Tests.UnixDeviceTests.A_block_device_is_scanned_like_its_image` |  | Set FILECAT_TEST_BLOCK_DEVICE to a readable device holding the fat16 fixture. |
| `Core.Tests.UnixDeviceTests.A_descriptor_passed_over_a_local_socket_arrives_open` |  | Descriptors pass over local sockets on Linux and macOS. |
| `Core.Tests.UnixDeviceTests.The_system_bus_answers_calls_and_errors` |  | Needs Linux with a system bus. |
| `Core.Tests.UnixDeviceTests.This_computers_disks_are_listed_and_the_root_folder_has_one` |  | Linux and macOS list their disks this way.; No block devices are visible here (a container?). |
| `Core.Tests.UnixFatRecordTests.As_root_a_file_on_FAT32_and_exFAT_shows_its_own_directory_entry` |  | Set FILECAT_TEST_FAT_MOUNTS to FAT32 and exFAT mounts, and run as root. |
| `Core.Tests.UnixFileRecordTests.A_POSIX_ACL_reads_as_a_table_of_who_may_do_what` |  | POSIX ACLs are read on Linux.; This file system keeps no POSIX ACLs here. |
| `Core.Tests.UnixFileRecordTests.A_file_reads_with_its_inode_times_permissions_and_layout` |  | The Linux and macOS file-system record. |
| `Core.Tests.UnixFileRecordTests.A_macOS_file_shows_its_BSD_flags_and_added_time` |  | BSD flags and the added time are macOS's. |
| `Core.Tests.UnixFileRecordTests.The_record_lists_the_items_attributes_with_what_they_say` |  | The Linux and macOS file-system record.; This file system keeps no extended attributes here. |
| `Core.Tests.UnixFileRecordTests.World_writable_items_are_flagged_and_a_sticky_folder_is_only_noted` |  | The Linux and macOS file-system record. |
| `Core.Tests.UnixFilesTests.A_file_its_handle_and_its_link_report_what_they_are` |  | Linux and macOS read these through their C libraries. |
| `Core.Tests.UnixFilesTests.A_rename_keeps_the_identity_and_a_new_file_has_another` |  | Linux and macOS read these through their C libraries. |
| `Core.Tests.UnixFilesTests.A_rename_never_replaces_unless_asked` |  | The Linux and macOS rename. |
| `Core.Tests.UnixFilesTests.Links_resolve_to_where_they_lead_and_hard_links_share_an_identity` |  | Linux and macOS read these through their C libraries. |
| `Core.Tests.UnixMarkTests.A_download_origin_travels_in_extended_attributes` |  | Extended-attribute marks are for Linux and macOS. |
| `Core.Tests.UnixNetworkTests.A_Samba_servers_shares_are_listed_and_a_guest_share_opens_as_a_folder` |  | Needs the Samba server CI's Linux job starts (FILECAT_TEST_SAMBA=1). |
| `Core.Tests.UnixPermissionTests.A_copied_private_folder_stays_private` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixPermissionTests.A_recursive_change_reaches_folders_last_and_leaves_links_and_documents_alone` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixPermissionTests.An_entry_reports_its_mode_owner_and_group` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixPermissionTests.Permission_columns_cover_files_and_folders` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixTrashTests.A_trash_folder_that_is_a_link_is_never_used` |  | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| `Core.Tests.UnixTrashTests.A_volume_trash_records_paths_relative_to_the_volume` |  | Volume trash folders with .trashinfo files are a Linux (freedesktop.org) feature. |
| `Core.Tests.UnixTrashTests.Items_go_to_a_private_trash_with_their_origin_and_come_back` |  | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| `Core.Tests.UnixTrashTests.The_home_volume_uses_the_home_trash_without_creating_it` |  | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| `Core.Tests.VerificationTests.A_real_OpenPGP_signature_is_checked_with_the_systems_gpg` |  | No gpg here. |

#### FileCat.Platform.Windows.Tests: 19 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Platform.Windows.Tests.FatDriveRecordTests.As_administrator_a_file_on_FAT32_shows_its_entry_and_Windows_case_bits` | yes | Set FILECAT_TEST_FAT_DRIVE to a FAT32 drive's root, and run as administrator. |
| `Platform.Windows.Tests.LiveDriveRecoveryTests.A_usb_drive_is_scanned_through_the_helper_protocol_and_signed_files_recover_exactly` |  | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()) |
| `Platform.Windows.Tests.LiveDriveRecoveryTests.An_elevated_FileCat_reads_the_drive_itself_exactly_as_the_helper_serves_it` |  | Reading a drive directly needs the test to run as administrator.; Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()) |
| `Platform.Windows.Tests.LiveDriveScenarioTests.Files_Windows_deleted_come_back_as_written` |  | Formats the drive: set FILECAT_RECOVERY_LIVE_DESTRUCTIVE=1 as well. (in DestructiveDrive()) |
| `Platform.Windows.Tests.MtpRobustnessTests.Canceling_part_way_leaves_nothing_behind_and_never_loses_the_file_being_replaced` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Empty_files_Unicode_names_and_names_differing_in_letter_case` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Listing_a_folder_of_a_thousand_files` |  | Set FILECAT_MTP_BENCH=1 (and FILECAT_MTP_TEST=1) to measure listing on a device.; Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Moving_to_the_device_removes_the_originals_only_after_their_copies_are_complete` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpTests.A_device_file_reads_completely_and_again_from_an_earlier_offset` |  | Set FILECAT_MTP_READTEST=1 with an unlocked device to run the read-only check.; No portable device is connected.; The device offers no file to read.; The device offers no file to read through the provider. |
| `Platform.Windows.Tests.MtpTests.A_device_folder_can_be_created_filled_read_renamed_and_removed` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpTests.Jobs_upload_a_tree_download_it_rename_and_delete_inside_the_test_folder` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.RegistryBenchmark.Registry_listing_and_search_are_fast_bounded_and_cancelable` |  | The Registry is part of Windows.; Set FILECAT_REGISTRY_BENCH=1 to measure the Registry. |
| `Platform.Windows.Tests.SmbLabTests.A_copy_cut_off_by_the_server_arrives_intact_after_retrying` |  | Set FILECAT_SMB_LAB_DROP to a command that drops this machine's sessions on the server.; Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.A_copy_to_the_share_cancelled_part_way_leaves_nothing` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_downloaded_file_moved_to_the_share_asks_before_its_origin_is_lost` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_file_open_on_the_share_is_replaced_or_reported_in_use` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_move_within_the_share_renames_on_the_server` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.A_tree_goes_to_the_share_and_back_byte_for_byte_through_jobs` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.Deleting_on_a_share_keeps_the_items_unless_permanent_deletion_was_agreed` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |

#### FileCat.Remote.Tests: 19 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Remote.Tests.FtpIntegrationTests.A_name_with_a_backslash_is_refused_rather_than_reaching_another_item` |  | A backslash cannot be part of a file name on Windows, where this server keeps its files.; No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON). |
| `Remote.Tests.OpenSshIntegrationTests.A_connection_is_not_held_to_the_small_socket_buffers_SSH_NET_sets` |  | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). (in Connect()) |
| `Remote.Tests.OpenSshIntegrationTests.A_wrong_host_key_stops_the_connection` |  | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).; No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). (in Connect()) |
| `Remote.Tests.OpenSshIntegrationTests.Files_are_created_exclusively_read_at_offsets_and_replaced_atomically` |  | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). (in Connect()) |
| `Remote.Tests.OpenSshIntegrationTests.Links_are_renamed_and_deleted_themselves_never_their_targets` |  | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). (in Connect()) |
| `Remote.Tests.RemoteBenchmark.Transfers_through_jobs_stay_close_to_the_connection_itself` |  | Set FILECAT_REMOTE_BENCH=1 to measure remote transfers.; No FTP or SFTP test server here (pyftpdlib, or sshd on Linux/macOS). |
| `Remote.Tests.RemoteLabTests.A_cancelled_upload_leaves_nothing_under_its_name` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_goes_up_and_comes_back_byte_for_byte_through_jobs` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_moved_off_the_server_arrives_and_only_then_leaves_it` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_sftp_connection_gets_socket_buffers_for_long_links` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_cut_off_by_the_server_continues_and_arrives_intact` |  | Set FILECAT_REMOTE_LAB_DROP to a command that drops the test account's connections.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_the_server_refuses_says_so_and_leaves_nothing` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Ftps_asks_about_a_self_signed_certificate_once_and_pins_it` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Links_on_the_server_are_changed_themselves_never_their_targets` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Names_with_spaces_at_their_edges_are_listed_exactly_and_never_reach_another_item_over_ftp` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Odd_names_arrive_on_the_server_exactly_and_come_back_the_same` |  | Set FILECAT_REMOTE_LAB_SERVER_NAMES to a command that lists a server folder's names as bytes.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Plain_ftp_sends_no_password_without_consent` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Sftp_trusts_a_host_key_on_first_use_and_refuses_a_changed_one` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.SshAgentTests.A_real_server_accepts_a_key_that_only_the_agent_holds` |  | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |

### Windows ARM64 (build, tests, package start)

- FileCat.Core.Tests: 591 passed, 42 skipped, 0 failed of 633
- FileCat.Platform.Windows.Tests: 99 passed, 22 skipped, 0 failed of 121
- FileCat.App.Tests: 177 passed, 6 skipped, 0 failed of 183

#### FileCat.App.Tests: 6 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `App.Tests.HexEditorPosixTests.A_link_is_refused_and_a_byte_another_program_changed_blocks_the_save` |  | Windows keeps other writers out instead (HexEditorSmokeTests). |
| `App.Tests.HexEditorPosixTests.Save_as_keeps_no_copy_of_a_file_another_program_wrote_meanwhile` |  | Windows keeps other writers out while the file is open here. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_a_markdown_file_and_asks_the_web_for_nothing` |  | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` |  | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` |  | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()); Scanning a drive without the installed helper needs the test to run as administrator. (in GuardedDrive()) |
| `App.Tests.PermissionsDialogTests.Boxes_and_octal_agree_and_only_the_chosen_permissions_change` |  | POSIX permissions are a Linux and macOS feature. |

#### FileCat.Core.Tests: 39 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Core.Tests.ArchiveBenchmark.Archives_scan_open_extract_and_update_within_budgets` |  | Set FILECAT_ARCHIVE_BENCH=1 to measure archives. |
| `Core.Tests.CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable` |  | Set FILECAT_COMPARE_BENCH=1 to measure comparison. |
| `Core.Tests.ComputerProviderTests.Unix_lists_folders_but_not_bound_files_or_memory_scratch_space` |  | Mount points that are files or tmpfs exist on Linux and macOS. |
| `Core.Tests.FreedesktopIconsTests.Types_folders_and_drives_find_their_theme_icons_and_draw` |  | Freedesktop icon themes are a Linux desktop matter.; No icon themes or MIME database on this machine.; The icon theme {icons.ThemeName} has no text icon here.; gdk-pixbuf is not installed. |
| `Core.Tests.HiddenDataTests.A_files_own_streams_and_attributes_are_listed_read_and_deleted` |  | Windows' streams and NTFS attributes are tested with the Windows platform (WindowsHiddenDataTests).; This file system keeps no extended attributes here (errno {Marshal.GetLastPInvokeError()}).; No streams or attributes here. |
| `Core.Tests.HiddenDataTests.Find_finds_files_carrying_attributes_besides_their_download_mark` |  | Windows' streams are searched in WindowsHiddenDataTests.; This file system keeps no extended attributes here. |
| `Core.Tests.InspectorCrossCheckTests.ELF_reports_agree_with_readelf` |  | readelf and ELF system files are on Linux.; readelf is not installed. |
| `Core.Tests.InspectorCrossCheckTests.Mach_O_reports_agree_with_otool_and_codesign` |  | otool, codesign, and Mach-O system files are on macOS.; otool or codesign is not available. |
| `Core.Tests.MacIconsTests.Types_folders_and_the_home_folder_draw_as_Finder_shows_them` |  | NSWorkspace exists only on macOS. |
| `Core.Tests.PathAndStateTests.FileCats_own_folders_are_its_users_alone_on_Linux_and_macOS` |  | Windows keeps the user's application data private by its ACLs. |
| `Core.Tests.RecoveryBenchmark.Scan_and_preview_a_large_image` |  | Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images. |
| `Core.Tests.RecoveryEngineTests.Deleted_files_on_images_Windows_made_come_back_as_they_were` |  | Set FILECAT_RECOVERY_IMAGES to a folder of disk images Windows made (artifacts/vm/win-recovery-images.ps1). |
| `Core.Tests.SearchBenchmark.Searching_by_name_and_content_is_fast_complete_and_cancelable` |  | Set FILECAT_SEARCH_BENCH=1 to measure search. |
| `Core.Tests.SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret` |  | No system keychain or desktop keyring answers here.; The keychain is locked or missing here:  |
| `Core.Tests.UnixDeviceTests.A_block_device_is_scanned_like_its_image` |  | Set FILECAT_TEST_BLOCK_DEVICE to a readable device holding the fat16 fixture. |
| `Core.Tests.UnixDeviceTests.A_descriptor_passed_over_a_local_socket_arrives_open` |  | Descriptors pass over local sockets on Linux and macOS. |
| `Core.Tests.UnixDeviceTests.The_system_bus_answers_calls_and_errors` |  | Needs Linux with a system bus. |
| `Core.Tests.UnixDeviceTests.This_computers_disks_are_listed_and_the_root_folder_has_one` |  | Linux and macOS list their disks this way.; No block devices are visible here (a container?). |
| `Core.Tests.UnixFatRecordTests.As_root_a_file_on_FAT32_and_exFAT_shows_its_own_directory_entry` |  | Set FILECAT_TEST_FAT_MOUNTS to FAT32 and exFAT mounts, and run as root. |
| `Core.Tests.UnixFileRecordTests.A_POSIX_ACL_reads_as_a_table_of_who_may_do_what` |  | POSIX ACLs are read on Linux.; This file system keeps no POSIX ACLs here. |
| `Core.Tests.UnixFileRecordTests.A_file_reads_with_its_inode_times_permissions_and_layout` |  | The Linux and macOS file-system record. |
| `Core.Tests.UnixFileRecordTests.A_macOS_file_shows_its_BSD_flags_and_added_time` |  | BSD flags and the added time are macOS's. |
| `Core.Tests.UnixFileRecordTests.The_record_lists_the_items_attributes_with_what_they_say` |  | The Linux and macOS file-system record.; This file system keeps no extended attributes here. |
| `Core.Tests.UnixFileRecordTests.World_writable_items_are_flagged_and_a_sticky_folder_is_only_noted` |  | The Linux and macOS file-system record. |
| `Core.Tests.UnixFilesTests.A_file_its_handle_and_its_link_report_what_they_are` |  | Linux and macOS read these through their C libraries. |
| `Core.Tests.UnixFilesTests.A_rename_keeps_the_identity_and_a_new_file_has_another` |  | Linux and macOS read these through their C libraries. |
| `Core.Tests.UnixFilesTests.A_rename_never_replaces_unless_asked` |  | The Linux and macOS rename. |
| `Core.Tests.UnixFilesTests.Links_resolve_to_where_they_lead_and_hard_links_share_an_identity` |  | Linux and macOS read these through their C libraries. |
| `Core.Tests.UnixMarkTests.A_download_origin_travels_in_extended_attributes` |  | Extended-attribute marks are for Linux and macOS. |
| `Core.Tests.UnixNetworkTests.A_Samba_servers_shares_are_listed_and_a_guest_share_opens_as_a_folder` |  | Needs the Samba server CI's Linux job starts (FILECAT_TEST_SAMBA=1). |
| `Core.Tests.UnixPermissionTests.A_copied_private_folder_stays_private` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixPermissionTests.A_recursive_change_reaches_folders_last_and_leaves_links_and_documents_alone` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixPermissionTests.An_entry_reports_its_mode_owner_and_group` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixPermissionTests.Permission_columns_cover_files_and_folders` |  | POSIX permissions are a Linux and macOS feature. |
| `Core.Tests.UnixTrashTests.A_trash_folder_that_is_a_link_is_never_used` |  | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| `Core.Tests.UnixTrashTests.A_volume_trash_records_paths_relative_to_the_volume` |  | Volume trash folders with .trashinfo files are a Linux (freedesktop.org) feature. |
| `Core.Tests.UnixTrashTests.Items_go_to_a_private_trash_with_their_origin_and_come_back` |  | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| `Core.Tests.UnixTrashTests.The_home_volume_uses_the_home_trash_without_creating_it` |  | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| `Core.Tests.VerificationTests.A_real_OpenPGP_signature_is_checked_with_the_systems_gpg` |  | No gpg here. |

#### FileCat.Platform.Windows.Tests: 19 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Platform.Windows.Tests.FatDriveRecordTests.As_administrator_a_file_on_FAT32_shows_its_entry_and_Windows_case_bits` |  | Set FILECAT_TEST_FAT_DRIVE to a FAT32 drive's root, and run as administrator. |
| `Platform.Windows.Tests.LiveDriveRecoveryTests.A_usb_drive_is_scanned_through_the_helper_protocol_and_signed_files_recover_exactly` |  | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()) |
| `Platform.Windows.Tests.LiveDriveRecoveryTests.An_elevated_FileCat_reads_the_drive_itself_exactly_as_the_helper_serves_it` |  | Reading a drive directly needs the test to run as administrator.; Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()) |
| `Platform.Windows.Tests.LiveDriveScenarioTests.Files_Windows_deleted_come_back_as_written` |  | Formats the drive: set FILECAT_RECOVERY_LIVE_DESTRUCTIVE=1 as well. (in DestructiveDrive()) |
| `Platform.Windows.Tests.MtpRobustnessTests.Canceling_part_way_leaves_nothing_behind_and_never_loses_the_file_being_replaced` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Empty_files_Unicode_names_and_names_differing_in_letter_case` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Listing_a_folder_of_a_thousand_files` |  | Set FILECAT_MTP_BENCH=1 (and FILECAT_MTP_TEST=1) to measure listing on a device.; Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpRobustnessTests.Moving_to_the_device_removes_the_originals_only_after_their_copies_are_complete` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpTests.A_device_file_reads_completely_and_again_from_an_earlier_offset` |  | Set FILECAT_MTP_READTEST=1 with an unlocked device to run the read-only check.; No portable device is connected.; The device offers no file to read.; The device offers no file to read through the provider. |
| `Platform.Windows.Tests.MtpTests.A_device_folder_can_be_created_filled_read_renamed_and_removed` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.MtpTests.Jobs_upload_a_tree_download_it_rename_and_delete_inside_the_test_folder` |  | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| `Platform.Windows.Tests.RegistryBenchmark.Registry_listing_and_search_are_fast_bounded_and_cancelable` |  | The Registry is part of Windows.; Set FILECAT_REGISTRY_BENCH=1 to measure the Registry. |
| `Platform.Windows.Tests.SmbLabTests.A_copy_cut_off_by_the_server_arrives_intact_after_retrying` |  | Set FILECAT_SMB_LAB_DROP to a command that drops this machine's sessions on the server.; Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.A_copy_to_the_share_cancelled_part_way_leaves_nothing` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_downloaded_file_moved_to_the_share_asks_before_its_origin_is_lost` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_file_open_on_the_share_is_replaced_or_reported_in_use` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |
| `Platform.Windows.Tests.SmbLabTests.A_move_within_the_share_renames_on_the_server` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.A_tree_goes_to_the_share_and_back_byte_for_byte_through_jobs` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()); Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in ServerHashes()) |
| `Platform.Windows.Tests.SmbLabTests.Deleting_on_a_share_keeps_the_items_unless_permanent_deletion_was_agreed` |  | Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows). (in RemoteFolder()) |

### macos-latest (build, portable tests)

- FileCat.Core.Tests: 593 passed, 35 skipped, 0 failed of 628
- FileCat.Core.Tests: 8 passed, 1 skipped, 0 failed of 9 (step `Run gunzip -c tests/FileCat.Core.Tests/TestData/Recovery/fat16.img.gz > "$RUNNER_TEMP/fat16.img"`, only UnixDeviceTests)
- FileCat.Remote.Tests: 92 passed, 22 skipped, 0 failed of 114
- FileCat.App.Tests: 173 passed, 10 skipped, 0 failed of 183

#### FileCat.App.Tests: 10 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `App.Tests.ExitWithWaitingJobTests.A_delete_that_meets_a_locked_file_asks_and_closing_then_asks_about_it` |  | Only Windows keeps an open file from being deleted. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_a_markdown_file_and_asks_the_web_for_nothing` |  | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` |  | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` |  | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()); Scanning a drive without the installed helper needs the test to run as administrator. (in GuardedDrive()) |
| `App.Tests.NetworkPlaceTests.The_Network_lists_known_servers_then_their_shares_and_leads_back` |  | The Windows network first; Linux and macOS follow. |
| `App.Tests.NetworkPlaceTests.With_nothing_known_or_answering_the_Network_says_how_to_reach_a_server` |  | The Windows network first; Linux and macOS follow. |
| `App.Tests.PanelKeysTests.A_drive_letter_opens_the_drive_at_once_at_the_folder_another_panel_shows_there` |  | Drive letters are Windows'. |
| `App.Tests.PanelKeysTests.Alt_F1_then_a_drive_letter_opens_that_drive_as_the_keyboard_sends_them` |  | Drive letters are Windows'. |
| `App.Tests.ShellPictureUiTests.Quick_view_shows_the_Shells_thumbnail_of_a_picture_through_the_restricted_helper` |  | Shell thumbnails exist only on Windows.; This build has no Shell helper beside the tests.; This Windows installation has no thumbnail handler for .bmp files. |
| `App.Tests.WindowsContextMenuTests.Files_in_one_folder_use_the_shell_even_when_access_checks_would_fail` |  | Windows Shell menus are Windows-only. |

#### FileCat.Core.Tests: 30 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Core.Tests.ArchiveBenchmark.Archives_scan_open_extract_and_update_within_budgets` |  | Set FILECAT_ARCHIVE_BENCH=1 to measure archives. |
| `Core.Tests.CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable` |  | Set FILECAT_COMPARE_BENCH=1 to measure comparison. |
| `Core.Tests.ContentAndToolTests.Tool_launcher_refuses_batch_files_with_metacharacters` |  | Batch files and cmd.exe's metacharacters are Windows'. |
| `Core.Tests.ContentAndToolTests.Tool_launcher_uses_absolute_paths_list_files_and_splits_long_selections` |  | Windows' command-line length limit and list files. |
| `Core.Tests.FreedesktopIconsTests.Types_folders_and_drives_find_their_theme_icons_and_draw` |  | Freedesktop icon themes are a Linux desktop matter.; No icon themes or MIME database on this machine.; The icon theme {icons.ThemeName} has no text icon here.; gdk-pixbuf is not installed. |
| `Core.Tests.InspectorCrossCheckTests.ELF_reports_agree_with_readelf` |  | readelf and ELF system files are on Linux.; readelf is not installed. |
| `Core.Tests.InspectorCrossCheckTests.Edits_after_linking_and_signing_are_noticed` |  | Needs a signed Windows system library. |
| `Core.Tests.InspectorCrossCheckTests.Windows_version_information_and_signatures_are_what_Windows_reads` |  | Windows' version and signature APIs exist only on Windows. |
| `Core.Tests.InspectorTests.A_native_system_library_shows_architecture_imports_exports_and_signature_state` |  | Windows system libraries exist only on Windows. |
| `Core.Tests.JobEngineTests.Unrecyclable_items_are_left_alone_or_deleted_by_consent_with_positions` |  | The Recycle Bin's classification of items is Windows'. |
| `Core.Tests.ListingModelTests.Hidden_items_can_be_hidden` |  | The hidden attribute is Windows'. |
| `Core.Tests.Lznt1Tests.Whatever_Windows_compresses_comes_back_exactly` |  | Windows' own LZNT1 compressor is the reference. |
| `Core.Tests.PathAndStateTests.Windows_name_rules` |  | Windows' name rules. |
| `Core.Tests.RecoveryBenchmark.Scan_and_preview_a_large_image` |  | Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images. |
| `Core.Tests.RecoveryEngineTests.Deleted_files_on_images_Windows_made_come_back_as_they_were` |  | Set FILECAT_RECOVERY_IMAGES to a folder of disk images Windows made (artifacts/vm/win-recovery-images.ps1). |
| `Core.Tests.SearchBenchmark.Searching_by_name_and_content_is_fast_complete_and_cancelable` |  | Set FILECAT_SEARCH_BENCH=1 to measure search. |
| `Core.Tests.ShellFileIconsTests.A_folder_customization_names_its_icon_in_either_form` |  | Windows paths resolve only on Windows. |
| `Core.Tests.ShellFileIconsTests.A_shortcut_names_its_target_and_the_icon_it_shows` |  | Windows paths resolve only on Windows. |
| `Core.Tests.ShellFileIconsTests.Internet_shortcuts_name_a_local_icon_file_but_never_a_web_address` |  | Windows paths resolve only on Windows. |
| `Core.Tests.TruthfulOutcomeTests.A_move_that_would_lose_metadata_asks_before_the_original_is_deleted` |  | The metadata a move would lose here is NTFS's streams. |
| `Core.Tests.TruthfulOutcomeTests.A_really_locked_file_is_reported_as_in_use` |  | Only Windows keeps an open file from being shared; sharing modes are advisory elsewhere. |
| `Core.Tests.TruthfulOutcomeTests.A_share_is_named_as_what_cannot_store_the_metadata_not_the_file_system_it_claims` |  | The metadata a move would lose here is NTFS's streams. |
| `Core.Tests.TruthfulOutcomeTests.Streams_a_destination_cannot_store_are_named_as_lost` |  | Named streams are an NTFS concept. |
| `Core.Tests.UnixDeviceTests.A_block_device_is_scanned_like_its_image` | yes | Set FILECAT_TEST_BLOCK_DEVICE to a readable device holding the fat16 fixture. |
| `Core.Tests.UnixDeviceTests.The_system_bus_answers_calls_and_errors` |  | Needs Linux with a system bus. |
| `Core.Tests.UnixFatRecordTests.As_root_a_file_on_FAT32_and_exFAT_shows_its_own_directory_entry` |  | Set FILECAT_TEST_FAT_MOUNTS to FAT32 and exFAT mounts, and run as root. |
| `Core.Tests.UnixFileRecordTests.A_POSIX_ACL_reads_as_a_table_of_who_may_do_what` |  | POSIX ACLs are read on Linux.; This file system keeps no POSIX ACLs here. |
| `Core.Tests.UnixNetworkTests.A_Samba_servers_shares_are_listed_and_a_guest_share_opens_as_a_folder` |  | Needs the Samba server CI's Linux job starts (FILECAT_TEST_SAMBA=1). |
| `Core.Tests.UnixTrashTests.A_volume_trash_records_paths_relative_to_the_volume` |  | Volume trash folders with .trashinfo files are a Linux (freedesktop.org) feature. |
| `Core.Tests.VerificationTests.A_checksum_file_that_is_a_cloud_placeholder_is_not_read` |  | Placeholder attributes are Windows'. |

#### FileCat.Remote.Tests: 13 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Remote.Tests.RemoteBenchmark.Transfers_through_jobs_stay_close_to_the_connection_itself` |  | Set FILECAT_REMOTE_BENCH=1 to measure remote transfers.; No FTP or SFTP test server here (pyftpdlib, or sshd on Linux/macOS). |
| `Remote.Tests.RemoteLabTests.A_cancelled_upload_leaves_nothing_under_its_name` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_goes_up_and_comes_back_byte_for_byte_through_jobs` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_moved_off_the_server_arrives_and_only_then_leaves_it` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_sftp_connection_gets_socket_buffers_for_long_links` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_cut_off_by_the_server_continues_and_arrives_intact` |  | Set FILECAT_REMOTE_LAB_DROP to a command that drops the test account's connections.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_the_server_refuses_says_so_and_leaves_nothing` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Ftps_asks_about_a_self_signed_certificate_once_and_pins_it` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Links_on_the_server_are_changed_themselves_never_their_targets` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Names_with_spaces_at_their_edges_are_listed_exactly_and_never_reach_another_item_over_ftp` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Odd_names_arrive_on_the_server_exactly_and_come_back_the_same` |  | Set FILECAT_REMOTE_LAB_SERVER_NAMES to a command that lists a server folder's names as bytes.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Plain_ftp_sends_no_password_without_consent` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Sftp_trusts_a_host_key_on_first_use_and_refuses_a_changed_one` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |

### ubuntu-latest (build, portable tests)

- FileCat.Core.Tests: 594 passed, 34 skipped, 0 failed of 628
- FileCat.Core.Tests: 9 passed, 0 skipped, 0 failed of 9 (step `Run gunzip -c tests/FileCat.Core.Tests/TestData/Recovery/fat16.img.gz > "$RUNNER_TEMP/fat16.img"`, only UnixDeviceTests)
- FileCat.Core.Tests: 4 passed, 0 skipped, 0 failed of 4 (step `Run sudo apt-get update -qq && sudo apt-get install -y -qq --no-install-recommends gnome-keyring dbus libsecret-1-0`, only SecretStoreTests)
- FileCat.Core.Tests: 5 passed, 0 skipped, 0 failed of 5 (step `Run sudo apt-get update -qq && sudo apt-get install -y -qq --no-install-recommends samba smbclient gvfs-backends gvfs-fuse dbus`, only UnixNetworkTests)
- FileCat.Core.Tests: 1 passed, 0 skipped, 0 failed of 1 (step `Run sudo apt-get install -y -qq --no-install-recommends dosfstools exfatprogs`, only UnixFatRecordTests)
- FileCat.Remote.Tests: 92 passed, 22 skipped, 0 failed of 114
- FileCat.App.Tests: 173 passed, 10 skipped, 0 failed of 183
- FileCat.App.Tests: 2 passed, 0 skipped, 0 failed of 2 (step `Run sudo apt-get update -qq && sudo apt-get install -y -qq --no-install-recommends libwebkit2gtk-4.1-0 xvfb`, only LinuxPageEngineTests)

#### FileCat.App.Tests: 10 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `App.Tests.ExitWithWaitingJobTests.A_delete_that_meets_a_locked_file_asks_and_closing_then_asks_about_it` |  | Only Windows keeps an open file from being deleted. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_a_markdown_file_and_asks_the_web_for_nothing` | yes | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` | yes | WebKitGTK is Linux's.; No X display. |
| `App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` |  | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). (in GuardedDrive()); Scanning a drive without the installed helper needs the test to run as administrator. (in GuardedDrive()) |
| `App.Tests.NetworkPlaceTests.The_Network_lists_known_servers_then_their_shares_and_leads_back` |  | The Windows network first; Linux and macOS follow. |
| `App.Tests.NetworkPlaceTests.With_nothing_known_or_answering_the_Network_says_how_to_reach_a_server` |  | The Windows network first; Linux and macOS follow. |
| `App.Tests.PanelKeysTests.A_drive_letter_opens_the_drive_at_once_at_the_folder_another_panel_shows_there` |  | Drive letters are Windows'. |
| `App.Tests.PanelKeysTests.Alt_F1_then_a_drive_letter_opens_that_drive_as_the_keyboard_sends_them` |  | Drive letters are Windows'. |
| `App.Tests.ShellPictureUiTests.Quick_view_shows_the_Shells_thumbnail_of_a_picture_through_the_restricted_helper` |  | Shell thumbnails exist only on Windows.; This build has no Shell helper beside the tests.; This Windows installation has no thumbnail handler for .bmp files. |
| `App.Tests.WindowsContextMenuTests.Files_in_one_folder_use_the_shell_even_when_access_checks_would_fail` |  | Windows Shell menus are Windows-only. |

#### FileCat.Core.Tests: 29 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Core.Tests.ArchiveBenchmark.Archives_scan_open_extract_and_update_within_budgets` |  | Set FILECAT_ARCHIVE_BENCH=1 to measure archives. |
| `Core.Tests.CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable` |  | Set FILECAT_COMPARE_BENCH=1 to measure comparison. |
| `Core.Tests.ContentAndToolTests.Tool_launcher_refuses_batch_files_with_metacharacters` |  | Batch files and cmd.exe's metacharacters are Windows'. |
| `Core.Tests.ContentAndToolTests.Tool_launcher_uses_absolute_paths_list_files_and_splits_long_selections` |  | Windows' command-line length limit and list files. |
| `Core.Tests.InspectorCrossCheckTests.Edits_after_linking_and_signing_are_noticed` |  | Needs a signed Windows system library. |
| `Core.Tests.InspectorCrossCheckTests.Mach_O_reports_agree_with_otool_and_codesign` |  | otool, codesign, and Mach-O system files are on macOS.; otool or codesign is not available. |
| `Core.Tests.InspectorCrossCheckTests.Windows_version_information_and_signatures_are_what_Windows_reads` |  | Windows' version and signature APIs exist only on Windows. |
| `Core.Tests.InspectorTests.A_native_system_library_shows_architecture_imports_exports_and_signature_state` |  | Windows system libraries exist only on Windows. |
| `Core.Tests.JobEngineTests.Unrecyclable_items_are_left_alone_or_deleted_by_consent_with_positions` |  | The Recycle Bin's classification of items is Windows'. |
| `Core.Tests.ListingModelTests.Hidden_items_can_be_hidden` |  | The hidden attribute is Windows'. |
| `Core.Tests.Lznt1Tests.Whatever_Windows_compresses_comes_back_exactly` |  | Windows' own LZNT1 compressor is the reference. |
| `Core.Tests.MacIconsTests.Types_folders_and_the_home_folder_draw_as_Finder_shows_them` |  | NSWorkspace exists only on macOS. |
| `Core.Tests.PathAndStateTests.Windows_name_rules` |  | Windows' name rules. |
| `Core.Tests.RecoveryBenchmark.Scan_and_preview_a_large_image` |  | Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images. |
| `Core.Tests.RecoveryEngineTests.Deleted_files_on_images_Windows_made_come_back_as_they_were` |  | Set FILECAT_RECOVERY_IMAGES to a folder of disk images Windows made (artifacts/vm/win-recovery-images.ps1). |
| `Core.Tests.SearchBenchmark.Searching_by_name_and_content_is_fast_complete_and_cancelable` |  | Set FILECAT_SEARCH_BENCH=1 to measure search. |
| `Core.Tests.SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret` | yes | No system keychain or desktop keyring answers here.; The keychain is locked or missing here:  |
| `Core.Tests.ShellFileIconsTests.A_folder_customization_names_its_icon_in_either_form` |  | Windows paths resolve only on Windows. |
| `Core.Tests.ShellFileIconsTests.A_shortcut_names_its_target_and_the_icon_it_shows` |  | Windows paths resolve only on Windows. |
| `Core.Tests.ShellFileIconsTests.Internet_shortcuts_name_a_local_icon_file_but_never_a_web_address` |  | Windows paths resolve only on Windows. |
| `Core.Tests.TruthfulOutcomeTests.A_move_that_would_lose_metadata_asks_before_the_original_is_deleted` |  | The metadata a move would lose here is NTFS's streams. |
| `Core.Tests.TruthfulOutcomeTests.A_really_locked_file_is_reported_as_in_use` |  | Only Windows keeps an open file from being shared; sharing modes are advisory elsewhere. |
| `Core.Tests.TruthfulOutcomeTests.A_share_is_named_as_what_cannot_store_the_metadata_not_the_file_system_it_claims` |  | The metadata a move would lose here is NTFS's streams. |
| `Core.Tests.TruthfulOutcomeTests.Streams_a_destination_cannot_store_are_named_as_lost` |  | Named streams are an NTFS concept. |
| `Core.Tests.UnixDeviceTests.A_block_device_is_scanned_like_its_image` | yes | Set FILECAT_TEST_BLOCK_DEVICE to a readable device holding the fat16 fixture. |
| `Core.Tests.UnixFatRecordTests.As_root_a_file_on_FAT32_and_exFAT_shows_its_own_directory_entry` | yes | Set FILECAT_TEST_FAT_MOUNTS to FAT32 and exFAT mounts, and run as root. |
| `Core.Tests.UnixFileRecordTests.A_macOS_file_shows_its_BSD_flags_and_added_time` |  | BSD flags and the added time are macOS's. |
| `Core.Tests.UnixNetworkTests.A_Samba_servers_shares_are_listed_and_a_guest_share_opens_as_a_folder` | yes | Needs the Samba server CI's Linux job starts (FILECAT_TEST_SAMBA=1). |
| `Core.Tests.VerificationTests.A_checksum_file_that_is_a_cloud_placeholder_is_not_read` |  | Placeholder attributes are Windows'. |

#### FileCat.Remote.Tests: 13 tests skipped by a run (theory rows counted once)

| Test | Run by a targeted step | Skip reason in the source |
|---|---|---|
| `Remote.Tests.RemoteBenchmark.Transfers_through_jobs_stay_close_to_the_connection_itself` |  | Set FILECAT_REMOTE_BENCH=1 to measure remote transfers.; No FTP or SFTP test server here (pyftpdlib, or sshd on Linux/macOS). |
| `Remote.Tests.RemoteLabTests.A_cancelled_upload_leaves_nothing_under_its_name` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_goes_up_and_comes_back_byte_for_byte_through_jobs` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.A_tree_moved_off_the_server_arrives_and_only_then_leaves_it` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_sftp_connection_gets_socket_buffers_for_long_links` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_cut_off_by_the_server_continues_and_arrives_intact` |  | Set FILECAT_REMOTE_LAB_DROP to a command that drops the test account's connections.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.An_upload_the_server_refuses_says_so_and_leaves_nothing` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Ftps_asks_about_a_self_signed_certificate_once_and_pins_it` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Links_on_the_server_are_changed_themselves_never_their_targets` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Names_with_spaces_at_their_edges_are_listed_exactly_and_never_reach_another_item_over_ftp` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Odd_names_arrive_on_the_server_exactly_and_come_back_the_same` |  | Set FILECAT_REMOTE_LAB_SERVER_NAMES to a command that lists a server folder's names as bytes.; Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Plain_ftp_sends_no_password_without_consent` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |
| `Remote.Tests.RemoteLabTests.Sftp_trusts_a_host_key_on_first_use_and_refuses_a_changed_one` |  | Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server. (in Lab()) |

