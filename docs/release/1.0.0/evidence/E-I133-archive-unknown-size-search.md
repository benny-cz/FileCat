# E-I133 - unknown archive member sizes cannot satisfy size criteria

**Requirement:** V13, SEARCH-002, PI-05; size criteria and search exclusions must be truthful.
**Severity/disposition:** Medium, search correctness/metadata truth; must fix.
**Status:** remediated at `578a0ed`; verified preliminarily on host, clean CI and both VMs.
**Baseline:** clean `1e5416e7718b4032e3882d177a14fa79d530c538`; Core/Archives source is unchanged
since `fdb17b453d515df437bce34eaaaf8f3626f7cb09`, whose independently pinned production DLLs run the probe.

Python's standard gzip, bzip2 and xz encoders each compress the known five-byte payload `alpha`.
Independent decoding verifies every byte before FileCat runs. Actual registered archive providers,
the composite filesystem detector and SearchSession list each member with size -1, intentionally
avoiding content decompression during member-name search.

All twelve size-filter controls fail: three formats, initial/narrowed scope, and minimum 100 bytes
or maximum one byte. FileCat returns the unknown-sized five-byte member as a match, with finished/
complete results and no log. Six corresponding unfiltered controls correctly retain the member and
its unknown size. Narrowing retains original scope and does not discover additional members.

Private `archive-search-formats-20261005` in the authorized second workspace's `FileCatReleaseEvidence`
retains exact compressed fixture bytes/manifest, source probe, publish/command logs, pinned production
inputs and all eighteen observations. `baseline-v1-independent.json` SHA-256
`83b7e3f438459b7463c9fea6804066c01b412b30a0f171391ce89e158b4e6348`.

Expected correction: when a file member's length is unknown and a size criterion is active, exclude
it with a typed original-location log explaining that size cannot be checked. Preserve unfiltered
unknown-size results, known-size filtering, directory semantics and the content-free search policy.
Earlier I115/I116 passes do not cover this unknown-size boundary. Affected search/archive/Find checks,
clean CI, applicable native execution and final candidate qualification remain to validate.

## Correction and working validation

SearchEngine now excludes an unknown-length file member whenever a minimum or maximum size criterion
is active. Its bounded Inaccessible log retains the typed original parent/name and explains why size
cannot be checked. Both initial and narrowed searches share this check. Unfiltered unknown lengths,
known-length filtering and directory behavior remain unchanged; no content is decompressed to guess size.

Twenty Core regressions cover gzip/bzip2/xz, both scopes, both bounds and unfiltered controls, plus
known-length/zero-byte ZIP controls. Two new headless Find cases verify status, the exclusion log and
original-parent navigation; the complete affected Find class passes all four cases without skips.
An independent actual-production probe repeats all eighteen baseline observations with only Core.dll
changed: twelve exclusions now carry the reason and six unfiltered positives retain original identity.
Every other probe file remains byte-identical. Independently decoded fixture inputs are unchanged.

Host Core coverage is the exact disjoint union of 819 cases on a short owned E: temp path and one
large-file case on C:, using unchanged builds: **774 passed, 46 declared skips, zero failures**.
The full App rerun on a short temp path outside the repository passes **324, with 21 declared skips**.
Unique TRX IDs independently establish full coverage despite several duplicate display names.
Seven owned temp roots are absent after cleanup. Exact three-file source overlays, working DLLs,
commands, logs, full failed/successful inventories and skip identities remain private.
`working-independent-v1.json` SHA-256
`402e810567a4ba7fded37041231354182b98173975e4065743e29bac1ea78620`.

Earlier failed attempts remain failed and retained: GPG fixture key generation rejects long C: paths
and a separate short C: attempt reports IPC connection failure; an E: large-file fixture cannot allocate
enough space. These failures precede FileCat verification. The first full App run used a temp root inside
the repository, contradicting a Git test's outside-repository fixture assumption. A folder-count test
also fails only while deleting an owned directory held by another process; its historical handle owner
is unknown. Both cases pass in the unchanged full App rerun. No production/test change masks these failures.

This establishes preliminary working behavior. Clean committed-source CI/Windows/Ubuntu validation,
other archive formats, native desktop interaction and exact-candidate qualification remain required.

## Clean-source CI and native execution

Exact source `578a0edd06f1269b235ef21ce91ec38280833f3c` passes all four required CI jobs in
[run 37244741557](https://github.com/benny-cz/FileCat/actions/runs/37244741557).
Four downloaded artifacts match the server's SHA-256 digests; six complete TRX inventories retain
every outcome and skip reason. All twenty new Core cases pass without skips in Windows' inventory;
all four affected Find cases pass without skips per Windows/Ubuntu/macOS App inventory.
The complete Windows Core inventory has 773 passes/47 declared skips, Windows App 330/15,
and each Unix App lane 302/43. ARM64 Core/App and package-start/installer checks pass with log totals;
no per-case ARM64 or Unix Core TRX is claimed. Three tag/manual packaging jobs correctly skip.
Private `ci-37244741557/independent-ci.json` SHA-256
`85722d923dff8b8da830728c607b907fb240b2ba9c92ec83bcd0367e11b58451`.

Self-contained clean-source producers execute twenty Core/four headless Find cases without skips
on Windows 11 Insider 26300 (VM UUID `9D224D56-1161-A849-ABA7-2581A980895C`, Admin/elevated)
and Ubuntu 26.04.1 x64 (benny/UID 1000). Input ZIPs, every one of 1,262 Windows/687 Linux payloads,
1,263/688 ZIP members, sixteen canonical Git source exports per lane, exact host/native case
inventories and retrieved output hashes independently verify. Inputs remain byte-identical after
execution. Owned fixture files and tracked bootstrap/controller/test processes are absent.
`guests-independent-v1.json` SHA-256
`3b6f18c1047905dca7ea8ebe3291197d6e226626c6c67a5ce5b9eee6d499018f`.

Both first cleanup observers fail and remain retained. Windows' embedded Python observer includes
its own executable in the owned-path census; an independent PowerShell observer outside that runtime
verifies its exit as well as the other tracked processes. Ubuntu's ordinary-account census cannot
read `/proc/2036/exe`; a later authorized read-only root census verifies owned-path/process absence,
rechecks all input pins and observes that denied process outside the fixture. No test rerun, skip,
permission weakening, system setting or FileCat source change is used to repair these private observers.
The failed cleanup records remain failed; additive `post-bootstrap-v2.json` proofs establish cleanup.

Earlier I115/I116 evidence does not cover this boundary. The affected committed-source checks now pass;
other formats, native desktop/AT and exact-candidate qualification remain. Overall **NO-GO**.

Mac work is deferred at the owner's request. A later authorized rediscovery used cached network entries,
one known-host DNS query and one Bonjour packet with a four-second response window; no Mac address was
found and no Mac command ran. Raw observations remain private in `mac-rediscovery-20261005`.
Both VMs stay running; G: is untouched and its historical source-change hold remains. Overall **NO-GO**.
