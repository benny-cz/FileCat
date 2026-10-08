# E-I231 — picture request and response bounds

Exact source `8a5833f9b8f0fd0721db6fc3375350625dccbc47` corrects two proved parent-decoder defects. A request below the worker's minimum could reject its otherwise valid answer; a request above the absolute maximum could let the parent attempt to read an oversized pixel response. Severity is Medium for picture/API resilience and response admission. This finite finding establishes neither a hostile ordinary-UI exploit nor whole containment closure.

## Controlled defect and repair

The final same-source fourteen controls execute first against unmodified product `67f648a` with only the new tests overlaid: eight genuine failures and six healthy passes. Eight public decode cases use an independently checked owned 32×24, 3,126-byte BMP and requests -1, 0, 1, 15, 16, 8192, 8193 and int.MaxValue. Six synthetic FCPX cases send a 8193×1 or 1×8193 header for three requests; a guard fails if any pixel read is attempted.

The parent now clamps the request to the same 16–8192 range before dispatch and independently before response parsing. The worker exposes its existing minimum as a shared constant; CLI behavior and its existing clamp remain. Fixed working-source controls pass all fourteen. Exact committed-source reruns independently verify the same input bytes, source/output dimensions, all six invalid-header refusals and zero pixel reads. Headless bitmap pixel persistence is unavailable and no output-pixel fidelity claim is made here.

## Canonical and four-platform validation

Two clean, independently blob/archive/payload-pinned exports of `8a5833f` qualify maintained Remote 1,602 passes/135 explicit skips and full App 1,211 passes/25 skips. Every 2,959 predecessor case name, outcome and exact skip message remains; the fourteen added App cases pass. Physical/network fixture environment variables are unset and all original production deadlines remain.

Original CI `37842873148`, attempt 1, succeeds in all four required lanes. All fourteen new cases run on every lane: 56 passes, comprising 32 decoded BMP-input/dimension observations and 24 exact oversized-header refusals with zero pixel reads. All 21,532 immediate `67f648a` predecessor names/outcomes/skip texts are retained without transitions. Sixteen existing edit-source unavailable/oversize controls and all 160 current FTP/Git executions pass. Windows x64/ARM full App each has 1,217 passes/19 skips, Ubuntu 1,143/93 and macOS 1,145/91.

The independent collector verifies 21 original archives against server size/digest, every 2,731 member, fourteen raw TRX inventories, four toolchains, 92 locked restore graphs and 25 original API receipt sets. Sixteen Git adverse/recovery and 212 rename observations are reread. The parent rehashes all 31 essential collector inputs. Prior transfer/notice/worker byte-oracle evidence retains its actual producer; CI counts do not relabel it as `8a5833f`.

## Retained incomplete runs and restoration limits

Original baseline v1 stops during build with no TRX and no detailed stderr; its cause stays unavailable. Baseline v2 included four invalid pixel assertions because the headless bitmap does not retain pixels between locks; those failures do not prove pixel corruption. Fresh final v3 removes that unsupported oracle and preserves eight actual defects/six positives. Final test source is unchanged between the controlled baseline and fixed working/canonical runs.

An early working full-App observer had a 300-second aggregate fence, expired without a result and left one exact owned App process; its verified identity was cleaned up. The first clean full-App run preserves two genuine no-disk-space fixture errors on C: while creating >1 GiB files, then expires at its 1,200-second observer fence with no TRX. Native CPU/I/O samples establish a quiet wait, but the active test and causal connection to storage are unknown. A later exact-PID cleanup observes that App already absent and kills nothing; it is not relabelled as the termination cause.

The fresh successful full-App run sets TEMP/TMP/SYSTEMTEMP only in its child environment to an owned E-drive folder, with >60 GB free at preparation. Parent and machine environment remain unchanged. Its completed test namespaces are removed after verifying no owned App/testhost remains. Initial recursive cleanup encounters locked compiler analyzers; nine compiler-cache files (1,642,376 bytes) are inventoried and retained until released. No compiler or unrelated process is killed. Earlier aborted C-temp namespaces are not claimed restored. Raw source, payloads, logs and both incomplete results remain. Reader source-scope and leaf-schema refusals/corrections are retained rather than rewriting executed inputs.

The first private document writer used the Windows default encoding and produced mojibake; a follow-up reader refused that encoding before mutation. The corrected writer reapplies changes from the pinned UTF-8 originals and preserves the activity log's original byte prefix. Both original scripts and the correction receipt remain private; current document/row/link and evidence-byte guards verify the final registers before commit.

No VM, Mac, USB, native desktop, installed/elevated/helper account matrix, reference acceptance, full I08 closure or candidate qualification is claimed. Physical-source HOLD and explicit human GO remain.

## Selected evidence

Private `FileCatReleaseEvidence/picture-request-bounds231-v1`; linked nested inventories retain exact originals and corrections.

| File | SHA-256 |
|---|---|
| independent-focused-bounds-v1.json | 0ce94242ffcdfdc2b859f1289458ce5e3bc1e1f92f4410f28b690ec5c2087512 |
| independent-committed-bounds-v2.json | 18ea0badbe258159a1c1c76c51c909106f5a2128e9de83fa80031c0d2d8214ad |
| parent-independent-ci-reread-v1.json | c38e2231102a6f90e40a6bb57b9a04d4d7bb2331ad5e1cdc1b9a8ef402431a0d |
| clean-v1/command.json | b444887bdd6207d8b4fc339ee9510b8dbd11d1d9af5fed40590d09fa79a96716 |
| clean-v1/results/remote.trx | 2150ce5b0249c28c3400ed118f891c54da294969cbc3e0dbc53c2a8f921e9a75 |
| clean-v2/command.json | f9fc76e0235355606481a549b1cc50d3591c2e6f02b3e0c28598f372971e8aea |
| clean-v2/results/app-full.trx | 3543aaa546ad6225be548507fc14c48e2222623a1662b0a7034db1a9daec8dcb |
| observer-corrections-v1.json | c5625d5fdb7cf4abe38b55a2b6d274692d3ecaae8dce53826069c732d34a6630 |
| aborted-working-restoration-v1.json | 13b1d65f0c25fe83371b5fbea9e9e35f6d86e25b632c1b9a0dd66831f7094ed7 |
| canonical-app-abort-v1.json | c92b81d852bbdfab08f093ba832e3773246f20d5fd88c4b9d2fab9e40e9d6e4d |
| restore-owned-temp-v1.ps1 | 4a938c6fec42714d5eaca782c2f9145a6e0dbd4b2e802ef1620b9f1cff572769 |
| restore-owned-temp-v2.ps1 | 61344b0bc259b9295babeb211c6ac5f363970f37304901a6797b143e36bb194e |
| owned-temp-restoration-v2.json | ae19d2bbef69281cded1be6d0fe94db27baa2dba2ceac0e89ceeeedddedcd852 |
| ../picture-bounds231-ci-v1/independent-picture-ci-final-v2.json | 91667297c6fcc0aaf0137fe51a2fc175091bbb22ea72dd4ed9d9d7a3d30f6cf3 |
| ../picture-fullapp231-diagnostic-v1/independent-local-wait-observation-v1.json | d66a6efbb450cc7aee929c14a49fda1341491a32b9cd1bf11586441a679cb001 |
| ../picture-fullapp231-diagnostic-v1/independent-ci-app-observations-v1.json | 443cc10a228409af5e512876798e0276e83a337b9872d569e1e9c5b5302e9db0 |
