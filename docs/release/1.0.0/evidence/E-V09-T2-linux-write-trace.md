# E-V09-T2 — recovery sessions under strace on the Ubuntu VM (I09)

Preliminary V09 evidence on Linux, the counterpart of E-V09-T1: the same gated session
(`RecoveryWriteTraceTests.A_recovery_session_writes_only_where_it_should`, `FILECAT_V09_UNIX_DEVICE`) run under
`strace -f -y` (every process and thread FileCat starts; every open for writing and every call that writes data or
changes names, sizes, modes, owners, times or attributes, recorded with its path; `analyze_strace.py` sums them up).
FileCat reads the devices directly (a loop device this user may read; root for the system disk), not through UDisks2.

- **Machine:** the lent Ubuntu 22.04.5 VM (E-ENV-02), kernel 6.8.0-138; its disk sda: sda1 (BIOS boot), sda2 (FAT, the
  EFI partition, mounted at `/boot/efi`), sda3 (ext4, `/`, which holds `/tmp` and the home folders). Fuzz processes ran
  meanwhile (other processes, not traced).
- **Builds:** `901b4a5` (L1–L3), `d39c402` (L4, after the fix below), `1477de3` (L5), `134db5e` (L6).

| Case | What | Result |
|---|---|---|
| L1 — safe topology | A 64 MiB FAT32 image in memory (`/dev/shm`, four files of recorded hashes, three deleted), attached as `/dev/loop21`, scanned as the user with FileCat's files in the home folder (`--data`); a deleted 3 MiB file read as the viewer reads it and recovered; 70 s open; closed | The device opened once, `O_RDONLY`; **no write to it or to its image; the image's SHA-256 before and after: `ca0e9b5bde6bd50019a30147b2d9eae5fb1489fe12d5d72d3da495190ddb9979`**. FileCat's writes: its data folder and the recovered file; .NET's own files in `/tmp` (see below) |
| L2 — a partition of the system disk | The EFI partition (`/dev/sda2`, mounted) scanned as root with FileCat's files in memory; scan only; 70 s | Opened once `O_RDONLY`; **no write to it or under `/boot/efi`**. The question said it is mounted and programs can write to it meanwhile |
| L3 — FileCat's own files on the source | The whole system disk (`/dev/sda`) with FileCat in its usual places (`~/.config/FileCat`, `~/.local/share/FileCat`) | **Refused; `/dev/sda` never opened**; the refusal names the folders and gives the `--data` command |
| L5 — through UDisks2 | A user not in `disk` (the loop device `root:disk 0660`; an earlier test had left it readable to all, which the first attempt caught) scans a 64 MiB FAT32 image in memory through UDisks2 (2.9.4): FileCat cannot open the device, asks UDisks2's `OpenDevice`, and polkit (0.105) grants it here without a password by a local authority entry made for this test and removed after it. The files were deleted by editing the image as Windows deletes them (I66, below); a deleted 3 MiB file read as the viewer reads it and recovered; 70 s open | **FileCat's process never opened the device**: the descriptor came from UDisks2, opened read-only (`/proc/…/fdinfo` flags `02100000`: `O_RDONLY`, large file, close-on-exec). **No write to the device or its image; SHA-256 before and after `0b2fa99025a863bf6c1590aefa6c540149c4dac819cdc3c84eceaf657725bf9b`**. FileCat's writes: its data folder, the recovered file, .NET's endpoints in `/tmp` (no `TMPDIR` set here), thread names under `/proc`, and its anonymous memory file |
| L6 — the approval refused | As L5, but polkit says no (a local authority entry denying `open-device` to this user, made for the test and removed after it) | The scan tab failed with **"Reading /dev/loop3 was not authorized: Not authorized to perform operation"**; **the device never opened, nothing read; the image unchanged (SHA-256 `b81c650326a6d2f38c9f76acd09d76821a8255b88d93f659de0566afc930da19`)**. The first run of L6 (`63a215a`) showed only "Access is denied.": every refusal's reason was dropped by the listing (I67, fixed `134db5e`) |
| L4 — the whole system disk | `/dev/sda` scanned as root with FileCat's files in memory and `TMPDIR` there (as the `--data` command now sets it); scan only; 70 s | Opened twice, `O_RDONLY`; **not one write of FileCat's processes on any disk**: all in `/dev/shm` (its data folder, `TMPDIR` with .NET's own endpoints) and an anonymous memory file of .NET's (`memfd:doublemapper`) |

**Found and fixed (`d39c402`):** in L1 the trace showed `/tmp/.dotnet/shm` made while the disk was being chosen: in a
FileCat started with `--data`, asking whether the usual FileCat runs looked up a named mutex, which .NET keeps as files
in the temporary folder (its folders are made even for a lookup) — a write onto the disk holding `/tmp` at the moment it
was chosen. The usual FileCat is now asked through its pipe, which writes nothing. The .NET runtime's own debugger and
diagnostics endpoints (`clr-debug-pipe-*`, `dotnet-diagnostic-*-socket`) live in the temporary folder from start to exit;
the `--data` command now sets `TMPDIR` inside the data folder, and L4 shows them there.

Files (`artifacts/release-evidence/i09/v09-linux/`): `out.txt` `c97ac59ad975cb8be484560e4fb0cf28d6595c1d3307f19cfb799c8f99ff8bb4`;
L1 `l1-test.txt` `40a2751fb6e6ea48d8a4879d304adbcef54d9d8b334c8025c57b857abfa44f3b`, `l1.strace`
`603977222a29166c675c0534fb0724b6a986a44ec6fb8411a3a76264ee6602b9`, `l1-writes.txt`
`730bba372367cb58a61b204823be1e6d2491d7f76324306f681dc00424af661e`; L2 `l2-test.txt`
`e5dee1309ef152cd2953c50abd448463fb88df874af3cc9e3e97b55f29afb9e6`, `l2.strace`
`453ee15c6f5b6f66000c4dbc5b32bad6750e837e5b516a55907f506559e11701`, `l2-writes.txt`
`211fbc2253532d18099364d558091aae38d81a9f4942ba0e7dbc8d3c46ddbcf3`; L3 `l3-test.txt`
`c9b3310dac5f1d9177dcf9bb575e2057073fa55fed233975724054b126088557`, `l3.strace`
`62454f551c6a2916988fc37298e1fdbf4f75543826e6c53a2ecdfc551247dd19`, `l3-writes.txt`
`f33eb98e6eaa279247ee8978f5199822d7ef8b78c5686ff1a3b8370df478139a`; L4 `l4-test.txt`
`4d007da0e3fb34229d09e7c27d248f91cde56ecd571fce35c0b90f16b82798fa`, `l4.strace`
`d8654aba624b6fb374bb84e9cea07212d7ddbc64fa1db1a1b10377f517659f77`, `l4-writes.txt`
`27a92eb2f90e166b1ab4f2a66581dbeec7df1dca918fd981bf6c1c88c3988b36`.

**Found on the way (I66, fixed `1477de3`):** in L5's first attempts, files deleted with `rm` came back listed as "0 bytes,
recoverable: the file was empty". The raw entries showed first cluster 0 and size 0: Linux's FAT driver may write the
emptied file's entry back after marking it deleted (L1's identical recipe had kept them; it depends on the driver's
timing). Such an entry is now listed by its name only, saying why; L5 then deletes by editing the image
(`artifacts/vm/fat-delete.py`), which keeps the size and start, as Windows does.

L5 files: `l5-out.txt` `97b5ae5d4e5d03c4c240623c1360c90d0501f931699558085802b8288e8b1c94`, `l5-test.txt`
`0aa3a4160796c384e0e3c32e0811e0a62c308c16a0475183312181ef4e2a4476`, `l5.strace`
`468d7640c068190eebcf358e65c9889f8e8e7610f7752319827e7f96d55115a3`, `l5-writes.txt`
`a1f126005073949ff0d25e7fa5c340d5a76c21cd788cbcf91df06a87a9183de7`, `l5-fd.txt`
`e0fd8b0ce57335161d2bd539c7f97d178ab200f3eaf82165ab2deb7dfa3f17be`.

L6 files: `l6-out.txt` `e2d64d36ef4e08242ef9a6c182d6e9c3331966258f71a1f6e1b93f1d039fdd30`, `l6-test.txt`
`dba0c71533999862823c513f5167827e712bec66dcccc2a51e454ef52dd6e9b1`, `l6.strace`
`def22e1a9f3031a7eb18264af91e92828a82f69c9a81ba7f0faf6206ef401888`, `l6-writes.txt`
`94b2a5b363a3c3a7f46c4e64781e6f24ca20889b3a052e03785c0064ad2eb588`.

**Not covered here:** device removal during a scan, and macOS (authopen, `fs_usage`).
