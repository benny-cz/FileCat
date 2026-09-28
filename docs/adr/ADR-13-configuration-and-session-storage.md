# ADR-13: Configuration and private session storage

**Status:** Decided (2026-09-28).

## Decision

Separate stores for separate obligations:

- **Preferences, history, workspaces, and working sets** are versioned JSON, written by atomic replacement with a
  last-known-good copy. A corrupt file is kept aside instead of overwritten, and a file from a newer FileCat opens
  read-only so an older one never downgrades it.
- **Jobs and edit sessions** have their own durable records: the append-only, CRC-checked journal (ADR-04) and edit
  session manifests. A corrupt preference cannot destroy recovery records.
- **Secrets are references.** Passwords and passphrases go to the OS store (Windows Credential Manager, the macOS
  keychain, the Secret Service on Linux) or live for the session only; settings files, logs, and workspaces never hold
  them.
- **Caches are disposable and bounded**, and nothing recovery-critical lives in them.
- **One process owns a writable profile.** Starting FileCat again forwards its locations to the running instance;
  portable mode keeps everything beside the executable, marked by `FileCat.portable`.
- **Workspaces** serialize layout and locations, never jobs or credentials. Locations that would ask for something on
  restore (a drive's recovery view, which needs administrator approval) are not restored.

## Consequences

- Migrations are per store; a schema bump is explicit in each file.
- A second instance with the same profile attaches instead of racing on state.
