# E-I165 — quoted Git values bypass worktree admission

2026-10-06. Original clean producer f449554bc2db94b2108769f09fa0b134b31baa75;
correction cceb9903630f38df44f45ffb57d84fe3d860558c. High path-admission risk
under I16/V23 B10/V24. Remediated preliminarily; broader/candidate scope remains open.

## Defect and correction

The actual unchanged native component admits three Git-valid spellings:
`"../tar"get-link`, `".."/target-link` and `"../target-""link"`.
Pinned installed Git decodes each to `../target-link`; FileCat previously
checked only the first quoted fragment. Each admitted case returns an Untracked
badge for an owned target-only file reached through the owned junction. Two ordinary
positive controls and the direct spelling's refusal pass. No server or physical
source is involved.

Git permits quoting parts of a configuration value and supports five value escapes;
see the primary [configuration syntax](https://git-scm.com/docs/git-config#_syntax)
and [value parser](https://github.com/git/git/blob/master/config.c).
The correction decodes the complete logical value before checking its path, joins
quoted/unquoted segments, preserves quoted/internal whitespace, trims outside
whitespace and recognizes comments outside quotes. It handles the five supported
escapes and refuses unknown/incomplete ones. Existing continuation/input bounds remain.
This is value-decoding remediation; complete Git parsing, indirect paths and races
still need broader qualification.

| Actual producer / comparison | Outcome |
|---|---|
| Original clean f449554 native component | Three quoted bypasses return the target-only badge; direct spelling refuses; two ordinary positives pass. |
| Original Git source with durable test overlay | 45 pass, five expected failures, one explicit network-fixture skip; exit 1. Three quoted-boundary failures and two invalid-escape failures; ordinary quoted controls pass. |
| Working correction, affected Git suite | 50 pass, zero failures, one explicit network-fixture skip; all eight new cases pass. |
| Clean raw committed cceb990 | Same 50/zero/one outcome and all eight new passes; all 1,052 canonical source blobs verify before/after. |
| Unchanged native probe against clean cceb990 | Four junction spellings refuse and two ordinary positives pass; all 141 executed payload files unchanged, natural exit 0. |

The ordinary ignore-file fixture now writes a Git-valid escaped Windows path.
The original native probe is unchanged between its original and corrected runs;
its embedded f449554 field stays baseline metadata. The clean correction's
actual loaded DLL hash is 92e409dbda77c2d5103b1d6e954b5c8c6240ec635a97a5c2f9d08119fe94aa3a.
The probe and all four configuration restorations/junction removals use only owned
fixtures; target bytes remain unchanged.

The two working test runs retain selected source snapshots rather than a claim
of complete source identity. Their actual compiled About literals are recorded:
baseline has the earlier text, working correction has the owner's concurrent text.
The final canonical build deliberately excludes that unrelated edit; its complete
source and all actual payload bytes are sealed. The owner subsequently authorized
and separately pushed that edit as `08f75e723c53368dd88e6832151ae0ef850cde42`. Earlier evidence is
not relabelled as this later producer. Existing unrelated build warnings remain.

Private `FileCatReleaseEvidence/git-quoted-worktree-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| result-v1.json | c6df2d6ab503bdbd0102b8f8249e0c1564b1ceca2c4f560155bb8f62bfdcb9f4 |
| durable-baseline-v2/command.json | 9c445a40d960383eb63669538f63f02bc01755190c8afcf0d4a889c170f58cec |
| durable-baseline-v2/results/baseline.trx | ed2ab555a0bfab0d36e603006eaf289db46d999d8bdfec5d64575cf2f187e9ed |
| fixed-tests-v3/command.json | aefa59f14cfaa53ae3139a9da8b9f08444b9b11e8c239dcc6dfe42f7284350d0 |
| fixed-tests-v3/results/fixed.trx | f8518c9448cf25eefb4dadc9acdadde956008010def90d6fbe5dd7b30d4ad604 |
| clean-fixed-v4/command.json | 918ab69f6b4840238a7ddc864de30acd78b94ae5868699639e4611283d20a7dd |
| clean-fixed-v4/results/clean.trx | 391d657c5721b8731f7a837336eb1faa92a91b165b6884333472a2e35a39e7d9 |
| clean-fixed-v4/source.zip | 2c6d3ed5fb92daa83f8446a4214681767f810e38004ec85a8475cb8b8ffb0916 |
| committed-native-v5/command.json | ee147d2f0403a60713bc23a14b85faa91fe357d50013b0b04aa07b923abdde38 |
| committed-native-v5/independent-committed-native-v5.json | 7ce916edb064f189165855699c4cfde13499ff05b2105443e3c055bb46cf6dfd |
| ci-summary-v6.json | 1aaad38c1fbcc0c0b182dcf5809b7120dd05ec76028e0cd5ffbc7ea3d5d38ad5 |
| independent-quoted-v6.json | 995ccaabe5570d10d535bac01678729b7a06d4b12fb299d24de65daab362c8bc |

Independent local seal v6 verifies 43 retained files, four private probe files,
423 actual test-payload pins, 141 original clean component inputs and 1,052 raw
committed source blobs. The original and corrected native six-case outcomes,
three Git decode controls and all TRX case names/outcomes/skips are retained.
The original CI snapshot at 21:48 UTC is unchanged; [run 37536153209](https://github.com/benny-cz/FileCat/actions/runs/37536153209)
attempt 1 has subsequently completed successfully; the original in-progress
snapshot is preserved and its claim remains limited to that instant.

## Original-attempt CI completion

Completion collected 2026-10-07 CEST. Original run 37536153209 attempt 1
passes producer policy, Windows x64/ARM64, Ubuntu 24.04 and macOS 26, including
ARM64 package startup/drawing and installer compilation. The main-push tag-only
package/draft jobs explicitly skip; no release is published.

All 19 artifact archives match actual original server digests. Fourteen complete
TRX inventories retain every result/skip message, with 435 App cases per lane.
The eight I165 cases have 32 distinct executions: eight pass per Windows
architecture; five pass and three junction cases explicitly skip per Unix lane.
The I163/I164 subsets also repeat their twelve/thirty-six executions with their
own expected outcomes. All compiler identities and 92 locked restore graphs verify.

Private `FileCatReleaseEvidence/ci-37536153209-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | f9e98a29539a3d1b240c5631801bea6bde2d77bf832f22ba6783f2c42859693d |
| independent-fixture-ci-v1.json | c878a51c2197e36afc65f5a5a374cff3b5e52bc7dc218d72f894626fcd0a5d6c |
| independent-producer-policy-ci-v1.json | 5e1b785c9a4a83ba159ce36829c9b6bd2f2ed108c7c040fb089b106071c5b486 |
| independent-draft-guard-ci-v1.json | 0112047b55e866680e3bec52f1d8d6f6a408ed990fff63ab79f744d967386a94 |
| independent-separation-ci-v1.json | 66d3fefa527c344f0b497325e234503fadd95887023616b74cd51651973f8320 |
| independent-restore-ci-v1.json | d3bb34e412ced3e6beb52728ef5567cd17c1fc2fed1e3c11a8c0794a29749d9d |
| independent-i163-ci-cases-v1.json | 72966852b427f63ae3d4f3ce54985a06dcfa86e9698b315c3a5886720a6ff6ca |
| independent-i164-ci-cases-v1.json | 11bf19f3483dda2cd4fcd5943145cfe01ab144f0c6a6941a0483ec13c436011e |
| independent-i165-ci-cases-v1.json | 59030a128f7ab8b9a68720c93a45e221ec392e74fd2ab4758edaa3a56490b6c6 |

These are preliminary finite regression/build/package controls at cceb990.
The subsequent About edit, native GUI/network/hardware scope and candidate
qualification are not relabelled as these artifacts.

## Remaining scope

No network packet-capture result, installed package, desktop workflow, physical
source or candidate qualification is claimed. Home-relative paths, other indirect
metadata spellings, Unix mounts, concurrent swaps, broader I16/V23/V24 and
exact-candidate reruns remain open. No candidate/tag/stable publication or human
GO occurs.
