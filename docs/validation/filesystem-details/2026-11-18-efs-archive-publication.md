# EFS protection during archive extraction and drag staging

ZIP and other archive providers admit staged output through the protection observations of the opened archive sources. The admission also covers a small member already materialized in memory. For a forward-only member the provider rechecks before and after reading each buffer. EFS or unknown protection prevents the initial output admission or interrupts publication if it is observed during a read.

On Windows 11 x64, four native cases exercised the actual drag and streamed transfer routes using EFS-encrypted ZIP and TAR archives. Each format included a small in-memory member and a 33 MiB member that supports forward-only reads. View reads succeeded. Drag staging and extraction were refused, the clear output directories contained no payload, and the source remained encrypted. The suite passed 111 tests with two fixture skips. The result is in `.tmp/filecat-efs-archive-publication-verified-tests.xml`.

Ubuntu and macOS each passed 50 ordinary archive and drag tests with eight fixture skips. This checkpoint covers the tested archive routes of E-OUT12 and E-OUT13 and uses the shared all-input observations qualified separately for disk caches. Recovery images and broader native qualification remain open.

## Multipart RAR output

The multipart RAR fixture copies six volumes to a disposable directory and encrypts only the third volume. A member can still be opened for viewing in memory. When the same member is sent through FileCat's disk spool, extraction job or drag staging, the protection of the opened later volume causes refusal. The archive cache is absent, the extraction destination is empty, and drag staging leaves no payload. The first and third volumes retain their distinct protection states.

The Windows 11 x64 VM passed all 10 archive cases and all 14 hex output cases at medium integrity on 4 KiB NTFS. The test source was `986d8edd` plus the output qualification changes. The XML receipts are `.tmp/filecat-arm64-output-986d8edd-r1-Core.xml` and `.tmp/filecat-arm64-output-986d8edd-r1-Platform.xml`. Native ARM64 verification uses the same selected suites in the manually dispatched Windows qualification workflow.
