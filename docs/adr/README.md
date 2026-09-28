# Architecture decision records

The plan (§26) summarizes all ADRs. The files here record the decisions that implementation evidence settled.
The IDs and rationale are the plan's.

| ADR | Decision | Status |
|---|---|---|
| [ADR-02](ADR-02-panel-control-and-storage.md) | Custom virtualized list over a paged, spillable record store; external sorted index beyond the shared budget | Decided (TV-01) |
| [ADR-03](ADR-03-native-operations-and-streaming.md) | One policy engine: CopyFile2/MoveFileEx for local files, managed streaming for provider content, IFileOperation for recycle | Decided for v1 |
| [ADR-04](ADR-04-operation-journal.md) | Append-only, CRC-checked journal per job instead of SQLite | Decided |
| [ADR-05](ADR-05-hex-save-modes.md) | P4b protected local baseline and journaled fixed-length in-place save, with Save As and patch export | Decided for P4b; TV-04 external fixtures pending |
| [ADR-06](ADR-06-worker-isolation-shell-host.md) | Shell handlers only in a low-integrity helper inside a job object (no children, memory cap, UI limits), with deadlines, per-item poisoning, and an exclusion policy for shortcut-like types, placeholders, and network drives | Decided for the Shell host (TV-16); other parser workers open until P8 |
| [ADR-07](ADR-07-archive-engines.md) | ZIP creation and updates by staged, verified rebuilds with parent-version checks; nested archives read-only; read-only TAR family, 7z, RAR, xz, bzip2, zstd (SharpCompress), and ISO/UDF (DiscUtils) under the same limits | Decided for ZIP and the approved read-only formats |
| [ADR-10](ADR-10-metadata-scheduling.md) | Cost classes, visible-row demand, explicit analysis for complete ordering | Decided for v1 |
| [ADR-14](ADR-14-privileged-broker.md) | Per-plan administrator broker: one UAC consent per displayed plan, closed verb set, link-refusing handle-relative steps, installed builds only | Decided for P4a; TV-15 VM checks pending |
| [ADR-15](ADR-15-installation-signing-servicing.md) | Per-machine installer plus portable and framework-dependent ZIPs; SignPath signing; notify-only update check | Decided; signing pending |
| [ADR-16](ADR-16-keyboard-model.md) | Agreement-first keymap with recorded additions (no Ctrl+Alt+letter chords) | Decided for v1; TV-10 manual part pending |
| [ADR-17](ADR-17-sftp-engine.md) | SFTP over SSH.NET behind a narrow channel; remote changes only through listing entries (SSH.NET's path operations follow links); own known_hosts seeded by OpenSSH's; temporary-name publishing. Addendum: FTP/FTPS over FluentFTP behind the same channel, pinned certificates, explicit unencrypted FTP | Decided for P6 and P8; TV-12 external checks pending |

The other ADRs (01, 08, 09, 11–13) stay open until their phases produce evidence.
