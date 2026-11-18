# EFS protection during derived uploads and extraction retries

ZIP, archive and Recovery providers now admit each derived upload reader through the same held-source checks as extraction. The initial check precedes remote folder creation or conflict changes for a refused item. The upload attempt and subsequent read-back reopen through the same provider admission. A retained edit snapshot checks the held object before and after review and around each actual read. Automatic cleanup retains a snapshot that is EFS-protected when it reaches the deletion check.

FileCat closes the upload reader before the server gives a staged copy its final name. The old order could publish a copy even when the source reader failed on close. It could also keep a Windows source open with sharing that prevented the upload regression fixture from rewriting it after publication. Failed or canceled reader cleanup now leaves only the stage for the existing cleanup logic.

On the Windows 11 x64 VM at medium integrity, all 34 remote upload and retained edit cases passed. They include EFS ZIP and TAR refusal, clear ZIP and TAR byte equality, and an edit snapshot encrypted after capture. The encrypted snapshot's upload preserved the prior remote bytes and kept the encrypted local copy. The tests use actual Windows EFS inputs with a controlled SFTP channel. The result is in `.tmp/filecat-efs-derived-remote-final-tests.xml`.

The Windows archive, Recovery and stream-staging suite passed all 35 cases. Two controlled source changes verified that retry and read-back both attempted fresh protection admission. The job discarded the owned stage and wrote no final output after the second admission was refused. The result is in `.tmp/filecat-stream-output-readmission-tests.xml`.

Ubuntu and macOS each passed the 944-case upload fault suite, including the 56 provider-close failures found during verification. Each system then passed 2,638 remote cases with 31 fixture skips, including ordinary live FTP, FTPES, FTPS and SFTP coverage. These runs qualify the reader cleanup boundary and ordinary transports. Native EFS input across live transports remains open.
