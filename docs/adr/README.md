# Architecture decision records

The plan (§26) summarizes all ADRs. The files here record the decisions that implementation evidence settled.
The IDs and rationale are the plan's.

| ADR | Decision | Status |
|---|---|---|
| [ADR-02](ADR-02-panel-control-and-storage.md) | Custom virtualized list over a paged, spillable record store; external sorted index beyond the shared budget | Decided (TV-01) |
| [ADR-03](ADR-03-native-operations-and-streaming.md) | One policy engine: CopyFile2/MoveFileEx for local files, managed streaming for provider content, IFileOperation for recycle | Decided for v1 |
| [ADR-04](ADR-04-operation-journal.md) | Append-only, CRC-checked journal per job instead of SQLite | Decided |
| [ADR-10](ADR-10-metadata-scheduling.md) | Cost classes, visible-row demand, explicit analysis for complete ordering | Decided for v1 |
| [ADR-15](ADR-15-installation-signing-servicing.md) | Per-machine installer plus portable and framework-dependent ZIPs; SignPath signing; notify-only update check | Decided; signing pending |
| [ADR-16](ADR-16-keyboard-model.md) | Agreement-first keymap with recorded additions (no Ctrl+Alt+letter chords) | Decided for v1; TV-10 manual part pending |

The other ADRs (01, 05–09, 11–14) stay open until their phases produce evidence.
