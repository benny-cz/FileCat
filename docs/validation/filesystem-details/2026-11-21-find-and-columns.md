# Find predicates and optional columns — 21 November 2026

This receipt maps every F-FIND and F-META expectation in the ledger to the tests that cover it. It also records three new tests:

- `SearchTraversalPredicateTests` and `ResultConsumerLifetimeTests.A_live_consumer_stays_on_its_own_search_while_a_later_search_publishes`, in commit `30bfb1e9d`;
- `UnixFileDetailsColumnsNativeTests`, in commit `0d30591ff`.

## F-META

| Expectation | Covered by |
|---|---|
| Get and Compute share eligibility and expiry; a same-size, same-time change | `MetadataRefreshTests.Compute_and_automatic_refresh_share_the_interval_and_effective_last_known_outcome`, `MetadataTests.Refreshed_folder_retires_a_same_size_restored_time_row_claim_only_there`, and natively `WindowsDetailsNativeGapTests.A_new_name_with_the_same_size_and_time_stays_cached_until_expiry_and_Get_and_Compute_then_read_again` |
| Invalidation races | `MetadataRefreshTests.Slow_location_admission_rechecks_a_racing_publication_with_the_effective_interval`, `MetadataRefreshTests.Cancellation_or_retirement_cannot_publish_a_returned_value`, `MetadataDemandTests.A_sidecar_change_cannot_restore_an_inflight_old_verification` |
| Provider restrictions | `FileDetailsColumnTests.Result_analysis_reads_original_devices_and_keeps_unavailable_rows_last` (an archive member stays unavailable), `MetadataRefreshTests.Offline_gates_precede_cached_values_in_every_production_path` |
| Saturation and eviction | `MetadataRefreshTests.Small_cache_eviction_retires_timestamps_as_well_as_values`, `MetadataDemandTests.Rapid_viewport_changes_drop_queued_rows_and_a_healthy_device_remains_available` |
| Analysis uses field applicability and the outcomes actually retained | `FileDetailsColumnTests.Allocated_analysis_includes_directories_and_keeps_pointer_inspection_cached`, `FileDetailsColumnTests.Canceling_result_analysis_retires_publication_while_the_original_read_finishes`, and the native 2,000-file analysis in the [native details receipt](2026-11-21-details-windows-native.md#fixtures) |
| No synthetic result-device dispatch | `FileDetailsColumnTests.Result_analysis_reads_original_devices_and_keeps_unavailable_rows_last`, for results and working sets |
| Immutable keys; unknown values last in both directions; `2+` and an unknown zero never sort as numbers | `FileDetailsColumnTests.Partial_and_unknown_zero_counts_sort_last_in_both_directions_and_never_as_exact_numbers`, `MetadataTests.An_explicit_sort_selector_can_leave_a_displayed_value_unknown` |
| No scheduling or provider access from compare, hover or speech | `MetadataPeekTests.Accessibility_cache_inspection_never_starts_production_and_respects_entry_revision`, `MetadataPeekTests.Gated_peek_reports_an_admitted_read_as_pending_without_starting_another_read`, and the pointer-inspection part of the Allocated analysis test |
| Native meaning | Windows NTFS: `WindowsDetailsNativeGapTests`. Linux ext4 and macOS APFS: `UnixFileDetailsColumnsNativeTests`, below. |

**`UnixFileDetailsColumnsNativeTests`** compares the Allocated and Hard links columns, computed through `MetadataService.Compute` on the real Unix reader, with what the system `stat` command reports for the same object. It covers:

- **Plain, hard-linked and sparse files:** the allocation and link count equal `stat`'s on both systems.
- **Sparse files:** ext4 kept the file sparse (4 KiB allocated of a 16 MiB length). APFS allocated it whole, and the column reports that.
- **Folders:** a Linux folder reports the directory object's own allocation. Darwin returns file attributes only for files, so a folder's allocation stays unsupported on macOS rather than guessed.
- **Folders and symbolic links:** the Hard links column applies to neither.

It passed on Ubuntu 24.04.5 x64 ([TRX](receipts/2026-11-21-find-and-columns/columns-ubuntu.trx)) and on macOS 27.2.1 Apple silicon ([TRX](receipts/2026-11-21-find-and-columns/columns-mac.trx)); both runs include `SearchTraversalPredicateTests`. The portable metadata-service and column tests above run in CI on Windows, Ubuntu and macOS.

## F-FIND

| Expectation | Covered by |
|---|---|
| Fresh, saved and refined active sets and clear masks | `NativeSearchPredicateTests.Admission_checks_both_polarities_platform_scope_and_unknown_saved_bits_without_IO`, `NativeSearchPredicateTests.Saved_criteria_reject_conflicting_set_and_clear_before_search_admission`, `NativeSearchPredicateTests.Refined_mixed_scope_keeps_local_matches_and_counts_members_without_reopening_the_archive` |
| Explicit removal or replacement of an unsupported criterion | the "Remove this criterion" admission case above, `HiddenSearchTests.Saved_broad_or_other_platform_criterion_requires_explicit_replacement_or_clearing` |
| Archive members never match negative native predicates; unavailable scope is visible | `NativeSearchPredicateTests.Discovered_archive_members_are_unexamined_for_set_and_clear_native_masks` |
| Existing extra-data search: stream-only Windows scope, no EA values, explicit replacement of an old criterion, partial or denied inventory left unexamined | `HiddenSearchTests.Search_requires_complete_negative_coverage_keeps_known_positives_and_never_reads_EA_values`, `HiddenSearchTests.Saved_broad_or_other_platform_criterion_requires_explicit_replacement_or_clearing`, and natively the EA-read deny fixture in the [native details receipt](2026-11-21-details-windows-native.md#fixtures) |
| Category-aware download marks | `HiddenInventoryTests` (a Windows EA named `Zone.Identifier` is not a mark, and a Linux name is not a mark on macOS), and the platform mark case in `HiddenSearchTests` |
| Unknown exact tags stay unknown | `NativeSearchPredicateTests.Refinement_keeps_a_captured_reparse_hint_until_the_entry_changes`, and F-STORE's unknown tags after reopening saved references |
| Traversal unchanged | `SearchTraversalPredicateTests`, new. With either polarity of the Hidden predicate, the results equal the unfiltered results filtered by that predicate, including a match inside a hidden folder and one inside an ordinary folder. It passed on Windows x64, Ubuntu x64 and Apple silicon. |
| Live result forwarding never retargets | `ResultConsumerLifetimeTests.A_live_consumer_stays_on_its_own_search_while_a_later_search_publishes`, new. A tab showing one running search keeps that search's set, members and completion while a later search is shown live in its own tab, and no producer is canceled. It passed on Windows x64 and Ubuntu x64. |
| Native compressed and EFS predicates, denied and vanished folders | Windows x64 only, in the [native details receipt](2026-11-21-details-windows-native.md#fixtures), including the hard-link listing lag that the predicate help now states |
