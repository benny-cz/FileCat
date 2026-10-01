# E-B02-A1 — damaged archives of every format (trust boundary B02)

`ArchiveFuzzTests.Damaged_archives_are_refused_or_listed_never_crashed_on` (`b0b2329`; run alone since `6bbbb21`)
damages an archive round by round — bytes changed, more often in the first and last kilobyte where the headers are,
sometimes cut short — and goes through it as FileCat browses: listed three folders deep, ten members read a megabyte
each. A round fails on an exception that is not a refusal (not an archive, damaged data, encrypted, unsupported), on 60
seconds without an answer, or on more than 512 MiB allocated by the thread that read the archive (`e85b86a`; 640 MiB
for RAR 4 since `325aa63`, below). Every round's damage depends only on the format and its number, so a failing round
is replayed alone (`FILECAT_ARCHIVE_FUZZ_START`). For the archives the test makes (ZIP, TAR, TAR+gzip, gzip) that holds
only since `c4d81d7`: before `c22c793` they carried the time of the run; until `c4d81d7` they still carried the writing
process's ID (.NET names each PAX header `./PaxHeaders.<id>/…`), and .NET's native deflate gives other bytes on x64 than
on ARM64, so their rounds replayed neither in another run nor on another machine. The test now names the PAX headers
for process 0 and deflates with SharpCompress's managed code; each summary line gives the original's SHA-256, and the
host and the Mac give the same four (`3882e0ea…` ZIP, `4743b809…` TAR, `0276a758…` TAR+gzip, `b7537f04…` gzip). Since
`cddce72` a failing round also keeps its damaged copy (`FILECAT_ARCHIVE_FUZZ_KEEP`).

Formats: ZIP through FileCat's own ZIP provider; TAR, TAR+gzip, gzip (archives made by the test), 7z LZMA2, 7z solid,
RAR 4, RAR 5, RAR 5 solid, TAR+xz, TAR+zstd (fixtures) through the archive provider over SharpCompress. Every run of
the suite takes 100 rounds of each.

## Findings

- **I58 (fixed `325aa63`):** TAR round 97053 allocated 512 MiB on the thread that read a 31 KiB archive. One digit of
  a PAX extended header's size field was changed; .NET's `TarReader` rents an array of the size a metadata header gives
  before it reads the data. A guard now checks those headers as they pass (ISSUES I58).
- **RAR 4's PPMd model (bounded by the format; accepted):** round 248010 of the RAR 4 fixture allocated 517 MiB for a
  58 KiB archive (516 MiB on the Mac, the same round). The damage made a PPMd block ask for the most model memory RAR 4
  allows: one byte, (n + 1) MiB, so 256 MiB, which unrar allocates as well. SharpCompress rents it from the shared array
  pool, which hands out the next power of two, 512 MiB (`SubAllocator.StartSubAllocator` from `ModelPPM.DecodeInit`,
  seen with a 256 MiB heap cap). It is taken only when such a member is read, at most once at a time. The budget for RAR 4
  rounds is 640 MiB, and the round is replayed in every run (`325aa63`).
- **A TAR+gzip round over budget that could not be rebuilt:** on the Mac (`325aa63`, rounds 2,000,000–2,999,999),
  TAR+gzip round 2294974 allocated 512 MiB on the reading thread. It passes alone on the Mac and on the host: that
  run's archive held its process's ID, so its damaged bytes are gone; candidate IDs near the logged one did not rebuild
  them, and FileCat's TAR, gzip and member-reading paths hold no allocation that size behind the I58 guard. Open; the
  generated formats run again with replayable rounds and kept copies (`cddce72`).
- **RAR 4 round 3655801, "still reading after 60 s" (Ubuntu, `cddce72`, rounds 3,000,000–3,999,999): not reproduced;
  open.** The round's archive was kept (60,029 bytes, SHA-256 `d153540d…e7ad`, the same bytes the round number makes
  anywhere). Alone it reads in milliseconds: 5 ms on the host, 27 ms on the same VM, and also under a 768 MiB memory
  limit; its one member is refused as damaged (`InvalidDataException`) after about 25 ms. What it is not:
  - **the VM standing still:** the RAR 5 solid lane ran on the same VM through that minute (it finished at 15:32 UTC)
    and none of its million rounds took over 195 ms; the kernel logged nothing, and swap was barely used (1 MB);
  - **leftover memory:** SharpCompress 0.50.4 takes the RAR VM's memory and the RAR 4 window from the shared array pool
    and does not clear them (its PPMd model memory it does clear), so a long run could hand a round bytes an earlier
    round left; but the round reads alike, refused after 20–28 ms, with the pool's arrays of those sizes given back
    first filled with zeros, 0xFF, random bytes or text;
  - **leaked threads:** the process repeating the run had 14 threads half an hour in.
  The 655,801 rounds before it are being run again in one process, as in the failing run (`~/fc-v8/rar-prefix`).

**The Mac's TAR+gzip round 2294974** (build `325aa63`, 512 MiB) is not the same input as the host's run over that same
range on `cddce72`: `325aa63` built each machine's TAR original differently, which is why that round could not be
rebuilt, and `cddce72` normalizes it. So the host's pass says the range is clean for the bytes everyone now builds; it
neither reproduces nor refutes what the Mac saw. The round stays recorded as unexplained.

## Runs

| Machine | Build | Rounds per format | Result |
|---|---|---|---|
| CI (every commit) | from `6bbbb21` | 0–99 | run alone; at `b0b2329` it ran alongside other tests and counted their allocations as its own (CI red; rounds that allocate at most 25 MiB alone); the budget is the reading thread's since `e85b86a` (CI's ARM64 runner counted 1.1 GiB of other work within a round) |
| Host | `b0b2329` (Debug) | 0–4,999, all eleven formats | **all passed**, 30–54 s each; at most 25 MiB in a round (TAR+xz round 27), slowest round 44 ms (`fuzz-host/archive-b0b2329/out-0/`) |
| Ubuntu VM | `b0b2329` (Release) | 0–199,999 of 7z LZMA2, 7z solid, RAR 4, RAR 5, RAR 5 solid; copies in `/dev/shm` | **all five passed** (466 s, 477 s, 1,284 s, 830 s, 962 s) |
| Host | `b0b2329` (Debug) | 5,000–104,999 of ZIP, TAR, TAR+gzip, gzip, TAR+xz, TAR+zstd | ZIP, TAR+gzip, gzip, TAR+xz, TAR+zstd **passed** (188–343 s); TAR **stopped at round 97053** (I58) (`fuzz-host/archive-b0b2329/out-5000/`) |
| Owner's Mac | `6bbbb21` | 205,000–304,999 of all eleven, copies on a 1 GiB RAM disk | nine **passed** (134–171 s); TAR stopped at round 230427 (512 MiB; that build's TAR archives carried the run's time, so the round is not today's; I58's class) and RAR 4 at round 248010 (516 MiB, the PPMd model above) |
| Host; owner's Mac | `c22c793` | TAR from 0, RAR 4 from 240,000 | stopped at TAR 97053 and RAR 4 248010 on both machines alike: the rounds replay |
| Owner's Mac | `325aa63` | 0–999,999 of all eleven (RAM disk) | **all eleven passed** (1,830–2,345 s each, 10:57–11:36 UTC). Most allocated by one round: ZIP 48 MB, TAR 16 MB (was 512 MiB before I58's fix), TAR+gzip 18 MB, gzip 0 MB, 7z LZMA2 24 MB, 7z solid 24 MB, RAR 4 516 MB (round 536421, the PPMd model above), RAR 5 1 MB, RAR 5 solid 5 MB, TAR+xz 25 MB, TAR+zstd 29 MB; slowest round 158 ms (`fuzz-c1/mac-af7/`, `SHA256SUMS.txt` `0543acb9d93110414fa0492c49adbdb87d05a95b36ed865096bfb49d0596de74`) |
| Ubuntu VM | `325aa63` (Debug) | 1,000,000–1,999,999 of both 7z, the three RAR, TAR, TAR+xz, TAR+zstd (`/dev/shm`) | **all eight passed** (3,056–8,871 s). Most allocated by one round: RAR 4 516 MB (round 1464987, the PPMd model, within its 640 MiB budget), both 7z and TAR+xz 24 MB, TAR 16 MB, RAR 5 solid 5 MB, TAR+zstd 3 MB, RAR 5 1 MB; slowest round 3,064 ms (RAR 4, round 1091475) |
| Ubuntu VM | `cddce72` (Debug; zip `0fac8dea…792a`, the build the host runs) | 3,000,000–3,999,999 of both 7z, the three RAR, TAR+xz, TAR+zstd, four lanes; failing rounds kept | 7z solid and TAR+zstd **passed** (3,072 s and 1,639 s; at most 24 MB and 22 MB in a round); TAR+xz, RAR 5 solid, 7z LZMA2 and RAR 5 **passed** too (5,478 s, 5,894 s, 2,229 s, 3,322 s; at most 24 MB in a round, RAR 5 solid 5 MB, RAR 5 1 MB); **RAR 4 stopped at round 3655801**, "still reading after 60 s", the round kept (60,029 bytes, SHA-256 `d153540d…e7ad`): see Findings (`~/fc-v8`, script `artifacts/vm/ubu-fuzz-v8.sh`) |
| Owner's Mac | `5394c71` (the archive code and fixtures of `cddce72`, so the same bytes per round; zip `08888eb6…948d`) | 3,000,000–3,999,999 of RAR 4, in one process as on the Ubuntu VM where round 3655801 stopped, and of ZIP, TAR, TAR+gzip and gzip, which no machine had run over that range; the heap capped at 768 MiB as there, copies on the RAM disk, failing rounds kept | started 16:56 UTC; running (`~/fc-q9`, `artifacts/vm/mac-queue-v9.sh`) |
| Host | `325aa63` (Debug) | 1,000,000–1,999,999 of ZIP, TAR+gzip, gzip | **all three passed** (2,093–3,313 s; at most 48 MB in a round, ZIP) (`fuzz-host/archive-325aa63/`) |
| Owner's Mac | `325aa63` | 2,000,000–2,999,999 of all eleven (RAM disk) | ten **passed** (RAR 4 at most 516 MB, the PPMd model; the rest at most 48 MB); TAR+gzip **stopped at round 2294974** (512 MiB; not rebuildable, above) |
| Host | `cddce72` (Debug) | 5,000,000–5,999,999 of ZIP, TAR+gzip, gzip | **all three passed** (1,335–2,232 s; nothing kept): most allocated by one round ZIP 48 MB (round 5697472), TAR+gzip 22 MB (5578265), gzip under 1 MB; slowest round 846 ms (gzip 5900739) (`fuzz-host/archive-cddce72/out-5000000/`) |
| Host | `cddce72` (Debug) | 4,000,000–4,999,999 of ZIP, TAR, TAR+gzip, gzip | ZIP, TAR+gzip, gzip **passed** (2,220–3,158 s; nothing kept): most allocated by one round ZIP 49 MB (round 4389188), TAR+gzip 25 MB (4858239), gzip under 1 MB; slowest round 1,915 ms (gzip 4835555). TAR **passed** too (4,015 s): at most 16 MB in a round (4274167), slowest 1,052 ms (4394481) (`fuzz-host/archive-cddce72/out-4000000/`) |
| Host | `cddce72` (Debug) | 2,000,000–2,999,999 of ZIP, TAR, TAR+gzip, gzip, replayable, failing rounds kept | **all four passed** (1,830–3,819 s; nothing kept). Most allocated by one round: ZIP 48 MB (round 2927387), TAR+gzip 21 MB (2640410), TAR 16 MB (2540928), gzip under 1 MB; slowest round 667 ms (ZIP 2117542). Originals' SHA-256 printed by each run, so another machine rebuilds the same bytes (`fuzz-host/archive-cddce72/out-2000000/`) |

Host round outputs (`out-0/`): 7z LZMA2 `8ffb9731b073a590ffe486875c00380e44320f507f6a6107aecc0475cbd4fe0e`, 7z solid
`9da80a10d139829c1bef9d4bf96b6053080f6c31670abba6574a8cdfbf967012`, RAR `39630aac2125110305f4a4af1bf09d02caad9fbf2d73687356ef3c9d5069c208`,
RAR 5 `8529e1967e2b68caa4ebe2a9cd034b4c8d57ad9712481811fd598d640d53cd3f`, RAR 5 solid
`6c83ff15e6c17363d8dc7293404ecee6dd88cb37f52d24509cd83d417e4b8b88`, TAR+xz `2cda0842b61c271f3c4536c0db73d9da4502e8dcc628b92d1b765f496fc48554`.
