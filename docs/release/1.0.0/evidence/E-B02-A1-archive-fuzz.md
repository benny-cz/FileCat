# E-B02-A1 — damaged archives of every format (trust boundary B02)

`ArchiveFuzzTests.Damaged_archives_are_refused_or_listed_never_crashed_on` (`b0b2329`; run alone since `6bbbb21`)
damages an archive round by round — bytes changed, more often in the first and last kilobyte where the headers are,
sometimes cut short — and goes through it as FileCat browses: listed three folders deep, ten members read a megabyte
each. A round fails on an exception that is not a refusal (not an archive, damaged data, encrypted, unsupported), on 60
seconds without an answer, or on more than 512 MiB allocated by the thread that read the archive (`e85b86a`; 640 MiB
for RAR 4 since `325aa63`, below). Every round's damage depends only on the format and its number, so a failing round
is replayed alone (`FILECAT_ARCHIVE_FUZZ_START`) — since `c22c793`: before it, the archives the test makes (ZIP, TAR,
TAR+gzip, gzip) carried the time of the run, and their rounds did not replay.

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
| Ubuntu VM | `325aa63` (Debug) | 1,000,000–1,999,999 of both 7z, the three RAR, TAR, TAR+xz, TAR+zstd (`/dev/shm`) | started 10:57 UTC; running |
| Host | `325aa63` (Debug) | 1,000,000–1,999,999 of ZIP, TAR+gzip, gzip | started 10:58 UTC; running (`fuzz-host/archive-325aa63/`) |

Host round outputs (`out-0/`): 7z LZMA2 `8ffb9731b073a590ffe486875c00380e44320f507f6a6107aecc0475cbd4fe0e`, 7z solid
`9da80a10d139829c1bef9d4bf96b6053080f6c31670abba6574a8cdfbf967012`, RAR `39630aac2125110305f4a4af1bf09d02caad9fbf2d73687356ef3c9d5069c208`,
RAR 5 `8529e1967e2b68caa4ebe2a9cd034b4c8d57ad9712481811fd598d640d53cd3f`, RAR 5 solid
`6c83ff15e6c17363d8dc7293404ecee6dd88cb37f52d24509cd83d417e4b8b88`, TAR+xz `2cda0842b61c271f3c4536c0db73d9da4502e8dcc628b92d1b765f496fc48554`.
