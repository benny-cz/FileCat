# Creation provenance integration, 6 November 2026

Development checkpoint over `82508a029d9639264145b4664efb27ca16ed3fc2`. The
[implementation plan](../../design/FILECAT_ARCHIVE_WRITE_IMPLEMENTATION_PLAN.md) remains incomplete and no new writer
profile is enabled. This extends the unregistered [private creation transaction](PRIVATE_NEW_FILE_PUBLICATION.md);
it does not qualify existing-archive replacement, arbitrary metadata copying or automatic cleanup.

## Transaction behavior

After the transform worker exits and the native stage is sealed, creation preparation rereviews its retained inputs
and sidecars. It reads the complete scoped origin evidence, binding every record in stable input-ID order to the
captured native object version and exact origin digest. A different version, mark, order, count, kind, format, scope,
checksum or EOF refuses. A maximum Windows zone does not stop later records from being checked. Owned evidence views
are closed on success and refusal.

Windows creation derives the maximum supported declared zone from every input. Raw source URLs remain in the private
evidence sidecar; the output has a canonical zone-only mark, without an invented aggregate URL. A present zone zero
remains present. Unsupported/unreadable declarations refuse. Mac/Linux typed absence remains supported; marked Unix
creation still refuses until its semantic aggregate and native representation are qualified.

The required origin is created exclusively on the sealed, unpublished native stage during `Finalizing`, before the
fresh verifier starts. The refreshed observation must preserve native identity, parent/name, payload length/hash and
member time/mode. Windows also checks that the observed creation time, mtime, attributes, link count and queried
permission descriptor did not change. Existing/late origin marks cannot be erased or overwritten. Once the Windows
origin write has an effect, native flush/readback completes without cancellation bypassing reconciliation.

Fresh verification uses the observation including the applied origin. Publication rederives the required result from
the retained evidence and captured sources, compares it with the verified stage, then follows the existing durable
intent, exclusive claim and conservative reconciliation path. Input and evidence rereviews continue after publication.
All sidecars remain reported as retained artifacts; uncertain outcomes remain `NeedsReview`.

Native Windows reviews require a complete bounded `FileStreamInformation` inventory: exactly the unnamed data stream
and, for a marked output, exactly the expected-length `:Zone.Identifier:$DATA` stream. Duplicate, additional, missing,
overflowed or malformed inventory refuses before claim; inventory and exact mark/metadata are checked again after
publication. The parser follows Microsoft's [FILE_STREAM_INFORMATION contract](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_stream_information)
and [stream record layout](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/f8762be6-3ab9-411e-a7d6-5cc68f70c78d).

Unmarked `FCCMETA1` creation evidence retains version/policy 1. Windows zone-only creation uses version/policy 2 and
a version-2 native witness. Its metadata digest binds the exact origin; the sidecar checksum is the journal's security
snapshot. This does not reinterpret old unmarked evidence or grant a deserialized record native authority. Journal
schema 5 and the retention-only recovery behavior remain unchanged.

## Validation

Final Core and UI runs use the broad Archive/EditSession/BackingArchiveTarget filters, plus PaxTimestamp for Core.
The shared tests cover complete capture binding after the highest zone, malformed later declarations, checksums,
owned-view disposal, all three typed-absence formats and refusal of unqualified Unix marks.

| Environment | Core | UI | Native lane |
|---|---|---|---|
| macOS 27.0.1 ARM64 / APFS, .NET 10.0.12 | 1,461 passed, 65 skipped; 1,526 total | 239 passed | Native retained preparation/publication and crash cases are included in Core |
| Ubuntu x64 / ext4, .NET 10.0.14 | 1,474 passed, 52 skipped; 1,526 total | 239 passed | 61 passed, one skipped; 62 total |
| Windows 11 x64 VM / NTFS, .NET 10.0.14 | 1,314 passed, 212 skipped; 1,526 total | 239 passed | Corrected storage: 124 passed, 57 skipped; 181 total. Confined worker lane: all 68 passed |
| Windows 11 x64 host / NTFS, .NET 10.0.14 | Additional focused Core: 29 passed | — | Storage: 122 passed, one skipped; 123 total. Confined worker lane: 63 passed, five skipped; 68 total |

Mac and Ubuntu independent readers each checked 12 TAR.ZST, 11 TAR.BZ2, 11 TAR.GZ and eight exact-timestamp outputs.
These are regression fixtures, not complete format admission or release interoperability qualification.

The first Mac run exposed an exit/CPU-query race in the controlled crash-test launcher: macOS can throw either
`Win32Exception` or `InvalidOperationException` after the child exits. The harness now retains its last observation
only when native exit is confirmed, matching the existing controlled-child helper. Production monitor failures remain
fail-closed. The complete final Mac run passed all ten creation process-crash boundaries.

The first Windows storage run reached successful marked publication but its fixture attempted a pathname read without
delete sharing while the native lease was still open. Fixture reads now allow the retained lease's sharing mode.
The passing Windows host cases also cover a late extra stream and all four TAR profiles through separate application/probe
transform and verifier workers, checking that the native mark exists before verification and survives publication.
All eight new confined marked-creation cases passed. The five native lane skips require an elevated parent, while
the actual host launcher recorded integrity 8192 before and after and exit zero. The earlier VM Core/UI parents
recorded 12288 before / 8192 after and exit zero. These do not establish all ordinary-user or installed-package gates.
After the other tester exited, the VM completed the corrected storage and native replay with exit zero, integrity
12288 before / 8192 after, no native skips and all eight marked-creation cases passing. Host storage uses the
WindowsArchive/WindowsFileOperations filter; VM storage uses the broader archive/edit-session filter above.

## Evidence and remaining gates

Raw evidence is retained under ignored `.tmp/test-results/archive-writing/creation-origin-v1` and the task-owned guest
directories. Both Unix guests' 14 tested-source files were independently matched to the transferred source archive.
Windows test-only sharing corrections follow that archive; the tested production sources are unchanged.

- Unix/final runtime source delta: `6F2259566910314CE398FC10229CF50E0FF68683CBC01BCBC8F583A816F0F964`.
- Final source delta including Windows fixture corrections: `68D971B907AD29B353360AD1BE05E5BF80685E0AE3DD38D2A39E2BB64A330433`.
- Windows final managed overlay: `7B2044B8400BE877B3264D6B077758C425AB46A030D4FE1342B71FD762740D8D`.
- Mac final evidence: `C2E7F28A50A258062F85D19D4F13C5FED25B632FEA5220A799389C8862A7AC85`.
- Ubuntu final evidence: `9F88E2C17C29C6C5621F7A25052C4A3EDD095B4ABCE537C41362907269B52850`.
- Windows host final native evidence: `12C12364F73066BFB4DD0FEE8DE9F665ECC1DE321ADAF0F1C32FEDD6DAD75A31`.
- Windows VM corrected storage/native evidence: `C0CE9B552261B71C09FF773E70A68C8A0D3BB0973861601719CA9F63DDDDCE0A`.

The refreshed win-x64 development application SPDX inventory is retained at ignored
`.tmp/test-results/archive-writing/creation-origin-artifacts-v1/application-final.spdx.json`, SHA-256
`8537067302167F027E874FC6936CC4F3071E19117AAC04AD0F85D7F919C95C36`. Its 261 files have unchanged payload hashes
from the independently rechecked inventory; 34 reviewed NuGet packages and one runtime pack contribute 235 files
matching their reviewed archives. No dependency changed. Its source state explicitly names the base commit plus the
final source-delta checksum above and does not claim a reproducible release identity. Report edits follow capture.

Other testers caused explicit deferrals on Mac and Windows. Test-created FileCat/worker instances must exit before
releasing the machines; the Mac's temporary keep-awake/activity assertions remain until reboot. Full Unix marked-origin
semantics, complete outer-metadata admission/application, existing-file publication/recovery/cleanup, scalable capture,
Mac hard-memory containment, remaining formats and full native GUI/package/fuzz qualification remain open. The complete
implementation loop, subsequent RAR analysis/viable implementation and final independent review remain active.
