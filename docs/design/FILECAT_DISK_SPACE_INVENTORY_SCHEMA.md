# Disk space inventory schema 1

This documents the implemented stream codec. Private storage, publication, named-save UI, history and fresh live binding are separate work. The codec accepts and returns inert observations; reading never opens a saved locator or restores an operation capability.

The uncompressed `.fcdsa` container is UTF-8 JSON Lines, with an LF after every record including the trailer. Writer property order is the order below. Readers accept property reordering but require exactly the documented properties. Unknown records/properties, duplicate properties at any nesting level, invalid UTF-8 or escaped Unicode, missing fields and mismatched types are errors. There is no salvage mode.

Bounds are 8 GiB per container, 1 MiB per line, depth 16, 128 total object properties and 16,384 tokens per record, and 50 million total records including the trailer. Object and path-node counts each have a 10 million bound; detailed issues have a 10,000 bound. Imports share a conservative 64 MiB memory reservation and fail entirely at that budget. The lease retains this reservation until disposed. Large inventories currently need the future private spill/index path; this bounded memory fallback does not qualify the 1M/10M cases.

All identifiers and counts are canonical unsigned decimal **strings**, without signs, whitespace or leading zeroes. Root, object and fact identifiers are independently remapped to positive local IDs by the writer; path IDs retain their observed numeric IDs. `"0"` is only a path-parent sentinel, never an actual record ID. Counts and IDs fit UInt64; measurement values fit UInt128. Enum values are exact case-sensitive names, never numeric enum values. UUIDs use the `D` format. Times use invariant `O` format with offset `+00:00`.

Nullable properties remain present with JSON `null`. A measurement is `[state, decimalStringOrNull]`: only `Known` has a value, including zero. A nullable Boolean is `true`, `false` or `null`; unknown never means false. A state-count list is `[[state, count], ...]`, without duplicate states.

Order and properties:

1. One `manifest`: `kind`, `schema` (`"1"`), `inventory`, `producer`, `session`, `epoch`, `revision`, `hostProfile`, `started`, `observed`, `timeZone`, `roots`, `policy`, `policyFingerprint`, `historicalPhase`, `coverage`, `freshness`, `historicalTaint`.
2. Zero or more `object`: `kind`, `id`, `identity`, `length`, `links`, `modified`, `version`, `precision`.
3. Zero or more `path-node`, parent before child: `kind`, `id`, `root`, `parent`, `encoding`, `raw`, `entryKind`, `object`, `sparse`, `compressed`, `cloud`, `metadata`, `included`, `identity`, `times`.
4. Zero or more `storage-fact`: `kind`, `id`, `object`, `directory`, `component`, `basis`, `measurement`, `method`, `observed`, `includes`, `mayShare`.
5. Zero or more `issue`: `kind`, `code`, `node`, `root`, `state`.
6. One `coverage`: `kind`, `requested`, `completed`, `failed`, `denominatorFinal`, `boundaries`, `excludedIssues`, `failedIssues`, `pendingIssues`, `measurements`, `issueTotals`, `identityVerified`, `identityUnverified`, `staleNodes`, `staleRoots`.
7. One `trailer`: `kind`, `counts`, `sha256`. Nothing follows it.

`counts` has exactly the six preceding record-kind names as properties, with their decimal counts. `sha256` is lowercase SHA-256 of every preceding byte, including each LF. It detects corruption and does not authenticate origin. Both the trailer and its LF are required for partial/stopped analyses too.

`roots` contains 1–32 fixed 15-element tuples, avoiding a manifest with excessive property tokens. Tuple positions are ID, scheme, display locator, canonical locator, source identity, identity assurance, label, entry kind, name rules, traversal boundary, historical connection reference, disposition, covered-by root ID, covered relative path, and observed object identity. Display/canonical locators, labels and non-null covered paths use `["utf16le", canonicalBase64]`; this preserves unpaired source code units without putting them into JSON strings. They remain historical data. Root schemes and graph relationships are independently validated.

The policy object is exactly `ScanPolicy.CanonicalJson`: fixed known property names, integer strings, sorted ordinal set arrays and limit keys. Its SHA-256 must match `policyFingerprint`; supported algorithm versions and enum values are checked independently.

Node `encoding` is `utf8`, `unix-bytes` or `utf16le`. `raw` is canonical base64 with no whitespace; at most 64 KiB of decoded name bytes. `utf8` must decode strictly, `unix-bytes` preserves arbitrary bytes, and `utf16le` preserves code units. The display name is derived, never supplied as a conflicting second authority. Native observed names may include DOS-reserved names or trailing dots/spaces while remaining ineligible for a future Win32 action route. Separators, navigation components, NUL and Windows stream syntax are rejected in children. A failed reparse observation has `Unknown` kind when its tag cannot be read. Unavailable directory entries have no descendants. `included=false` requires `metadata=Excluded` and contributes to no file metric.

`times` is null or `[observed, modified, modifiedPrecisionTicks, created, createdPrecisionTicks, changed, changedPrecisionTicks]`. Object alias lists and node issue-code lists are derived from path and issue references. They are not potentially unbounded arrays in an object record. Fact `includes` contains component IDs, not fact IDs; component graphs must be acyclic and compatible. Synthetic chart groups are never path records.

After counts and hash verification, the reader validates IDs, roots, parents, aliases, kinds, measurements, component graphs, time envelopes, issue references and coverage consistency. Summaries and chart areas are computed from these facts. There is no stored trusted aggregate total. Every successful read has runtime origin `ImportedInventory`, temporary persistence and `ImportedUnverified` taint joined with the declared historical classification. Runtime origin, live eligibility, tokens, authorization, executable objects, external references and connection activation are absent from the schema.

The stream writer does not publish a filename or replace an existing inventory. Its caller must supply a guarded private create-new sibling, flush it and publish atomically through a qualified route. A failed or interrupted write has no valid trailer and cannot load successfully.
