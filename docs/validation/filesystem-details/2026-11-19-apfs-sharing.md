# Held APFS sharing and sparse details on Apple silicon

macOS 27.2.1 arm64 passed the native sharing fixture on a disposable APFS image. The summary left the sharing section unread. The metadata-only full report returned the same item identity, the clone ID, the private-byte count and the APFS sparse representation flag where applicable.

The source and intact clone had the same clone ID, reported all blocks shared and showed zero private bytes. An ordinary copy reported no sharing flag and 524,288 private bytes. Rewriting the first 65,536 bytes of a second clone produced the **May share blocks** observation and that private-byte count. A separate file with a gap between bytes reported the APFS sparse representation flag. Directory and leaf-symlink reports omitted file sharing. The source, both clones and the ordinary copy matched the fixture's expected bytes.

The APFS image was ejected after one pass with zero skips or failures. `apfs-sharing-080538ad-r1.trx` has SHA-256 `87682C42201D7A8B117A53B25C3F9B887EB87CA309D990B39835931CB1C43B15`. This receipt qualifies the observed APFS fields on the tested Apple silicon setup. Intel and other macOS versions remain separate scopes.
