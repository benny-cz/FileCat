# E-I164 — continued Git values bypass worktree admission

2026-10-06. Original clean component source
1669cb63c2dcb24ed1cdeb1bff95317f7ca997ef; correction
f449554bc2db94b2108769f09fa0b134b31baa75. High path-admission risk under
I16/V23 B10/V24. Remediated preliminarily; broader/candidate scope remains open.

## Actual defect and correction

The unchanged production `SafeRepository` and `ReadAsync` run through a private
BCL-only native probe against the earlier clean committed App payload. An ordinary
repository returns the expected Modified badge. An ordinary spelling of a
configured junction worktree is refused. Splitting that same value across two
physical lines admits it and returns Untracked for a file only in the owned target.
All three variants reproduce: unquoted LF, unquoted CRLF and quoted LF.

The explicitly pinned installed Git executable independently decodes each value
as `../target-link`. Git documents that a trailing unescaped backslash and newline
are removed before interpreting the continued value in its
[configuration syntax](https://git-scm.com/docs/git-config#_syntax).
FileCat previously examined each physical line separately and checked only the
incomplete prefix, which did not name the actual junction.

The correction checks logical lines before the existing configuration/path
admission. It joins continuations, preserves quoted/escaped characters for the
value reader, and stops comments at each physical line. Backslashes inside
comments and escaped trailing backslashes must not hide a following filter/include
section. Incomplete/oversized input is refused; file-size and decoded-character
limits are each 1,000,000, and physical line endings count toward the latter.
Ordinary continued worktrees stay available.
This is a narrow continuation correction, not a claim of complete Git parsing.

| Actual producer / comparison | Result |
|---|---|
| Original native component | Two ordinary controls pass; normal junction spelling refuses; three continued spellings admit and return the owned target-only badge. All six case outputs retained. |
| Original source with durable overlay | 26 pass, three expected continuation failures, one explicit share-case skip; exit 1. Six other new controls pass. |
| Working correction, all affected Git cases | 42 pass, zero failures, one explicit share-case skip; exit 0. All nine new cases pass. |
| Controlled native comparison | Same original probe; only FileCat.dll changes among 141 input files. Four junction spellings refuse; two ordinary controls pass. |
| Clean committed f449554 raw export | 42 pass, zero failures, one explicit share-case skip; exit 0. All 1,051 source blobs verify before/after. |
| Clean committed native component | Unchanged original probe repeats all six cases against actual clean built bytes; all four refusals/two positives pass, with all 141 input files unchanged. |

The intermediate affected run before the final physical-line accounting adjustment
also passes 42/one skip; its exact source, outputs and payload are retained separately.
Four actual test payloads have 564 individual file pins. Three baseline failures
and all nine new controls have durable names/outcomes in retained TRX inventories.
The frozen native probe still embeds its original 1669cb6 source field; independent
records identify the working overlay and final f449554 DLL separately rather than
relabeling that field. Existing unrelated CS0618/xUnit2031 build warnings remain.

Every native fixture restores its configuration, removes only its exact owned
junction without recursion and preserves the target bytes. No GUI, physical
source, network fixture, credential transmission or repository-defined program
execution is measured. The share integration test explicitly skips in every local
run. Earlier receipt labels removed an unrelated host variable; the independent
seal retains that scope limit and uses the actual skip rather than claiming a
captured complete ambient environment.

Private `FileCatReleaseEvidence/git-config-continuation-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| result-v1.json | 8c8520961e65d63961def48d09a490aa4a175490d8d9516b70bcd3a7c282bb76 |
| durable-baseline-v2/command.json | 93c5149ea3b7d3505f0e577430884636a0760030e45a86781048a2b4a1a89202 |
| durable-baseline-v2/results/baseline.trx | f4ecd9ded7c7932e1d180a87469dc854a808617826606eea7fbe822209668431 |
| fixed-tests-v5/command.json | 7e1fbca38b0bdd8ee27eb25fea8d91d4394ab50868cf13b9e3ac29c1440f1976 |
| fixed-tests-v5/results/fixed.trx | c61cd3cffecb05edebb5431e2fd8281d2565209f9786fde0327fbbb020eafdc5 |
| controlled-native-v6/independent-native-overlay-v6.json | 02c038406440f6a006f85193351c83ef5bfcd42d6927d54d3c1b8ae8555a81c8 |
| clean-fixed-v7/command.json | 17e721a7e31574ce5c6c181476339b1b061f35dddad1b22a162d64108ede2d21 |
| clean-fixed-v7/results/clean.trx | 88bb5726652f3413f809d65f7d99b66f770dc3079683dc0c902f1060e4ddec27 |
| clean-fixed-v7/source.zip | 87b239c23222163484a71ae03d049de70263065ec1738600b4f19e1205aea209 |
| committed-native-v8/independent-committed-native-v8.json | 83cdf4ad38ac8c61a232beda18e9d06ee2fb2219896e9773b9ced10b40e10c88 |
| ci-summary-v9.json | e39925bf2e3771d9330944ccc5bc20b149dcf1d6083b9e0d9d92ac9a659413b7 |
| independent-continuation-v9.json | 56bd2bd76a0160e6ce967daccaef457bf1752aa0aac2515dd420e8c9816a4c76 |

The v9 independent seal verifies 61 retained files, four original probe files,
all four test payloads, native comparison inputs/cases, original Git decode
controls, raw canonical source and the original in-progress CI snapshot at
2026-10-06 21:26 UTC. [CI 37533338023](https://github.com/benny-cz/FileCat/actions/runs/37533338023)
attempt 1 has subsequently completed successfully. That original snapshot remains
unchanged and makes no final CI claim.

## Original-attempt CI completion

The original f449554 run passes producer policy and all four required lanes:
Windows x64, Windows ARM64, Ubuntu 24.04 and macOS 26. ARM64 starts/draws the
actual package and compiles its installer. The three tag-only package jobs and
draft-preview job explicitly skip on this main push; no release was published.

All 19 uploaded artifact archives match their original server digests. Fourteen
complete TRX inventories retain every outcome and skip reason. Each App inventory
contains 427 cases. The nine new I164 cases have 36 distinct executions:
nine pass on each Windows architecture; six pass and three junction-specific
cases explicitly skip on each Unix lane. The three I163 lifetime tests also pass
on each lane (12 distinct executions). Compiler/tool receipts, build/source
identities and all 92 locked restore graphs are independently checked.

Private `FileCatReleaseEvidence/ci-37533338023-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | 68a2bb7c39167c7a4017e30ff684035a63da87cf95fd324a78a22e5f979992a2 |
| independent-fixture-ci-v1.json | 486f5cdcf38418867f350ac86db681d7bfd2b0796128aecd51d90bb63ffd86bd |
| independent-producer-policy-ci-v1.json | 21de838b09adaf92bb0b54d8f750071d70e3e6d3458aea5502bbb17b1c9a31db |
| independent-draft-guard-ci-v1.json | ef987b88588d25cc3c164d05c2cf5806f25b5185fd83cf446f155639d6c82b5d |
| independent-separation-ci-v1.json | 26e583e804823c6f0ea93bb6da4c9f81d906defc6f2ba2482b19941a3d785891 |
| independent-restore-ci-v1.json | a605c595c2441888ab7a7e201ccbbfe5d93f7bcaa032c6f672547be9268e4515 |
| independent-i163-ci-cases-v1.json | cbcd67fbe6989e3b1d3e81e9e9f4b31f2d4fec6731df030b93557376b1ba2d65 |
| independent-i164-ci-cases-v1.json | a9b1a693492691b11d57a974583c0990016a05f276e72dfedca2717355dcec46 |

These are finite regression/build/package-control results at the exact producer,
with complete skipped scope retained. They do not qualify a candidate, actual
Git network contact or desktop interaction.

## Qualification limits

Remaining Git escape/home-relative/alternate/parser/race and Unix-mounted-path
scope, measured network contact, native desktop workflows, installed/candidate
artifacts and broader I16/V23/V24 qualification remain open. No release tag,
candidate, stable publication, human GO or machine-policy change occurs.
