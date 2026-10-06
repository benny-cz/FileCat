# AppImage wrapper notices

This frozen snapshot retains the original type2-runtime root LICENSE and available
libfuse/squashfuse license texts from immutable declared sources. `index.json`
records their original byte hashes and the exact runtime input SHA-256. The package
tool verifies both before copying this directory into the AppImage payload.

The runtime recipe declares static squashfuse, zstd, zlib, libfuse, mimalloc and
musl inputs. This snapshot does not establish their complete actual binary
composition, all exact versions, full notices or static-link/source obligations.
It does not establish legal eligibility or a qualified release candidate. The
release audit remains Open. Retaining GPL/LGPL source texts does not assert that
every file or tool in those source archives ships in FileCat.

Review and update runtime bytes, source/provenance references and these notice
texts together when changing the pinned input or selecting another architecture.
Preserve the original upstream bytes and revalidate the final AppImage prefix and
all packaged notice bytes after assembly.
