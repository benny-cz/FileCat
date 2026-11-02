# I614 — Linux copies must retain POSIX ACL access rules

**Status:** Remediated preliminarily at product `6a9fc6de23b42241041138981ccdf28c213d2832`. **Severity:** High, because a copied file can become readable to an account denied by the source ACL. Exact-source five-lane CI and the frozen installed-candidate check remain open. Public preview.5 predates this source; stable 1.0.0 remains NO-GO.

## Distinct journey and before result

The owned Ubuntu 24.04.5 ext4 source file `secret.txt` contained 27 bytes, SHA-256 `47c2cebc964b2ddef68cf5536a56c611c6530a64081b4df8a161eca5e85368dc`. Its mode was 0644 but its POSIX ACL included `user:nobody:---`. `sudo -u nobody test -r` denied source access. In the native FileCat copy review, F5 copied it into a distinct empty owned target and reported success; the destination bytes matched, but `getfacl` showed only base mode entries. The same `nobody` access check **could read the destination**. Mode-only copy preservation therefore silently broadened access. This Linux ACL copy case is distinct from prior Linux ACL *inspection* and accepted macOS ACL copy evidence.

## Correction and exact native verification

The new Linux path reads and verifies source ACLs through held file descriptors, creates files and folders owner-only while filling them, applies access and default ACLs plus final mode through held destination descriptors, and verifies the source and destination identity/mode/ACL snapshots before success. A newly created folder with ACL metadata cannot silently merge into an existing folder, where its source protections would be discarded; the job gives a reviewable refusal. An ACL-copy failure is a failed or partial job, never a completed copy. On a filesystem lacking ACL/mode support, the existing best-effort mode behavior remains only when no source or inherited ACL is present. File content hashes and existing destination conflict rules are unchanged.

An isolated Ubuntu GUI source build used App DLL SHA-256 `9CAE281681C121CA5D247875D4653ECA8E1A31730156701F2F188FA564C34A23` and corrected Core DLL SHA-256 `91175E4B1BF4D0FF7D6E857368E196A45376EE9F886E40CF38A0B3BAAE80EA16`. The owned target began empty. The operator selected each source row, pressed F5, reviewed the exact destination and clicked Copy. Afterward:

| Case | Independent check | Result |
|---|---|---|
| `secret.txt`, named `nobody` denied | `getfacl -cn` source/destination; SHA-256 source/destination; `sudo -u nobody test -r` | ACLs and `47c2…68dc` hashes match; destination denied. Baseline destination was readable. |
| `restricted/leaf.txt`, folder access and default ACL deny `nobody` | `getfacl -cn` folder and leaf, including default entries; SHA-256 leaf | Folder ACL and default ACL match; leaf ACL matches; leaf hash `240E84F59F14CB7FCC2661D78FC1201845A7A842C6496062EC78FC17036BCCA5` matches; `nobody` denied. |
| `plain.txt`, no source ACL, destination parent has an inherited named-user default ACL | `getfacl -cn` source/destination; SHA-256 | Inherited ACL is removed from the new file; both have only base entries; hash `8D45CF6B7B3955D6BADFA5A7CB03417045B180F8519EC307C736F57198A2A63E` matches. |

The targeted `UnixPermissionTests` class built/passed on Windows x64 (18 passed, six expected Unix/Linux skips); two additional Linux-only tests use external `setfacl`/`getfacl` when available and compare copied ACLs and bytes. The three native GUI cases above are the direct Ubuntu positive controls; CI will determine hosted Linux test availability. The source and target ACL comparisons produced no diff. All owned test processes and `/tmp/filecat-cycle35-linux-acl` were removed under exact PID/path guards. The baseline review screenshot is retained only under ignored `.tmp/pdf-proto/cycle35-linux-acl-copy-review.png`. No unrelated system ACL was changed.

## Finite candidate acceptance and stop

On the frozen installed Ubuntu 1.0.0 candidate, use one disposable ext4 source file whose 0644 mode would allow reading but whose named-user ACL denies it, plus one nested folder with access and default ACLs and a destination parent with an inherited ACL. Review and copy into a new target. Require matching exact source/destination bytes and access/default ACLs, denied access for the named user, removal of an inherited ACL absent on the source, and no completed-success report on injected ACL-copy failure. Reconcile the latest product-source five required CI lanes and tag-only skips. Stop at these distinct controls; do not repeat the broader accepted Linux permissions and macOS ACL suites without a consequential counterexample.
