# EFS protection during archive extraction and drag staging

ZIP and other archive providers admit staged output through the protection observations of the opened archive sources. The admission also covers a small member already materialized in memory. For a forward-only member the provider rechecks before and after reading each buffer. EFS or unknown protection prevents the initial output admission or interrupts publication if it is observed during a read.

On Windows 11 x64, four native cases exercised the actual drag and streamed transfer routes using EFS-encrypted ZIP and TAR archives. Each format included a small in-memory member and a 33 MiB member that supports forward-only reads. View reads succeeded. Drag staging and extraction were refused, the clear output directories contained no payload, and the source remained encrypted. The suite passed 111 tests with two fixture skips. The result is in `.tmp/filecat-efs-archive-publication-verified-tests.xml`.

Ubuntu and macOS each passed 50 ordinary archive and drag tests with eight fixture skips. This checkpoint covers the tested archive routes of E-OUT12 and E-OUT13 and uses the shared all-input observations qualified separately for disk caches. Recovery images and broader native qualification remain open.
