# E-I42 — Remote small-file copies: fewer round trips per file

Release issue I42: at a 100 ms round trip each small file cost about 1.4 s up and 0.76 s down over SFTP, and 1.5 s up
and 0.85–0.95 s down over explicit FTPS (E-I41's trace). The owner decided that every known issue is fixed for rc.3.
**Status:** remediated preliminarily for rc.3 on 2026-11-25 ([I42](../FILECAT_1_0_RELEASE_ISSUES.md#i42), merged into main as `a2f983e9a`; the owner's decision of 2026-11-24 accepts the rest for 1.0.0); first recorded as fixed on `fix/i42-remote-small-file-copies` (from `rc3/integration-3` at `3a76703d8`) for SFTP, and for FTPS
downloads; several files in flight, the record's second remedy, is not part of the fix (see below). Automated evidence
on Debug builds; raw outputs are kept locally in `.tmp/i42-remote-small-files/` (SHA-256 at the end).

## Setup

- **SFTP:** the lent Ubuntu VM (Ubuntu 24.04.5, OpenSSH 9.6p1, the system's `sftp-server`), reached from the Windows host
  with a dedicated test key whose authorized-keys entry forces `sftp-server`. Each run used a new folder in the test
  account's home, removed afterwards.
- **FTPS:** pyftpdlib with explicit TLS (the tests' `TestFtpServer`: MLST and MFMT) on the host, bound to 127.0.0.2 with
  passive ports 51200–51259.
- **Delay:** `LatencyProxy` (`tests/FileCat.Remote.Tests/LatencyProxy.cs`) on 127.0.0.1 holds every chunk 50 ms each way,
  so a round trip takes 100 ms; it relays the SFTP connection and every FTPS control and data connection. Each run first
  checks it: five stats through it must take at least five round trips.
- **Load:** `SmallFileLatencyBenchmark` (`FILECAT_I42_BENCH=1`): 200 files of 4,096 bytes with distinct content and
  modified times in whole seconds, up through an F5 job into a new folder, then down through an F5 job. After the upload
  the server's copies are checked (SFTP: listed over a direct connection; FTPS: the server's own files), and after the
  download every byte and every modified time is compared with the source. The median of three runs (five for SFTP
  after the change) is reported; the SFTP connection counts every request by type. Jobs ran at below-normal priority and
  medium integrity (FileCat refuses host-key trust when elevated), on the owner's workstation, which other sessions
  also used.

## Before

Build `b89e290ec`: the product of `3a76703d8` plus a request observer that sends nothing, and the measurement harness.
It was measured twice. The first runs (SFTP up 277.6 s, down 220.8 s; FTPS up 260.0 s, down 209.3 s) shared the host
with other work, builds of this work among them for FTPS. The table gives the second runs, made with no build of this
work running, next to the runs after the change.

| 200 files of 4 KiB at 100 ms | Median total | Per file | Runs |
|---|---|---|---|
| SFTP up | 266.9 s | 1,334 ms | 264.9, 274.4, 266.9 s |
| SFTP down | 205.5 s | 1,028 ms | 205.5, 206.7, 204.7 s |
| FTPS up | 209.2 s | 1,046 ms | 209.2, 209.2, 208.3 s |
| FTPS down | 146.3 s | 732 ms | 146.5, 146.3, 146.3 s |

SFTP requests per file, the folder's own included: up 13.06 — `REALPATH` 6, `LSTAT` 2, and one each of `OPEN`, `WRITE`,
`CLOSE`, `SETSTAT` and `RENAME`; down 11.04 — `REALPATH` 3, `LSTAT` 3, `READ` 2, and one each of `OPEN`, `FSTAT` and
`CLOSE`. Each was waited for before the next, so an upload took about 13 round trips and a download about 10.

## What changed (`469cb9376`)

FileCat's fork of SSH.NET gains transfer operations on the exact path FileCat holds (`SftpClient.FileCatTransfer.cs`,
`SftpSession.FileCatTransfer.cs`, `SftpFileStream.FileCatTransfer.cs`; listed in `THIRD-PARTY-CHANGES.md`). SSH.NET's own
methods have the server resolve each path first (`SSH_FXP_REALPATH`: a round trip each, and one that follows a link in
the last part) and wait for every answer before the next request. The new operations send together the requests whose
answers do not decide what is sent next, and wait once for all of them.

- **Upload of a new file** (`ISftpChannel.UploadNewStated`): open the temporary name (create-exclusive, as before; waited
  for, since its handle decides the rest), then every write and, behind the last one on the same handle, the modified
  time (`SSH_FXP_FSETSTAT`), a stat of the handle (`SSH_FXP_FSTAT`) and the close, all answered in one more round trip.
  The job checks the size and the time as before, then takes the name with a plain rename of the exact paths.
- **Download** (`ISftpChannel.OpenReadStated`): the open and a stat of the path go together. The stat describes the file
  as opened, so until the first read the copy's revision is that stat rather than another question. A file of up to
  1 MiB is asked for whole at once, the revision after reading is the server's own stat of the path
  (`ISftpChannel.StatContent`, one request), and the close is sent without waiting for its answer.
- **Reads of a known length** (read-back verification, the checks before replacing): no `SSH_FXP_FSTAT` after the open.
- **New folders** are made with `SSH_FXP_MKDIR` on the exact path.
- **FTPS:** a download's first revision is the stat it opened by (its transfer begins with the first read): one MLST
  fewer per file. Uploads are unchanged: every command FileCat sends for a file is the transfer itself or a check it
  relies on (FTP has no exclusive create and no atomic rename), and FluentFTP does not pipeline commands.
- Channels that cannot do this (test doubles, wrappers) keep the old sequence through the interface's defaults.

## Each removed round trip, and why it is safe

Upload of one small file over SFTP (before: SSH.NET's `UploadFile`, `SetLastWriteTimeUtc`, `GetAttributes` and
`RenameFile`):

| Before | Round trips | After | Why the safety property holds |
|---|---|---|---|
| `REALPATH` of the temporary name | 1 | — | The name is one FileCat made in the folder it listed. The open is still create-exclusive (`CREAT EXCL TRUNC WRITE`) on it, so it never opens an existing file or one a link points to. |
| `OPEN` | 1 | `OPEN` | Unchanged and waited for. |
| `WRITE` × n, then `CLOSE` | 2 | `WRITE` × n, `FSETSTAT`, `FSTAT`, `CLOSE`, sent together | The SFTP drafts require a server to process requests for the same file in the order received, so the stat sees every write and the time, and the close comes last. A refused write or close still fails the upload. |
| `REALPATH`, `LSTAT`, `REALPATH`, `SETSTAT` (set the time) | 4 | — (the `FSETSTAT` above) | The same attribute and encoding (whole seconds), applied through the handle of the file just written rather than a path that could be swapped meanwhile. Where the server refuses `FSETSTAT` or does not keep the time, FileCat sends `SETSTAT` on the exact path as before, with the access time the file has, and a stat, in one round trip. A refusal there is still not an error, and the job still reports a time not kept (I43). |
| `REALPATH`, `LSTAT` (the size check's question) | 2 | — (the `FSTAT` above) | The stat of the handle describes exactly the file written. The job's check is unchanged: a size other than the bytes sent fails the upload, and nothing takes the name. On a server without `FSTAT`, the exact path is stat'ed after the close. |
| `REALPATH` × 2, `RENAME` | 3 | `RENAME` | `SSH_FXP_RENAME` never replaces. On the exact paths, a link put at either name meanwhile is refused (target) or renamed as the link itself (source); with `REALPATH` first, the rename went through the link: the copy landed at the link's target, outside the folder, or a file the link pointed to was moved in. Replacing an existing file still uses the unchanged `posix-rename` and recovery-backup paths. |
| **14 requests, 13 round trips** | | **6 requests, 3 round trips** | |

Download of one small file over SFTP (before: `Stat`, SSH.NET's `Open`, the copy's two revisions, the read and the
close):

| Before | Round trips | After | Why |
|---|---|---|---|
| `REALPATH`, `LSTAT` (is it there, is it a folder, its length) | 2 | `STAT` of the exact path, sent with the `OPEN` | The server follows links itself, as `REALPATH` did. A folder is stat'ed and its handle closed; nothing at the path reads "no longer exists", as before. |
| `OPEN`, `FSTAT` (SSH.NET asks the length) | 2 | `OPEN`, answered with the `STAT` | The length comes from the stat. It only paces read-ahead; reads end where the server says. |
| `REALPATH`, `LSTAT` (the revision before reading) | 2 | — | The stat sent right behind the open describes the file as opened, which is what this revision was. Any read clears it, so every later revision is asked of the server again. |
| `READ` (and one past the end, not waited for) | 1 | `READ` × 2, sent together | A file of up to 1 MiB is asked for whole at once, as SSH.NET's own `DownloadFile` does. |
| `REALPATH`, `LSTAT` (the revision after reading) | 2 | `STAT` | One request instead of two. It still follows the reads, so a change while reading is still seen. |
| `CLOSE` | 1 | `CLOSE`, not waited for | A read handle's close changes nothing FileCat relies on (its failure was ignored before too), and the server handles whatever the connection sends next after it. |
| **11 requests, 10 round trips** | | **6 requests, 3 round trips** | |

Unchanged: the temporary names and their journal steps; the conflict questions and their "apply to all" answers; the
replace paths (`posix-rename`, the recovery backup, the hash checks before replacing); the refusal of links as sources
and destinations; read-back verification; cancellation and cleanup of the temporary copy (I33); resuming a broken
upload (I39, which still sets the time and stats through SSH.NET); and listings (`REALPATH` still precedes `OPENDIR`,
and changed items are still changed only through a fresh listing's entries). One value differs: the access time an
upload sends with `FSETSTAT` is the upload's own time by FileCat's clock, where before it was the server's creation time
read back with `LSTAT`. SFTP version 3 sets both times together, and asking the server first would cost the round trip
this removes.

## After

Build `469cb9376`, same harness, same files, with no builds running.

| 200 files of 4 KiB at 100 ms | Before: median, per file | After: median, per file | Runs after | Faster |
|---|---|---|---|---|
| SFTP up | 266.9 s, 1,334 ms | **62.3 s, 312 ms** | 62.4, 62.3, 62.3, 62.3, 62.3 s | 4.3× |
| SFTP down | 205.5 s, 1,028 ms | **62.3 s, 311 ms** | 62.3, 62.7, 62.3, 62.3, 62.2 s | 3.3× |
| FTPS up | 209.2 s, 1,046 ms | 224.2 s, 1,121 ms | 223.9, 228.2, 224.2 s | unchanged (see below) |
| FTPS down | 146.3 s, 732 ms | **125.2 s, 626 ms** | 125.2, 124.8, 137.3 s | 1.17× |

The FTPS uploads after ran while another session's test suite ran on the host; nothing in them changed (the same FTP
commands in the same order). Run alternately, before and after one run at a time on 50 files (four pairs; the folder's
own requests weigh more per file here), the builds compare under the same conditions:

| 50 files of 4 KiB at 100 ms, per file | Before (`b89e290ec`) | After (`469cb9376`) |
|---|---|---|
| SFTP up | 1,345 ms (1,345, 1,344, 1,345, 1,345) | **330 ms** (330, 330, 330, 330) |
| SFTP down | 1,027 ms (1,027, 1,027, 1,026, 1,026) | **318 ms** (318, 317, 318, 317) |
| FTPS up | 1,056 ms (1,146, 1,056, 1,056, 1,056) | 1,056 ms (1,057, 1,056, 1,056, 1,056) |
| FTPS down | 749 ms (750, 749, 749, 749) | **647 ms** (647, 647, 647, 647) |

SFTP requests per file after: up 6.06 — one each of `OPEN`, `WRITE`, `FSETSTAT`, `FSTAT`, `CLOSE` and `RENAME`, and the
folder's own (`REALPATH` 2, `OPENDIR` 2, `READDIR` 4, `MKDIR` 1, `CLOSE` 2 over the 200 files); down 6.04 — `OPEN`,
`STAT` 2, `READ` 2 and `CLOSE` per file, and the folder's listing. Each file now costs three round trips each way
(300 ms of the 311–312 ms); every byte and modified time came back intact in every run. A thousand small files 100 ms
away now take about 5 minutes up, where the record measured about 23.

## Tests

New tests in `469cb9376`:

- `SmallFileRoundTripTests`, against a user-mode OpenSSH sshd (`TestSshd`: Linux and macOS) and pyftpdlib (every
  platform):
  - `Small_files_cost_sftp_one_open_and_one_round_of_requests_each_way_and_come_back_whole`: three small files up and back
    through F5 jobs, with every SFTP request counted by type. Up: exactly `OPEN`, `WRITE`, `FSETSTAT`, `FSTAT`, `CLOSE`
    and `RENAME` per file, plus one listing of the folder, and no `REALPATH`, `LSTAT`, `STAT` or `SETSTAT` per file. Down:
    one `OPEN` and two `STAT`s per file, and no `LSTAT`, `FSTAT` or `REALPATH` per file. Every byte and time is compared
    on the server and after the way back, and no temporary name is left.
  - `An_upload_creates_its_file_exclusively_and_reports_the_bytes_and_time_the_server_holds`: the content and time on the
    server equal the stat the upload returns; a taken name is refused and keeps its bytes and time; with no time asked,
    the server's own stands.
  - `A_server_refusing_times_through_the_handle_still_gets_them_through_the_path`: with `sftp-server -P fsetstat` the time
    still holds (through `SETSTAT`); with `-P fsetstat,setstat` the stat reports the server's own time, so the job
    reports it as not kept.
  - `A_rename_or_a_new_folder_never_goes_through_a_link_put_at_the_name`: a link at the target name is refused and nothing
    appears at its target; a link at the source name is renamed as the link, its target untouched; a link at a new
    folder's name is refused.
  - `Sftp_content_is_asked_for_its_revision_again_once_it_has_been_read` and its FTPS twin: before the first read the
    revision is the stat taken with the open (no request over SFTP); after a read it is asked again and sees a file
    grown meanwhile.
  - `Small_files_go_up_and_come_back_whole_over_ftps`: bytes and times both ways over explicit FTPS.
- `SftpJobTests.An_upload_takes_its_name_only_once_its_bytes_and_time_are_on_the_server`: when the staged copy is
  stat'ed, the final name does not exist yet and the stage holds every byte and the source's time.
- `SftpJobTests.A_server_that_keeps_fewer_bytes_than_sent_gets_nothing_published`: a server that silently keeps 4,096 of
  10,000 bytes gets "The server holds 4,096 bytes of the 10,000 sent", nothing under the name and no stage left.

Suites at `469cb9376`:

| Suite | Windows host (medium integrity) | Ubuntu VM (24.04.5, OpenSSH 9.6p1) |
|---|---|---|
| `FileCat.Remote.Tests`, whole | 2,812: 2,593 passed, 217 skipped, 2 failed — the two `A_killed_FTP_upload…` cases, which look for the Git checkout and were run from a copy of the build; from the checkout they pass (2 of 2) | 2,812: 2,738 passed, 72 skipped, 2 failed — the same two cases; in a checkout-shaped layout with the VM's `dotnet` they pass (2 of 2) |
| Skipped | sshd-based classes (no sshd on Windows), lab and benchmark cases without their variables, link-evidence and permission fixtures | lab and benchmark cases without their variables, permission-admission fixtures, Windows-only cases |
| `FileCat.Core.Tests`, job and copy classes (`JobEngineTests`, `Resume*`, `InterruptedCopy*`, `BackingSourceTransferTests`, `JobRateLimitTests`, `JobDataPaceTests`, `ConflictReviewTests`, `LocalCopyStagingOwnershipTests`, `RecoveryTransferConflictTests`) | 386: 384 passed, 2 skipped | not run (FileCat.Core is unchanged) |

`FileCat.App` builds against the change with no warnings. The existing `RemoteBenchmark` (`FILECAT_REMOTE_BENCH=1`, the
VM's own sshd and pyftpdlib over loopback, no added delay) passes with the change: over SFTP, 1,000 files of 1 KiB went
up through F5 in 5.2 s and down in 5.8 s, and 64 MiB went up at 64 MiB/s and down at 67 MiB/s, identical after the way
back, which exercises the new upload and download on a file of many requests.

## Mutation checks

Each mutation was applied to `469cb9376`, built, run against the tests named, and reverted; the working tree was clean
after the last. Windows runs were at medium integrity; the VM's against its own sshd. Every mutant failed its tests for
the reason named.

| Property | Mutation | Where | Failed, and why |
|---|---|---|---|
| Temporary name | the copy is written under its final name | Windows | `An_upload_takes_its_name_only_once_…`, `Uploads_publish_files_and_folders_through_temporary_names`: the job did not complete |
| Size check | the size the server holds is not compared with the bytes sent | Windows | `A_server_that_keeps_fewer_bytes_than_sent_gets_nothing_published`: the truncated copy was published |
| Times, in the job | no modified time is asked for | Windows | `Uploads_keep_modified_times_and_say_so_where_the_server_does_not`, `An_upload_takes_its_name_only_once_…`: the server kept the arrival time |
| Times, over SFTP | no `FSETSTAT`, and no fallback | VM | `An_upload_creates_its_file_exclusively_…` (time differs), `Small_files_cost_sftp_…` (no `FSETSTAT` counted) |
| Size, over SFTP | the `FSTAT` is sent before the writes | VM | `An_upload_creates_its_file_exclusively_…` (size 0 reported for the upload without a time), `Small_files_cost_sftp_…` (an extra `FSTAT` per file; with a time asked, the time fallback's stat after `SETSTAT` still gave the job the right size) |
| Exclusive create | the temporary name is opened with create-or-open | VM | `An_upload_creates_its_file_exclusively_…`: a taken name was written over |
| Link refusal, rename | both names resolved with `REALPATH` first, as SSH.NET did | VM | `A_rename_or_a_new_folder_never_goes_through_a_link_put_at_the_name`: the rename went through the link |
| Link refusal, new folder | the name resolved with `REALPATH` first | VM | the same test: the folder was made at the link's target |
| Revision after reading | the stat taken with the open keeps answering after reads | Windows (FTPS) and VM (SFTP) | `Ftp_content_…` and `Sftp_content_is_asked_for_its_revision_again_…`: a file grown while read went unseen |

## Several files in flight: not in rc.3

The record's second remedy is not part of this change. A job copies one file at a time, and its engine is built around
that:

- **Questions.** `Job.Ask` holds one pending decision. Conflict and error questions, their "apply to all" answers and the
  EFS confirmation would have to be serialized across workers.
- **Progress and pace.** `Job.BeginItem`, `CurrentFileProgress` and `SetCurrent` describe one file, and `Job.Throttle`
  paces each file to the whole speed limit, so N files at once would run at N times the limit.
- **The job's connection.** `SftpExecutorBase` keeps one lease, its listing cache and its reconnect-on-retry logic, and
  the cleanup of a temporary copy (`DiscardTemp`) uses them.
- **More connections.** The pool allows four connections per SFTP server and two per FTP server, shared with panels and
  viewers: a job holding three would make a panel wait (up to two minutes, then "All 4 connections … are busy"). A new
  connection may also ask the user again: a host key trusted once, a password not saved, a keyboard-interactive or
  one-time code.

What it would need: a quiet lease (an idle connection, or one opened without any question, taken only while a slot
stays free for the panels); a dispatch stage that sends out only files that need no question (new local files with no
conflict in the folder's listing, not links, not EFS-protected, no speed limit, no read-back verification, no resume),
each worker uploading and publishing on its own connection and removing its own temporary copy on failure or cancel;
everything else, and any file a worker could not finish, through the existing serial path with its questions;
completion (journal step, counts, issues, listing additions, moved sources) on the job thread; and a cancel that waits
for the workers' cleanup. N would be 3 connections per SFTP job (the pool's four less one for the panels); FTP would stay
at one, since its pool has two. At 100 ms that would cut the time per file to about a third again.

## Not verified

- One SFTP server (OpenSSH 9.6p1) and one FTPS server (pyftpdlib). Other servers' handling of requests sent together on
  one handle rests on the SFTP drafts' rule that requests on the same file are processed in order; a server that refuses
  `FSETSTAT` falls back to `SETSTAT`, but one that would reorder them was not available to test.
- One link shape: 100 ms added by a relay on the client host, no loss, no bandwidth limit.
- The sshd-based tests ran on the Ubuntu VM only here; CI's macOS lane runs them for the first time.
- Folder trees: each new folder still costs its listing (`REALPATH`, `OPENDIR`, `READDIR` × 2, `CLOSE`); only `MKDIR`
  lost its `REALPATH`. Not measured.
- FileCat itself was not run against a server by hand; all of this is through jobs in the test host.

## Raw outputs

In `.tmp/i42-remote-small-files/` of the main checkout (not in the repository), SHA-256:

| File | What | SHA-256 |
|---|---|---|
| `i42-before-200.txt` | the first runs before (SFTP and FTPS) | `2c2a26c57aa8ea52669b585c8d8f6ce32dfb1d1285f1da95c8557d78f910e7fc` |
| `i42-before-sftp-200-quiet.txt` | SFTP before, second runs | `2580b83116da7b45446f84313a507df2b388befc21e87974864701077a3c3d75` |
| `i42-after-sftp-200.txt` | SFTP after | `e2694c30d13a4d23445ae25438b721eda3cb29f5533a0cb6b1ef3dd7a2ac34b5` |
| `i42-before-ftps-200-quiet.txt` | FTPS before, second runs | `9a101c474300eb50ced57479d332d12587e882573f84080f394eaab311b7689e` |
| `i42-after-ftps-200-quiet.txt` | FTPS after | `1a35deafdfd318e7edb9ae27b63c782d5321340be1367e5161742a69476a194c` |
| `i42-alternating-50.txt` | before and after alternately, 50 files | `707e5be7ac7eecf8ff19884a7742e5549f139cdc5107d96c1fc7dc901fb29451` |
| `i42-windows-remote-suite.console.txt` | `FileCat.Remote.Tests` on Windows | `f41e8f929108f7c56875211fe252d5b732afe6ca152de45e10602858bc02d523` |
| `i42-windows-remote-crash-harness.console.txt` | the two crash-harness cases from the checkout | `02fa6c73bc27a0b843fadce26224c6209a912b1dd570c38705346a1d5aae25ac` |
| `i42-windows-core-job-classes.console.txt` | `FileCat.Core.Tests` job and copy classes | `0260b609eb6d6fa1ea737a7503abb97103e636c21157e83d96aec2733ab5ac01` |
| `i42-vm-remote-suite.txt` | `FileCat.Remote.Tests` on the Ubuntu VM | `91a61752db74cd3824f48be487dc8d875177e7c44f91f10a5c5b56b8d3274943` |
| `i42-vm-remote-crash-harness.txt` | the two crash-harness cases on the VM | `0152f62119f33aedb24a8496596d6898cf9fa705b6e6fcc398b99f49f726d79c` |
| `i42-vm-remote-benchmark.txt` | `RemoteBenchmark` on the VM | `f6093aee30387bd3dacf64c35d4f1845b95e4bcfa49bb9e7ec653537f92e3ca7` |
| `i42-mutations.txt` | the ten mutation results | `91ed7b9e1a8735714b4683768773464320af63a45d3660336c72f69a7de86e38` |

The benchmark results (`i42-before-*`, `i42-after-*`, `i42-alternating-50`) have their runner's console output beside
them (`.console.txt`), and each mutant's own test output is kept as `i42-mutant-*`.
