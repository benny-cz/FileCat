# Live remote transport tests with Windows EFS inputs

The medium-integrity Windows 11 x64 VM uploaded an EFS-encrypted 384 KiB file through FileCat's normal jobs and production SFTP, FTP, FTPES and FTPS connectors. The Ubuntu VM ran OpenSSH and pyftpdlib on its VMware adapter. TLS and SSH decisions used the exact fingerprints of the temporary lab identities.

Each transport exercised two cases. While the consent question was open, a separate connection listed the owned test directory and confirmed it was empty. Declining left it empty and preserved the encrypted original. Approval published one file whose bytes matched through a real remote read. The source remained encrypted and Operations reported the approved loss of EFS on the server. All eight cases passed. See `.tmp/filecat-efs-live-transport-results.log`.

The Windows upload rollback rerun passed 944 fault cases and three local move preservation cases, including the rewrite-before-retirement case seen in hosted Windows ARM64 CI. Its medium-integrity x64 result is in `.tmp/filecat-remote-reader-retirement-results.log`. The wider hosted runs qualify their own architecture separately.

## Native ARM64 FTP qualification

The manual **Windows ARM64 EFS live FTP qualification** workflow runs the live consent cases for FTP, explicit TLS and implicit TLS against three owned loopback servers. The test process must report ARM64 and run at medium integrity. The controller requires both cases to pass for each transport and checks that the server roots are empty after test cleanup. It publishes the test counts, certificate fingerprints for TLS, and exact test receipts. Live ARM64 SFTP requires its own fixture.
