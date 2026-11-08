# Prepared private inventory replacement

This packet implements and qualifies the core prepared-review and transaction layer for replacing a named `.fcdsa` inventory. It is not completion of W3 or of the full disk-space plan. The themed conflict review, pending-replacement browser/recovery actions, comparison replacement, optional history, import/export and remaining qualification are still required. The product command and action gates remain off. The user's independent final UX/UI/functional/security review has not yet taken place.

The catalog now decodes a strict inert pending-replacement record containing the unchanged previous catalog entry. Generated previous/incoming identities and the human name are reserved; the previous and current records occupy one logical slot while all actual metadata counts against the fixed byte budget. Conflicting, foreign, malformed or future records refuse writes. Reading never replays maintenance. Ordinary deletion/maintenance refuses an identity involved in a replacement.

Preparation encodes the complete immutable revision before returning a one-use runtime review. Snapshot encoding uses the existing canonical encoder through a bounded two-chunk pipe; indexed encoding uses the existing bounded dataset writer. Preparation preserves the normal destination reserve, does not publish a named inventory, and holds actual staging/root ownership across the decision. An indexed source gets its own retained revision until decision and cleanup. Disposing the review chooses Keep existing and awaits actual close. Closing the store waits for its outstanding review. An execution token is fresh and independent of the token used to prepare the immutable bytes.

Explicit replacement obtains the catalog writer and exclusive native metadata/data leases, rechecks profile/catalog identities and the reviewed old native identities and complete bytes, then moves the old immutable catalog record to its generated pending name. It publishes the new incoming payload, verifies the publication identity and complete payload again, and prepares a complete previous-copy backup in 64 KiB admitted turns above the usual free-space reserve. The backup's complete container and SHA-256 must match the reviewed old bytes before switching the original name. macOS publication returns the newly created clone's identity; the staging inode is not used as the published identity.

The existing narrow native replacement primitive switches the original `.fcdsa` name. Actual new-file close and create-new publication/close of its new immutable catalog record precede the logical success result. Previous-copy cleanup follows that commit. A cleanup refusal reports the confirmed new save and pending cleanup separately. Before commit, an exact runtime-owned rollback can restore the verified backup and previous metadata. Unknown substituted names are preserved. A failed or uncertain rollback/publication/close leaves an explicit pending/uncertain result rather than claiming that the previous file was restored. Restart recovery is deliberately not inferred or executed by the reader; its explicit fresh-review implementation remains open.

The guarantee applies to the application-prepared private namespace and its cooperating leases. It is not generic source replacement, source cleanup authority, or authority obtained from an imported inventory. No lower-strength publication fallback was added.

## Actual guest results

All original r936 and r939 runners and r941 focused runners returned exit 0. All result XMLs report zero failures/errors. Expected platform skips remain visible.

| Packet / machine | Core passed / skipped | Windows native | Headless rendering passed / skipped |
|---|---:|---:|---:|
| r936 Windows VM | 1,455 / 48 | 75 | 331 / 1 |
| r936 Mac | 1,481 / 4 | — | 332 / 0 |
| r936 Ubuntu VM | 1,481 / 4 | — | 332 / 0 |
| r939 Windows VM | 1,472 / 48 | 75 | 335 / 1 |
| r939 Mac | 1,498 / 4 | — | 336 / 0 |
| r939 Ubuntu VM | 1,498 / 4 | — | 336 / 0 |

r936 first qualified the strict catalog decoder and prepared writer: 19 focused core cases and four existing presentation controls passed on each platform. r939 added the retained review, backup, transaction and publication-identity checks. Its 76-case maintenance preflight and four new native transaction cases passed on all three machines before their broad suites ran. Core controls cover preserving cancellation before/after switch, stale objects/bytes/profile, active readers, low backup space, rollback refusal, new-catalog publication refusal, postcommit cleanup refusal, one-use/owner binding, store-close ownership and substitution of a generated payload after publication. Native NTFS/APFS/ext4 controls exercise replace, keep, reader refusal and old-object substitution using real private files.

r941 added two native indexed-source cases per machine: Keep and Replace after the caller closes its source lease, with unchanged generated source contents, retained coverage and inert saved-view mapping. All six passed. Build r940 changed only the rendering test DLL/PDB; local and guest cache verification covered 109 Windows, 105 Mac and 104 Ubuntu inputs. Product inputs were unchanged, so broad r939 suites were not repeated.

Builds r938 (production plus transaction tests) and r940 (the two extra tests) completed with zero warnings/errors. [source-manifest-r942.json](source-manifest-r942.json) records the final source projection, frozen broad inputs, test-only delta and retained results. The broad r939 rendering binary contains the original four native cases; the final test source adds only the two r941 cases. Qualification does not imply physical desktop, themed replacement/recovery UI, process-death recovery, package, scale, full plan or final-review acceptance.

One result archive was collected per completed guest batch. All task-owned runners exited. Mac and Ubuntu final inventories were clear. Two transient `dotnet` processes appeared in the first Windows post-r941 inventory after its runner returned; no process was stopped, and a fresh inventory one minute later was clear. No FileCat GUI or input automation was run on the user's host.
