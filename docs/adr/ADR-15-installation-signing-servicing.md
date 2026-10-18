# ADR-15: Installation scope, code signing, and servicing

**Status:** Decided (2026-09-27). Signing is pending the SignPath Foundation project, which is a manual step.

## Decision

- **Artifacts** come from `eng/publish.ps1`:
  - a per-machine Inno Setup installer (`PrivilegesRequired=admin`) of the self-contained ReadyToRun build;
  - a portable ZIP with a `FileCat.portable` marker, keeping state in `Data/`;
  - a framework-dependent ZIP, which receives .NET servicing independently;
  - an SBOM.
- **Signing** goes through SignPath Foundation in CI. CI already has the placeholder step. Until the project exists, an
  explicitly labeled, unsigned preview release comes first.
- **Servicing** is explicit, with no updater. A security release follows within 14 days after relevant .NET Patch Tuesday fixes
  ([SERVICING.md](../SERVICING.md)).
- **Update check.** Automatic checks are opt-in and notify-only: at most one request a day to the GitHub releases API
  at startup when enabled. Help → Check for updates works on explicit request even when automatic checks are off.
  FileCat never downloads or installs update packages. Requested network locations and optional network services
  can also connect; the update preference does not control them.

## Consequences

The TV-13 items that need real infrastructure remain manual:

- Smart App Control enforcement on a clean machine;
- installed versus portable helper trust;
- signature inventory.

ReadyToRun startup was measured at about 0.9 s warm (TV-01).
