# EFS provenance bounds for opened archive and image inputs

The ZIP, general archive and Recovery image readers now record EFS state and the held file identity and revision when they open a contributing input. The general archive reader uses its existing actual-open callback, including later multipart RAR volumes. Reopening the same identity at the same revision uses one entry. The reader retains earlier observations after an input handle closes.

An unavailable identity or revision, more than 1,024 distinct contributors, or more than 1 MiB of contributor metadata makes protection unknown unless EFS was already observed. An unknown result refuses disk cache, extraction, drag and remote upload where those routes use the existing archive or Recovery admission. Read-only memory and forward-only access retain their existing bounds. The current EFS state is still checked around persistence-producing reads.

The Windows 11 x64 VM at medium integrity passed 149 archive and Recovery tests. The native count test opened 1,025 distinct files, including a repeated open of the first file. Admission accepted 1,024 distinct inputs and refused the overflow. The metadata-budget test confirmed that a later ordinary input could not clear overflow. Existing EFS controls verified ZIP, TAR, a later RAR part and a Recovery image. Ubuntu x64 and Apple silicon macOS each passed 139 cases with 10 expected Windows-only skips.

Exact results are in `opened-contributor-limit-tests.xml`, `opened-contributor-limit-ubuntu.xml` and `opened-contributor-limit-mac.xml` in the validation `.tmp` locations. This checkpoint bounds each reader's actual opened sources. Carrying the immutable input union across nested spool stages and binding it to broader output receipts remain open.
