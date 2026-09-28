# ADR-01: Heterogeneous resources and capabilities

**Status:** Decided (2026-09-28), as the recommended direction, by ten working providers.

## Decision

A small navigation and identity model, typed payloads, and qualified capabilities, not a file-system VFS:

- **Location** is a scheme, a raw provider path (never case-folded), an optional container (the ZIP file of an archive
  folder, the image of a recovery view), and an optional session (a Registry view, a connection, a volume).
- **ItemRef** is an item's identity: its container, its kind, its exact raw name, and an ordinal for items that share a
  name (duplicate archive entries, deleted files). Size and time travel as revision evidence, outside equality.
- **ResourceProvider** lists (`EnumerateAsync` into a sink), navigates (`GetChildLocation`, `GetParent`), opens content
  (`OpenContent`, random access), and states capabilities per location (`GetCapabilities`). Every capability a
  location lacks comes with `ExplainUnavailable`, which the UI shows instead of a dead command. A source may also refuse
  a destination before a copy starts (`CheckTransferDestination`).
- **Typed entries** instead of synthetic files: Registry keys and values are entry kinds with typed data; provider
  payloads reach the Kind and Details columns through `IDisplayDetails`.
- **Jobs route by operation pair.** A job's kind, its sources' schemes, and its destination pick an executor; modules
  (archives, remote, MTP, Registry) register for their pairs. An unsupported pair is refused with a sentence, never
  converted silently.

## Evidence

TV-02's seven cases all run as providers: local file systems (NTFS, ReFS, FAT, exFAT, SMB), ZIP and the read-only archive
formats, the Registry, SFTP and FTP, result sets and working sets, phones and cameras (MTP), and recovery (images and
drives). None of them needed a stream or directory operation it cannot honor: the Registry has no content stream,
recovery and archives have no mutation, and working sets hold references only. Identity survives sorting, filtering,
renames, and refresh (ADR-02 store, PI-10 tests).

## Consequences

- There is no generic copy for every node pair; each pair is a deliberate executor.
- Capability combinations stay per location, so a read-only archive inside a writable ZIP is expressible.
