# E-A01 — CI run 36722039034: per-lane outcomes and skip inventory

- Source: `4f6b062fa8548fc8fd417a50262a72c0b804461f` (main), workflow `CI` (.github/workflows/ci.yml at that commit), event push, 2026-09-30T13:29:46Z–13:36:49Z, conclusion success.
- Run: https://github.com/benny-cz/FileCat/actions/runs/36722039034 — jobs: Windows 109909331631, Windows ARM64 109909331877, macOS 109909331999, Ubuntu 109909332456 (all success); the three package jobs were **skipped** (not a tag or manual run).
- Inputs: TRX artifacts downloaded 2026-09-30 with `gh run download` and parsed by a TRX summarizer (outcome counts; message of every non-passed result). Only lanes that upload TRX are covered below: Windows x64 (all four suites) and the App suites of Ubuntu and macOS. The Core/Remote counts of the portable lanes and the ARM64 lane come from their job logs only (plan §6.1).
- SHA-256 of the parsed TRX files (as downloaded):
  - Windows Core `21c5e16c23f484f56140eabddf3cedd62ff85748e292d9ae3ef716a0ad772d1d`
  - Windows Platform `e7f861cbcc95d5500829755a80510bd5ea34b864b5fcf965749d2febd54d49d6`
  - Windows Remote `91a082618f1ee0828b95e88b2dabe3c3533c19201db38d317030e1c8f41fc463`
  - Windows App `4f5daa141a8d59bd5a40a584bfc9bbcce390454bd854b0f241a2c61605c18507`
  - Ubuntu App `c54fe232674f450947e27f2f7f79bbb528e3e3091045b14ceb335cee6b3fbcfc`
  - macOS App `eb48498978683ff224783e659754bc828131574e7cb27ff4075dbef3b6431f45`
- Limitation: `NotExecuted` is an explicit xUnit skip. A test that returns early without asserting is reported **Passed**; those are inventoried separately (E-A03), not here.

### W64-Core

- file: `test-results-windows/tests/FileCat.Core.Tests/TestResults/results.trx`
- start: 2026-09-30T13:32:15.7510493+00:00 finish: 2026-09-30T13:33:53.4478610+00:00
- counts: NotExecuted 33, Passed 499

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
| NotExecuted | `FileCat.Core.Tests.VerificationTests.A_real_OpenPGP_signature_is_checked_with_the_systems_gpg` | No gpg here. |

### W64-Windows

- file: `test-results-windows/tests/FileCat.Platform.Windows.Tests/TestResults/results.trx`
- start: 2026-09-30T13:32:15.7510492+00:00 finish: 2026-09-30T13:34:06.4707925+00:00
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

### W64-Remote

- file: `test-results-windows/tests/FileCat.Remote.Tests/TestResults/results.trx`
- start: 2026-09-30T13:32:15.7510492+00:00 finish: 2026-09-30T13:32:52.3257364+00:00
- counts: NotExecuted 5, Passed 38

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.Remote.Tests.OpenSshIntegrationTests.A_wrong_host_key_stops_the_connection` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |
| NotExecuted | `FileCat.Remote.Tests.OpenSshIntegrationTests.Files_are_created_exclusively_read_at_offsets_and_replaced_atomically` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |
| NotExecuted | `FileCat.Remote.Tests.OpenSshIntegrationTests.Links_are_renamed_and_deleted_themselves_never_their_targets` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |
| NotExecuted | `FileCat.Remote.Tests.RemoteBenchmark.Transfers_through_jobs_stay_close_to_the_connection_itself` | Set FILECAT_REMOTE_BENCH=1 to measure remote transfers. |
| NotExecuted | `FileCat.Remote.Tests.SshAgentTests.A_real_server_accepts_a_key_that_only_the_agent_holds` | No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD). |

### W64-App

- file: `test-results-windows/tests/FileCat.App.Tests/TestResults/results.trx`
- start: 2026-09-30T13:32:15.7510493+00:00 finish: 2026-09-30T13:34:17.3935288+00:00
- counts: NotExecuted 4, Passed 154

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.App.Tests.HexEditorPosixTests.A_link_is_refused_and_a_byte_another_program_changed_blocks_the_save` | Windows keeps other writers out instead (HexEditorSmokeTests). |
| NotExecuted | `FileCat.App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` | WebKitGTK is Linux's. |
| NotExecuted | `FileCat.App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.App.Tests.PermissionsDialogTests.Boxes_and_octal_agree_and_only_the_chosen_permissions_change` | POSIX permissions are a Linux and macOS feature. |

### Ubuntu-App

- file: `app-test-results-ubuntu-latest/tests/FileCat.App.Tests/TestResults/app-results.trx`
- start: 2026-09-30T13:32:09.5108878+00:00 finish: 2026-09-30T13:32:58.8932149+00:00
- counts: NotExecuted 9, Passed 149

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.App.Tests.ExitWithWaitingJobTests.A_delete_that_meets_a_locked_file_asks_and_closing_then_asks_about_it` | Only Windows keeps an open file from being deleted. |
| NotExecuted | `FileCat.App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` | No X display. |
| NotExecuted | `FileCat.App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.App.Tests.NetworkPlaceTests.The_Network_lists_known_servers_then_their_shares_and_leads_back` | The Windows network first; Linux and macOS follow. |
| NotExecuted | `FileCat.App.Tests.NetworkPlaceTests.With_nothing_known_or_answering_the_Network_says_how_to_reach_a_server` | The Windows network first; Linux and macOS follow. |
| NotExecuted | `FileCat.App.Tests.PanelKeysTests.A_drive_letter_opens_the_drive_at_once_at_the_folder_another_panel_shows_there` | Drive letters are Windows'. |
| NotExecuted | `FileCat.App.Tests.PanelKeysTests.Alt_F1_then_a_drive_letter_opens_that_drive_as_the_keyboard_sends_them` | Drive letters are Windows'. |
| NotExecuted | `FileCat.App.Tests.ShellPictureUiTests.Quick_view_shows_the_Shells_thumbnail_of_a_picture_through_the_restricted_helper` | Shell thumbnails exist only on Windows. |
| NotExecuted | `FileCat.App.Tests.WindowsContextMenuTests.Files_in_one_folder_use_the_shell_even_when_access_checks_would_fail` | Windows Shell menus are Windows-only. |

### macOS-App

- file: `app-test-results-macos-latest/tests/FileCat.App.Tests/TestResults/app-results.trx`
- start: 2026-09-30T13:32:07.7199110+00:00 finish: 2026-09-30T13:33:47.0681740+00:00
- counts: NotExecuted 9, Passed 149

| Outcome | Test | Message |
|---|---|---|
| NotExecuted | `FileCat.App.Tests.ExitWithWaitingJobTests.A_delete_that_meets_a_locked_file_asks_and_closing_then_asks_about_it` | Only Windows keeps an open file from being deleted. |
| NotExecuted | `FileCat.App.Tests.LinuxPageEngineTests.WebKitGTK_draws_the_page_with_its_scripts_off_and_refuses_the_web` | WebKitGTK is Linux's. |
| NotExecuted | `FileCat.App.Tests.LiveDriveScanTests.The_drive_of_the_current_folder_is_scanned_in_a_new_tab` | Set FILECAT_RECOVERY_LIVE (a drive such as G:) and FILECAT_RECOVERY_LIVE_SERIAL (its disk's serial number). |
| NotExecuted | `FileCat.App.Tests.NetworkPlaceTests.The_Network_lists_known_servers_then_their_shares_and_leads_back` | The Windows network first; Linux and macOS follow. |
| NotExecuted | `FileCat.App.Tests.NetworkPlaceTests.With_nothing_known_or_answering_the_Network_says_how_to_reach_a_server` | The Windows network first; Linux and macOS follow. |
| NotExecuted | `FileCat.App.Tests.PanelKeysTests.A_drive_letter_opens_the_drive_at_once_at_the_folder_another_panel_shows_there` | Drive letters are Windows'. |
| NotExecuted | `FileCat.App.Tests.PanelKeysTests.Alt_F1_then_a_drive_letter_opens_that_drive_as_the_keyboard_sends_them` | Drive letters are Windows'. |
| NotExecuted | `FileCat.App.Tests.ShellPictureUiTests.Quick_view_shows_the_Shells_thumbnail_of_a_picture_through_the_restricted_helper` | Shell thumbnails exist only on Windows. |
| NotExecuted | `FileCat.App.Tests.WindowsContextMenuTests.Files_in_one_folder_use_the_shell_even_when_access_checks_would_fail` | Windows Shell menus are Windows-only. |

