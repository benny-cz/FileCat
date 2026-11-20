# Visible Unix extended attributes, 5 November 2026

Partial checkpoint over `dc9e8c06`; no new profile is enabled. Complete security admission/application and publication
remain unfinished under the controlling archive-writing plan.

## Implemented observations and guards

Held Unix outer-metadata capture now inventories visible native extended-attribute names and exact binary values.
It uses flistxattr/fgetxattr on the retained descriptor, retains its lifetime, bounds names before string allocation,
and checks list/value lengths and repeated inventories alongside the existing native before/after observations.
Empty present values remain distinct from absence. Invalid UTF-8 names, duplicate/malformed lists, count/byte limits,
unreadable values and observed changes refuse; they cannot become an empty inventory.

Limits are 64 KiB of native name-list bytes, 255 UTF-8 bytes per name, 1,024 attributes and 1 MiB of complete encoded
inventory including framing/names/values. Oversize values are refused from the size query before allocating their
value buffer. Immutable copies retain binary values; ordinal name ordering does not normalize Unicode. Canonical
hash framing is `FCUXATR1`, little-endian count, then name/value byte lengths and exact bytes for every ordered record.

Native outer snapshots use digest schema 2 and include this inventory digest. Legacy manually constructed snapshots
without an inventory retain their schema-1 digest and remain explicitly unqueried; they cannot pass the visible-xattr
admission helper. Native origin bytes must agree with the inventory. Linux POSIX access ACL bytes/presence must also
agree with the separately captured ACL. Retained input rereview now observes changed/removed unknown attributes.

Origin application permits only its expected fixed mark to change; every other inventoried name/value/presence must
remain the same. Post-rename reconciliation compares the full inventory digest. These operations still move/read
the same native object; no arbitrary xattr copying, erasing or blind metadata application has been added.

`RequireSupportedExtendedAttributes` is a visible-attribute subset gate: it accepts the fixed origin mark and Linux
access ACL already handled by their typed adapters, and refuses other inventoried attributes as unsupported. It is
not complete outer-security admission and does not enable a writer or publication coordinator. Complete Windows
security admission, Unix policy coverage, promised outer metadata application and actual namespace transactions remain
open. The complete integration must apply every required gate before publication.

Enumeration does not prove all possible policy state absent. Linux may omit inaccessible attribute names, and some
filesystems expose synthetic security attributes separately; Mac can hide special attributes behind API options.
The snapshot therefore explicitly describes a visible inventory, not a complete backup/security-policy proof. This
limitation and equivalent-identity ABA/race limits remain relevant to future admission. See the primary
[Linux listxattr contract](https://man7.org/linux/man-pages/man2/listxattr.2.html),
[GNU portability notes](https://www.gnu.org/software/gnulib/manual/html_node/flistxattr.html) and
[Apple native attribute API](https://developer.apple.com/library/archive/documentation/System/Conceptual/ManPages_iPhoneOS/man2/fgetxattr.2.html).

## Validation

| Environment | Affected Core replay | Additional native replay |
|---|---|---|
| Windows 11 x64 VM / NTFS | 1,153 passed, 147 skipped; 1,300 total | Storage: 92 passed, 21 skipped. LPAC: 33 passed |
| macOS ARM64 / APFS | 1,253 passed, 47 skipped; 1,300 total | Independent xattr/stat/capacity observers and journaled native create/update included |
| Ubuntu x64 VM / ext4 | 1,266 passed, 34 skipped; 1,300 total | Same independent observers; confined Linux lane: 44 passed, one skipped |

No final failures. Sixteen new cases cover canonical/frozen binary values, malformed/duplicate/invalid names and
pre-allocation bounds, empty versus unqueried inventory, origin/ACL agreement, subset admission, origin-only exclusions,
native changed/removed attributes, APFS oversize-value refusal and Linux invalid raw UTF-8 names. A FileCat-owned C
observer independently opens the owned fixture and reads its actual native name/value bytes and identity before origin
application and after rename. Binary, empty and Unicode attributes match exactly; only the expected origin mark is
added. The earlier C stat/flag observer is also explicitly enabled in the final Mac/Ubuntu replay instead of skipped.

The initial Windows-host focused run passed 47 and skipped 13 (60 total). Final SDK 10.0.101 builds/publishes completed
with zero warnings/errors. Ubuntu's first attempt stopped before tests because its runner redundantly defined an
existing C observer's GNU feature macro under Werror; corrected compiler flags allowed the final replay. The Mac
observer uses Apple clang 21.0.0 with the explicit CommandLineTools SDK; Ubuntu uses GCC 13.3.0. Production source
did not change for that runner correction.

The broad filter includes Archive, EditSession, BackingArchiveTarget and PaxTimestamp FullyQualifiedName terms; native
lanes use tracked containment runners. Eight existing exact-time independent-reader fixtures per platform pass again.
Actual test-host runtimes remain Mac .NET 10.0.12 and Windows/Ubuntu .NET 10.0.14. Existing filtered-user, installed-package,
RID/filesystem, fuzz/scale/GUI and Mac hard-worker-memory qualification limits remain. No UI behavior changed; no new
GUI acceptance or complete AW packet is claimed. Global metadata/heap accounting remains a separate unfinished gate.

Final runner checks found no FileCat/test process left by this task. A later transient Mac dotnet process had exited
before follow-up inspection; no unrelated process was stopped. The Windows evidence collector initially returned
nonzero; a subsequent process census was empty and collection then succeeded. No tests were restarted for collection.
Mac caffeinate PIDs 1339/6428 remain, and shared `E:\FileCat` remains tracked-clean on `main`.

## Evidence

Raw TRX, launch facts, C observer executables/output and oracle fixtures/logs are retained under ignored
`.tmp/test-results/archive-writing/xattr-v1` and the task-owned guest roots.

| Transfer/result archive | SHA-256 |
|---|---|
| Six-file source overlay | `F46EE34081E2E1423262CB49F3B9D6A31E659DC654214B48BABF44C00BA4B2B4` |
| Windows 84-file managed overlay | `486C029B804BF0FF90C18BE25DF9D10014512A10DE12C8D62D6C60BB07C29959` |
| Mac results/observers/fixtures | `AEDF519F79FC8EFB71AA4D3E66C6E3E58E8A402E24087C99C9768DBD0013A63F` |
| Ubuntu results/observers/fixtures | `71F8B8C3E7285035787EA682C985CE396811F6F74AFCA81CEDC43FA2D6900781` |
| Windows results/fixtures | `B7AE2CBE0B8C77CE116042144093C3CFAD48C2330FD6D0FCD1135789994F48F7` |

Only an explanatory admission-method comment and checkpoint documentation were added after these builds. No
third-party source or runtime package was added/upgraded. The full implementation/validation loop, final independent
UX/UI/functional/security review and subsequent RAR licensing analysis remain unfinished and ordered as requested.
