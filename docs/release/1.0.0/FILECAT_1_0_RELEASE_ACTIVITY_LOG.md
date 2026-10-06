# FileCat 1.0.0 — activity log

Append one completed slice at the end. The [dashboard](FILECAT_1_0_RELEASE_EXECUTION_REPORT.md) is current state; [frozen execution history](FILECAT_1_0_RELEASE_EXECUTION_HISTORY_20261006.md) preserves every earlier work-log entry.
Do not append current-state summaries to the issue, blocker or evidence registers.

## 2026-10-06 — native QuickView ownership slice

- Exact product source: 6215329; documentation-only slice commits 0b2cfd3, 50d65b0 and 2a018f3.
- Evidence: [E-I06-B1](evidence/E-I06-B1-retained-picture-memory.md) and [E-I06-B2](evidence/E-I06-B2-quickview-picture-memory.md).
- F3: six successful host/VM cases, 27 viewers; eight large images retain 512 MiB including while hidden, then release all pixel/page charges on close.
- QuickView: unchanged C# observer passes host/Windows VM/physical Mac; 72 decoded pictures/96 readers, one-MiB per-run plateau, immediate old-buffer disposal and zero final pixel/page/admission charges.
- Original compile/binding/key-delivery/observer warnings/failures and unavailable Mac private-memory counter remain explicit. No product bytes, persistent settings or physical source change.
- Audit v21 independently reconciles 223 selected private evidence pins. SHA-256: 7cd87b5defd635b6612439f6e9549d5a0e9b7aba8cf6a09bbd29189b014655ed.
- I06 and broader scopes remain open; no candidate/stable GO. Two GitHub HTTPS retries failed after local native-slice commit. SSH on port 443 subsequently pushed 2a018f3; independent GitHub API read confirmed exact server main. No persistent Git/SSH configuration changed.

## 2026-10-06 — tracking-document consolidation

Owner requested compact, current progress and remaining-work documents before the next test slice. Four canonical tracking files now have distinct roles; complete original Git-canonical bodies are retained in four clearly labelled, linked frozen histories in the same directory, preserving relative links.

Current dashboard lists all 21 unresolved issue statuses, all 24 campaign gaps, next executable slices and updated operational-plan steps. The issue register retains all 162 IDs and original title/severity/disposition fields with unchanged status classes; the index retains all 170 evidence IDs/source/class/record/issue mappings. No release gate or qualification is waived.

Independent preservation proof is retained privately under FileCatReleaseEvidence/document-consolidation-20261006-v1. `independent-document-preservation-v2.json` SHA-256: 6721504ba34e8c7e47f277c4dc2c6943474810a408dcb87401a7f98a676edc24. It verifies the four original bodies byte for byte, unchanged authority, all IDs/mappings/status classes, 113 legacy issue anchors, 162 direct issue anchors and 380 current local links/fragments. Active register size fell from 728,467 to 142,332 bytes (about 80%); complete original detail remains in the frozen histories.

Audit v22 independently rechecks 223 selected evidence hashes plus the retained F3/QuickView files and deployed/compiled private binaries. `independent-records-v22.json` SHA-256: 7cd87b5defd635b6612439f6e9549d5a0e9b7aba8cf6a09bbd29189b014655ed (same verified content as v21). Next test slice: native Linux QuickView.

Read-only GitHub policy snapshot at 2026-10-06 19:55 UTC confirms private reporting disabled, zero rulesets, main unprotected and Actions enabled with all actions allowed. Immutable-release policy was not queried. No settings changed or owner decisions waived. Private `github-policy-readonly-v1.json` SHA-256: 111b81b8e91a69580c6f3c08138547a58ca913cb9193060814d7ab3acd039208.
