# Exact Unix origin on private new archives, 9 November 2026

Commit `1b5e6129` extends the unregistered private creation path. No writer profile is enabled. It does not grant existing-archive update, arbitrary metadata replay, or cleanup authority.

## Behavior

Complete sealed origin evidence must match every captured file and directory. For Linux `user.xdg.origin.url` and macOS `com.apple.quarantine`, one exact opaque present value may be carried to the new archive, including an empty or binary value and a selection with unmarked inputs. Differing present values refuse publication; an unavailable read never becomes absence. Windows' separate maximum declared-zone policy is unchanged.

The Unix stage applies the selected value through the existing descriptor-bound, create-only native attribute operation after rebuild and before fresh verification. It flushes and rereads the mark, payload and unaffected native metadata. Publication rereviews the verified stage and its origin after an exclusive name claim. A marked Unix creation now emits `FCCMETA1` version/policy 3 with the native witness; unmarked creation stays at 1 and Windows zone-only creation stays at 2. The journal schema remains unchanged.

## Validation on the frozen source

The 66 MiB `1b5e6129` source packet has SHA-256 `071ccdceea0d5a6a27fed779ea0e2264b2a7505c62b1300cde57e8db0af56de4`. Host compilation of Core and the Core test project succeeded without warnings. Live tests ran only in guests.

| Guest | Result | Evidence |
|---|---|---|
| macOS APFS | Focused 52 passed, one Linux-only skip; broad Unix storage 190 passed, two platform/fixture skips. Both empty and binary marked new-archive publications passed. | `.tmp/marked-creation-mac-v2-evidence.tar.gz`, SHA-256 `e42e4937c9fdb176bbac79aab74555ca6d7639fda1236e07a0f98a7761d4c688` |
| Windows 11 NTFS | Core policy 25 passed, two Unix-only skips; native storage 120 passed, 139 explicitly gated skips. Both Windows zone-only exclusive publication cases passed under a measured 12288-to-8192 integrity transition. This administrator token still has an Administrators SID, so it is not a standard-user or AppContainer qualification. | `.tmp/marked-creation-windows-v4-evidence.zip`, SHA-256 `c74f2277bb023cd5592e8fd28cfefb02b34598526d088ffc688191691cf92b41` |
| Ubuntu ext4 | Native and broad policy tests are queued behind the existing fuzz, adoption and AW0 performance work. No result is claimed. | `filecat-archive-marked-creation-v2.service` |

The first Mac and Windows focused wrapper checks read a nonexistent TRX `skipped` attribute and reported a wrapper failure despite zero test failures. The independent verifier at ignored `.tmp/verify-marked-creation-v1.py` parsed the raw TRX results and confirmed exact expected skips and both Mac native marked cases. The later broad Mac and medium-integrity Windows runs use corrected checks and report `PASS`. Guest source-file hashes match the packet. Both finished guests had no FileCat or test processes; Mac's until-reboot keep-awake assertion remained active.

Ubuntu's RAR5 password long fuzz run reached 12,003 CPU seconds with no crash artifacts. Its earlier 60-second short qualification timed out and did not satisfy the sealer, so the old adoption/strict/wrapper/AW0/marked queues failed without running their dependent work. A bounded longer clean short run and a new guarded recovery chain are active. Once qualification passes, the chain runs the remaining adoption checks, independent 7z/RAR5 strict long campaigns concurrently in separate 1 GiB units, then gzip/BZip2/Zstd long campaigns concurrently under the same per-target limits and 7,200 CPU-second floors. AW0 and Ubuntu marked creation follow those gates. These queued actions are not passing evidence.

Remaining work includes marked Unix update provenance, full promised outer-metadata handling, production registration and integration, broader crash/interoperability/resource qualification, and the final independent review.
