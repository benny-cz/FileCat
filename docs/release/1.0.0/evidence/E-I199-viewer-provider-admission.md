# E-I199 — F3 provider-open device admission

Recorded 2026-10-07. **Discovery retained before remediation; broader I06 qualification remains open.**
Original product ba05bf2195ecfe3018c6b1cadaadeceff67c3fc6; discovery documentation ccc1e717856795013dfa9113f5e8033c786ae427.

The ordinary F3 `MainViewModel.ViewItemAsync` provider-open path used unbounded thread-pool work before the already bounded viewer metadata admission. Seven requests against one held provider all started concurrently. A request made after scheduler shutdown also opened content, then discarded it during later viewer admission. Quick View already uses the device scheduler.

Six preliminary headless controls exercise the actual F3 method and an owned provider exposing a real 31-byte FileContentSource. Four repeated-open variants return content, unsupported/null, I/O failure or access denial. The separate already-stopped case and a responsive opening/closing control verify admission and ownership. The original producer has five failures and one positive pass. All held calls run off the UI on pool workers; the UI marker and another device remain responsive. All original returned sources close once after their held calls return, without metadata/read work after shutdown or a late viewer. Before/after file hashes agree with independently reconstructed known bytes. This finding concerns admission bounds and late opens; it does not claim a reproduced use-after-dispose.

Private `FileCatReleaseEvidence/po199-v1`:

| Retained path | SHA-256 |
|---|---|
| I199-discovery-v3.json | e1b81d66601ea3c306f3ad744c96368fd4ee4abe90f3875846b7e5aeae5e7078 |
| baseline-v1/command.json | 57e9741b25f9843745a4a6f9f5fa7d303b5068653dfabf34aefa9e947bd4446a |
| baseline-v1/results/app.trx | 60611dde97060f0fc0dc9335671453177c807564861c00997f34842cae4bd24b |

The first discovery preparation compared a lower-case Python digest to uppercase C# output and failed before writing evidence or changing product files. The fresh independent reader normalizes the known input's representation; original source/build/results remain intact.

No native desktop/input, human, physical/reference or candidate qualification is claimed. Physical-source/USB hold, contract freeze, candidate formation and explicit human GO/publication gates remain. No persistent machine setup or physical source changed.
