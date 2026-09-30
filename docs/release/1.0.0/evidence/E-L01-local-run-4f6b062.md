# E-L01 — Local preliminary run on the physical developer machine

- Source: `4f6b062fa8548fc8fd417a50262a72c0b804461f` (working tree clean apart from untracked planning documents).
- Environment: physical PC, AMD Ryzen 9 5900X (12 cores), 64 GiB RAM, Windows 11 Pro **Insider Preview** build 26220.9568 (DisplayVersion 25H2), x64, elevated shell. .NET SDK 10.0.401 (global.json 10.0.100 + latestFeature), runtime 10.0.12. gpg 2.x present (C:/Program Files (x86)/GnuPG), no sshd, no phone, no USB fixture variables.
- Commands (the S10 Windows lane, run from the repository root): `dotnet build FileCat.slnx -c Release` (0 warnings, 0 errors; incremental), then `dotnet test FileCat.slnx -c Release --no-build --logger "trx;LogFileName=results.trx" --blame-hang-timeout 4m --blame-hang-dump-type mini` (exit 0). 2026-09-30T17:35:41Z–17:37:05Z.
- Result: 0 failed. Core 500 passed / 32 skipped, Platform.Windows 87/15, Remote 38/5, App 154/4. Core ran one more test than CI (`VerificationTests.A_real_OpenPGP_signature_is_checked_with_the_systems_gpg`) because gpg is installed here.
- Classification: **preliminary** automated evidence on a physical Windows 11 x64 machine. It is not final qualification: the OS is an Insider build, not a serviced GA release (plan §4.2 W64), the build is a development build, and the FAT, live-drive, MTP and benchmark lanes were not enabled.
- SHA-256 of the TRX files: App 6c82275d92add5cf80c2f73f600f7c1477c886550d2042a2fbdb7c374e1775a6, Core 124b1ca15f5557f396e8c0e9dad63afd962ee1c285ff892a5e4f6bbe64410cdc, Platform.Windows 43397f743d243e692a3524b99cd31170dbdee5a034ec13b776fede7c7814e041, Remote 0b89816807ce9edb8698afde4dd3426fa7be98a7d08cec8805cfe749805e3bd9 (retained outside the repository with the raw logs).

### Local-Core

- file: `local-run1/Core/results.trx`
- start: 2026-09-30T19:35:54.5094498+02:00 finish: 2026-09-30T19:36:09.2212681+02:00
- counts: NotExecuted 32, Passed 500

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.Core.Tests.ArchiveBenchmark.Archives_scan_open_extract_and_update_within_budgets` | Set FILECAT_ARCHIVE_BENCH=1 to measure archives. |
| NotExecuted | `FileCat.Core.Tests.CompareBenchmark.Comparison_stays_truthful_bounded_and_cancelable` | Set FILECAT_COMPARE_BENCH=1 to measure comparison. |
| NotExecuted | `FileCat.Core.Tests.ComputerProviderTests.Unix_lists_folders_but_not_bound_files_or_memory_scratch_space` | Mount points that are files or tmpfs exist on Linux and macOS. |
| NotExecuted | `FileCat.Core.Tests.FreedesktopIconsTests.Types_folders_and_drives_find_their_theme_icons_and_draw` | Freedesktop icon themes are a Linux desktop matter. |
| NotExecuted | `FileCat.Core.Tests.HiddenDataTests.A_files_own_streams_and_attributes_are_listed_read_and_deleted` | Windows' streams and NTFS attributes are tested with the Windows platform (WindowsHiddenDataTests). |
| NotExecuted | `FileCat.Core.Tests.HiddenDataTests.Find_finds_files_carrying_attributes_besides_their_download_mark` | Windows' streams are searched in WindowsHiddenDataTests. |
| NotExecuted | `FileCat.Core.Tests.InspectorCrossCheckTests.ELF_reports_agree_with_readelf` | readelf and ELF system files are on Linux. |
| NotExecuted | `FileCat.Core.Tests.InspectorCrossCheckTests.Mach_O_reports_agree_with_otool_and_codesign` | otool, codesign, and Mach-O system files are on macOS. |
| NotExecuted | `FileCat.Core.Tests.MacIconsTests.Types_folders_and_the_home_folder_draw_as_Finder_shows_them` | NSWorkspace exists only on macOS. |
| NotExecuted | `FileCat.Core.Tests.RecoveryBenchmark.Scan_and_preview_a_large_image(image: "bench-fat32.img", fileSystem: "FAT32")` | Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images. |
| NotExecuted | `FileCat.Core.Tests.RecoveryBenchmark.Scan_and_preview_a_large_image(image: "bench-ntfs.img", fileSystem: "NTFS")` | Set FILECAT_RECOVERY_BENCH to a folder with the benchmark images. |
| NotExecuted | `FileCat.Core.Tests.SearchBenchmark.Searching_by_name_and_content_is_fast_complete_and_cancelable` | Set FILECAT_SEARCH_BENCH=1 to measure search. |
| NotExecuted | `FileCat.Core.Tests.SecretStoreTests.The_system_store_keeps_replaces_and_removes_a_secret` | No system keychain or desktop keyring answers here. |
| NotExecuted | `FileCat.Core.Tests.UnixDeviceTests.A_block_device_is_scanned_like_its_image` | Set FILECAT_TEST_BLOCK_DEVICE to a readable device holding the fat16 fixture. |
| NotExecuted | `FileCat.Core.Tests.UnixDeviceTests.A_descriptor_passed_over_a_local_socket_arrives_open` | Descriptors pass over local sockets on Linux and macOS. |
| NotExecuted | `FileCat.Core.Tests.UnixDeviceTests.The_system_bus_answers_calls_and_errors` | Needs Linux with a system bus. |
| NotExecuted | `FileCat.Core.Tests.UnixDeviceTests.This_computers_disks_are_listed_and_the_root_folder_has_one` | Linux and macOS list their disks this way. |
| NotExecuted | `FileCat.Core.Tests.UnixFatRecordTests.As_root_a_file_on_FAT32_and_exFAT_shows_its_own_directory_entry` | Set FILECAT_TEST_FAT_MOUNTS to FAT32 and exFAT mounts, and run as root. |
| NotExecuted | `FileCat.Core.Tests.UnixFilesTests.A_file_its_handle_and_its_link_report_what_they_are` | Linux and macOS read these through their C libraries. |
| NotExecuted | `FileCat.Core.Tests.UnixFilesTests.A_rename_keeps_the_identity_and_a_new_file_has_another` | Linux and macOS read these through their C libraries. |
| NotExecuted | `FileCat.Core.Tests.UnixFilesTests.A_rename_never_replaces_unless_asked` | The Linux and macOS rename. |
| NotExecuted | `FileCat.Core.Tests.UnixFilesTests.Links_resolve_to_where_they_lead_and_hard_links_share_an_identity` | Linux and macOS read these through their C libraries. |
| NotExecuted | `FileCat.Core.Tests.UnixMarkTests.A_download_origin_travels_in_extended_attributes` | Extended-attribute marks are for Linux and macOS. |
| NotExecuted | `FileCat.Core.Tests.UnixNetworkTests.A_Samba_servers_shares_are_listed_and_a_guest_share_opens_as_a_folder` | Needs the Samba server CI's Linux job starts (FILECAT_TEST_SAMBA=1). |
| NotExecuted | `FileCat.Core.Tests.UnixPermissionTests.A_copied_private_folder_stays_private` | POSIX permissions are a Linux and macOS feature. |
| NotExecuted | `FileCat.Core.Tests.UnixPermissionTests.A_recursive_change_reaches_folders_last_and_leaves_links_and_documents_alone` | POSIX permissions are a Linux and macOS feature. |
| NotExecuted | `FileCat.Core.Tests.UnixPermissionTests.An_entry_reports_its_mode_owner_and_group` | POSIX permissions are a Linux and macOS feature. |
| NotExecuted | `FileCat.Core.Tests.UnixPermissionTests.Permission_columns_cover_files_and_folders` | POSIX permissions are a Linux and macOS feature. |
| NotExecuted | `FileCat.Core.Tests.UnixTrashTests.A_trash_folder_that_is_a_link_is_never_used` | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| NotExecuted | `FileCat.Core.Tests.UnixTrashTests.A_volume_trash_records_paths_relative_to_the_volume` | Volume trash folders with .trashinfo files are a Linux (freedesktop.org) feature. |
| NotExecuted | `FileCat.Core.Tests.UnixTrashTests.Items_go_to_a_private_trash_with_their_origin_and_come_back` | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |
| NotExecuted | `FileCat.Core.Tests.UnixTrashTests.The_home_volume_uses_the_home_trash_without_creating_it` | The Recycle Bin is Windows' own; this is the Linux and macOS trash. |

### Local-Windows

- file: `local-run1/Platform.Windows/results.trx`
- start: 2026-09-30T19:35:54.5090412+02:00 finish: 2026-09-30T19:36:05.4879644+02:00
- counts: NotExecuted 15, Passed 87

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.Platform.Windows.Tests.FatDriveRecordTests.As_administrator_a_file_on_FAT32_shows_its_entry_and_Windows_case_bits` | Set FILECAT_TEST_FAT_DRIVE to a FAT32 drive's root, and run as administrator. |
| NotExecuted | `FileCat.Platform.Windows.Tests.LiveDriveRecoveryTests.A_usb_drive_is_scanned_through_the_helper_protocol_and_signed_files_recover_exactly(searchFreeSpace: False)` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.Platform.Windows.Tests.LiveDriveRecoveryTests.A_usb_drive_is_scanned_through_the_helper_protocol_and_signed_files_recover_exactly(searchFreeSpace: True)` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.Platform.Windows.Tests.LiveDriveRecoveryTests.An_elevated_FileCat_reads_the_drive_itself_exactly_as_the_helper_serves_it` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.Platform.Windows.Tests.LiveDriveScenarioTests.Files_Windows_deleted_come_back_as_written(fileSystem: "FAT32")` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.Platform.Windows.Tests.LiveDriveScenarioTests.Files_Windows_deleted_come_back_as_written(fileSystem: "NTFS")` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.Platform.Windows.Tests.LiveDriveScenarioTests.Files_Windows_deleted_come_back_as_written(fileSystem: "exFAT")` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.Platform.Windows.Tests.MtpRobustnessTests.Canceling_part_way_leaves_nothing_behind_and_never_loses_the_file_being_replaced` | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| NotExecuted | `FileCat.Platform.Windows.Tests.MtpRobustnessTests.Empty_files_Unicode_names_and_names_differing_in_letter_case` | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| NotExecuted | `FileCat.Platform.Windows.Tests.MtpRobustnessTests.Listing_a_folder_of_a_thousand_files` | Set FILECAT_MTP_BENCH=1 (and FILECAT_MTP_TEST=1) to measure listing on a device. |
| NotExecuted | `FileCat.Platform.Windows.Tests.MtpRobustnessTests.Moving_to_the_device_removes_the_originals_only_after_their_copies_are_complete` | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| NotExecuted | `FileCat.Platform.Windows.Tests.MtpTests.A_device_file_reads_completely_and_again_from_an_earlier_offset` | Set FILECAT_MTP_READTEST=1 with an unlocked device to run the read-only check. |
| NotExecuted | `FileCat.Platform.Windows.Tests.MtpTests.A_device_folder_can_be_created_filled_read_renamed_and_removed` | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| NotExecuted | `FileCat.Platform.Windows.Tests.MtpTests.Jobs_upload_a_tree_download_it_rename_and_delete_inside_the_test_folder` | Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario. |
| NotExecuted | `FileCat.Platform.Windows.Tests.RegistryBenchmark.Registry_listing_and_search_are_fast_bounded_and_cancelable` | Set FILECAT_REGISTRY_BENCH=1 to measure the Registry. |

### Local-Remote

- file: `local-run1/Remote/results.trx`
- start: 2026-09-30T19:35:54.5099802+02:00 finish: 2026-09-30T19:36:01.5856653+02:00
- counts: NotExecuted 5, Passed 38

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.Remote.Tests.OpenSshIntegrationTests.A_wrong_host_key_stops_the_connection` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |
| NotExecuted | `FileCat.Remote.Tests.OpenSshIntegrationTests.Files_are_created_exclusively_read_at_offsets_and_replaced_atomically` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |
| NotExecuted | `FileCat.Remote.Tests.OpenSshIntegrationTests.Links_are_renamed_and_deleted_themselves_never_their_targets` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |
| NotExecuted | `FileCat.Remote.Tests.RemoteBenchmark.Transfers_through_jobs_stay_close_to_the_connection_itself` | Set FILECAT_REMOTE_BENCH=1 to measure remote transfers. |
| NotExecuted | `FileCat.Remote.Tests.SshAgentTests.A_real_server_accepts_a_key_that_only_the_agent_holds` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |

### Local-App

- file: `local-run1/App/results.trx`
- start: 2026-09-30T19:35:54.5096588+02:00 finish: 2026-09-30T19:37:05.8311368+02:00
- counts: NotExecuted 4, Passed 154

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.App.Tests.HexEditorPosixTests.A_link_is_refused_and_a_byte_another_program_changed_blocks_the_save` | Windows keeps other writers out instead (HexEditorSmokeTests). |
| NotExecuted | `FileCat.App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` | WebKitGTK is Linux's. |
| NotExecuted | `FileCat.App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.App.Tests.PermissionsDialogTests.Boxes_and_octal_agree_and_only_the_chosen_permissions_change` | POSIX permissions are a Linux and macOS feature. |

