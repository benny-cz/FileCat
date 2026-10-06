# E-I166 — automatic Git badges fetch promised objects

2026-10-07 CEST; observations recorded 2026-10-06 UTC. Original unchanged native
producer cceb9903630f38df44f45ffb57d84fe3d860558c; durable baseline producer
09f068e550fdfb29bbb07ea5e8dc50d1e080ed25 has the same Git service blob.
Correction 61b43fc378a9265003a645c068224b77d0298aa5. High automatic-browse
unrequested-fetch risk under I16/V23 B10/V24; remediated preliminarily.

## Proved behavior and correction

The original actual component admits an owned local partial clone. Its HEAD tree
is absent before FileCat's automatic status read; the read returns a Modified badge,
fetches the missing tree from the owned local source and changes the object's pack
directory from four files to eight. A direct installed-Git status control with
`GIT_NO_LAZY_FETCH=1` exits 128 and leaves the exact pack names/hashes unchanged.
The ordinary repository's Modified positive control passes. Git's
[partial-clone documentation](https://git-scm.com/docs/partial-clone) describes
fetching missing objects from promisor remotes; this observed source is local.
No remote server, external-network capture or repository-defined program is tested.

The correction refuses known partial-clone repository configurations before Git
starts: `extensions.partialClone`, `remote.*.partialCloneFilter`, and promisor
properties other than explicitly false/empty values. Admission handles bare values,
case variants, the old dotted remote section spelling and decoded quoted fragments.
The restricted Git child also receives `GIT_NO_LAZY_FETCH=1`; the installed-Git
control verifies its effect here. See the primary [Git environment reference](https://git-scm.com/docs/git#Documentation/git.txt-codeGITNOLAZYFETCHcode).
Ordinary repositories and explicit false promisor controls remain available.

FileCat-derived Git badges are omitted for known partial clones even when their
objects happen to be complete. Windows Shell overlays have a separate producer.
This avoids relying solely on the environment guard being supported by every Git
version; it does not qualify post-admission configuration swaps or all indirect
paths. The README records the badge behavior.

| Actual producer / comparison | Outcome |
|---|---|
| Original clean cceb990 native component | Ordinary control passes; partial clone is admitted and status adds the missing owned tree and four pack files. Direct no-lazy-fetch control exits 128 without those writes. |
| Original Git source with durable test overlay | 54 pass, 12 expected failures, one explicit network-fixture skip; exit 1. Eleven admission failures plus the real owned pack-effect failure; four false-promisor controls pass. |
| Working correction, affected Git suite | 66 pass, zero failures, one explicit network-fixture skip; all 16 new cases pass. |
| Clean raw committed 61b43fc | 1,054 canonical Git blobs verify before/after; locked restore and isolated build; the same 66 affected cases/all 16 new cases pass, with one explicit skip. |
| Unchanged original native probe against clean committed bytes | Ordinary Modified control passes; partial clone is refused, the missing tree stays absent and all four pack files retain their exact names/hashes. All 141 actual component inputs remain unchanged. |

The skipped case in all three test runs is
`GitStatusTests.A_repository_that_points_Git_at_a_share_is_never_run_in`;
its actual missing `FILECAT_V24_SHARE` fixture reason is retained. Each runner
explicitly unsets that fixture and physical-source fixture keys. No measured share
or physical-source qualification is claimed. Existing unrelated compiler warnings
remain in the full output. All native commands exit naturally within their bounds.

## Exact identities and independent seal

The original compiled probe keeps its cceb990 source field as baseline metadata;
it is not relabelled. Corrected native provenance comes from the canonical committed
build receipt and actual loaded DLL. Original FileCat.dll SHA-256:
`92e409dbda77c2d5103b1d6e954b5c8c6240ec635a97a5c2f9d08119fe94aa3a`.
Corrected actual DLL SHA-256:
`6b6070719f48637f4faf4773477b00ca232a22ea611200d7979e7348f34891ca`.
Original probe DLL SHA-256:
`63082afa758a10b19549a5a8269f1f880ef1fd05e2185b36f154e3a46adb3a6f`.

Independent seal v6 reconciles 57 retained files, four original probe binary pins,
423 actual test-payload pins, 141 original cceb990 inputs, twelve complete native
post-run pack-file snapshots and 1,054 raw canonical source blobs/ZIP identities.
It independently reads all TRX outcomes, baseline failures, explicit skips and both
native before/after controls. The pending E-I165 prose correction is explicitly
excluded from the clean build; canonical committed bytes are used instead.

Private `FileCatReleaseEvidence/git-lazy-fetch-20261006-v1`:

| Path | SHA-256 |
|---|---|
| result-v1.json | 98b7fdab69fb56f0beebb015903d3aa9a2625a0d25ae12cc849fd61151fccc30 |
| durable-baseline-v2/command.json | ccd3172e57ba287797f3769f4eccb29b518e3172e81f8676e39012ac6f0c0083 |
| durable-baseline-v2/results/baseline.trx | 191549ecfa2f4d6f333dafb7ee2614ce4c38e4591de816beb3db82f9bef57bfd |
| fixed-tests-v3/command.json | 50eb31bd9b773decb5df90e439dd27f498b7b185572a12162c5581a07ef993da |
| fixed-tests-v3/results/fixed.trx | 0d3bbe3b65997e583b077c221d9d08f82beceb244d9b72b3c79b41b242b7c37a |
| clean-fixed-v4/command.json | 164d233a79c808187308668a69b254fefa63dc812e28b794054275c23efc0ac2 |
| clean-fixed-v4/results/clean.trx | a6897579f88bcbfcd6739bb7cc9e4b1549432d923d6e850e65f2f779f8c38f2a |
| clean-fixed-v4/source.zip | 20e97fd96f736ee6ec1b761ae23dbea5f005f5034247f2275be491651469b412 |
| committed-native-v5/command.json | 508e51e8f9b6d0db400f135a6e19843933469165351c0941ec554b6a133db655 |
| committed-native-v5/independent-committed-native-v5.json | 6101a51c910637e0c5825f1ca75ff5a1ffc26adb8cdcc9bc172acf1b23ca6703 |
| ci-summary-v6.json | 705c073b7bb192d8fd5befdfd0f3bc3ec01ed27dd32366a23df0e4238b5cfc15 |
| independent-lazy-fetch-v6.json | 24861a03b9d11b9e00d51c2bcc665cc9d9fd98323ffecafb67d709b7c1284ce7 |

## CI and remaining qualification

Original [CI 37539040491](https://github.com/benny-cz/FileCat/actions/runs/37539040491),
attempt 1, is bound to the exact correction. The retained 22:16 UTC snapshot has
policy green and all four required lanes still running; it remains historical
evidence, not a final CI result. Completed artifact/inventory collection is queued.

Broader I16/I17/V23/V24 work remains: other parser/indirect paths, home expansion,
aliases, source/configuration swaps, identity/loader/lifetime boundaries and actual
native browse/network effects. No native desktop interaction, final-candidate
qualification, physical-source opening, machine-policy change or publication
occurred here. Owned local fixture directories are retained as private evidence.
No persistent global/user Git configuration changed.
