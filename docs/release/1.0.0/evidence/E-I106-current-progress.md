# E-I106 — source-safety progress and remaining work

2026-10-11 CEST. **Open; physical-source validation remains on HOLD.** This record preserves the full preliminary I106 scope previously in the issue table, at ec5868e, and adds the latest independently qualified correction. It does not replace the operational plan or authorize a candidate.

[I347](E-I347-backing-visibility-loss.md) stops cleanup/publication after a formerly observed source-container location becomes unavailable: 24 local/48 native controls, all 6194 affected results/131 exact skips, full App and 1279 native file checks. These are controlled unavailable-query cases with actual owned-file identities and whole bytes.

The remaining scope includes physical device entry/number reuse; provider handles versus captured backing identities; broader visibility and aliases; topology transitions and atomic directory/file mutations; full physical direct/helper tracing; and final installed-candidate qualification. I110's strict physical repeat shares the source-safety HOLD. Required owner decisions and explicit human GO remain.

[I348](E-I348-backing-identity-replacement.md) additionally refuses changed known source identities at still-observable paths:24 local/48 native final passes,6242 affected results/131 skips,347 affected App results and1449 native files/restoration. [Exact I347](E-I347-native-qualification.md) and [original hosted CI](E-CI-backing-visibility-loss.md) are sealed at0beaa10. [I350](E-I350-initial-backing-identity.md) now refuses unavailable initial identities with 60 host/90 native safety passes, 4957 affected results/97 skips, 347 headless App results and 1275 native files/restoration. Provider/handle identity, broader aliases/topology/atomic/physical/candidate scopes remain.

[I351](E-I351-archive-source-case-isolation.md) separately corrects case-distinct ZIP/TAR cache source and ZIP nested-member isolation. It does not establish provider open-handle identity or atomic mutation safety; physical-source HOLD remains.

[I354](E-I354-archive-source-replacement.md) now refreshes ZIP/TAR indexes and nested spools after deterministic same-metadata source replacement, with actual stable identities and an unavailable-identity fallback that prevents reuse. This does not establish atomic path/open-handle binding or arbitrary concurrent source mutation safety; physical-source HOLD remains.

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

## Prior dashboard scope retained from ec5868e

[Twenty native alias controls](E-I106-native-runtime-aliases.md) pass;

[I314](E-I314-incomplete-linux-topology.md) corrects one incomplete dependency-directory path;

[I317](E-I317-network-source-topology.md) keeps unresolved NBD/RBD source backing unknown, passing 16 targeted/16 fixed Ubuntu controls and preserving 82 affected passes/29 exact skips;

[exact I317](E-I317-native-qualification.md) passes at 59d37cd;

[I318](E-I318-mac-topology-refresh.md) corrects an actual stale Mac mount classification, with [committed native qualification](E-I318-native-qualification.md) sealed at 55737cb;

[original I317 CI](E-CI-network-source-topology.md) passes at 59d37cd;

[I319](E-I319-recovery-source-case.md) separates case-distinct Unix admission/cache identities with six native passes;

[I320](E-I320-incomplete-mac-topology.md) preserves unknown for incomplete Mac replies with eighteen Windows/twenty-five Mac passes;

[exact I319](E-I319-native-qualification.md) and [original I318 CI](E-CI-mac-topology-refresh.md) are sealed;

[I321](E-I321-recovery-device-reselection.md) refreshes explicit choices with seven passes each on Windows/Ubuntu/Mac;

[exact I320](E-I320-native-qualification.md) and [original I319 CI](E-CI-recovery-source-case.md) are sealed;

[exact I321](E-I321-native-qualification.md) passes;

[I325](E-I325-mac-backing-fixture.md) retains actual c0677d9 empty-bus/Unknown backing and verifies refusal in twenty known/twenty controlled-unknown native passes;

[I330](E-I330-held-windows-source-topology.md) corrects held Windows source topology with 56 native medium/high batch passes;

[I333](E-I333-source-container-publication.md) adds 63 fixed native source-container publication controls and preserves every 3416 affected predecessor outcome/message/107 exact skips; finish broader visibility/device-number reuse/active-source identity/runtime-alias/topology races and keep physical-source testing held.
