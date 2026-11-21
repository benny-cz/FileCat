# Parser bounds and link operation policy — 21 November 2026

This receipt maps the F-PARSER and F-OPS expectations in the ledger to the tests that cover them. No code changed. All the tests named here are deterministic, except the Windows link fixtures and the live macOS File Provider fixture. The portable ones run in CI on Windows, Ubuntu and macOS.

## F-PARSER

| Expectation | Covered by |
|---|---|
| Bad offsets and odd UTF-16 lengths | `ReparsePresentationTests.Odd_or_out_of_range_name_offsets_inside_a_full_length_buffer_are_refused`, `ReparsePresentationTests.Stored_names_and_relative_flag_remain_separate_and_malformed_data_is_refused` |
| Truncated GUID, header or version | `ReparsePresentationTests.Opaque_vendor_data_and_unsupported_versions_remain_evidence_without_executable_targets`. Every truncation of a vendor GUID header is Malformed; unsupported WOF and app-alias versions stay Unsupported. Also `ReparsePresentationTests.Full_reparse_buffer_uses_declared_payload_boundary_for_typed_report_text`. |
| Differing print and substitute names; no display-text navigation | `ReparsePresentationTests.Stored_names_and_relative_flag_remain_separate_and_malformed_data_is_refused`, and natively the forged print name case in the [Windows native details receipt](2026-11-21-details-windows-native.md#fixtures) |
| No unbounded allocation | `ReparsePresentationTests.Reparse_payloads_above_the_sixteen_KiB_cap_are_refused_without_retaining_their_bytes` |
| macOS masks with missing or partial 32-bit, 64-bit and time slots; known zero with actual bytes | the macOS packed-result cases in the [gap fixtures receipt](2026-11-21-details-gap-fixtures.md#deterministic-fixtures) |
| decmpfs truncation, unknown methods and unsigned overflow | `HiddenInventoryTests.Decmpfs_header_shorter_than_sixteen_bytes_never_declares_a_method_or_size`, `HiddenInventoryTests.Decmpfs_never_infers_active_state_or_guesses_an_unknown_or_overflowed_size` |
| decmpfs with the compression flag clear or unknown | The header is described as a claim only ("A decoded header does not establish active compression"), never as active compression, whatever the flag says. The decmpfs tests above assert that no "Compressed by" text appears. |
| Dataless context | Files: `UnixFileDetailsNativeTests.Live_Mac_File_Provider_metadata_keeps_prepared_files_dataless` ([live Mac provider run](2026-11-20-macos-held-directory-xattrs.md)). Folders and packages: **open**. They need a live File Provider domain that leaves a folder and a package dataless. |

## F-OPS

| Consumer | Covered by |
|---|---|
| Recursion, compare, sync, sizing, export, transfer admission and Find | `ReparseOperationPolicyTests`: one Link-flagged and raw-reparse corpus, with each decision following the Link flag alone ([gap fixtures receipt](2026-11-21-details-gap-fixtures.md#deterministic-fixtures)) |
| Native delete (Windows) | `LinkReparseTests.A_junction_inside_a_deleted_folder_is_removed_as_a_link_and_its_target_kept`, `LinkReparseTests.A_junction_below_the_chosen_folder_is_refused_with_or_without_elevation`, `LinkReparseTests.A_folder_reached_through_a_junction_is_held_and_deleted_without_elevation_and_left_alone_with_it`, `BoundDeleteTreeTests.Permanent_delete_does_not_follow_a_child_swapped_to_an_outside_directory_link` |
| Native move (Windows) | `AliasSafetyTests.A_move_onto_itself_through_a_junction_keeps_the_file`, `AliasSafetyTests.A_folder_is_not_moved_into_itself_through_a_junction` |
| Edit | `HexEditingTests` (Windows): `Protected_open_refuses_empty_read_only_shared_and_linked_files_with_reasons`, `Patch_import_checks_the_opened_file_size_before_reading_a_link_target`. `NativeEditStorageTests`: `A_native_root_whose_folder_cannot_be_held_without_following_links_keeps_the_legacy_store`, `On_Unix_a_state_folder_reached_through_a_symbolic_link_keeps_the_legacy_store_and_its_real_path_does_not` |
| Presentation causes no policy expansion | The corpus test holds the Link flag fixed while the presentation (letters, `<LINK>`, subtypes) changes, and no consumer's decision changes |
