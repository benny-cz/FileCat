# I631 — A refused FTPS data connection left its final reply on the control channel

**Source:** `a9343253` (parent `3069448f`), merged into main as `1ec1207f` through PR #9 on 2026-11-23. [PR run 45255358149](https://github.com/benny-cz/FileCat/actions/runs/45255358149) passed all 19 jobs in its second attempt; in the first, GitHub could not get hosted macOS runners for two macOS jobs. **Disposition:** must fix (ENV-06/V08); remediated preliminarily at source. The native Mac run of the release tree found it ([E-REL-NATIVE-20261123](E-REL-NATIVE-20261123-release-tree.md)).

## Fault and user consequence

FileCat checks every FTPS data connection against the certificate its session was accepted with (`BoundedFtpClient.CheckData`). It refuses a connection that presents another certificate, or none, before any data crosses it. The refusal disposed the data stream and threw.

Disposing closes only the sockets. FluentFTP's `FtpDataStream.Close`, which reads the command's final reply, was not reached. The server had accepted the command that opened the connection with a 125 or 150 reply, so it still owed that command a final reply. It sends that reply, 226 or 426, only once the data connection closes. The reply stayed on the control connection, which went on serving commands and went back to the pool.

On a loaded server the reply arrives after FluentFTP's check for stale data, so the next command read it as its own answer. In the macOS native run of 2026-11-23:
- The MLST after a refused listing read "226", and `Stat` found nothing.
- Opening a file that was on the server then reported "The file is not on the server."

The refusal itself was correct. Every command after it on the same connection could take another command's reply as its own, until the connection ended. It is the same kind of fault as [I553](../FILECAT_1_0_RELEASE_ISSUES.md#i553), where a bounded listing left its final reply behind (fixed at `559a7572`).

## Change

- **Read the owed reply.** `CheckData` notes whether the refused stream's command got a preliminary positive reply (`FtpDataStream.CommandStatus`, 1xx). After disposing the stream, it reads that one pending reply (`TakeFinalReply`), with the read timeout capped at five seconds, and then throws the refusal.
- **End a connection that cannot be resynchronized.** If no reply comes within the bound, the control connection is ended instead of reused.
- **Keep the refusal's meaning.** `FtpChannel.Run` passes a `CertificateRejectedException` through unchanged, so ending the connection cannot turn a certificate refusal into a lost-connection error.

## Tests

- **Fixture.** The owned pyftpdlib test server gains `FILECAT_FTP_DATA_CLOSE_DELAY` (`TestFtpServer.TryStart(…, dataCloseDelaySeconds)`). It holds a data connection's close, and with it the final reply, as a loaded server does. It applies only to the server that presents a separate data certificate.
- **New test: `FTPS_refused_data_connection_leaves_no_late_reply_for_the_next_command`.** With a 0.5-second delay, a listing is refused, and `Stat` must then find a file that is there.
  - On the previous client it failed 3 of 3 times (`Stat` found nothing).
  - With the fix it passed 10 of 10 times at medium integrity on the development host. So did the original `FTPS_refuses_a_data_connection_presenting_another_certificate_within_the_session`.
  - The whole `FtpIntegrationTests` class passed: 28 tests, 2 skipped.
- **Hosted.**
  - PR run 45255358149 passed all 19 jobs.
  - The first attempt of [PR #10's run 45267952326](https://github.com/benny-cz/FileCat/actions/runs/45267952326) had the same product tree. Its macOS Remote job passed all 2,728 Remote tests that ran, out of 2,801.

## Limits

- Only pyftpdlib, the owned fixture, was observed. Other servers may send the final reply at other times, or not at all after a refused TLS data connection. The five-second bound and the ended connection are meant to cover those cases, but no other server was tried.
- The fixture's delay imitates load. The original failure was intermittent: it appeared under the native run's parallel lanes, and the class passed alone 27 of 27.
- On the final release tree `7ba3aa00`, the Mac and Ubuntu native runs passed every Remote test that ran (2,728 and 2,730, 0 failed), this class included ([E-REL-NATIVE-20261123](E-REL-NATIVE-20261123-release-tree.md#second-round-the-final-tree)). The Windows run had not finished when this was written.

## Finite candidate check and stop

1. **Hosted CI.** At the candidate source, the Remote test jobs pass `FtpIntegrationTests`, the new test included.
2. **Native runs.** On the Mac and the Windows VM, `FtpIntegrationTests` passes natively on the final release tree.

Stop when both pass. Reopen only for:
- a change to `CheckData`, the channel's reply handling or FluentFTP's data-stream close;
- a demonstrated command desynchronization after a refused data connection.
