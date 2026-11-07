# Single-use worker password grants and transport cleanup

**7 November 2026.** The shared worker foundation now has an explicit protocol-2 secret frame and owned-buffer
cleanup. The full 770-case affected matrix passes on Windows/Mac/Ubuntu. This does not connect passwords to the
production format requests, qualify a native secret role or enable a writer. Those gates remain open in the
[plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md).

## Contract and ownership

Legacy callers still select protocol 1 and refuse secret frames. Protocol 2 is selected explicitly. A secret frame
has one exact schema/encoding, a validated role and the SHA-256 of the complete canonical request. That digest is
public request-binding data, never a password hash. The existing nonce and monotonic sequence bind the frame to
its worker. Request consumers must validate the complete request, receive its secret before serving IO, and use
the expected role/digest. Unknown versions, replay, a second secret, wrong role/request, malformed UTF-16 and bad
lengths fail permanently. Secret lengths are checked from the fixed header before body allocation: 38–2,086 bytes,
including at most 1,024 UTF-16 code units. Format-specific encoded-password limits remain additional requirements.

`ArchiveWorkerSecretGrant` owns mutable characters, lends a span to one synchronous consumer and clears that
buffer on success, failure or disposal. It preserves whitespace, Unicode and NUL exactly; empty grants support
legacy reading, while new-output sessions retain their nonempty-password policy. It has no public password getter
or generated value diagnostics. Its JSON converter explicitly refuses persistence/restoration. Each fresh worker
needs its own grant. Parent job/view/edit lifetime, the single-prompt UI owner and the actual canonical-request
digest computation in format consumers still need integration; the grant itself is not a persistent credential cache.

The shared transport keeps short reads in their original bounded buffers instead of abandoning resized copies.
It clears consumed input ranges, pending input on cancellation, completed/failed output and unqueued failed sends.
Completion follows the actual write/flush and cleanup. Queued and in-flight ownership stay distinct: a buffer still
used by a blocked OS write is cleared after that write returns, rather than being changed concurrently. Cancellation
still interrupts parser waits, and process lifetime controls remain required for blocked OS IO. Framework copies,
immutable strings and complete process-memory erasure are not claimed. Secret wire bodies and failed partial
protocol reads clear their owned payload buffers; password bytes do not enter ordinary control-record serialization.

## Live checks and fixes

The final full matrices have no failures: Mac 768 passed/two platform skips, Ubuntu 767/three, Windows 649/121
Unix-specific skips under the actual loaded standard account. All 46 secret-contract cases, five transport-ownership
cases and 19 existing cancellation cases pass. The affected password/codec/header, framing, proxy, assessment,
transform, verification and native Unix storage regressions are included.

Nine test-only real child journeys cover success, role substitution and partly received password cancellation for
assessment, transform and verification. They use only a labelled public synthetic password and return a fixed
acknowledgement or bounded refusal, never a password-derived receipt. Existing session deadlines/queue ownership
supervise them; every child exits zero, is reaped and needs no hard kill. These ordinary child/pipe probes are not
native-confinement or production archive-password admission evidence.

The first full attempt had four failures per platform. Three expected JSON `{}` but the runtime refused the span
constructor; explicit nonserialization is now implemented and tested. The fourth was an old write observer which
did not count the newly selected byte-array overload; payload and flush checks already passed. Its observer now
covers both overloads. Failed logs/TRX and source identities are retained.

Collection then exposed a recorder defect: Windows reused PIDs and overwrote old `PID.json` records. All nine
secret journeys passed in the full matrix, but only eight separate Windows secret-exit records survived. The recorder
now uses unique, create-only names and always disposes process resources; repeated disposal is harmless. A focused
nine-case replay passes on each platform, retaining all nine distinct records and checking repeat cleanup. This
recorder-only correction does not rerun or replace the unchanged production-code regression matrix.

## Evidence and limits

The full packet has 4,044 files / 30,147,843 compressed bytes, SHA-256
`854513C8A276E95D4E2BACC1D5EBADFD3FAF9609F5278E2CCB56B33E751D96D6`.
The recorder follow-up has 4,046 files / 30,148,540 bytes, SHA-256
`A067155551F707008E963FEEB34532F2E5DE7DEF8D5776939157EF926E096A79`.
Its production sources are unchanged. Actual PDB inputs, binaries, runners, TRX, child records and final census
are captured. The [machine-readable checkpoint](worker-secret-checkpoint.json) records both sets and their limits.
Each set captures 1,319 matching compiler document instances. The full matrix has 12,497,775 source bytes on
Mac/Ubuntu and 12,512,476 on Windows; the recorder follow-up has 12,498,425 and 12,513,126 respectively. All nine
current changed/new C# files match the follow-up's actual compiler inputs on every platform.

| Platform | Full bundle bytes / rows | SHA-256 |
|---|---:|---|
| Mac | 37,630,036 / 1,490 | `DC0DF157A78A80072A08B46C5DEE9582D243A775F2360B2965E7BCBFFECBDD23` |
| Ubuntu | 37,626,396 / 1,490 | `A52A3B5D817579D2FABFC946AB2AAEA8E7FE2A552082D51C35EAFFDF4D9D1FD0` |
| Windows | 37,654,840 / 1,418 | `B2E97DB423F44E74386056DBE6E63C5B28478D3F8558E6391B4DC3AC748F5E40` |

| Platform | Focused recorder bundle bytes / rows | SHA-256 |
|---|---:|---|
| Mac | 37,224,875 / 1,357 | `58CACD5BEA8FEB7C04298F0332F78AB0155CD30AF03773DB13B32CA69E28A9A6` |
| Ubuntu | 37,216,154 / 1,357 | `97B170F82E81DD953754A916B57ADDE6D37E33F353B31C6EDD07F021846FF6CE` |
| Windows | 37,232,329 / 1,367 | `93E836F03EB98F88954175B9E09C2B0DF915840836AA6E0DDE76206581CDD936` |

All six bundles are retained locally and verified file-for-file after transfer. Other testers were deferred; owned
FileCat/test processes exited and the Mac wake helper remains. A Windows idle-guard launcher path typo was corrected
before its second batch started; test sources/results were unaffected. No host FileCat or test execution occurred.

No new third-party source or runtime dependency is added. Remaining work includes BCJ2, strict raw catalogs and
preservation, typed password-aware format requests and fresh verification, native secret-role/resource/lifetime
qualification, shared transactions/capture, complete password/open/update/edit UI and cache lifetime, adversarial
and fuzz campaigns, six-RID/package checks and the final independent review. The overall goal remains active.
