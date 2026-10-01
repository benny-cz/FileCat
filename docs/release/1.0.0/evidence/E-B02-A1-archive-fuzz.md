# E-B02-A1 — damaged archives of every format (trust boundary B02)

`ArchiveFuzzTests.Damaged_archives_are_refused_or_listed_never_crashed_on` (`b0b2329`; run alone since `6bbbb21`)
damages an archive round by round — bytes changed, more often in the first and last kilobyte where the headers are,
sometimes cut short — and goes through it as FileCat browses: listed three folders deep, ten members read a megabyte
each. A round fails on an exception that is not a refusal (not an archive, damaged data, encrypted, unsupported), on 60
seconds without an answer, or on more than 512 MiB allocated. Every round's damage depends only on the format and its
number, so a failing round is replayed alone (`FILECAT_ARCHIVE_FUZZ_START`).

Formats: ZIP through FileCat's own ZIP provider; TAR, TAR+gzip, gzip (archives made by the test), 7z LZMA2, 7z solid,
RAR 4, RAR 5, RAR 5 solid, TAR+xz, TAR+zstd (fixtures) through the archive provider over SharpCompress. Every run of
the suite takes 100 rounds of each.

## Runs

| Machine | Build | Rounds per format | Result |
|---|---|---|---|
| CI (every commit) | from `6bbbb21` | 0–99 | run alone; at `b0b2329` it ran alongside other tests and counted their allocations as its own (CI red; rounds that allocate at most 25 MiB alone) |
| Host | `b0b2329` (Debug) | 0–4,999, all eleven formats | **all passed**, 30–54 s each; at most 25 MiB in a round (TAR+xz round 27), slowest round 44 ms (`fuzz-host/archive-b0b2329/out-0/`) |
| Ubuntu VM | `b0b2329` | 0–199,999 of the five SharpCompress archive formats, copies in `/dev/shm` | 7z LZMA2 and 7z solid **passed** (477 s, 466 s); the RAR formats running |
| Host | `b0b2329` (Debug) | 5,000–104,999 of ZIP, TAR, TAR+gzip, gzip, TAR+xz, TAR+zstd | running |
| Owner's Mac | `6bbbb21` | 205,000–304,999 of all eleven, copies on a 1 GiB RAM disk | running |

Host round outputs (`out-0/`): 7z LZMA2 `8ffb9731b073a590ffe486875c00380e44320f507f6a6107aecc0475cbd4fe0e`, 7z solid
`9da80a10d139829c1bef9d4bf96b6053080f6c31670abba6574a8cdfbf967012`, RAR `39630aac2125110305f4a4af1bf09d02caad9fbf2d73687356ef3c9d5069c208`,
RAR 5 `8529e1967e2b68caa4ebe2a9cd034b4c8d57ad9712481811fd598d640d53cd3f`, RAR 5 solid
`6c83ff15e6c17363d8dc7293404ecee6dd88cb37f52d24509cd83d417e4b8b88`, TAR+xz `2cda0842b61c271f3c4536c0db73d9da4502e8dcc628b92d1b765f496fc48554`.
