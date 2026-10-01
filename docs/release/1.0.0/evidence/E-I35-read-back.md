# E-I35 — "Read back and compare content" was ignored outside copies between folders on disk

Release issue I35. Preliminary automated evidence on a development build.

## Discovery (E-I35-D1)

Planning V08's "alter resume tails and earlier content" case: an upload's resume checks only the last 64 KiB of the
server's partial copy, so bytes changed earlier in it during a break would be continued from. The copy dialog's
verification choice ("Read back and compare content", also a default in Settings) should catch that. A code review
showed that only the executor for copies between folders on disk (`TransferExecutor`) read `TransferOptions.Verify`:

- uploads to SFTP and FTP (`SftpUploadExecutor`) checked the server's size only;
- downloads from servers, extraction from archives and copies out of other providers (`StreamTransferExecutor`, also
  used by a move from a server) never compared;
- copies to a phone (`MtpUploadExecutor`) never compared;

and none of them said so. The user who chose verification was told nothing different from a verified copy.

## Remediation `53b0794`

- **Uploads:** after the server's size check and before publishing, the copy is read back through the connection and
  compared with the source by SHA-256; a difference discards it ("Read-back verification found different content on
  the server; the copy was discarded, and nothing was published under this name"). Reading it all also catches what the
  resume check cannot: bytes before the checked tail that changed on the server during a break.
- **Downloads and extraction:** before the copy takes its name, the source is read again from its provider (downloaded
  again, decompressed again) and compared with the copy on disk; a difference or a skipped re-read discards the copy.
  Recovered content with lost parts is not read back (the same guesses would read the same); its caveat stands.
- **Other copies:** an executor that can neither read back nor makes no copy (a move within one server renames) is
  marked; any other copy with read-back chosen completes as before and ends with "Not read back: this kind of copy
  cannot read its copies back to compare them, so they were checked by their size only."
- Progress counts the verification's reading (both sides), as for local copies (I26).

## Tests (E-I35-T1)

- `SftpJobTests`: read-back catches bytes a server stored differently (a fake server inverting one byte) and publishes
  nothing; a faithful upload passes and counts both readings; V08 "alter resume tails": a partial copy changed at its
  end is not continued from (the whole file goes again); "…and earlier content": a byte changed before the checked tail
  is continued from, and read-back catches it before publishing.
- `ResumeTransferTests`: a download reads the source again and counts both readings; a copy that reads back
  differently (same size and time, one byte changed) is discarded; a copy whose engine cannot read back says it was
  checked by size only.
- Host suites at `02acee6` (which contains `53b0794`): Core 561, Remote 91, Platform.Windows 118 — 0 failed; App 173 — 0
  failed (run on the working tree with this change before it was committed).

## Limitations

- Without read-back (the default "Size and metadata"), a resumed upload relies on the 64 KiB tail check; a change
  before it on the server during a break is not detected. Disclosed by the choice's own wording ("fast").
- Copies to a phone are not read back (no device here to test a WPD read-back); they say so.
