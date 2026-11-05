# Windows outer metadata and mutation guard checkpoint

2026-11-05, over `b5eea947`. This adds held-object observations of owner/group, DACL, mandatory integrity label,
creation/mtime, attributes, link count and typed origin, and makes the snapshot mandatory for native archive rename
and deletion. It remains unregistered; no new write profile is enabled. It does not apply promised output metadata,
establish complete Windows backup/security fidelity, or implement journaled publication/recovery.

## Observation contract

`WindowsArchiveOuterMetadata` freezes a bounded native permission descriptor in a deterministic self-relative layout,
preserving returned ACE order and control/presence flags. Owner and group remain raw SIDs, with no account lookup or
network access. The descriptor is capped at 128 KiB before copying/parsing. Origin evidence remains bounded by the
existing 64 KiB policy. No caller-owned mutable array is retained.

The exact queried security-information mask is recorded in the canonical digest: `0x17` (owner, group, DACL, mandatory
integrity label), using `READ_CONTROL` on the held file. The native API returns security information selected by those
flags; see [GetSecurityInfo](https://learn.microsoft.com/en-us/windows/win32/api/aclapi/nf-aclapi-getsecurityinfo) and
[query rights](https://learn.microsoft.com/en-us/windows/win32/secauthz/security-information).
This does **not** establish that an audit SACL, resource attributes, scoped access policy or other unqueried state is
absent. Release preservation/admission must address them before enabling a profile; no privilege is enabled here.

Schema-one hashing uses explicit little-endian fields: schema/mask/identity length, exact ASCII volume/file identity,
raw 100-ns creation and modification times, attributes, link count, descriptor length/bytes and the typed origin digest.
Access/change times are not harvested or promised. The digest binds observations to the actual object, not a filename.
It is not by itself an authenticity, preservation or publication receipt.

`CaptureOuterMetadata` accepts readonly source leases and sealed owned artifacts, refuses writable artifacts, and
returns no partial/default snapshot when permission, label or origin capture fails. Two matching complete observations
are required; a detected change refuses capture. These observations are not an atomic filesystem snapshot and do not
isolate an equivalent-privilege attacker or remove the requirement to reread actual retired/published objects.

## Mandatory namespace guards

`RenameNoReplace` and `DeleteReviewed` now require both the full `ArchiveObjectVersion` and the native metadata snapshot
for the same file identity. Neither has an optional metadata bypass. They verify the metadata digest before and after
the full-content SHA-256 reread, retain the specific readonly/type/physical-limit refusal, and still require artifacts'
private permissions. Changed captured origin, ACL, times, attributes, link count or identity refuses the operation.
Matching bytes and length cannot erase a newly acquired mark or authorize an unrelated object's metadata.

The existing exact-handle/no-replace namespace semantics remain. This is not atomic compare-and-swap of all metadata
and namespace state: the coordinator must persist intent first and perform actual post-publication reconciliation,
guarded rollback and conservative retention. Journal integration, complete output-permission preparation, capability/
durability qualification and object-bound recovery actions remain pending. Current artifact operations require private
permissions; applying final broader permissions will require a separately reviewed prepared-publication state.

## Validation

Ten new native cases cover complete selected-field capture, repeated stability, unchanged payload with changed times/
attributes/ACL/origin, unreadable-mark refusal, writable-artifact refusal, rename identity stability, readonly sources,
cancellation, fresh close/reopen stability, and mandatory namespace refusal for changed mark/ACL/mtime or metadata from
another object. Existing storage/origin fixtures now supply actual native snapshots rather than a placeholder bypass.

The live ACL fixture intentionally edits permissions, then restores the original ACEs. Its earlier review cannot be
assumed to authorize the resulting native descriptor state. The fixture takes a fresh baseline after that known edit,
independently checks unchanged owner/group, creation/mtime, attributes, link count and origin, and proves private-only
rename still succeeds. An intermediate run also caught generic metadata error wrapping hiding the precise readonly
refusal; mutation now validates that condition directly first. Failed intermediate TRX remains in ignored evidence.

Affected filter is the 106-case origin/storage/replacement filter plus `WindowsArchiveMetadataTests` (116 total).

| Environment | Result | Evidence |
|---|---|---|
| Windows 11 x64 VM / NTFS, runtime 10.0.14, medium integrity | 114 passed, 2 skipped, 0 failed | `archive-metadata-platform-vm4.trx` |
| macOS ARM64 / APFS, source built, SDK 10.0.101 | 24 passed, 92 Windows-only skips, 0 failed | `archive-metadata-platform-mac-final3.trx` |
| Ubuntu x64 / ext4, source built, SDK 10.0.101 | 24 passed, 92 Windows-only skips, 0 failed | `archive-metadata-platform-ubuntu-final3.trx` |

All ten new native cases execute on Windows. The two Windows skips remain the separately tested elevated-refusal case
and an existing physical FAT case. Unix results establish compilation/portable guards, not native metadata preservation
there. Windows executes host-built assemblies; it is not a guest source build or ordinary filtered-user/installed-
package qualification. Launcher facts: parent PID 4288, child 3620, runtime 10.0.14, integrity 12288 before / 8192 after,
exit 0. Builds finish with zero warnings/errors. This Windows-only change does not repeat unrelated Core codec tests.

Final seven-file source delta SHA-256 applied over the origin-updated owned Unix trees:
`B1309FA30A2AF77F39D4B485BE43FCB74F50E466AA19DD1068338C8363383DF4`.
Final Windows binary/symbol transfer archive:
`9261B43717DAE88DDC40C53CD609B6C8ABEDF0DF4155766A4D64EDA7B32E610C`.

| Artifact | SHA-256 |
|---|---|
| FileCat.Platform.Windows.Tests.dll | `C0538CBC1716AE444C304940CA33C7749FCC22BD813AAFB14F363FAF1A58A54B` |
| FileCat.Platform.Windows.dll | `56B49F7D874539C4B8F56BFEEC13B5A5607E68E888C106EEF612393C64464A6A` |
| FileCat.Core.dll | `B5226378511067665EDE6FFB880432C08074E015598BB64A9E131DDE11F8EE45` |
| FileCat.Archives.dll | `F6DEF9E8424A5BFA22D3D6CF1B5B0E0C3DC23F406E530A7FB10221B02E7332D9` |

TRX/facts remain under ignored `.tmp/test-results/archive-writing`. All guest FileCat/test processes exited; Mac
until-reboot keep-awake assertions remain active. Shared `E:\FileCat` remains tracked-clean on `main`. No dependency or
third-party source was added. The full implementation/qualification loop, independent final review and conditional RAR
follow-up remain pending.
