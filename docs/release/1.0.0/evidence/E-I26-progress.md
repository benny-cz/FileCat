# E-I26 — progress at 100% while a job still worked; the time left

Issue: [I26](../FILECAT_1_0_RELEASE_ISSUES.md#i26--progress-at-100-while-an-operation-still-works-and-a-time-left-that-was-not-honest).
Plan: PI-07 (truthful outcomes), §9.1 (no fabricated ETA). Owner's requirements (2026-09-30): no 100% while the work
is not done; a realistic, steady time left that does not jump; honest about its uncertainty, with a lowest and a highest
estimate (the owner pointed to the estimator of their `screener` project as the model).

## E-I26-R1 — reproduction (physical host)

A probe copied one 512 MB file with read-back verification through the real job engine and sampled the job every 5 ms:
all bytes were counted done at 1,134 ms (current item "big.bin (verifying)"), the job finished at 2,845 ms — **the bar
stood at 100% for 1,711 ms of 2,696 ms, 63% of the job**, and the time left disappeared then (it was computed from bytes
left only). In the source: verification (`ContentEqual`) hashed both files without counting any work; skipped or failed
files left their bytes in the total, so the bar stopped short and then jumped; the time left came from a 1-second
moving average of bytes per second, which a folder of small files or a change of pace misleads.

## E-I26-V1 — change `d40fda0`

- **Work counted:** `Job.WorkBytesTotal/Done` = copying + reading back to verify (both files) + what a file settles when
  it is skipped, fails or turns out smaller (`BeginItem` at the start of each file; the outcome settles the rest).
- **Estimator (`ProgressEstimator`, Core):** time ≈ a·megabytes + b·files, fitted by weighted least squares with a short
  (15 s) and a long (120 s) memory; samples of at least half a second; a single outlier limited for the long memory; a
  lasting change of pace moves the long memory to the short one; a weak prior (10 ms/MB, 2 ms/file) where the samples
  cannot tell (one large file). Likely time left from the fit; pessimistic = likely + 1.28 σ, where σ combines the
  residual noise of the samples to come and the model's own uncertainty for the work left — which is large when the work
  left differs from the work measured (small files after large ones). Nothing is claimed while measuring (first 2 s),
  counting (totals growing), stalled (no progress for 6 s or more), paused or waiting for an answer, or finishing. The
  bar weighs the work by the model, never goes back, and stays at or below 99% until the job ends. The shown time counts
  down between updates, rises slowly (8 s time constant; 3 s for a real slowdown), falls quickly (2.5 s; 0.75 s for a
  big speed-up).
- **Display:** "about 40 s left", "20–40 s left", "2–4 min left", "1 h 5 min – 2 h 30 min left"; one value when the
  pessimistic time is within 15% (or 10 s) of the likely one; coarser rounding for longer times.
- **Tests:** `ProgressEstimatorTests` (8: one large file; 20,000 small files; small files after large ones — the
  pessimistic time covered the truth in at least 90% of the ticks while only large files had been measured, and the
  likely time is right half a minute into the small files; noisy progress does not jump; pauses are not the pace;
  stalls and growing totals claim nothing; pessimistic ≥ likely; a real verified copy counts its reading back — about a
  third of the work is done when the last byte is copied); `JobProgressViewTests` (the Operations view model never
  shows 100% while the job runs, and shows 100% after); `FormattersTests.The_time_left_reads_as_people_say_it`.
- **Regression:** host, all four suites (run `run-i26a`): Core 554, Remote 43, Platform.Windows 111, App 172 — 0 failed
  (TRX: Core `bfb24be8b277a60be9b285864497179267285e8532e12700de30d0141523bf8b`, App
  `853efb8bc29df1aa6852a45029587e796ed60057858e2bb325e53668fbffd333`, Platform.Windows
  `0840fe40d80cd2181a681d834ca3453ba38f946b6839e8e130703eeb7cabaf32`, Remote
  `60ad14998ff29c9512d851ce70a3016e5d0f1f22783df78625f45e02edc19e8d`).
- **Picture:** the screenshot tool's new `FILECAT_SHOT_OPERATION` mode pictures a verified, speed-limited copy under way
  (`i26-operation-strip.png` `310146ea969acfa6dd28f13c857db7e536a0e0fc0209ee3199de2cfd03c93767`): "477 MB of 709 MB · 57,3
  MB/s · about 30 s left" with the bar near 22% — about 1.65 GB of the job's 2.1 GB of work (copy and read-back) was
  left, 29 s at that speed. The previous display would have shown 67% with 4 s left.

## Limitations

- The estimator's behavior is tested on simulated jobs and short real copies; long real jobs (USB sticks whose write
  cache fills, network shares, phones) are still to be watched on the candidate.
- Work that no file stands for (folder times at the end, journal) is not modelled; it shows as "finishing…".
- Stream jobs (archives, remote transfers) count their bytes as before; they gain the estimator but not verification
  accounting.
