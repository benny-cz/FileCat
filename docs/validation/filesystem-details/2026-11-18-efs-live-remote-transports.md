# Live remote transport tests with Windows EFS inputs

The medium-integrity Windows 11 x64 VM uploaded an EFS-encrypted 384 KiB file through FileCat's normal jobs and production SFTP, FTP, FTPES and FTPS connectors. The Ubuntu VM ran OpenSSH and pyftpdlib on its VMware adapter. TLS and SSH decisions used the exact fingerprints of the temporary lab identities.

Each transport exercised two cases. While the consent question was open, a separate connection listed the owned test directory and confirmed it was empty. Declining left it empty and preserved the encrypted original. Approval published one file whose bytes matched through a real remote read. The source remained encrypted and Operations reported the approved loss of EFS on the server. All eight cases passed. See `.tmp/filecat-efs-live-transport-results.log`.

The Windows upload rollback rerun passed 944 fault cases and three local move preservation cases, including the rewrite-before-retirement case seen in hosted Windows ARM64 CI. Its medium-integrity x64 result is in `.tmp/filecat-remote-reader-retirement-results.log`. The wider hosted runs qualify their own architecture separately.

## Native ARM64 FTP qualification

[GitHub Actions run 44676084393](https://github.com/benny-cz/FileCat/actions/runs/44676084393) passed the live EFS consent cases on revision `9b80d048b6254ade89602f473f1e9576bff6a862` with a medium-integrity ARM64 process on Windows 11 build 26200 and 4 KiB NTFS. The six executed cases covered approval and decline over plain FTP, explicit TLS FTP and implicit TLS FTP using owned loopback servers. Before consent, the second connection found no output. Approval published the exact bytes and reported the loss of EFS on the server. Decline left the remote directory empty. All three servers had empty roots after cleanup. The run artifact contains the two TLS certificate fingerprints, assembly hash and three matching TRX receipts.

## Native ARM64 SFTP and FTP-family qualification

[GitHub Actions run 44676874435](https://github.com/benny-cz/FileCat/actions/runs/44676874435) passed all eight live consent cases on revision `f68efa97ac9751174d21cdaed9debd8b1ea444af` using a medium-integrity ARM64 process on Windows 11 build 26200 and 4 KiB NTFS. The run repeated the FTP-family cases and added SFTP through FileCat's production SSH.NET connector to an owned loopback Paramiko 5.1.0 server, with trust bound to the exact server host-key fingerprint. A separate connection found an empty directory before approval and after decline. After approval, the connector read back the exact bytes and FileCat reported the loss of EFS on the server. The source remained encrypted in both cases. All four server roots were empty after cleanup.

The App test assembly SHA-256 was `94E0626BFFA657C4EA3531B52E01AC81D5ADE56FC49AE57A62D618C3C1775918`. The matching `sftp.trx` receipt has SHA-256 `0EC27551160313388AE6B062F2E64CC2421AD9A167F872BA0FDFC63294290943`. The run artifact retains TRX files and SHA-256 results for every transport. These live cases cover the reviewed local-file copy route. The separate controlled upload suite covers additional retry, move and derived inputs.
