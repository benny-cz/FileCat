# P6 and P8: remote transfer measurements

Status: **executed 2026-09-28** against servers on the same machine: pyftpdlib (FTP, Windows 11) and OpenSSH's sshd
(SFTP, Ubuntu 24.04 in WSL). Loopback has almost no latency, so these numbers show FileCat's own cost per file and
per byte; on a real network, round trips dominate. TV-12 (varied real servers, host-key changes, disconnects) remains
an external validation.

```
FILECAT_REMOTE_BENCH=1 FILECAT_PYTHON=python dotnet test tests/FileCat.Remote.Tests --filter RemoteBenchmark --logger "console;verbosity=detailed"
# SFTP: on Linux or macOS with sshd (FILECAT_SSHD names another binary); FILECAT_FTP_LOG=<file> logs the FTP server's commands
```

Each row compares an ordinary F5 job with writing or reading the same files straight through the connection. The
straight path creates each file and writes it. The job also checks for a conflict, writes under a temporary name,
checks the size that arrived, sets the time, and renames. For downloads it also stages, journals, and marks each file.

| Server | 1,000 files of 1 KiB, up | 1,000 files of 1 KiB, down | 64 MiB up | 64 MiB down |
|---|---|---|---|---|
| FTP (pyftpdlib) | 6.6 s; connection alone 2.1 s (3.1×) | 3.7 s; alone 1.9 s (2.0×) | 518 MiB/s; alone 349 MiB/s | 746 MiB/s; alone 1,203 MiB/s |
| SFTP (OpenSSH) | 2.7 s; alone 0.9 s (3.1×) | 1.8 s; alone 1.2 s (1.5×) | 104 MiB/s; alone 112 MiB/s | 231 MiB/s; alone 263 MiB/s |

Large-file rates vary between runs by up to 2× with the Python FTP server; SFTP's are steady.

**Budgets** asserted by the benchmark: small-file uploads within 5× of the connection alone, downloads within 4×, and
large transfers within 2.5×.

## What the measurements found and changed

The first FTP run uploaded the 1,000 files in 37 s (10.5× the connection alone). Fixed:

1. **A disk flush per uploaded file.** Each upload recorded its journal intent durably. New files are now
   group-committed like local copies (plan §9.3); moves (whose source goes) and replacements are still flushed first.
2. **The destination folder was listed again after every file**, so copying n files into one folder read the
   growing folder n times (quadratic). A folder's listing now stays valid for every name the job did not add itself.
3. **Checking a missing name cost three round trips on FTP** (MLST, SIZE, CWD), and every successful check a fourth
   (MDTM). A server that has MLST now answers with MLST alone, including the time.
4. Each upload allocated its 256 KiB buffer anew; one buffer is now reused.

Also found: the test harness never read the FTP server's log output, so the server stopped after a few hundred
transfers once the pipe filled. The harness now drains it.
