# Omission sentinels and report output — 21 November 2026

This receipt closes the F-EXPORT omission-sentinel expectation natively and maps the rest of F-EXPORT's metadata-only route to its tests.

## Sentinel fixtures

Each fixture places distinct sentinel bytes where a metadata-only report must not look. It then checks that the summary text and the full report text contain none of them. Every Save and copy variant is built from that text: all, source, displayed and selection (see the table below).

**NTFS** — `WindowsHiddenDataTests.Metadata_reports_carry_no_resident_slack_stream_or_EA_value_sentinel`, commit below.

- A small resident main stream holds one sentinel.
- The stale tail of its MFT record holds another: a longer value was written first, then a shorter one.
- A named stream value and an EA value each hold a sentinel.

It ran with the existing `WindowsFileDetailsTests.Summary_and_metadata_full_inspect_one_object_without_legacy_payload_sections` on the Windows 11 x64 VM: 2 of 2 passed ([TRX](receipts/2026-11-21-export-sentinels/ntfs-sentinels-vm.trx) `d5571589…`). The full report reads no MFT record: FileCat's only raw NTFS reader is the `$LogFile` code behind the old inspection report. The [gap fixtures](2026-11-21-details-gap-fixtures.md#deterministic-fixtures) show with counting fakes that neither the summary nor the full tier calls that reader.

**ext4 with inline data** — `UnixFileDetailsNativeTests.Metadata_reports_carry_no_inline_data_or_xattr_value_sentinel_on_ext4`.

- A small file whose bytes live in its inode holds one sentinel, and a `user.` xattr value holds another.
- **Run:** on Ubuntu 24.04.5 x64, against a 64 MiB loop-mounted ext4 image made with `mkfs.ext4 -O inline_data`. A probe file on it showed the inline-data flag (`N` in `lsattr`). The image was unmounted and deleted afterwards.
- **Result:** 1 of 1 passed ([TRX](receipts/2026-11-21-export-sentinels/ext4-inline-ubuntu.trx) `3a443677…`).

## F-EXPORT, metadata-only route

| Expectation | Covered by |
|---|---|
| MetadataOnly, EfsDerived and Unknown content kinds; a consent never authorizes unresolved Unknown | `SupplementaryReportWindowTests.Protected_or_unverified_report_text_cannot_reach_a_picker_or_clipboard`, `SupplementaryReportWindowTests.Canonical_details_refuses_unqualified_full_content_before_display_copy_or_save` |
| Every Save and copy variant | `SupplementaryReportWindowTests.A_tier_switch_or_close_cancels_a_pending_details_copy_before_publication` (all, source, displayed), `SupplementaryReportWindowTests.A_real_text_selection_copy_is_classified_like_every_other_copy_variant` |
| Immutable revision across a blocked picker; refresh, tier switch and close cancel before publication; never approve A and write B | `SupplementaryReportWindowTests.Saving_a_value_report_cancels_a_stale_picker_and_only_creates_new_files`, `SupplementaryReportWindowTests.A_tier_switch_or_close_cancels_a_pending_details_save_before_its_first_write`, and the cancellation fixtures in the [gap fixtures receipt](2026-11-21-details-gap-fixtures.md#deterministic-fixtures) |
| Omission sentinels | the two fixtures above |
| Destination EFS replacement, refusal, pre-write cancellation and an honest partial output | `SupplementaryReportWindowTests.Saving_file_system_details_replaces_a_clear_file_and_preserves_an_Efs_destination`, `SupplementaryReportWindowTests.Saving_a_general_metadata_report_preserves_an_existing_Efs_destination`, `SupplementaryReportWindowTests.Saving_metadata_reports_a_partial_file_when_the_native_volume_fills`, `SupplementaryReportWindowTests.Saving_metadata_on_full_NTFS_reports_the_written_prefix_and_rolls_back_replacement` |
| Retained origin surviving source replacement | This applies to EFS-derived report content. It belongs to the EFS output route (E-OUT15 in the NTFS plan) and stays with the EFS gates. Metadata-only reports carry no derived origin. |
