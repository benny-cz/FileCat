# E-I03-APPIMAGE-COMPOSITION — producer versions, full notices and static-runtime observation

**Preliminary I03 child accepted at `8ae4229b9e93ed698a669e730b84ba685380188b`; not a legal disposition, reproducible-build claim, frozen candidate or release GO.**

## Exact producer and source binding

The pinned x86_64 runtime remains GitHub asset ID `657374641`, 944,632 bytes, SHA-256 `617aa77314d57c8b8e4fae9c3f0b35af7b30d5529cb69e98f64f92510ed98a46`, declared source `fedfad51acc2860b2916a1eef32ba4a500ff31ef`. The complete official x86_64 producer job log for workflow run `39320578745`, job `118293253809`, is retained with SHA-256 `22794e55ef34965af66616cb90823655f4297f2d0fb278a0a9906b9c97919495`. It records:

- base image `docker.io/library/alpine:3.21@sha256:ce64758a109eb420d874a118f87920e625e12d3634e03b4a5573fd9f6e5d3507`;
- musl-dev 1.2.5-r11;
- mimalloc2 and mimalloc2-dev 2.1.7-r0;
- zlib-dev and zlib-static 1.3.2-r1;
- zstd, zstd-dev and zstd-static 1.5.6-r2;
- source archives libfuse 3.15.0 (`70589cfd…2ca0`) and squashfuse 0.5.2 (`db0238c5…43d3`);
- final static-PIE link command `clang ... -static -static-pie runtime.c -lsquashfuse -lsquashfuse_ll -lzstd -lz -lfuse3 -lmimalloc -o runtime`.

An exact Git archive of the producer workflow, Dockerfile, Makefile and root LICENSE is retained from `fedfad51` with core line-ending conversion disabled. Archive SHA-256 is `728c6308b9df18c94ba561688dcbcea990d7f6468e9cb785366596b434e0f111`; the root LICENSE is 1,678 bytes, SHA-256 `aa154fc9070614bbe7921f89db11efd1dba7a1f3a41685958110e2230f9c0ca1`, matching Git blob `a9fd00b042f392357a84fd81b7789976a53f5b4e` and FileCat's reviewed copy.

## Original full texts

The runtime notice snapshot now adds the original license members from exact source archives:

| Component | Producer version | Source archive SHA-256 | Retained member | Member SHA-256 |
|---|---|---|---|---|
| musl | 1.2.5-r11 (musl-dev) | `a9a118bbe84d8764da0ea0d28b3ab3fae8477fc7e4085d90102b8596fc7c75e4` | `musl-1.2.5/COPYRIGHT` (6,204 bytes) | `f9bc4423732350eb0b3f7ed7e91d530298476f8fec0c6c427a1c04ade22655af` |
| zstd | 1.5.6-r2 | `30f35f71c1203369dc979ecde0400ffea93c27391bfd2ac5a9715d2173d92ff7` | `zstd-1.5.6/LICENSE` (1,549 bytes) | `7055266497633c9025b777c78eb7235af13922117480ed5c674677adc381c9d8` |
| zlib | 1.3.2-r1 | `bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16` | `zlib-1.3.2/LICENSE` (1,002 bytes) | `e32ff4e00d9d94930537635291da39e7e612703334bf6fde8c7f1686fe8a45a2` |
| mimalloc | 2.1.7-r0 | `0eed39319f139afde8515010ff59baf24de9e47ea316a315398e8027d198202d` | `mimalloc-2.1.7/LICENSE` (1,096 bytes) | `19c99805e7a44a34b297a75d1edea9985e300066dfc024d5c99d4236d4573b5d` |

The production notice validator now binds the producer run/job, base digest, package versions and all four file hashes, and rejects an altered producer version. Its bounded campaign passes 71/71 controls (11 allowed, 60 refused); exact clean-source rerun and packaged-byte verification are retained with the cross-platform draft set.

## Independent runtime observation

Ubuntu 26.04.1 inspects the downloaded immutable asset without executing it. `file` reports x86-64 ELF static PIE, stripped, build ID `ef655b05ac26feebfd3bc24ec7ccb30389cefc33`; `ldd` reports `not a dynamic executable`; the dynamic table has no `DT_NEEDED` entries. The `.comment` section identifies Alpine GCC 14.2.0 and Alpine clang 19.1.4. Retained strings include the upstream root notice, `squashfuse 0.5.2`, squashfuse, zlib, zstd, mimalloc and musl markers. This agrees with the exact link command and source recipe but is not a bit-for-bit source reproduction.

The native inspection archive SHA-256 is `b5df7e646fbd9adedb8604303b7eef6ee519579c195dea6ceadd4eee757dfba2`. Raw evidence is under `V:\FileCat\artifacts\release-evidence\i03-appimage-full-texts-20261028-v1`; the final exact package set is retained separately under `i03-cross-platform-draft-20261028-v4`.

## Boundary

This closes the previously named unknown producer-version and missing full-text gap for musl, zstd, zlib and mimalloc, and adds a bounded static-runtime composition observation. It does **not** resolve libfuse LGPL/static-link source and relinkability obligations, make a legal eligibility decision, prove the downloaded runtime reproducible from source, assign independent meaning to its embedded checksum, freeze a candidate, sign an artifact or authorize publication. Those stopping conditions remain explicit in `REL-I03-PROVENANCE`; SharpCompress RAR eligibility remains the separate `EXT-02` dependency.
