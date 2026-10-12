# E-I106 — source-safety progress and remaining work

Current follow-up: [I369 cloned Windows media](E-I369-cloned-windows-disk-interface.md) defeats the earlier GUID-only correction: exact `cd19d7c` scans replacement B after selecting A, while exact `cc93071` opens the selected PnP interface and refuses that clone with zero recovery-provider reads or entries. An additional exact FileCat probe replaces a byte clone at the **same owned VHD file path** and again refuses stale direct/helper opens with zero provider scan reads/entries. [Exact helper-protocol provider qualification](E-I106-broker-protocol-provider-clone.md) at `8b18c91` now combines `RecoveryProvider` with `DeviceReadHost`/`PipeDeviceSource`: unchanged A scans one volume in 15 reads; after byte-cloned B takes the same number/GUID, the selected A interface refuses before any provider read or entry. [Current native broker-launch control](E-I106-brokered-virtual-clone.md) at `095dadd3` adds medium-token `BrokeredDeviceSource.Open`, actual protected helper, automated VM consent, unchanged A/fresh B positive 4,096-byte reads, and stale-A refusal before a read session. It uses a FileCat-named test harness, pre-existing UAC auto-consent and a virtual disk; human/default-UAC/app-UI/physical/candidate scope remains. [I371 pre-scan safety](E-I371-selected-disk-safety-path.md) corrects the separate path mismatch in whole-disk write-safety checks: a controlled Windows VM case shows the checks used the reusable disk number while the reader was configured for the selected interface. The preceding [pre-open disk-number-reuse control](E-I106-preopen-device-identity.md) and [held-reader-reuse control](E-I106-virtual-device-reuse.md) retain their separate exact producers. These finite VM controls do not qualify real USB, all interface-path reuse, atomic races or a candidate; physical-source HOLD remains. [Exact committed I362](E-I362-native-qualification.md) and [I363](E-I363-exited-worker-stderr.md) retain their separate source/lifetime qualifications.

Updated 2026-10-12 CEST. **Open; physical-source validation remains on HOLD.** This record preserves the full preliminary I106 scope previously in the issue table, at ec5868e, and adds the latest independently qualified correction. [I400](E-I400-recovery-image-in-place-change.md) refreshes a regular-image catalog after a same-file rewrite, [I401](E-I401-held-recovery-image-reader.md) refuses a held read of replacement bytes, and [I402](E-I402-recovery-image-scan-change.md) refuses an image rewritten during free-space rescan. [I401 original-push CI](E-CI-I401-held-recovery-image-reader.md) failed two Windows ARM64 cases; [I403](E-I403-windows-image-journal-token.md) adds a positive file-journal token where available; [I404](E-I404-held-image-unknown-identity.md) makes held reads fail closed when identity is unavailable but a known token changes. [I402/I403 exact hosted CI](E-CI-I402-I403-recovery-image-correction.md) passes all required lanes, including ARM64; I404 exact hosted CI remains pending. These owned-image controls do not replace the operational plan or authorize a candidate. A [read-only G: journal-availability check](E-V09-G15-usb-journal-availability.md) reconfirms the historical USB identity, then reports NTFS Error 1179: the change journal is inactive. It cannot attribute G6's unexplained before/after hash difference or lift the hold.

[I347](E-I347-backing-visibility-loss.md) stops cleanup/publication after a formerly observed source-container location becomes unavailable: 24 local/48 native controls, all 6194 affected results/131 exact skips, full App and 1279 native file checks. These are controlled unavailable-query cases with actual owned-file identities and whole bytes.

The remaining scope includes physical device entry/number reuse; provider handles versus captured backing identities; broader visibility and aliases; topology transitions and atomic directory/file mutations; full physical direct/helper tracing; and final installed-candidate qualification. I110's strict physical repeat shares the source-safety HOLD. Required owner decisions and explicit human GO remain.

[I348](E-I348-backing-identity-replacement.md) additionally refuses changed known source identities at still-observable paths:24 local/48 native final passes,6242 affected results/131 skips,347 affected App results and1449 native files/restoration. [Exact I347](E-I347-native-qualification.md) and [original hosted CI](E-CI-backing-visibility-loss.md) are sealed at0beaa10. [I350](E-I350-initial-backing-identity.md) now refuses unavailable initial identities with 60 host/90 native safety passes, 4957 affected results/97 skips, 347 headless App results and 1275 native files/restoration. Provider/handle identity, broader aliases/topology/atomic/physical/candidate scopes remain.

[I351](E-I351-archive-source-case-isolation.md) separately corrects case-distinct ZIP/TAR cache source and ZIP nested-member isolation. It does not establish provider open-handle identity or atomic mutation safety; physical-source HOLD remains.

[I354](E-I354-archive-source-replacement.md) now refreshes ZIP/TAR indexes and nested spools after deterministic same-metadata source replacement, with actual stable identities and an unavailable-identity fallback that prevents reuse. This does not establish atomic path/open-handle binding or arbitrary concurrent source mutation safety; physical-source HOLD remains.

[I355](E-I355-recovery-image-replacement.md) now refreshes regular recovery-image sessions after deterministic same-metadata replacement. Actual source IDs identify known images; unavailable IDs prevent source reuse. Existing readers and unchanged-image search modes remain valid for its original deterministic controls; [I404](E-I404-held-image-unknown-identity.md) may safely refuse an ambiguous changed-token held read when identity is unavailable. Atomic path/open-handle binding, arbitrary in-place changes and unavailable-identity advisory metadata remain unqualified; device keys are unchanged and physical HOLD remains.

[I357](E-I357-nested-reader-admission.md) additionally keeps the outer ZIP index alive through nested-member copying, preserving source bytes and actual handle retirement in all64 final host/native controls. [Exact I355/I356](E-I355-I356-native-qualification.md) now seals04f6f18 without overlays. Atomic identity, in-place mutation, other spool-eviction interleavings and physical/candidate scope remain; physical-source HOLD is unchanged.

[I358](E-I358-opened-source-binding.md) now binds known fresh source identities to actual archive/regular-image handles before reading.72 final controls qualify36 refusals/84 complete reads, with unchanged source bytes and exclusive handle closure. [Exact I357](E-I357-native-qualification.md) separately seals dfe32b3. Controlled lazy reopens, unknown initial identity, cached-path races, in-place changes, spool eviction and physical/candidate scope remain; physical HOLD is unchanged.

## Recent 2026-10-11 follow-up

[Controlled lazy reopening](E-I106-lazy-source-reopen.md) qualifies72 controls/36 refusals/108 complete reads on d9889ce with no product change. [I362](E-I362-nested-spool-admission.md) separately corrects an observed nested-spool eviction/admission defect, with112 final controls and full byte/lifetime oracles. [Exact preceding source](E-I359-I361-committed-qualification.md) and [original CI](E-CI-scheduler-rename-archive.md) qualify separately. Wider provider/topology/atomic/native/candidate scope remains; physical HOLD is unchanged.

## Full preliminary scope retained from ec5868e

Open: discovery corrections tested; [Current medium/high Windows controls](E-I106-current-windows-admission.md) pass 52/64 records with twelve explicit skips and actual parent/child tokens; recording readers never open devices.

[Twenty native alias controls](E-I106-native-runtime-aliases.md) pass at 1da71e7; eight renamed muxers refuse to start, twelve live workers produce positive census results; Windows/Ubuntu unknown baseline visibility remains.

[I314](E-I314-incomplete-linux-topology.md) corrects incomplete Linux dependencies being treated as a known leaf: eight targeted/18 native passes, 74 affected passes/29 exact skips.

[I317](E-I317-network-source-topology.md) keeps unresolved NBD/RBD source backing unknown: eight original failures/eight positives become 16 targeted/16 fixed Ubuntu passes; 82 affected passes/29 exact skips remain.

[Exact I317](E-I317-native-qualification.md) passes at 59d37cd; [I318](E-I318-mac-topology-refresh.md) corrects an actual stale Mac mount classification with known bytes and independent restoration; [committed Mac qualification](E-I318-native-qualification.md) passes at 55737cb and [original I317 CI](E-CI-network-source-topology.md) is green at 59d37cd.

[I319](E-I319-recovery-source-case.md) separates case-distinct Unix admission/cached sources: four original failures/two positives become six native passes, with 98 affected passes/29 exact skips retained.

[I320](E-I320-incomplete-mac-topology.md) keeps incomplete Mac query answers unknown: twelve unsafe answers/two exceptions per platform become eighteen Windows/twenty-five Mac passes, preserving 100 affected passes/34 skips.

[Exact I319](E-I319-native-qualification.md) and [original I318 CI](E-CI-mac-topology-refresh.md) are sealed at their own identities.

[I321](E-I321-recovery-device-reselection.md) retires stale readers on explicit device reselection: five original failures/two positives become seven passes each on Windows/Ubuntu/Mac; 118 affected passes/34 exact skips remain.

[Exact I320](E-I320-native-qualification.md) and [original I319 CI](E-CI-recovery-source-case.md) are sealed at their own identities.

[Exact I321](E-I321-native-qualification.md) passes at 1b2229c.

[Original I320 CI](E-CI-incomplete-mac-topology.md) retains one Mac unknown-target failure; safety remains fail-closed and cause unproven.

[Original I321 CI](E-CI-recovery-reselection.md) retains Mac classification and ARM64 cancellation-fence failures; all 100 current new controls pass.

[I323](E-I323-smb-cancellation-startup.md) corrects independently reproduced cancellation setup sensitivity and diagnostic collection, preserving safety assertions; [I325](E-I325-mac-backing-fixture.md) identifies unavailable backing in the actual subsequent c0677d9 Mac job and corrects the fixture to require refusal; twenty known/twenty controlled-unknown Mac passes preserve product safety. Earlier hidden causes remain unproven.

[I328](E-I328-recovery-destination-admission.md) checks every descendant/staging destination before writing: fourteen unsafe cases become 21 controls/63 native passes; every 926 affected outcome/message/19 exact skips remains.

[I329](E-I329-held-unix-source-path.md) refuses changed/missing Unix held-source paths: three unsafe cases become five passes per native Unix platform, ten fixed passes.

[I330](E-I330-held-windows-source-topology.md) adds held Windows direct/helper topology and 56 native medium/high batch passes; controlled identities do not qualify kernel device replacement.

[Exact Windows follow-up](E-I330-I331-native-qualification.md) passes at 2de33a0. Physical entry/number reuse, atomic directory/topology races, broader visibility/aliases and physical/candidate tracing remain.

[I333](E-I333-source-container-publication.md) fixes demonstrated source-image/archive replacement during stream publication: 63 native fixed controls and every 3416 predecessor outcome/message/107 exact skips; wider aliases/atomic races/physical HOLD remain.

[I334](E-I334-publication-retry-admission.md) rechecks backing targets on every publication retry and clears failed byte progress: seven local/21 native passes; broader native alias/atomic/physical scopes remain.

[Fourteen native Windows image-alias controls](E-I106-native-image-aliases-windows.md) pass under actual ordinary/elevated tokens; wider kernel transitions/atomic/physical scopes remain.

[I335](E-I335-recovery-publication-admission.md) rechecks destination-disk admission at every publication attempt after read/close/verification/retry: 36 local/108 native passes; controlled topology does not qualify kernel races or physical safety.

[I336](E-I336-all-selected-source-admission.md) protects every selected recovery source during later folder/staging/publication admission: 48 local/144 native passes; broader kernel/atomic/physical scopes remain.

[I337](E-I337-recovery-folder-attempt-admission.md) rechecks all sources inside root/relative/descendant folder creation attempts and retries:24 local/72 native passes; kernel/atomic/physical scopes remain.

[I339](E-I339-recovery-metadata-admission.md) rechecks admission before origin metadata calls: eighteen local/54 native passes, 3960 predecessor outcomes/messages/107 exact skips. Wider cleanup/kernel/atomic/physical scopes remain.

[I340](E-I340-stream-staging-ownership.md) limits stream staging cleanup to successful creation: fourteen local/42 native passes preserve actual pre-existing collision bytes.

[I341](E-I341-local-staging-ownership.md) also delegates failed local staging cleanup to its owning copy engine. Final combined tests pass 38 local/90 native controls; wider identity/atomic/physical scopes remain.

[I343](E-I343-backing-staging-replacement.md) preserves selected source containers renamed into staging before cleanup/verification/publication: 24 local/48 native fixed controls with actual Unix/Windows file identities. Wider atomic/unknown identity/physical/candidate scopes remain.

[Full record](../FILECAT_1_0_RELEASE_ISSUE_HISTORY_20261006.md#i106--recovery-discovery-misses-a-separate-portable-installation)
