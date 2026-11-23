# Target scope and extra-data coverage — 21 November 2026

This receipt maps the F-SCOPE and F-EXTRA expectations in the ledger to the tests that cover them. It adds one deterministic Windows test: `FileDetailsVolumeTargetTests`, nine cases, which passed on Windows x64.

## F-SCOPE

| Expectation | Covered by |
|---|---|
| Network, missing, device and WSL targets; a forged print name; a changed payload; a link under a redirected parent; exact scope text and only explicit qualified navigation | the 11 native cases in the [Windows native details receipt](2026-11-21-details-windows-native.md#fixtures); `FileDetailsTests.Target_navigation_only_accepts_qualified_filesystem_names` and `FileDetailsTests.Windows_target_navigation_requires_a_confirmed_ordinary_entry`; the [native Unix link-target fixtures](2026-11-18-unix-link-target-details.md) on Ubuntu x64 and Apple silicon |
| Volume GUID targets | `FileDetailsVolumeTargetTests`, new. `\??\Volume{GUID}\…`, its `\\?\` spelling and a mount point to a volume root resolve to local `\\?\Volume{GUID}\` paths. A malformed GUID, a missing brace or separator, a stream suffix and a `GLOBALROOT` spelling are refused. |
| Replacing the path between the summary and full reads, including the same size and time and a recycled-ID fake | the [replacement fixture](2026-11-17-foundation.md#a-replacement-file-at-the-observed-path) on Windows x64, Apple silicon and Ubuntu x64, with stale-publication cancellation under a reused fake identity |
| Retained contributor origin | the value-report origin in the [selection identity checkpoint](2026-11-20-supplementary-selection-identity.md) |
| Absent adapters leave optional mutation links unavailable; the compression link needs I-DETAILS-C; per-adapter permission and attribute cases | Not applicable to the current product. The details report offers no mutation link, only "Open streams and attributes", "Open target location" and the target copy action (`ReportWindow.Details.cs`). The compression link stays unbuilt until I-DETAILS-C. |

CI's hosted Windows ARM64 App jobs run the same native Open-target fixtures. Their logs show no failure or skip for that class.

## F-EXTRA

| Expectation | Covered by |
|---|---|
| Same-name stream and EA with different bytes; reversed or split refresh order; marks, focus, restored selection and a disappeared category; F3, F5 and F8 keep the exact kind and name | `HiddenInventoryRefreshTests.Loading_restart_keeps_full_category_identity_through_split_reversed_arrival`, `HiddenInventoryTests.Refresh_retains_both_same_name_marks_and_exact_focus_and_drops_a_missing_category`, `HiddenInventoryTests.Listing_reads_no_values_and_actions_keep_fixed_category_identity_without_combined_lookup`, `HiddenDataUiTests.Delete_confirmation_keeps_the_source_identity_from_before_a_refresh`, and the native same-name cases in `WindowsHiddenDataTests` |
| Stream lookup issues no EA query | the EA-read deny fixture and counting wrapper in the [Windows native details receipt](2026-11-21-details-windows-native.md#fixtures) |
| Folder and empty streams; a stream named `$DATA` | `WindowsHiddenDataTests`, including the new empty-stream case |
| Required-EA flag with an unknown name; managed WOF, decmpfs and fork deletion refused after current resolution | the existing native required-EA and WOF-backing cases in `WindowsHiddenDataTests`, `HiddenInventoryTests.Managed_roles_and_download_mark_exceptions_use_the_typed_category`, and the Btrfs generic-deletion refusal on Linux. On macOS, values and deletion run only through a held directory: a regular file, including a compressed or dataless one, is refused before any read (`UnixHiddenData.Inventory.cs`). |
| Native names round-trip or refuse; growth, denial and terminal errors stay typed | the round-trip, growth and deny cases in `WindowsHiddenDataTests`, `HiddenInventoryTests.Names_are_bounded_exact_and_do_not_replace_malformed_native_bytes`, `HiddenInventoryTests.Growing_Unix_attribute_names_report_partial_after_two_retries_without_reading_lengths`, `HiddenInventoryTests.Shrinking_Unix_attribute_names_never_publish_an_exact_complete_count` |
| Darwin ordinary and compression-visible views: honest zero and completeness, zero value reads during inventory | `UnixHiddenInventoryNativeTests.Mac_directory_inventory_and_value_report_bind_to_a_held_directory`, `HiddenDataTests.A_real_Darwin_compressed_file_discloses_that_the_ordinary_view_omits_backing`, `HiddenInventoryTests.Mac_attribute_length_that_requires_materialization_refuses_the_whole_inventory` |

The inventory's 1 MiB buffer step is barely reachable on NTFS: NTFS refused a 606th stream with a 200-character name. Growth is tested from 64 KiB to 256 KiB and ends typed at the entry limit.
