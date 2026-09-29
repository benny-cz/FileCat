# Architecture decision records

The plan (§26) summarizes all ADRs. The files here record the decisions that implementation evidence settled.
The IDs and rationale are the plan's.

| ADR | Decision | Status |
|---|---|---|
| [ADR-01](ADR-01-resources-and-capabilities.md) | Locations with containers and sessions, exact-name identity with ordinals, typed entries, per-location capabilities with explanations, jobs routed by operation pair | Decided (ten providers, all TV-02 cases) |
| [ADR-02](ADR-02-panel-control-and-storage.md) | Custom virtualized list over a paged, spillable record store; external sorted index beyond the shared budget | Decided (TV-01) |
| [ADR-03](ADR-03-native-operations-and-streaming.md) | One policy engine: CopyFile2/MoveFileEx for local files, managed streaming for provider content, IFileOperation for recycle | Decided for v1 |
| [ADR-04](ADR-04-operation-journal.md) | Append-only, CRC-checked journal per job instead of SQLite | Decided |
| [ADR-05](ADR-05-hex-save-modes.md) | P4b protected local baseline and journaled fixed-length in-place save, with Save As and patch export | Decided for P4b; TV-04 external fixtures pending |
| [ADR-06](ADR-06-worker-isolation-shell-host.md) | Shell handlers only in a low-integrity helper inside a job object (no children, memory cap, UI limits), with deadlines, per-item poisoning, and an exclusion policy for shortcut-like types, placeholders, and network drives; no native parsers were adopted, so it is the only worker | Decided (TV-16) |
| [ADR-07](ADR-07-archive-engines.md) | ZIP creation and updates by staged, verified rebuilds with parent-version checks; nested archives read-only; read-only TAR family, 7z, RAR, xz, bzip2, zstd (SharpCompress), and ISO/UDF (DiscUtils) under the same limits | Decided for ZIP and the approved read-only formats |
| [ADR-08](ADR-08-recovery-engine.md) | FileCat's own read-only NTFS, FAT, and exFAT engines (MIT); evidence-based states with declared lost bytes; managed, fuzzed parsing with user rights; drives through a read-only session of the ADR-14 helper, recovered only to another disk | Decided; the elevated read on real drives is a manual check |
| [ADR-09](ADR-09-registry-representation.md) | Typed Registry panels with explicit views, routed alias writes, links never traversed, expected-value guards, previewed `.reg` interchange | Decided for P4a; TV-05 VM checks pending |
| [ADR-10](ADR-10-metadata-scheduling.md) | Cost classes, visible-row demand, explicit analysis for complete ordering | Decided for v1 |
| [ADR-11](ADR-11-workload-adaptive-comparison.md) | Exact streamed binary comparison; budgeted anchored text alignment with labelled unaligned regions; recursive comparison into result sets and one-way sync | Decided for P7 |
| [ADR-12](ADR-12-platform-adapters-and-packaging.md) | Portable Core, Windows adapter, portable Linux/macOS platform; installer, ZIPs, `.tar.gz`, `.deb`, AppImage, and `.app`, each started in CI | Decided; ARM64 later |
| [ADR-13](ADR-13-configuration-and-session-storage.md) | Versioned JSON with last-known-good copies, a separate job journal, OS secret stores by reference, one profile owner | Decided |
| [ADR-14](ADR-14-privileged-broker.md) | Per-plan administrator broker: one UAC consent per displayed plan, closed verb set, link-refusing handle-relative steps, installed builds only | Decided for P4a; TV-15 VM checks pending |
| [ADR-15](ADR-15-installation-signing-servicing.md) | Per-machine installer plus portable and framework-dependent ZIPs; SignPath signing; notify-only update check | Decided; signing pending |
| [ADR-16](ADR-16-keyboard-model.md) | Agreement-first keymap with recorded additions (no Ctrl+Alt+letter chords) | Decided for v1; TV-10 manual part pending |
| [ADR-17](ADR-17-sftp-engine.md) | SFTP over SSH.NET behind a narrow channel; remote changes only through listing entries (SSH.NET's path operations follow links); own known_hosts seeded by OpenSSH's; temporary-name publishing. Addendum: FTP/FTPS over FluentFTP behind the same channel, pinned certificates, explicit unencrypted FTP | Decided for P6 and P8; TV-12 external checks pending |
| [ADR-18](ADR-18-workspace-layout.md) | Panels in a normalized split tree: docked beside, above, or below another panel or swapped, by drag-and-drop or keyboard; targets follow identity, not position; persisted with the workspace | Decided for P11 (D-41) |

All plan ADRs are decided; external validations (TV-04, TV-05, TV-09 on real drives, TV-10, TV-12, TV-13, TV-15) remain release gates.
