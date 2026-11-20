# Live remote transport tests with Windows EFS inputs

The medium-integrity Windows 11 x64 VM uploaded an EFS-encrypted 384 KiB file through FileCat's normal jobs and production SFTP, FTP, FTPES and FTPS connectors. The Ubuntu VM ran OpenSSH and pyftpdlib on its VMware adapter. TLS and SSH decisions used the exact fingerprints of the temporary lab identities.

Each transport exercised two cases. While the consent question was open, a separate connection listed the owned test directory and confirmed it was empty. Declining left it empty and preserved the encrypted original. Approval published one file whose bytes matched through a real remote read. The source remained encrypted and Operations reported the approved loss of EFS on the server. All eight cases passed. See `.tmp/filecat-efs-live-transport-results.log`.

The Windows upload rollback rerun passed 944 fault cases and three local move preservation cases, including the rewrite-before-retirement case seen in hosted Windows ARM64 CI. Its medium-integrity x64 result is in `.tmp/filecat-remote-reader-retirement-results.log`. The wider hosted runs qualify their own architecture separately.

## Native ARM64 FTP qualification

[GitHub Actions run 44676084393](https://github.com/benny-cz/FileCat/actions/runs/44676084393) passed the live EFS consent cases on revision `9b80d048b6254ade89602f473f1e9576bff6a862` with a medium-integrity ARM64 process on Windows 11 build 26200 and 4 KiB NTFS. The six executed cases covered approval and decline over plain FTP, explicit TLS FTP and implicit TLS FTP using owned loopback servers. Before consent, the second connection found no output. Approval published the exact bytes and reported the loss of EFS on the server. Decline left the remote directory empty. All three servers had empty roots after cleanup. The run artifact contains the two TLS certificate fingerprints, assembly hash and three matching TRX receipts.

The manual **Windows ARM64 EFS live remote qualification** workflow now includes SFTP. It runs the same approval and decline cases against an owned loopback SSH server and checks the exact server host-key fingerprint. The SFTP fixture smoke test ran on Ubuntu with Paramiko 2.12.0. The hosted ARM64 SFTP result is pending.
