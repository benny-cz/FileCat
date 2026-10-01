# E-V09-T2 — recovery sessions under strace on the Ubuntu VM (I09)

Preliminary V09 evidence on Linux, the counterpart of E-V09-T1: the same gated session
(`RecoveryWriteTraceTests.A_recovery_session_writes_only_where_it_should`, `FILECAT_V09_UNIX_DEVICE`) run under
`strace -f -y` (every process and thread FileCat starts; every open for writing and every call that writes data or
changes names, sizes, modes, owners, times or attributes, recorded with its path; `analyze_strace.py` sums them up).
FileCat reads the devices directly (a loop device this user may read; root for the system disk), not through UDisks2.

- **Machine:** the lent Ubuntu 22.04.5 VM (E-ENV-02), kernel 6.8.0-138; its disk sda: sda1 (BIOS boot), sda2 (FAT, the
  EFI partition, mounted at `/boot/efi`), sda3 (ext4, `/`, which holds `/tmp` and the home folders). Fuzz processes ran
  meanwhile (other processes, not traced).
- **Builds:** `901b4a5` (L1–L3), `d39c402` (L4, after the fix below).

| Case | What | Result |
|---|---|---|
| L1 — safe topology | A 64 MiB FAT32 image in memory (`/dev/shm`, four files of recorded hashes, three deleted), attached as `/dev/loop21`, scanned as the user with FileCat's files in the home folder (`--data`); a deleted 3 MiB file read as the viewer reads it and recovered; 70 s open; closed | The device opened once, `O_RDONLY`; **no write to it or to its image; the image's SHA-256 before and after: `ca0e9b5bde6bd50019a30147b2d9eae5fb1489fe12d5d72d3da495190ddb9979`**. FileCat's writes: its data folder and the recovered file; .NET's own files in `/tmp` (see below) |
| L2 — a partition of the system disk | The EFI partition (`/dev/sda2`, mounted) scanned as root with FileCat's files in memory; scan only; 70 s | Opened once `O_RDONLY`; **no write to it or under `/boot/efi`**. The question said it is mounted and programs can write to it meanwhile |
| L3 — FileCat's own files on the source | The whole system disk (`/dev/sda`) with FileCat in its usual places (`~/.config/FileCat`, `~/.local/share/FileCat`) | **Refused; `/dev/sda` never opened**; the refusal names the folders and gives the `--data` command |
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

**Not covered here:** UDisks2/polkit (the path for a user who may not read the device), approval refusal, device
removal, and macOS (authopen, `fs_usage`).
