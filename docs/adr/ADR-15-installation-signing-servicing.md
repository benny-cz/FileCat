# ADR-15: Installation scope, code signing, and servicing

**Status:** Decided (2026-09-27). Signing amended on 2026-11-20 (DEC-12): SignPath Foundation declined FileCat on
2026-10-31.

## Decision

- **Artifacts** come from `eng/publish.ps1`:
  - a per-machine Inno Setup installer (`PrivilegesRequired=admin`) of the self-contained ReadyToRun build;
  - a portable ZIP with a `FileCat.portable` marker, keeping state in `Data/`;
  - a framework-dependent ZIP, which receives .NET servicing independently;
  - an SBOM.
- **Signing (amended 2026-11-20, DEC-12)** uses the owner's individual-validated SSL.com certificate through eSigner,
  in CI's protected `signing` environment, which the owner approves for each run. It signs the Windows payloads, both
  Setups and their uninstallers for a release candidate. The explicitly labeled, unsigned previews came first, as
  decided while SignPath Foundation was the planned route; it declined FileCat on 2026-10-31.
- **Servicing** is explicit, with no updater. A security release follows within 14 days after relevant .NET Patch Tuesday fixes
  ([SERVICING.md](../SERVICING.md)).
- **Update check (amended 2026-11-03).** Automatic checks are on for new profiles and can be turned off in Privacy;
  saved off preferences stay off. At startup a request goes to the GitHub releases API unless the last check finished
  less than a day ago; a check still running when the main window closes is not remembered, so the next start asks
  again. An administrator window or a root FileCat, which keeps no settings and so cannot see the user's choice, never
  checks automatically (I670). A newer release
  uses the same dialog as Help → Check for updates and can be skipped by version; offline checks stay silent. Help's
  explicit check works even when automatic checks are off. FileCat never downloads or installs update packages.
  Requested network locations and optional network services can also connect; the update preference does not control them.

## Consequences

The TV-13 items that need real infrastructure remain manual:

- Smart App Control enforcement on a clean machine;
- installed versus portable helper trust;
- signature inventory.

ReadyToRun startup was measured at about 0.9 s warm (TV-01).
