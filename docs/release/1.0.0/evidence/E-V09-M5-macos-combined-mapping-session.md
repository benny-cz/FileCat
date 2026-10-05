# E-V09-M5 — Mac recovery with simultaneous raw and mapping observations

Preliminary actual execution, 2026-10-05; follows [E-V09-M4](E-V09-M4-macos-recorded-session.md).
No production, permission-policy, dependency or recovery-oracle change. This is an owned-image
test on the physical macOS 27.0.1/26A434 arm64 machine, not a release-candidate qualification.

## Exact inputs and execution

The SDK-free App payload remains clean `593583e585d4a79cbb7ff961770a2d826858e14d`.
Private alias app/FCMapApp_v13 is byte-identical to app/FileCat.App.Tests; its addition makes
**1,197 payload pins**, independently checked on the Mac before and after execution. Original
manifest SHA-256 is `f3b8e93a97f0d4a9383548f5d1d7fcc98ad32d0084d2cb8558d9f235bafe1288`;
alias manifest is `997c595759a784f6fdc80987a3d43734eea6ed27f36544fc9dac47a6c3e70e81`.
The private layout is not a packaged release artifact. Ten additional staged inputs verify.

The owner starts TraceMappingSession-20261005-v13.command locally with sudo. The separate
session launcher returns; the root preflight verifies sudo has exited before FileCat starts.
The root controller owns the worker process directly, replacing v12's failing string-based
watcher supervision. The ordinary worker retains UID 501/GID 20 and all sixteen normal
supplementary groups. Production census admission remains unchanged. SIP stays enabled.

Before the source image opens, a future-process mapping control passes: three known 16-KiB
mappings, six nested mmap/__mmap entry/return pairs, exact PID/TID/FD/offset/protection/flags
and complete return addresses. The complete owned 32-KiB file equals MAPPED plus zeros;
SHA-256 `7a71e046c351e19379b1f43a9a31bca070bdc49ddb8568515308baf6f639b738`.
One additional observed 32-KiB startup mapping is retained separately from those three controls.

Actual recovery passes **1/1**, no skips/errors, command exit zero. It uses the owned,
unmounted, writable **25-MiB FAT16 image**, reads as the viewer would, copies tiny.txt with
Completed, waits **70 seconds** for timed saving and closes. All recovered **60 bytes** match
the independent filename/numbered-line generator; SHA-256
`2c9aed8755e52ee19e225e615a4cb8a11fbc509343a3e0e93618d2cfe089fda3`.
Drawn F3 and authopen are not exercised.

Root supervisor, ordinary worker, both recorders and both offline decoders finish cleanly;
all recorded command exits are zero. Cleanup has no failure or forced action. The source
image before/after SHA-256 is `19f74a085272a8080035ffb5a649b8b53ae8fe1cdcd323555c89037293a10819`.
Independent inventory verifies detachment and temporary-file cleanup. **117 retained pins**,
**21 tracked owned process absences**, **15 additional diskutil child absences**, and the
image-attachment daemon's absence verify. The full source is hashed on the Mac, not downloaded.

## Independent trace interpretation

The unfiltered C3/C4/C7 raw capture, 256-MiB buffer, decodes **3,171,197 events over
136.463 seconds**. Raw size is **208,816,880 bytes**, SHA-256
`c8ab259930393d45338600010db0a6b695507fc777f1c8a79ec04f0d361eda4e`.
Three inspected loss-marker IDs are absent, with no recorder loss/overrun report. This is
a finite observation, not universal zero-loss qualification. All **430 known before/after
I/O controls**, six processes/eight native TIDs and twelve whole control files match.

Actual App PID 9728 has **33 native TIDs**, all with matching thread-birth metadata, and
fifteen direct diskutil children with syscall observations. No further child fork is observed.
Fifteen unmatched thread-terminate starts are nonreturning lifecycle events. These observations
do not establish universal system-helper coverage under SIP.

The availability probe's **FD 166** opens read-only and closes without reading. Recovery
**FD 140** opens read-only, performs two size/count query ioctls and **eight successful
positional reads totaling 264,192 bytes**, then closes after the timed-save wait. Raw and
formatted path/FD records agree. No source-FD writes/truncations, dup/fcntl aliases, forks
or matched backing mappings while either source FD is open are observed. The service's one
recovery-device-open callback does not erase the separate native availability probe.

Raw output contains **264 mmap calls**. All **221 observed library __mmap pairs** have a
unique monotone match against native PID/TID, four raw arguments and full returned address;
their enclosing mmap wrappers also match. Matching does not assume a conversion between
the two timestamp domains. Every one of the **77 nonanonymous shared mappings**, initially
PROT_READ, is among these matches with independently observed backing FD and offset.

Descriptor-close/reuse and duplication are followed explicitly. **75 shared mappings** have
an observed filesystem-open path; **two** originate from observed POSIX shared-memory-object
opens, for which no filesystem path is invented. None is attributed to the source descriptors,
raw/block source path or source image. The installed SDK confirms F_DUPFD_CLOEXEC is command 67,
consistent with [Apple's header](https://github.com/apple/darwin-xnu/blob/main/bsd/sys/fcntl.h).

The remaining **43 raw mappings** lack library-probe matches. All are MAP_PRIVATE and occur
before the first source open, last at 16:14:17.573142 CEST. Their backing FDs are not invented.
Direct-syscall, future/helper, native authorization, adverse backing-topology and exact-candidate
qualification remain open. Historical v12's 79 mapping FDs remain unresolved; this new run
does not reconstruct them. **No universal zero-source-write or complete V09 pass is claimed.**

## Retained private provenance

Private base: authorized second workspace's FileCatReleaseEvidence/mac-resume-20261005.
Native root: `/Users/benny/FileCatReleaseValidation/macresume-9ef36e9f70e64744ae94cee88e3868f8`.

- session-mapping-staged-v13 native stage manifest SHA-256
  `4e136c1b528a37c02cdb9686033fe7053d3d1731219d384036f2bbaaa6a9a800`;
  staging verification receipt `bcd33afbdd260bc83c53204d31310f88de3cad781c2693d697da6edb58e5598f`.
- session-executed-v13/independent-executed-v13.json SHA-256
  `47c1f58dda05827462828d71e02ddbe5c86450dcb69275f4d916588f8bee462b`:
  complete pin, byte, control, native source-FD, mapping and cleanup verification.
- session-executed-v13/outputs.zip SHA-256
  `726a8770dfa3da368bee5ced1db2f92a5705acd656df0c44b93cb2fe96472c58`;
  retained-collection-v1.json `28996cb1a5df0472e590b869abb0261b7959ff3eb06513adff5019030fe18d9d`.
- session-executed-v13/mapping-match-v2.json SHA-256
  `88f54907cf6cceb8fb5118d9b4eebcf9e41365903b7e77c5fabca30a026951e2`:
  all unique library/raw matches, shared mappings and unmatched private observations.
- session-executed-v13/derived-children-absence-v1.json SHA-256
  `9219fe85f5e6edb5bf2729b2aa18b747895cd5e0c98df3c851016ddb69bdc49e`:
  fifteen derived children and the image-attachment daemon absent.
- session-executed-v13/sdk-fcntl.h SHA-256
  `807bfdae1695967faf13532c1d0eb0d5eb08a67e0bef8bf529f732f6ce00176b`:
  retained installed SDK constants; not a claim that this SDK is the running kernel's build.

An initial local default-encoding preparation failure remains retained. Independent verifier
failures from assuming direct-open origins, matching open-start instead of completion times,
treating shared-memory opens as filesystem opens, and counting the startup map as a known
control remain pinned. Corrections preserve all native outputs and verify actual alias chains,
completion timestamps, object kinds and each independent control. Earlier native supervisor,
provider and calibration failures remain in their own records.

Progress remains **117/139 issue rows remediated**, one separately Closed, **24/26 checklist
steps partly or fully open**. Both VMs remain running; G: stays untouched/HOLD. No candidate
or human GO; stable publication remains **NO-GO**.
