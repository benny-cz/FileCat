# Observation, lifecycle, details UI and type coverage — 21 November 2026

This receipt maps the F-OBS, F-LIFE, F-UI and F-TYPE expectations in the ledger to the tests that cover them, and adds one macOS test.

## New test

`UnixFileDetailsNativeTests.Mac_summary_of_a_large_folder_is_a_minimal_request_without_an_entry_count` reads the summary of a folder holding 347 entries. The summary:

- describes the folder object itself;
- carries the reader's note that the summary request excludes the directory entry count, fork values and APFS sharing statistics;
- shows no entry count.

The request itself asks for no directory attributes at all (`UnixFileRecords.ReadMacDetailsFields`). It passed on macOS 27.2.1 Apple silicon ([TRX](receipts/2026-11-21-observation-lifecycle-ui/obs-mac.trx)).

## F-OBS

| Expectation | Covered by |
|---|---|
| Linux statx masks | `Statx_masks_allocation_units_kinds_and_attribute_support_are_independent`, `Unreturned_statx_slots_cannot_report_overflow`, `Unsupported_or_unreturned_fields_never_become_known_zero_or_off` |
| Qualified same-object acquisition; stat→FIFO or device replacement | `Linux_FIEMAP_reopens_the_pinned_inode_even_after_its_path_becomes_a_FIFO`, `Linux_summary_of_FIFO_does_not_open_it_for_data_or_wait_for_a_writer`, `Unix_details_describe_a_character_device_from_metadata_without_opening_it` |
| macOS mask-plus-byte validity | `Mac_returned_masks_also_require_complete_packed_slots`, `Mac_64_bit_slot_cut_mid_slot_is_failed_and_never_reads_a_partial_value`, `Mac_result_cut_inside_a_time_slot_keeps_type_and_fails_every_later_scalar`, `Mac_known_zero_needs_both_the_returned_bit_and_the_whole_64_bit_slot` |
| macOS minimal summary request, without the entry count | the new test above |
| Denied, deleted and changed objects | `Unix_details_refuse_a_missing_or_denied_subject_with_a_typed_error_and_no_snapshot`, `WindowsFileDetailsTests.Native_details_refuse_a_missing_or_denied_subject_with_a_typed_error_and_no_snapshot`, and the replacement fixture in the [foundation receipt](2026-11-17-foundation.md#a-replacement-file-at-the-observed-path) |
| Stream, fork and directory scopes | The native tests assert each fact's scope: main data on Windows, all forks on macOS, and the directory object or directory policy for folders. |
| ReadSummary never calls the legacy reader, values or the inventory, including after F5; ReadFull gets its own same-context summary; omitted sections acquire nothing | the counting fakes in the [gap fixtures receipt](2026-11-21-details-gap-fixtures.md#deterministic-fixtures), `Native_summary_describes_files_directories_and_links_without_legacy_value_reads`, `WindowsFileDetailsTests.Summary_and_metadata_full_inspect_one_object_without_legacy_payload_sections` |
| Cached inventory keeps its own time, scope and stale qualification | The details reader and window never read the inventory. The F5 tab shows the inventory's own observation time (`TabViewModel.Supplementary.cs`), separate from the details window's observation time. |
| NoFileRecords and Unix Properties dispatch are explicit | `NoFileRecords_refuses_every_tier_before_touching_the_path`, and the fallbacks in the gap fixtures |

## F-LIFE

| Expectation | Covered by |
|---|---|
| Refresh storms; blocked and canceled calls | the refresh-storm fixture in the gap fixtures, `FileDetailsOwnershipTests.Cancellation_retains_the_native_permit_and_refreshes_coalesce_to_one_latest_request`, `FileDetailsOwnershipTests.Caller_cancellation_retires_publication_while_the_started_read_keeps_its_permit` |
| Close and shutdown | `FileDetailsOwnershipTests.Disposing_the_reader_during_a_held_read_retires_publication_once_and_never_starts_its_pending_request`, `FileDetailsOwnershipTests.Scheduler_shutdown_retires_a_running_observation_only_after_the_handle_closes` |
| Eight process-wide and four per-device requests; atomic admission; no nested worker wait | `FileDetailsOwnershipTests.Device_and_process_admission_are_reserved_together_and_leases_are_idempotent`, `FileDetailsOwnershipTests.Device_case_variants_share_the_same_four_observation_permits`, `FileDetailsOwnershipTests.Nested_observation_accepts_the_admitted_devices_case_variant` |
| A blocked queue, repeated cancel before entry, rejection after scheduler shutdown and an injected start failure: no native call and no leaked permit | `FileDetailsOwnershipTests.Repeated_queued_cancellation_and_scheduler_shutdown_return_every_observation_permit`, `FileDetailsOwnershipTests.A_worker_start_failure_releases_admission_and_the_failed_read_never_reaches_a_later_worker`, `FileDetailsOwnershipTests.Unsupported_records_fail_inside_the_owned_read_and_return_the_permit` |
| Cancel between retained EFS user and recovery queries | the [EFS record cancellation receipt](2026-11-21-efs-record-cancellation.md) |
| F5 and F6 use the same outer permit owner | Both call `FileObservationRequests.RunOwned`. F5 does so in `MainViewModel.HiddenData.cs`; F6 in `MetadataService` for observation-lane fields and in `TabViewModel.Analysis`. |

## F-UI

| Expectation | Covered by |
|---|---|
| Windows Properties and the Unix fallback; summary and full loading and focus; marks ignored; cached inspection | the focus, menu and fallback fixtures in the gap fixtures, `FileDetailsWindowTests.Tier_switch_cancels_stale_publication_pairs_full_with_its_summary_and_retains_failed_refresh`, and the native Properties shortcut in the [Windows details NVDA receipt](2026-11-18-windows-details-nvda.md) |
| Find, context menu and shortcut keep results and open supplementary data in the main workspace; unsupported-provider reasons with no I/O | the Find and panel routes and accessible reasons in the gap fixtures |
| Toolbar, spoken controls, status and actions at 320 DIP, with large text and High Contrast | the headless 320 DIP fixture and the combined NVDA run with 150% text and High Contrast ([narrow controls receipt](2026-11-20-details-narrow-controls.md)); 150% text and display scale in the [Windows details NVDA receipt](2026-11-18-windows-details-nvda.md) |
| F3 works without F5 and discloses old previews; an EA-only, zero-row F5 tab keeps its inventory action and honest text | the "Not acquired:" omission text in `WindowsFileDetailsTests`, the summary-only fallbacks, `HiddenInventoryUiTests.Empty_inventory_exposes_keyboard_EA_action_and_acquires_values_only_after_its_scoped_review` |

The native large-text and High Contrast checks ran on Windows x64. The Linux and macOS screen-reader checks are tracked in N-UX.

## F-TYPE

| Expectation | Covered by |
|---|---|
| Full tag corpus: unknown surrogate and non-surrogate tags, cloud, WOF, app alias, WSL, socket; a mount tag alone stays ambiguous | `ReparsePresentationTests.Taxonomy_preserves_tag_and_does_not_use_surrogate_bit_as_link_policy`, `ReparsePresentationTests.Wsl_special_file_tags_are_special_objects_never_links`, `ReparsePresentationTests.Every_cloud_subtag_is_cloud_managed_and_lookalike_tags_are_not`, and the F-TYPE cases in the gap fixtures |
| One classifier across text, icons, pictures and speech; operational flags unchanged | the label and shortcut-arrow fixtures in the gap fixtures; F-OPS |
| Unix and provider details unaffected | the Unix native details tests above |
| Pin and unpin intent, partial recall, no recall evidence and queried-EA provenance invent no residency | `WindowsFileRecordsTests.A_cloud_placeholder_reads_its_state_without_being_downloaded`, in the provider's own process only. **Open:** FileCat sees other providers' placeholders disguised as ordinary files ([native listing receipt](2026-11-21-windows-native-listing-x64.md#finding-cloud-files-placeholders-are-disguised-for-filecat)), so the Cloud Files view in FileCat's actual mode is unqualified. |
