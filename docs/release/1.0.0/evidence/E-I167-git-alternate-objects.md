# E-I167 — alternate Git object-store admission

2026-10-07 CEST; observations recorded 2026-10-06 UTC. Original actual clean native
producer 61b43fc378a9265003a645c068224b77d0298aa5; durable baseline product
3b7a6f7f8d084eb8a5cfa6840a7f3ff079c75b97 has the same Git service blob.
Correction a0a8ecea3958f5e4d3d70b9dc57f6c5a735b3796.
High path-admission risk under I16/V23 B10/V24; remediated preliminarily.

## Proved defect and chosen remedy

An owned shared clone has no object files of its own. Installed pinned Git obtains
the exact `one` blob from an owned alternate; FileCat's actual component returns
a Modified badge. Replacing the alternate with a missing store makes both Git
controls fail (128) and FileCat return no snapshot. Ordinary absolute and relative
alternates are positive controls. An absolute path through the owned junction is
refused by FileCat, but three Git-valid relative spellings are admitted and produce
the badge through the same junction:

- `../../../target-link/.git/objects`
- `"../../../target-link/.git/objects"`
- `"\056\056/\056\056/\056\056/target-link/.git/objects"`

The native missing-store control and exclusive object location establish FileCat's
dependency on borrowed objects; no syscall/file-contact trace is claimed. Direct
Git controls independently verify the borrowed blob bytes. The source has three
owned loose objects; both runs preserve all object hashes, restore their exact
alternate file and remove their exact owned junction. No network server or physical
source is involved. Git documents relative alternates against the object database
in the primary [repository layout reference](https://git-scm.com/docs/gitrepository-layout#Documentation/gitrepository-layout.txt-objectsinfoalternates).

The remedy resolves each unquoted alternate against its actual object directory,
checks Windows ancestor/metadata links, and follows further ordinary alternate
stores with a shared bound (32 directories, one million characters, 250 ms).
Repeated stores/cycles terminate; over-budget work leaves optional badges plain.
Missing local stores can be admitted but cannot supply objects. Nonempty HTTP
alternate locations are refused.

Alternatives considered were omitting all shared-store badges or implementing a
complete Git C-style byte/octal path decoder. The chosen bounded traversal preserves
ordinary shared-store badges; quoted alternates and leading/trailing whitespace are
refused until their separate path encoding is qualified. This deliberately omits
some safe quoted-store badges and is recorded in README. It does not reinterpret
a quoted path as a harmless relative filename, or reuse the different config-value
decoder. General aliases/swaps, home-relative config and Unix mounted paths remain
broader scope.

| Actual producer / comparison | Outcome |
|---|---|
| Original clean 61b43fc native component | Three relative junction spellings admitted with Modified badges; direct absolute junction refuses; two ordinary positives and missing-object control pass. |
| Original product with durable test overlay | 70 pass, eleven expected failures, one explicit network-fixture skip; exit 1. New failures include the proved relative boundary, transitive metadata boundary and the chosen quote/HTTP/graph limits; four new positive controls pass. |
| Working correction, affected Git suite | 81 pass, zero failures, one explicit network-fixture skip; all fifteen new cases pass. |
| Clean raw committed a0a8ece | 1,056 canonical Git blobs verify before/after; locked isolated build; the same 81 affected cases/all fifteen new cases pass, with one explicit skip. |
| Unchanged original compiled native probe against clean committed bytes | Four junction spellings refuse; two ordinary Modified positives and missing-object control pass. All 141 actual component inputs remain unchanged. |

Durable cases cover relative/quoted/octal junction paths, ordinary absolute/relative/
chained stores, quoted-policy refusal, transitive alternate and pack-directory
junctions, repeated cyclic stores, the graph bound, HTTP locations and a real
shared-object positive/junction refusal. The last case runs its ordinary control
everywhere and its junction branch on Windows; the five dedicated junction-only
cases explicitly skip on Unix. Native desktop/mounted-share effects are separate.

All three local test runs explicitly skip
`GitStatusTests.A_repository_that_points_Git_at_a_share_is_never_run_in` with
the actual missing `FILECAT_V24_SHARE` fixture reason. Each runner unsets the
physical/network fixture keys. Existing unrelated compiler warnings are retained.
Native commands exit naturally within their bounds.

## Identities and independent seal

Original actual FileCat.dll SHA-256:
`6b6070719f48637f4faf4773477b00ca232a22ea611200d7979e7348f34891ca`.
Corrected clean actual FileCat.dll SHA-256:
`5f2498a7968b443766249902ade77a5d63c1e02fc9e8d8dded97c2f0568f97b7`.
Original compiled probe SHA-256:
`61bef49f0b8cf7fa1f614dcaf1c00d526cd7fe48e9f87fada52e10e61862f838`.
Its embedded original source field remains baseline metadata; corrected actual
producer identity comes from the clean build receipt and loaded DLL hash.

Independent seal v6 rechecks 52 retained files, four original probe pins, 423 actual
test-payload pins, 141 original clean inputs, six complete native source-object
snapshots and 1,056 canonical source blobs/ZIP. It independently decompresses each
loose object, verifies its Git SHA-1/header/length and the exact blob bytes, reads
all raw TRX outcomes and reconciles both native runs and cleanup.

Private `FileCatReleaseEvidence/git-alternate-objects-20261007-v1`:

| Path | SHA-256 |
|---|---|
| result-v1.json | d7fa223f87f75652fca6a2f7687454f2cecfa89740484da3752c725158d35f20 |
| durable-baseline-v2/command.json | d9ab33569103d43e2001cf0bca72dc82604392e7a25c54076122ec104f012245 |
| durable-baseline-v2/results/baseline.trx | 4fd573c37d593c0ee3c211eebd1ed0c414429faa37e6efc317c840b914b9360f |
| fixed-tests-v3/command.json | 8de7e07ecb7aff77a13bac6ffb1d20977543231e1c6d34de54f6b39191b26d79 |
| fixed-tests-v3/results/fixed.trx | e6007cef2cdd39c7d857186f564cecd5eccd7e96b723bf45590c68136ae2bdcc |
| clean-fixed-v4/command.json | 09de485de5462ec0dfdf2ed13f20a07e80097baa7edc8de30205410599ebbf53 |
| clean-fixed-v4/results/clean.trx | 9f94e7d1e9454f4d10b2e5582c03fe068fb03eccf9a5423a58615f30ffd8fcca |
| clean-fixed-v4/source.zip | 7d28fca1ebe02a911cf9784deede35d9c795ba3260de91d96f9d44be4b86b819 |
| committed-native-v5/command.json | 6c095cf93add56631914d44ad951ba93a7ae61e02abc81ffe9d786a7a6789171 |
| committed-native-v5/independent-committed-native-v5.json | dfa41549ead96107e2d295fbcf76295a9dcf465719c9a470fa7dd9069c1a39bf |
| ci-summary-v6.json | 41974479c277e9a1aac3648038e960bee260c8b73c488dc48805b815056e2738 |
| independent-alternate-v6.json | 75fd07b6fe061f1a84bae33807eb1999aab25037ea65f43f2a7843cfbf06b584 |

## CI and remaining work

Original [CI 37541603291](https://github.com/benny-cz/FileCat/actions/runs/37541603291),
attempt 1, subsequently completes green on policy and all four required lanes,
including ARM64 package start/draw and installer compilation. The original 22:38 UTC
in-progress snapshot stays unchanged. Nineteen server artifact digests and all
archive members, fourteen complete raw TRX inventories with explicit skip reasons,
four compiler/tool receipts and 92 locked actual dependency graphs verify.

Each App lane retains all 466 cases. All sixty new I167 executions are reconciled:
fifteen pass per Windows architecture, ten pass/five explicit dedicated junction
skips per Unix lane. The shared-object effect case's ordinary control runs
everywhere; its junction branch runs on Windows. Overall App results are 449 pass/
17 skips per Windows lane, 389 pass/77 skips on Ubuntu, and 391 pass/75 skips on
macOS. I163/I164/I165/I166 subsets repeat at this producer with their own explicit
scope/skip inventories. These hosted controls are preliminary; native interaction,
network and exact-candidate qualification remain separate.

Private `FileCatReleaseEvidence/ci-37541603291-assets-attempt1-v1`:

| Path | SHA-256 |
|---|---|
| independent-assets-ci.json | b8393766eae19dca1ebf82cb297b7d7fd57987514de1ba88bd5af947634c6dfd |
| independent-fixture-ci-v1.json | a7c79bbc5ae35ed64f01623e3e6851931b68ccc16fa8769e1db2c7170e5af65e |
| independent-producer-policy-ci-v1.json | 96deabad2189c9c3232a851761c51b993fc3ce4be697ff1efa39545c6594b671 |
| independent-draft-guard-ci-v1.json | 829ab8a7139f31968690836aeac5d11e202e3940abf6079f88b38768d174d6f0 |
| independent-separation-ci-v1.json | 4a1a5046ffebd6f6e98003d6fa01d4ecdbf868e7a9e51517b711aae04b5cad00 |
| independent-restore-ci-v1.json | 36a201ea99abe7819bc4c6415f9eee3197064849db748c0be975f3bd56d57c97 |
| independent-i163-ci-cases-v1.json | 660ffbc0c90e64c12e143e82029c1e26acfa0d94e1772aaf73fa3846b888fff5 |
| independent-i164-ci-cases-v1.json | 4806375997da8c4fff62e34c3c05ad2121d8f29659bf2d22a673743d2478bf0f |
| independent-i165-ci-cases-v1.json | 53f3ce6c345dab9bd49c627dbf0f2cf9fab5fb4619776a8b3e9f840072855de7 |
| independent-i166-ci-cases-v1.json | f99a9ffe41fd321545a08bfce136bca463faa42c3d0ac636812b537ff3dd8469 |
| independent-i167-ci-cases-v1.json | d3cb51eb4cf430c9c6ba38436ee143fcc0300d0c96fb20840b787840973a6446 |

Broader I16/I17/V23/V24, source/configuration races, full path/parser semantics,
network/native desktop effects and exact-candidate qualification remain open.
No physical-source opening, persistent machine/Git setting, candidate/tag/stable
publication or human GO occurred. Owned fixture directories remain private
evidence; no unrelated user files are changed.
