# ADR-09: Registry representation and writes

**Status:** Decided for P4a (2026-09-28). TV-05's VM checks of machine-wide cases remain external release gates.

## Decision

The Registry lives in ordinary panels as typed resources:

- **One grouped listing** per key: subkeys, then values with their types and data previews. Values are typed records,
  never synthetic files; F3 inspects raw data, F4 opens type editors.
- **Explicit views.** The 32-bit and 64-bit views are part of the location and switch in place; nothing depends on
  FileCat's own bitness.
- **Aliases are honest.** HKCR and HKCC are browsed merged, but writes go to an explicit underlying location that the
  user picks (the writable-location route), never to whichever layer happens to win.
- **Links are shown as links** and never traversed by subtree operations; they are followed only on request.
- **Guarded changes.** Every change carries the value or subtree it expects; a key that changed since it was shown is
  refused instead of overwritten. Changes run as jobs, with Undo while nothing else changed the values, and deleting keys
  offers a `.reg` backup first (not for links, which `.reg` cannot represent); deleted keys come back only from one.
- **Interchange is explicit.** `.reg` export and import (previewed, never run on download or selection) and binary
  save/load of value data are conversions the user chooses.
- **Elevation** is the per-plan broker (ADR-14), with HKCU resolved to `HKU\<SID>` so the plan names the requesting
  user's hive.

## Consequences

- Import is not atomic, and `.reg` files omit ACLs and the view; the capability matrix says so.
- Transacted Registry APIs were not adopted: expected-value checks give the conflict detection the plan asked for
  without the deprecated transaction manager.

## Evidence

Application-hive tests for typed values, the live HKCU namespace for paths and views, alias routing, link refusal,
subtree fingerprints, change notifications, `.reg` round trips, and access-denied retry plans.
