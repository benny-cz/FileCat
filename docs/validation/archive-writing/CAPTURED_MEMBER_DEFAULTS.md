# Captured member defaults, 5 November 2026

Partial checkpoint over `d48da729`; no profile is enabled. The full archive-writing plan remains incomplete.

New regular-file additions now follow plan section 8.2: Unix captures only ordinary permission bits (0777),
and Windows uses 0644, including read-only sources. Full native attributes/modes remain in the retained review
digest. Changing a source's special permission bits or Windows read-only attribute therefore invalidates the
old review even when the derived archive-member mode stays the same. Replacement metadata behavior is unchanged.

Regression cases exercise those independent native observations. Actual journaled create/update pipelines inspect
the fresh observed manifest's mode: Unix special bits are omitted for creation, and Windows read-only sources
still create 0644 members. Both native confined Linux and Windows probe/application paths are included.

| Environment | Affected Core suite | Additional native suite |
|---|---|---|
| Windows 11 x64 VM / NTFS | 1,095 passed, 141 skipped; 1,236 total | Storage: 91 passed, 21 skipped; 112 total. LPAC: 33 passed |
| macOS ARM64 / APFS | 1,189 passed, 47 skipped; 1,236 total | Native observations and ordinary child create/update paths included in Core |
| Ubuntu x64 VM / ext4 | 1,202 passed, 34 skipped; 1,236 total | Confined Linux: 44 passed, one skipped; 45 total |

No failures. The broad filter was `FullyQualifiedName~Archive|FullyQualifiedName~EditSession|FullyQualifiedName~BackingArchiveTarget`.
SDK 10.0.101 builds/publishes passed with zero warnings/errors. Native runners are the tracked Windows/Linux
containment scripts. This checkpoint changes no UI and does not rerun or claim new GUI acceptance. Existing runtime,
token, confinement and release-qualification limits in [the preparation checkpoint](JOURNALED_TAR_PREPARATION.md) apply.

Mac testing initially postponed for another FileCat instance. A later idle census allowed the suite to run;
final censuses found no FileCat/test helper left by this task on any machine. Mac caffeinate PIDs 1339/6428 remain
running. The shared `E:\FileCat` checkout remains tracked-clean on `main`.

Ignored raw evidence is retained under `.tmp/test-results/archive-writing/capture-defaults-v1` and the owned guest roots.

| Transfer/evidence archive | SHA-256 |
|---|---|
| Six-file source overlay | `CDDDACC52C29C040E42641F467B33A551A0189E67FCC2CF13CDD391E59893659` |
| Windows managed overlay | `54B4B8D2A2069B0DD95D82AC4DD05DDFCA5B9E8AFBAD95E7F519AED43B2F4C88` |
| Windows result archive | `FAFF600D332386EC7770E7D833C2A9935405DE866CEE5786F4428923F1ABB84A` |
| Ubuntu result archive | `12555E1321C7665F59EC91D5F30C8BF96DE3EB6DB992B1EB47E9AE567EF8753F` |
| Mac result archive | `45518AB7ED3781B7214F47F69F72E730FEF704EF6CE28752FC4B4EBC6CB25E91` |

Exact native timestamp capture, directory defaults/capture, complete metadata/security application, publication,
production integration, other profiles, full qualification and the final independent review remain outstanding.
