# E-I263 / E-I264 — Markdown resource identity and directory links

Recorded 2026-10-09. **Preliminary I25/V10 component remediation.** Runtime producer `dc1a86e8bf5bc4805c3c7b4cd88b761ad0c5c536`; cleanup-only test refinement `0af65884f7dc443bf57a4f249277e2c687a20c92`. No native browser or candidate qualification is claimed.

I263: supplementary Unicode image filenames were encoded as replacement characters, preventing their rendered local requests from loading the original files. I264: requests through intermediate directory links could read outside the page folder. Eight picture cases, fourteen link cases and one requested-CSS-MIME control give **original nine failures/fourteen healthy passes → fixed 23 passes**. Production encodes complete supplementary scalars, resolves the folder anchor and every path component through a finite link walk, reads the contained resolved path, and preserves the requested MIME extension. Every noncyclic target is read before the request. All targets are owned synthetic fixtures.

The unchanged compiled observer gives fourteen Windows observations on the original payload, fourteen on the first private correction and fourteen on final v3. Existing compiled Core 56/App 21 cases retain their identities/outcomes. Private original v1/v2 relative-link and MIME corrections, guessed App-receipt and reader errors retain their distinct actual versions. Private restoration archives/removes 37 files and twelve link objects; all thirteen exact roots are absent and sixteen recorded PIDs were absent.

The exact dc1a86e full local run gives Core **2723 passed/one GnuPG failure/64 skipped**, Remote **2020/156**, Windows Platform **211/38**, and App **1217/25**. A same-82-payload full Core repeat at a shorter temporary path retains exactly the same outcomes and skip messages. The same compiled failed GnuPG fixture passes alone under a still shorter path. Separate native controls show short-home key generation succeeds and a long home fails with Filename too long; these controls do not retrospectively prove the two full-run IPC failures' exact cause. Both failed TRX/controller assertions remain. The record explicitly declines four-full-local-suite success. All 1,289 source Git blobs/ZIP members and 314 compiled suite payload members recheck. Fourteen fresh native resource observations use the exact committed Core payload and unchanged compiled observer, with no overlay/rebuild.

Original CI 37912152348 attempt 1 fails on Linux/macOS: all 46 actual Markdown resource observations have correct semantics, but 28 cases fail in fixture disposal of a cyclic directory link. Two original server-digest archives and their full TRX survive. The single test cleanup branch now unlinks Unix directory links as files; production, assertion bodies and skips are unchanged. Current Windows validation passes all 23 controls and finds all owned case roots absent.

The cleanup-only original CI 37913440150 attempt 1 passes both Unix lanes but fails Windows x64 on eleven archive helper-stack assertions. All 22 raw archive error/cleanup observations remain correct; the failed server-digest archive and full TRX survive. The subsequent two-test-file correction is `c0cfb7efbcf9664ef56c258257b391ddfb5ba35a`, with production unchanged from 0af6588. Its complete local Core run passes 2724 cases with 64 exact skips, preserving all 2788 logical identities and all other preceding outcomes. One PE inspector theory explicitly retains its changed payload-path display label at the same class/method; all raw labels survive. The original GnuPG failure becomes a pass under the longer temp-root conditions. I265/I266 retain their own fixture record.

The c0cfb7e original CI 37916424327 fails one unrelated ordinary-Git fixture. Its original server-digest artifact/full TRX and actual Windows GnuPG skip remain in I267. The bounded positive-only admission correction a2f6cd9 leaves production/refusal limits unchanged. Current original CI **37918903995 attempt 1** at a2f6cd9 succeeds in all four required lanes. All **25,312** actual case records retain the preceding 25,220 identities/outcomes/exact skips, adding **92 passing Markdown executions without new skips**. Original artifact/member/TRX/toolchain/92-graph/native-API evidence remains at its exact producer; the failed first run is not replayed or relabelled.

Current restoration archives/rechecks fifty files and four link objects, removes them all, and finds all seven exact owned roots absent with thirteen recorded test/native PIDs absent. One owned waiting reader was deliberately stopped before sealing anything after its expected Core result did not arrive; its identity and cancellation survive. No product, GnuPG service or global process is killed. No device, account, Mac/VM, persistent setting or physical-source action occurs.

Remaining I25/V10 includes actual WebView2/WebKitGTK/WKWebView rendering, fonts/CSS/permission/download/popup workflows and candidate qualification. Static symbolic-link observations do not establish Windows junction execution, case-sensitive macOS volumes, replacement races or atomic opened-handle identity. The historical full-run GnuPG failure is resolved for the exact current fixture producer; broader provider/native/candidate qualification, all 24 final-candidate campaigns, physical HOLD and explicit human GO remain.

| Retained input | SHA-256 |
|---|---|
| Reviewed child handoff — `child-markdown-ready-inputs-v1.json` | `f51ed86cf5d06673e7ba19ed18b9e6a5707f2d4b999d46cc3cf516567bf95d54` |
| Root source/raw review — `root-markdown-integration-review-v1.json` | `66a2120ca8cd113906d224c76df014489d67a2aab13467c77c3fcad34670bd52` |
| Private full semantic proof — `independent-private-markdown-semantics-v2.json` | `ed23dd4570b8ce235b0d9a048d1f7bba08c522b263fa05c5e3028d66e1c863d7` |
| Private owned restoration — `owned-markdown-restoration-v1.json` | `6d8e7f3abdc46024c40473a6633b2930134c5c4ff2cb2791ed3833f15f45698e` |
| Private retained final — `independent-owned-markdown-final-v1.json` | `97320cdc263f8ae0bcd125822dcb234a8e0356338abd6a2193c2a53bcfbbe3eb` |
| Exact runtime commit and push — `markdown-integration-main-push-v1.json` | `a6df3b2ffe03b61d141c0efd461f8202dde1847583938e9b49c9789e8d93b3db` |
| Original full local controller — `command.json` | `9808452556332e167059de9c5b1abd7922c552ade8d2c45dde95ab60abb5de82` |
| Original full local assertion — `run-committed-markdown-full-v1-stderr.txt` | `b33ff09d30800b0a39905838789f608cd8422c740d8ef8f001dd2b662c0bf552` |
| Native home-length controls — `owned-gpg-home-probe-v1.json` | `20e7e532ba53ff2b8e801fbae6955119b3ff2a439f726ce2fcb8dae3d5f4fb27` |
| Second full Core command — `core-command.json` | `32be01fdb5142656b8423ad49df9aefb78d589601b9b4afa75e04fa55d497f8d` |
| Second full Core assertion — `revalidate-compiled-core-short-temp-v1-stderr.txt` | `3db0ab3d9ab5c84fa03bcd2fb033570f68fac2392fb4688f9f9ca5c746ac7c57` |
| Qualified same-payload Core and native result — `committed-markdown-short-temp-final-v1.json` | `ee58ba5c0553759f37add4d0a25cb3ca1013a93166d6cc7a1ef26d7ec5a62153` |
| Owned waiting-controller cancellation — `owned-waiting-controller-stop-v1.json` | `fc05ea838b521d16ca66b10aa7a16a369fa0ec7a1f26b79bbac80d4422e2b422` |
| Independent committed component final — `independent-committed-markdown-final-v2.json` | `7b3a3080bab83f4d7e24af0263a1d99dce71078ed5ee4e702fc5189048fe747b` |
| Committed owned restoration — `owned-committed-markdown-restoration-v2.json` | `00885600b1d12aead660e70c159a0b2741b0988f8cc578ef4ed03832123c6ce6` |
| Original Unix server-digest failure proof — `independent-original-unix-fixture-failure-v1.json` | `55e43a936cdc0bf4dc08ce9d07e15940bcdb3693dfa9fedee45b0a3b29ea3688` |
| Unix cleanup-only commit and push — `unix-link-retirement-main-push-v1.json` | `c7a4d31d1f055858d414d7732707bbfa2e79b4a1b95db8e7be6772d6d68e542e` |
| Current 23-case local validation — `independent-current-cleanup-tests-v1.json` | `bd9562d88871bb8f79e816dc6b9adbf1daaddd4bf2267f807ef37690e9d61324` |
| Current original CI preparation — `preparation-v1.json` | `806559767df1371bb726fe89e095bf9130023ead9230c185e5c9da0dca442be0` |
| Current original CI collector — `collector-v1-command.json` | `4817b74ef91e231830e0d9e6e37457f7e180b264b8aeccee11feae763319ce95` |
| Current four-lane CI independent final — `independent-markdown-ci-final-v1.json` | `120bed3b5aaa44aae89202e6dffd4d1ac7f5730a265680eb213718c78673c821` |
| Current CI semantic reader — `seal-markdown-ci-v1.py` | `deae6d933107b70d45980fa52309993e4039a4674d1025b9bf7ca69a902c86f0` |
| src/FileCat.Core/Content/Markdown.cs — `Markdown.cs` | `5bc364480d67be575c8a6428f7e2ff4f5d2bca7f5a705bf5504d4df17fc344f7` |
| src/FileCat.Core/Content/HtmlPage.cs — `HtmlPage.cs` | `fc4e6c4c256f1200bdeac8de89d8186e95ea35e8365640e04d60ec634598d3b5` |
| tests/FileCat.Core.Tests/MarkdownResourceIntegrationTests.cs — `MarkdownResourceIntegrationTests.cs` | `9d3b5581d36c8612513231ea5ce1fc1d8b6ae4811e06720d9813dc9dc27ade35` |
| Current cleanup-only test input — `MarkdownResourceIntegrationTests-unix-cleanup-v1.cs` | `e8b1481f66eda4bbe3da5e38933ba6563e17fb385228cccc7082ae2b7bca610a` |
| Original Windows failed artifact and 22 actual cases — `independent-original-windows-fixture-failure-v2.json` | `0f7bdc3d5e9c8caac01418011648ac3ac50efb16c713a3a18a7468d9ee2b21b2` |
| Current full Core fixture qualification — `independent-committed-fixture-final-v1.json` | `de2518040cc9e2dc5b2de4e9b0b20a80f9647bd3f0c9330603165fc2437f5fe8` |
