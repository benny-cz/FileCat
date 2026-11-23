# Linux statx returned-mask validity — 21 November 2026

The Linux Summary/Full parser now checks the returned `statx` mask before interpreting raw size and block-count slots. A slot absent from the returned mask is **Unsupported** even when its bytes contain an all-ones value; a present slot that exceeds the supported signed size or allocation range is **Failed** with its overflow reason. This prevents unreturned bytes from being reported as a failed filesystem observation.

On the Windows x64 workstation with .NET SDK 10.0.101, the focused `FileDetailsTests` selection passed **15 tests**, failed 0, skipped 1 (the existing platform-only case). The four new synthetic mask combinations cover neither slot returned, each one returned alone, and both returned. The source SHA-256 for `UnixFileRecords.Details.cs` is `ED8A7870826A46B8E85F6129B66316D404BF5A1FF9803AE33FFDDA8BC4D8A9D4`. This is parser evidence only; no Linux native fixture or VM was used, and F-OBS remains partial.
