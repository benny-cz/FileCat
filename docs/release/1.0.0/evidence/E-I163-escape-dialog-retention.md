# E-I163 — closed dialog retained by the Escape routing guard

2026-10-06. Preliminary I06/V12 lifetime correction. The original production
payload is 6215329ed67da6c8463a996eb51b12ac569194aa; correction source is
1669cb63c2dcb24ed1cdeb1bff95317f7ca997ef. This record separates the original
observation, expected baseline failure and corrected committed tests.

## Defect and correction

The dialog service kept the last Escape `KeyEventArgs` strongly. Its `Source`
referenced the closed TextBox and therefore the detached dialog tree. Keeping
the service and empty host alive after a normal cancellation leaves neither
tracked control alive; closing with Escape leaves both alive. Releasing only
the stored guard makes both collectible. The service reports no open dialog
and the host has no children in both cases, so visual closure alone misses
the retained tree.

The guard now stores a weak reference to the event. The event remains available
during routing to prevent the same Escape from closing an outer dialog; the
service no longer extends the closed view's lifetime afterward. There is no
forced collection in production. The durable regression tests observe actual
prompt controls, keep the service/host alive and include a nested-dialog routing
control, without inspecting the guard's implementation.

| Producer / scope | Result |
|---|---|
| Unchanged original payload; causal observer | Normal cancellation: zero of two tracked controls retained. Escape: two retained; zero after releasing the guard. Natural process exit 0; no owned payload process remains. |
| Original source with new durable tests | Two pass; the Escape collectibility regression fails as expected with both controls retained. Exit 1; zero skips. |
| Working correction plus affected Escape/chooser checks | Eight pass, zero failures/skips, exit 0. Nested Escape closes only the inner dialog. |
| Clean raw Git export of committed 1669cb6 | All three new regressions pass, zero failures/skips, exit 0. |

The original private observer uses 33 original assembly references and four
verified private binary/configuration files; no original product DLL is rebuilt.
All 352 original payload pins match both copies. Each actual baseline/fixed/clean
test payload has 141 separately pinned files. The clean export verifies all
1,049 canonical source blobs and sizes before and after execution, and injects
the exact source revision into the isolated build. Actual loaded App and test
assemblies are retained separately from the original observer's product bytes.

The first clean-export preflight rejected a configured CRLF conversion from
`git archive` before compilation; v4 ran no compiler/test. That failure and the
one mismatching input remain retained. V5 exports raw Git blobs and verifies
Git blob IDs as well as SHA-256. Existing unrelated CS0618/xUnit2031 build
warnings remain recorded; they are not attributed to this correction.

Private `FileCatReleaseEvidence/dialog-escape-lifetime-20261006-v1`:

| Retained path | SHA-256 |
|---|---|
| host-v1/result.json | 2a995f56a9099e9a8c0c06c88f27fba1257ef797b10bd56c77ef0e21fadda7ea |
| baseline-tests-v2/command.json | c98ab1d4fa96a53f84f63993f07267bcc218cb1084fe82a7869db4b12b4530d3 |
| baseline-tests-v2/results/baseline.trx | 3091813cbf73c1b737351b1f9d517b3852d833fb968e494f62911c682c69b16a |
| fixed-tests-v3/command.json | 6e81857c63656cd7d385ca03e9f6b20468f71b278af63f0a8d202515ab594596 |
| fixed-tests-v3/results/fixed.trx | cee0403cde9d90861fdd45c1f8d7b19e6e41b877960785248cbafa865254ba26 |
| archive-preflight-failure-v4.json | cc8d35dd5e87f2803407ea161c4be6d38eb580f85d7b7188e4dac65a79a911fa |
| clean-fixed-v5/command.json | 2d1db5eb7a4da53693a61601fa8ff67eb6043bee87930487b5cf64aa75b59229 |
| clean-fixed-v5/results/clean.trx | bfc3cf37d2c178c13916518b561815fac9ebe7ac500f8258e7899587b9e2c78f |
| clean-fixed-v5/source.zip | cb54c5662ea7626f1ff8d8610ddbbf40e95c163daff093c33e3ad35751e2965b |
| ci-summary-v6.json | 4f9b47e103e91cbea1fbe56f7fc255512169c9f118b89af1b90bca98664122d4 |
| independent-dialog-lifetime-v6.json | a894afe00c8a611f0c5ad51a5fdaf6ae46ba8c6114cd0094c47fa17d4d046698 |

The v6 seal verifies 45 retained files, both copies of four private observer
files, all three 141-file test payloads, the original product/reference pins,
the clean source archive, all cases and the earlier in-progress CI snapshot.
The final CI collection is recorded separately below; that earlier snapshot
is preserved unchanged.

## Original successful CI attempt

[Run 37527956906](https://github.com/benny-cz/FileCat/actions/runs/37527956906),
attempt 1 at exact 1669cb6, passes the policy and all four required lanes.
Each actual App inventory contains 418 cases and all three new lifetime cases
pass: twelve distinct executions, no new skips. Fourteen complete inventories
retain every outcome and explicit skip message; green CI does not turn skipped
integration/hardware cases into passes.

| Lane | App | Core | Windows platform | Remote |
|---|---|---|---|---|
| Windows x64 | 401 pass / 17 skip | 819 / 57 | 175 / 33 | 88 / 28 |
| Windows ARM64 | 401 / 17 | 819 / 57 | 174 / 34 | 82 / 34 |
| Ubuntu 24.04 | 352 / 66 | 829 / 42 | Not run | 94 / 22 |
| macOS 26 | 354 / 64 | 828 / 43 | Not run | 94 / 22 |

All inventories have zero failed cases. Original complete logs and nineteen
retrieved artifacts match server digests. Four clean SDK/compiler receipts,
92 actual locked dependency graphs, picture-admission controls, policy/draft/
package-set controls and ARM64 startup/drawing/installer receipts are retained.
Actual development packaging jobs and draft publication skip on this source
push; no package, release or final qualification is inferred from those skips.

Private `FileCatReleaseEvidence/ci-37527956906-assets-attempt1-v1`:

| Retained path | SHA-256 |
|---|---|
| independent-assets-ci.json | a8181bf44dd34f0cf7a245f6ea29f6cf62bcb0f526e02b61c1a6571bb7923e31 |
| independent-fixture-ci-v1.json | e9335042931a98bb69b3a2430090353e0b699b111964935f8308c933cf83c790 |
| independent-producer-policy-ci-v1.json | 08f959d3282bd03d1b8bc50b747be08712c1d3dc6c6a38ff8b4e610a6f94c344 |
| independent-draft-guard-ci-v1.json | f2fdfa23cb9909f240f6ca39da8b1669f5193a7a1cd45fc6b80400a109d87530 |
| independent-separation-ci-v1.json | 29c83c0a8b987662e53954b218dfd4100f8f813c4acd81dfb4389a10595e50e2 |
| independent-restore-ci-v1.json | e95dbfebbf5e68f438465cf669bd98cb0a300b8db875a9e5c6c98d318cc92784 |
| independent-i163-ci-cases-v1.json | 898865ae8d221ba4501f068eb243909d61c70ce3e02a1c1a52189b83c5c8e60c |

## Qualification limits

These are headless component lifetime/routing tests on real production APIs;
they do not qualify native desktop input, render frames, sustained performance
or final-candidate workflows. The last closed dialog's retained graph is proved,
but no process-wide leak rate or numeric aggregate-budget violation is inferred.
I163 is Remediated preliminarily. Broader I06 consumer/worker/frame/Shell/DPI/race
and materialized-workload qualification remains Open. Older bitmap/native
payload evidence keeps its original 6215329 producer, and any affected candidate
tests must use the new artifact identity. No physical source, persistent machine
setting, candidate, tag, release or stable publication is used.
