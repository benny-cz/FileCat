# EFS release audit — 22 November 2026

NTFS compression is now released on Windows 11 x64 and Windows on Arm. EFS is still not offered: every E gate in the [compression and EFS plan](../../design/FILECAT_NTFS_COMPRESSION_EFS_IMPLEMENTATION_PLAN.md) is Partial. This audit maps each gate's acceptance column to the evidence and lists what is left. WP9 ("enable only the passed action/target scope") is a release decision, so the scope proposal at the end needs the owner's choice.

## Two kinds of gaps

- **Concrete fixtures not yet run.** These close with a run on a machine already available, or with the owner present.
- **Open-ended notes.** Many E-OUT rows end with "wider destination and race coverage remains open" or "broader native qualification remains open". More runs cannot close an open-ended note. Compression closed the same kind of note by scope: the categories it did not qualify (removable and hot-plug drives, large sparse removal, SMB, cloud) stay refused with a reason. EFS needs the same decision for each route.

## Gate by gate

| Gate | Covered by evidence | Left |
|---|---|---|
| N-E1 | Selected and recursive Encrypt and Decrypt on x64 and native Arm64; NVDA round trip; sparse inputs; 64 KiB clusters; removable NTFS refusal; decryption without the key under matching ACLs; the 1,025-stream refusal; policy refusal before setup; wrong-key and key-drift stops (N-KEY receipts) | **Windows Home:** FileCat refuses EFS by an edition allowlist (`efs-build`: Pro, Enterprise and Education, with their N variants), but no native run on a Home edition has shown the refusal and its wording. Needs a Home installation; the Windows on Arm ISO on the test Mac includes Home. Unlisted editions such as Pro for Workstations and Pro Education are also refused, which the plan allows ("unknown edition/policy is not a success assertion"). Their reason reads "requires a qualified Windows 11 Pro, Enterprise or Education host", which a Pro for Workstations user may not recognize as their case. |
| N-E2 | The 21-case pathname and stream suite on x64 and Arm64; late compression of the unnamed, existing named and new named streams; retained key evidence after a sparse stream became unreadable | No concrete case is named as missing. Remaining step: map each acceptance item (file and directory ownership, held lifetime, redirection, post-inspection races, rename and deletion guards) to its receipt, as the C release did for its rows. |
| N-KEY | Software keys, PFX backup and restore, managed recovery agents and default-key changes on both architectures; virtual TPM and parented comparisons, CNG export policy and the YubiKey on x64 | **Arm64 TPM key:** the UTM VM has a TPM 2.0 and can run the x64 virtual-TPM cases. **Arm64 hardware key:** the YubiKey would need USB passthrough into the VM, with the owner present for the PIN. |
| N-UI.E | x64 NVDA: EFS review, acknowledgments, setup, wizard cancellation and completion, selected-file backup, High Contrast, 150% text, 144 DPI | **Arm64 NVDA:** the three EFS modes of the storage review runner (canceled, completed and selected-file key backup) on the UTM VM. They need a fixture EFS certificate in the guest profile. |
| N-OUT, T-EXPORT | Per route, see below | Per route, see below |
| Shared `.E` rows (T-UI, T-CAP, T-TRAV, T-OUT, T-LIFE, T-REC, T-LIMIT, T-REFRESH, T-REG, N-ID) | Each cites one or two E receipts beside its passed C side | No E-side release checkpoint yet. The C release mapped every acceptance item of these rows to tests and native runs; the E side needs the same mapping, then a focused run of the mapped tests on both architectures. |

## Output routes (E-OUT)

| Route | Decided behavior | Verified | Open note |
|---|---|---|---|
| E-OUT1 native copy and move | Preserve, or approve loss per attempt; refuse destructive replacement of an EFS target | x64 NTFS, FAT32, SMB fallback; Arm64 NTFS, sparse replacement | Wider destinations and races |
| E-OUT2 in-place hex save | Refuse | Admission, writes and final verification; Arm64 output evidence | None |
| E-OUT3 Save As and patch export | Explicit approval before any stage | x64 NTFS; Arm64 output evidence | Other destination filesystems |
| E-OUT4 uploads | Fresh consent per source and reconnect | x64 live; Arm64 39 controlled cases and four live transports | Wider providers and races |
| E-OUT5 administrator copy | Refuse | x64 and Arm64 NTFS | Other filesystems |
| E-OUT6 packing | Approve loss or refuse | x64 NTFS; Arm64 in the 49-case fixture | None named |
| E-OUT7 outer-EFS archive update | Refuse | x64 NTFS; Arm64 49-case fixture | None named |
| E-OUT8 member editing | Refuse | x64 and recovery routes; Arm64 49-case fixture | Broader providers and recovery |
| E-OUT9 legacy ZIP mutation | Refuse | x64 NTFS publication and recovery cases; Arm64 49-case fixture | Pathname interval, inducing a native partial replacement, detachment inside native calls |
| E-OUT10 spools | Refuse | x64 ZIP, TAR, multipart RAR; Recovery cache and Arm64 output evidence on both | Wider formats and re-admission races |
| E-OUT11 external editors | Guidance only | Wording | None |
| E-OUT12 extraction | Refuse | Both architectures for ZIP, TAR, RAR and Recovery | Broader native qualification |
| E-OUT13 drag staging | Refuse | Both architectures | Broader native qualification |
| E-OUT14 imported hex patches | Provenance before first write; refuse unprotected save | x64 partial apply, Undo and Redo, recovery refusal; Arm64 output evidence | Wider recovery and architecture coverage |
| E-OUT15 reports | Metadata only; approve or refuse payload | x64 and Arm64 | Other destinations; interruption inside the native call |

## Proposed release scope (owner's decision)

If EFS is to ship in the first release, a scope that the evidence supports:

- **Hosts:** native x64 and native Arm64 Windows 11 Pro, Enterprise and Education at normal integrity. Home and unlisted editions are refused with a reason.
- **Targets:** local fixed NTFS, like compression. Removable, hot-plug, network, ReFS, FAT and exFAT, cloud and the other refused categories stay refused.
- **Actions:** Encrypt and Decrypt for selected items and folders with their contents, with the reviewed setup, backup or declared recovery, and key comparison.
- **Outputs:** each E-OUT route keeps its decided behavior. Every open note becomes a refusal for that destination or condition, not an approval.

Before enabling, the concrete gaps above would run: the Windows Home refusal, the Arm64 TPM key, the Arm64 EFS NVDA modes, the Arm64 hardware key with the owner present (or an explicit decision to ship without it), and the shared-row mapping.

The alternative is to keep EFS unoffered in the first release and record the gates as Partial, as now.
