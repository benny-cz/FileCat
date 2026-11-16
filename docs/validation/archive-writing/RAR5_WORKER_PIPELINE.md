# RAR5 shared worker requests and password consumers

**7 November 2026.** RAR5 creation and complete updates now run through the shared parent clients and disposable
transform/assessment/fresh-verification endpoints. This connects the prior planner/stream engine to real process
conversations. It does not register RAR writing in the application or qualify native confinement/publication/UI.

The shared DTOs are now named `ArchiveTransformRequest` and `ArchiveTransformInvocation`. Existing TAR control
schemas and protocol-1 bytes remain unchanged. RAR preparation gives every payload reference a separate fixed
alias, while keeping the captured-input map and expected manifest in the parent. The worker receives public
profile/source facts, numeric input descriptors and occurrence edits/additions. It never receives expected output
digests, retained native identities or publication paths. RAR's stage ceiling derives from retained physical source
bytes, actual new-input lengths, the encoder's bounded block expansion and complete header/name/metadata overhead.
When only directories or stored empty files remain, deleted payload bytes do not inflate the stage reservation.

Protocol 2 is selected explicitly for the whole conversation, with distinct role/version hello bytes and no
mid-session negotiation. Password-bearing jobs refuse a protocol-1 endpoint before launch. A supplied grant must
match the requested encryption mode. After the complete public request, the parent sends one grant bound to the
actual task nonce, role and SHA-256 of the canonical request. Assessment/verifier hashes cover their complete
request; transform hashing streams the canonical records through a bounded digest sink. The password is not part
of that public hash. Wrong roles, changed requests and malformed/partial secret frames refuse without proxy IO.
The existing secret protocol rejects a second secret frame and clears owned framing buffers.

The RAR worker consumes that one grant into only the needed read/write sessions. Both sessions share one KDF work
budget within the transform role. Failed setup disposes already created sessions; normal/error/cancellation exits
dispose keys and owned password bytes. Parent client methods consume/dispose their supplied grants even when
validation, queueing, launch, protocol or verification fails. Fresh verification requires its own newly supplied
role-bound grant. Complete operation-wide work/IO/KDF banks across distinct processes remain a separate gate.

The transform endpoint verifies every source member and uses the parent-equivalent planner/assembler. It returns
an unverified receipt; the parent compares it with its private expectation. The caller must seal its native stage
and invoke a fresh read-only role. That verifier checks the actual stage hash, full strict catalog, password mode, ordered content,
metadata, terminal EOF and process completion. A requested password mode cannot make an unencrypted archive
appear protected. Existing TAR-family routes use their previous engines. ZIP's existing executor is unchanged.

RAR's counted/final encoding passes have a two-pass captured-input allowance. The absolute IO ceiling now includes
those passes, while each grant set still imposes its own actual-length-derived limit and bounded reopen credits.
The boundary test constructs the largest numeric allowance without opening a source; it does not claim to have
read hundreds of GiB. Runtime tests exercise both passes over chunked/noisy data. The authority retains one source
view at a time and reacquires the archive for the final whole-source hash after reading captured payloads.

Parent RAR planning now checks the existing reader/writer's **64 KiB UTF-8 name policy**, alongside the portable
32,000 UTF-16-code-unit ceiling. This is a current bounded profile policy, not a RAR format maximum. Too-large
new/renamed names refuse before worker/capture access; source names are checked even before deletion. The accepted
boundary uses exactly 65,536 UTF-8 bytes and crosses fragmented requests and encrypted-header/proxy buffers.

The dedicated Linux guest entry point compiles explicit protocol-2 startup roles 4/5/6 for assess/transform/verify.
The recorded runtime matrix uses actual controlled subprocess endpoints on the authorized machines, rather than
claiming those new guest bootstrap roles or Windows/macOS native factories are already qualified. Native launch,
capture-owner/publication integration, package qualification and UI still need their own implementation/evidence.

The [checkpoint record](rar5-worker-checkpoint.json) pins source packets, frozen evidence, actual CLR, standard
Windows account, dependency closure and compiler-source checksums. All nineteen changed C# inputs match final
compiler snapshots, including the built dedicated-worker entry point. No new third-party runtime code or codec
dependency was introduced; the dedicated writing dependency closure excludes Rar15 and DiscUtils.

The corrected full archive matrix has **2,958 cases**, including **60 new cases**, and zero failures:

| Platform | Passed | Skipped | New cases passed |
|---|---:|---:|---:|
| Mac | 2,903 | 55 | 60 |
| Ubuntu VM | 2,916 | 42 | 60 |
| Windows VM, standard account | 2,738 | 220 | 60 |

Eighteen workflows cover all six RAR creation methods and all three password modes. Each uses separate real
creation, assessment, update and fresh-verification processes, combines rename/replace/delete/file/folder additions,
and verifies aliasing, finite stage bounds and actual reaping. Independent Python header parsing and pinned official
7-Zip verify **36 archives, 108 payloads, 162 exact metadata records and 18 retained packed witnesses** per platform.
Semantic manifests and deterministic unencrypted archive hashes match across platforms; encrypted hashes are
recorded separately because their salts/IVs are fresh. Other cases exercise wrong role/request bindings, partial
secret cancellation, missing/unexpected grants, downgrade refusal, wrong passwords, observed password-mode
mismatches, unencrypted protocol-1 compatibility, request canonicalization, incompressible inputs, encrypted long
names, parent name refusals and absolute/actual-grant IO bounds.

The first **2,955-case** run is preserved separately with **19 failures on each platform**, all in the new tests.
Eighteen workflows asserted one archive-view open where the one-view authority correctly performs a second open;
the remaining positive name vector exceeded the existing 64 KiB byte policy. The assertion and parent/profile
boundary were corrected. Source review additionally corrected the old absolute single-pass captured-input ceiling.
Three boundary cases were added and the entire expanded application matrix repeated before independent checks.
The initial failed matrix has no interoperability or compiler-source qualification claim.

All runtime tests/oracles execute on the authorized Mac/VMs; the host performs edits, builds and static artifact
validation only. Final censuses have no owned FileCat/test processes and preserve the Mac wake helper. Remaining
work includes protocol-2 native launch/resource/package qualification, captured-owner/native transactions, creation/
update/edit/F6/password UI and legacy-cipher inventories, the complete 7z member graph/catalog and shared workflows,
all remaining crash/fuzz/six-RID gates and the final independent UX/UI/function/security review. The goal stays active.
