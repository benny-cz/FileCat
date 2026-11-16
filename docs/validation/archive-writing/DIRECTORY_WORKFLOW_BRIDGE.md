# Explicit directory inputs through native archive publication

Follow-up to [native directory observations](NATIVE_DIRECTORY_CAPTURE.md), over `eacefe55`.
This implements the explicit flattened-directory preparation/publication bridge. No production profile is enabled.

## Changes

Directory additions now carry validated ordinary mode and exact UTC time independently of regular-file payload
inputs. The parent binds captured additions to their exact sealed capture owner and directory record; dropping a
folder, retargeting its capture, or replacing it with equal unbound metadata refuses. Explicit newly requested folders
may supply metadata without pretending to be a captured native source. Every captured directory must be used.

The worker receives no directory source pathname, native identity, capture ID or read grant. Transform greeting
generation 4, request schema 3 and addition schema 2 distinguish the new contract; incompatible schemas, wrong
kind/grant combinations, missing metadata, invalid modes/times and trailing/truncated records refuse. File grants
retain their existing alias map. The TAR engine and shared gzip/bzip2/Zstandard paths use the admitted directory mode
and nanosecond time; parent expectations and fresh-process observations compare them exactly.

Durable origin evidence orders the original archive, captured files, then captured directories, with exact source
kinds and identities. Directory origins participate in the strongest Windows zone union. New outputs receive a
zone-only aggregate, while existing outputs retain their original attribution fields when raised. Unrepresentable
Unix marked-origin cases retain their existing refusal. Empty folder-only creation works with zero payload grants.

Native directory observations are rereviewed before preparation, after fresh verification and through final new-file
publication or NTFS content commit, including post-effect reconciliation. Stale folders prevent an uncommitted write.
Staging directly inside a captured source directory refuses before journal/artifact creation. Explicit flat directory
records do not discover children, establish complete recursive scope or grant source-tree deletion.

## Windows namespace finding

The first broad native run found that NTFS change time alone can miss a new child when last-write time is restored.
The adapter now also observes immediate child membership through the held directory using
[NtQueryDirectoryFile](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntquerydirectoryfile).
It hashes exact UTF-16 names, file IDs and directory/reparse kinds; sorted fixed-size entry digests make the result
independent of enumeration order. It follows no child link and retains no per-child handle or name string.

The reader bounds native batches, offsets, lengths, total records (including dot entries), name bytes and total native
bytes, and checks cancellation while scanning and hashing. Source-folder handles explicitly request list access;
failure never means an empty namespace. This is immediate membership evidence, not an atomic recursive snapshot:
the API documents that concurrent changes can affect what enumeration sees. Stable recursive capture and its
filesystem/resource qualification remain required. A repeated native mutation regression restores last-write time
after each of 64 child creations and requires refusal every time.

## Executed checks

Mac passed **229 / 2 platform skips / 231 total**; Ubuntu passed **228 / 3 platform skips / 231 total** across capture,
directory plan/wire/provenance, transform and native storage regressions. Both also passed two additional native
folder-only cases (captured empty folder and new empty folder). Independent native TAR extraction and a separate
Python TAR metadata reader checked all four generated profiles, each payload, permission modes and exact PAX time,
including -1 second plus 999,999,999 nanoseconds.

The strict Mac-hosted ARM64 Linux VM completed eight distinct fresh transform/verifier roles, followed by real APFS
new-file publication of all four folder-containing outputs. Each helper exited zero and was reaped. Its independent
readers passed and compiler checksums matched **814 source documents / 7,616,948 bytes**. The existing trusted-host
memory, installed signing/image/package and full boundary qualification gates remain open.

Windows' broad namespace-fix run passed **248 native cases / 1 platform guard skip / 249 total**, including existing
real crash/transaction regressions and all four new directory create/update profiles. The final bounded-reader build
then passed **25 affected native cases / no skips**, including the 64-mutation regression and multi-batch Unicode
membership. Its common run passed **122 / 110 Unix-only skips / 232 total**. The actual standard account has a loaded
profile and SID ending `1003`; the successful suite launchers exited zero. Deliberate crash cases retain their
separately asserted termination outcomes. The native run's temporary TxF opt-in stays process-local.

Directory-only mode/time travel through actual fresh confined workers. NTFS creation propagates the folder's zone
to a new output; content updates raise the original's zone while retaining its existing source attribution. A changed
captured folder refuses before either new-file claim or content commit, retaining the previous archive. Independent
Mac readers checked all eight Windows created/updated results, including unchanged original payloads and exact
directory metadata. No test skip is counted as a native pass.

Initial Unix builds caught a test using `Write` instead of the evidence writer's `Append` method and a duplicate
primary-constructor owner capture warning; both were corrected before successful tests. The first Windows build
caught a missing test namespace import. Failed/deferred builds and the NTFS child-mutation failure remain retained.

## Evidence and remaining work

Frozen source/test/worker inventories remain in the private checkout's ignored `.tmp`. The copied archive digest and
each payload size/hash are verified independently. No new production dependency was added; native oracles are test
tools, not shipped codecs or a fallback. Shipping licence/SBOM/candidate qualification remains separate.

Mac/Ubuntu production PDB checksums each matched **835 documents / 7,900,924 bytes**. The Windows collector compared
all **314 files** in the final three test/crash-harness/worker-probe runtime trees with their build outputs before
freezing them, including file counts and content hashes. Its production-source audit covers the five actually
loaded production assemblies and matched **920 documents / 9,270,302 bytes**; generated xUnit runner source is outside
that audit scope. Core builds have zero warnings/errors; Windows test builds retain the existing four WebView2/WPF
assembly warnings, with no new compiler warnings in this implementation.

| Artifact | Bytes | Payload rows | SHA-256 |
| --- | ---: | ---: | --- |
| `mac-directory-workflow-evidence-v1.tar.gz` | 5719600 | 873 | `A1064EC5513D2FE0EAAA0D0096DE7E1CA97945D2873D7FF8BFF5CE33D3786333` |
| `ubuntu-directory-workflow-evidence-v1.tar.gz` | 5719430 | 873 | `2E056C6BECF653EFC6507262D3E8C79198C7B60A0BB4C7025A0C4DABC1DFD62A` |
| `macos-vm-directory-workflow-evidence-v1.tar.gz` | 96427337 | 1092 | `C069DAE808ED19B5A0AEDB08606E3C165403308FCC75564C2359BDC0168796E2` |
| `directory-workflow-windows-evidence-v1.tar.gz` | 106621389 | 1566 | `67D321B6BA1AA5742DC69D994AA2199C29A5D62055A9B0E9B473912B107F20AB` |
| `windows-directory-workflow-oracle-evidence-v1.tar.gz` | 14405 | 23 | `FAE3ED40033DF643D5B93CE870ED84CB4CC74C4523C756D38A5EED8FFD80BCC4` |

Unix tested source packet v2: `9B1801E8950B1966E721A6E8929D000C3B82641F10B49E38770217BFD3720DB5`.
Final Windows source v7: `05E93FE348F754E68DD58FA3D76A70792CDD0271A5BC41546F076B8B26F75B66`.
Additional folder-only tests: `03A22A5F70C2F9EA4FB44D8C3BD4E734A2498DE62DB16BC2758F18CE861E8318`.
VM validation source: `BF60359B1D6DA13AA5E9B33B399EC72ADE587733A45935BE38070DF39B73C9F1`.

Recursive stable enumeration, exclusion/collision review, global source-root and transient-memory admission, private
snapshots, production command/UI/edit-session integration, remaining formats and the full filesystem/crash/fuzz/
packaging gates remain unfinished. RAR analysis and feasible licence-compatible implementation still precede the
final independent UX/UI/functional/security review. The full goal remains active.

Final censuses found no remaining test/worker instance from this task. Other testers were preserved and testing was
deferred while their processes were active. Mac's temporary until-reboot wake helper/assertions remain. No host GUI
or FileCat process was used. The shared `E:\FileCat` checkout remains `main`; implementation stays in its private worktree.
