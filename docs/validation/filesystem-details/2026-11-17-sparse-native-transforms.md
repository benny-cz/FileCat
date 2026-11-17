# Sparse native transform fixtures

The tests exercised Windows 11 x64 NTFS with 4,096-byte allocation units on the disposable VM as a medium-integrity user. They called native EFS encryption/decryption or the existing held-stream compression SET on synthetic 4 MiB main and named streams. These tests characterize filesystem behavior. FileCat's review, job and release admission rules still apply.

Each post-call observation records the object ID, the attributes returned for each stream, EOF, `FileStandardInfo` allocation, classic compression format, allocated ranges and SHA-256 of the complete logical stream. Both streams stayed on the original object and retained their EOF, logical bytes, reported sparse state and allocated-range lists after each tested call. The range reports use [FSCTL_QUERY_ALLOCATED_RANGES](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_query_allocated_ranges). They describe the filesystem API's view of areas that can contain nonzero bytes, rather than disk addresses.

| Native actions | Fixtures | Observations |
|---|---|---|
| Encrypt then decrypt | All-hole main, holes in main only, holes in named only, holes in both, sparse main with every range allocated | Five cases passed. Each call returned success, and the expected EFS attribute was observed. Allocation stayed unchanged in both streams, including zero bytes in the all-hole main. |
| Compress then remove main compression | Holes in main only, holes in named only, holes in both, sparse main with every range allocated | Four cases passed. The main stream observed classic compression and then no compression. Its two sparse extents used 131,072 allocation bytes before and after the round trip, and 8,192 while compressed. The named stream kept its attributes, format, ranges and allocation. |

The initial fixture assumed that a sparse named stream implies a sparse attribute on the main handle. Two cases stopped before the first call on that assertion. The corrected fixture queried both streams. The named-only fixture returned `SparseFile` and both 65,536-byte ranges on the named stream. The main handle returned no sparse flag and one full range. This distinction held through all four native actions. An EFS inventory must consider supported per-stream observations to reason about named-stream holes.

Final VM build completed with zero warnings and errors. All nine cases passed with `FILECAT_TEST_SPARSE_TRANSFORMS=1`. Exact outputs are in `.tmp/filecat-sparse-transforms-v2-tests.xml` and `.tmp/filecat-sparse-transforms-v2-results.log`. The initial probe is retained as `.tmp/filecat-sparse-transforms-tests.xml`.

The remaining gates include the FileCat held-path and held-stream adapters, read-only/denied cases, large logical size, named compression mixed with sparse layout, bounds under many extents and concurrent changes, and per-route output behavior. The fixture establishes native behavior for the tested shapes on this volume. Product sparse eligibility remains gated.
