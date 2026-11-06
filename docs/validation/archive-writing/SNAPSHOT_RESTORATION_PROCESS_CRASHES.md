# Restoration process termination and fresh retry

6 November 2026. Follow-up to `3b4cdb98`. This is native backend validation; no new writer profile or production recovery action is enabled.

## Boundaries and behavior

The test driver externally terminates an actual restoration parent at twelve flushed boundaries: after opening/claiming the retained source, unrecorded stage creation, completed preparation, unrecorded native transaction opening, sealed but unrecorded current-version backup, private output preparation, durable restore intent, native commit, post-commit review, recorded restoration, new-job cleanup and new-job closure. Test-only lease/transaction wrappers supply these boundaries; production has no checkpoint callbacks.

Each case first publishes a different archive version through the real NTFS recipe. The fresh restoration then uses actual distinct LPAC assessment and verification workers. Killing the parent before commit leaves the current published version intact through native transaction rundown. Killing after commit leaves the exact original bytes restored. File identity and the full strongest origin declaration remain bound. The original source snapshot and all six old catalog artifacts survive restoration itself. A separately reopened new-job catalog cleans only its own files; the old source catalog independently classifies `PublishedIntact` or `RestoredIntact` before cleaning its files. Early uncataloged artifacts remain reviewable and do not gain deletion authority.

A thirteenth case terminates at restore intent, independently reconciles the failed new job, and performs a second fresh restoration. It checks new job/journal/native transaction identities, a new current-version rollback snapshot and a changed modification-time metadata digest. The loop reproduced a production bug: restoration history required the prior attempt's output metadata digest, so an otherwise valid fresh retry was refused before native commit. The fix keeps immutable bytes, object identity, source/before scope and origin binding across attempts, while binding each fresh attempt to its own verified metadata. It also requires a distinct journal path and refuses another attempt after a recorded success. Same-attempt outcome metadata remains immutable.

## Validation

The initial twelve native termination cases passed. Adding the actual fresh-retry case reproduced the bug, with twelve passing and one failing. After the fix, all thirteen pass. The focused shared journal/reconciliation suite passes 34 cases. All final lanes below have zero failures.

| Actual lane | Passed | Skipped |
| --- | ---: | ---: |
| Windows focused actual restoration crashes and fresh retry | 13 | 0 |
| Windows complete native lane, administrator token | 200 | 0 |
| Windows complete native lane, genuine standard account with loaded profile | 195 | 5 |
| Windows common archive/edit regression | 1,407 | 216 |
| Windows recovery/archive UI | 245 | 0 |
| Windows storage regression, opt-in native tests disabled | 128 | 201 |
| Mac common archive/edit regression, including supplemental fixtures | 1,558 | 65 |
| Mac recovery/archive UI | 245 | 0 |
| Ubuntu common archive/edit regression, including supplemental fixtures | 1,571 | 52 |
| Ubuntu recovery/archive UI | 245 | 0 |
| Windows-only guard, each Unix platform | 0 | 132 |

The broad Unix runs initially omitted the environment variables for nine optional independent-input/native-observer
cases. All nine subsequently pass on each platform with those inputs/observers supplied; the table counts their
distinct outcomes once. The six resulting TAR.GZ/BZ2/ZST archives on each platform pass independent complete
catalog/member/metadata/digest readback. The first postprocessing invocation used the broad corpus's default count
and refused two outputs where it expected twelve; rerunning readback with the actual explicit two-per-codec count
passes without changing test outputs. Both Unix lanes finish with idle checks. The Mac keep-awake assertion remains active.

All 469 actual worker files in each Windows account match the complete runtime manifest, and all 261 host application files match the new development SPDX inventory. An incomplete binary overlay was refused by the full manifest check before testing; the completed delta includes every changed runtime entry. No dependency or encoder license was added.

The standard-user launch initially stopped before test dispatch because its manifest was in the administrator-owned
task root. Copying that same verified manifest into the account's existing readable test directory resolved the
launcher issue without changing production code or granting access to the administrator's root. The resumed lane
loads the real user profile and records native raw token groups without Administrators membership. Its parent and
test processes finish at medium integrity. The final Windows runner records successful completion and confirms no
FileCat/test process remains. Failed reproduction and launcher evidence is retained alongside the passing results.

## Evidence identities

Raw development/test artifacts remain under the private worktree's ignored `.tmp` directory.

| Artifact | SHA-256 |
| --- | --- |
| `restoration-crashes-source-v3.tar.gz` (six code/test/runner changes over `3b4cdb98`) | `99CFAE2E37C399DC6EF00A439F8CD75023D66D9FE8EA54722423547A6C09102D` |
| `restoration-crashes-runtime-v3.json` | `3B4905A80E6BB618E505808EB13BA530B46F181E12846D0889996E774C205EFD` |
| `restoration-crashes-windows-v4.tar.gz` (complete changed-file transfer, full runtime rereview) | `ADD62562331EA573AAD690073471D72EA559FE24938D81935443423C431CA9E5` |
| `restoration-crashes-artifacts-v3/application.spdx.json` | `D616F0D75C640A058F263BB7ED47186E3BD3F88C904337E3A38287B7F31872FF` |
| `restoration-crashes-focused-evidence-v3.tar.gz` | `12C81AB971F5A79ED83B2B063CA06D261BDC4AD6AC694260644E20A2C42BEC24` |
| `restoration-crashes-failed-evidence-v2.tar.gz` (initial twelve passing cases and reproduced retry failure) | `D16DC784C815C6EAEBECE6F5F23D5E2FC60318191EBE7E97CD3A0BD3D457D731` |
| `restoration-crashes-windows-evidence-v5.tar.gz` (complete administrator/standard-user/common/UI/storage lanes) | `7034D4A6CC3AA95182D9259A2DAAF977D9CCDF0328D72FB17885E1ADEDEF22D6` |
| `restoration-crashes-mac-evidence-v3.tar.gz` | `8D806D9A6676B51D1D779629169B09FF7084616F404987406B2C5219BC8B8A21` |
| `restoration-crashes-ubuntu-evidence-v3.tar.gz` | `5872A6E8367624C15DBC19DB5CB62313BB35A109F97955A5C68AE7DF10A4F39B` |
| `restoration-crashes-mac-fixtures-evidence-v4.tar.gz` | `8E9FF31FD38F63DF7903F334D23F8FECB93168A9DEB98F7F8A057174F99FEE20` |
| `restoration-crashes-ubuntu-fixtures-evidence-v4.tar.gz` | `DB82EA83A6F50DF62D66EDDF18D14D1B8E0D706697282F5CC50BB0E3386592E5` |

## Remaining gates

Cancellation and failure injection, restoration cleanup-deletion crash points, wider metadata/security/resource/alias/durability coverage, production recovery UI/actions and the rest of the format/platform/workflow plan remain unfinished. The actual process tests do not establish power-loss durability, Unix update publication or universal filesystem support. RAR analysis with feasible implementation remains before the final independent UX/UI/functional/security review. The full goal is active.
